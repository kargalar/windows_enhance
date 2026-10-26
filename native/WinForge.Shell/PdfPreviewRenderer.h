#pragma once

#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <d2d1_1.h>
#include <d2d1_1helper.h>
#include <wincodec.h>
#include <windows.data.pdf.interop.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Data.Pdf.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <stdexcept>
#include <vector>

namespace winforge::pdf {
    struct PreviewResult {
        std::uint32_t width;
        std::uint32_t height;
        std::uint32_t pageCount;
    };

    namespace detail {
        constexpr std::uint32_t MaximumDimension = 4096;

        struct Apartment final {
            Apartment() { winrt::init_apartment(winrt::apartment_type::multi_threaded); }
            ~Apartment() { winrt::clear_factory_cache(); winrt::uninit_apartment(); }
            Apartment(const Apartment&) = delete;
            Apartment& operator=(const Apartment&) = delete;
        };

        struct Page final {
            winrt::Windows::Data::Pdf::PdfPage value;
            ~Page() { try { if (value) value.Close(); } catch (...) { } }
        };

        class BitmapRead final {
        public:
            explicit BitmapRead(ID2D1Bitmap1* bitmap) : bitmap_(bitmap) {
                winrt::check_hresult(bitmap_->Map(D2D1_MAP_OPTIONS_READ, &mapped_));
            }
            ~BitmapRead() { bitmap_->Unmap(); }
            BitmapRead(const BitmapRead&) = delete;
            BitmapRead& operator=(const BitmapRead&) = delete;
            const D2D1_MAPPED_RECT& Data() const noexcept { return mapped_; }
        private:
            ID2D1Bitmap1* bitmap_;
            D2D1_MAPPED_RECT mapped_{};
        };

        inline void WritePng(const std::filesystem::path& output, std::uint32_t width, std::uint32_t height,
            const std::vector<std::uint8_t>& pixels) {
            winrt::com_ptr<IWICImagingFactory> factory;
            winrt::check_hresult(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                __uuidof(IWICImagingFactory), factory.put_void()));
            winrt::com_ptr<IWICStream> stream;
            winrt::check_hresult(factory->CreateStream(stream.put()));
            winrt::check_hresult(stream->InitializeFromFilename(output.c_str(), GENERIC_WRITE));
            winrt::com_ptr<IWICBitmapEncoder> encoder;
            winrt::check_hresult(factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, encoder.put()));
            winrt::check_hresult(encoder->Initialize(stream.get(), WICBitmapEncoderNoCache));
            winrt::com_ptr<IWICBitmapFrameEncode> frame;
            winrt::check_hresult(encoder->CreateNewFrame(frame.put(), nullptr));
            winrt::check_hresult(frame->Initialize(nullptr));
            winrt::check_hresult(frame->SetSize(width, height));
            winrt::check_hresult(frame->SetResolution(96, 96));
            auto format = GUID_WICPixelFormat32bppBGRA;
            winrt::check_hresult(frame->SetPixelFormat(&format));
            if (!IsEqualGUID(format, GUID_WICPixelFormat32bppBGRA))
                throw std::runtime_error("PNG encoder cannot preserve the bounded BGRA image.");
            winrt::check_hresult(frame->WritePixels(height, width * 4, static_cast<UINT>(pixels.size()),
                const_cast<BYTE*>(pixels.data())));
            winrt::check_hresult(frame->Commit());
            winrt::check_hresult(encoder->Commit());
        }

        inline PreviewResult Render(const std::filesystem::path& input, const std::filesystem::path& output,
            std::uint32_t maximumDimension) {
            auto file = winrt::Windows::Storage::StorageFile::GetFileFromPathAsync(input.wstring()).get();
            auto document = winrt::Windows::Data::Pdf::PdfDocument::LoadFromFileAsync(file).get();
            const auto pages = document.PageCount();
            if (!pages) throw std::runtime_error("PDF contains no pages.");
            Page page{document.GetPage(0)};
            const auto size = page.value.Size();
            if (!std::isfinite(size.Width) || !std::isfinite(size.Height) || size.Width <= 0 || size.Height <= 0)
                throw std::runtime_error("PDF page size is invalid.");
            const auto scale = std::min(1.0, static_cast<double>(maximumDimension) / std::max(size.Width, size.Height));
            const auto width = static_cast<std::uint32_t>(std::clamp(std::round(size.Width * scale), 1.0, static_cast<double>(maximumDimension)));
            const auto height = static_cast<std::uint32_t>(std::clamp(std::round(size.Height * scale), 1.0, static_cast<double>(maximumDimension)));

            // The device belongs to this operation. Never ask the Windows PDF
            // stream renderer to create/cache a process-wide hardware device.
            winrt::com_ptr<ID3D11Device> graphics;
            winrt::com_ptr<ID3D11DeviceContext> immediate;
            winrt::check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
                graphics.put(), nullptr, immediate.put()));
            auto dxgi = graphics.as<IDXGIDevice>();
            winrt::com_ptr<ID2D1Device> device;
            winrt::check_hresult(D2D1CreateDevice(dxgi.get(), nullptr, device.put()));
            winrt::com_ptr<ID2D1DeviceContext> context;
            winrt::check_hresult(device->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, context.put()));
            const auto pixelFormat = D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED);
            const auto targetProperties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW, pixelFormat);
            winrt::com_ptr<ID2D1Bitmap1> target;
            winrt::check_hresult(context->CreateBitmap(D2D1::SizeU(width, height), nullptr, 0, &targetProperties, target.put()));
            context->SetTarget(target.get());
            winrt::com_ptr<IPdfRendererNative> renderer;
            winrt::check_hresult(PdfCreateRenderer(dxgi.get(), renderer.put()));
            auto parameters = PdfRenderParams({}, width, height);
            context->BeginDraw();
            context->Clear(D2D1::ColorF(D2D1::ColorF::White));
            const auto rendered = renderer->RenderPageToDeviceContext(winrt::get_unknown(page.value), context.get(), &parameters);
            const auto ended = context->EndDraw();
            winrt::check_hresult(rendered);
            winrt::check_hresult(ended);
            context->SetTarget(nullptr);

            const auto readProperties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_CPU_READ | D2D1_BITMAP_OPTIONS_CANNOT_DRAW, pixelFormat);
            winrt::com_ptr<ID2D1Bitmap1> readback;
            winrt::check_hresult(context->CreateBitmap(D2D1::SizeU(width, height), nullptr, 0, &readProperties, readback.put()));
            winrt::check_hresult(readback->CopyFromBitmap(nullptr, target.get(), nullptr));
            std::vector<std::uint8_t> pixels(static_cast<std::size_t>(width) * height * 4);
            {
                BitmapRead mapping(readback.get());
                const auto& data = mapping.Data();
                if (!data.bits || data.pitch < width * 4) throw std::runtime_error("PDF bitmap readback is invalid.");
                for (std::uint32_t row = 0; row < height; ++row)
                    std::memcpy(pixels.data() + static_cast<std::size_t>(row) * width * 4,
                        data.bits + static_cast<std::size_t>(row) * data.pitch, static_cast<std::size_t>(width) * 4);
            }
            WritePng(output, width, height, pixels);
            immediate->ClearState();
            immediate->Flush();
            return {width, height, pages};
        }
    }

    inline PreviewResult RenderPreview(const std::filesystem::path& input, const std::filesystem::path& outputPng,
        std::uint32_t maximumDimension) {
        if (maximumDimension < 64 || maximumDimension > detail::MaximumDimension ||
            !std::filesystem::is_regular_file(input) || std::filesystem::exists(outputPng))
            throw std::invalid_argument("PDF input, output or maximum dimension is invalid.");
        detail::Apartment apartment;
        return detail::Render(std::filesystem::absolute(input), std::filesystem::absolute(outputPng), maximumDimension);
    }
}
