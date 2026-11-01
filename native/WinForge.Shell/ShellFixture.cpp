#include <windows.h>
#undef GetCurrentTime
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <cstdio>
#include <cmath>

using namespace winrt;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Hosting;

static Border fixture{nullptr};
static Slider bindingSource{nullptr};
static Button alignmentTarget{nullptr};
static HWND parentWindow{};
static LRESULT CALLBACK FixtureProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_USER + 1) return static_cast<LRESULT>(fixture.Width());
    if (message == WM_USER + 2) { bindingSource.Value(static_cast<double>(wParam)); return 1; }
    if (message == WM_USER + 4) return fixture.VerticalAlignment() == VerticalAlignment::Center &&
        alignmentTarget.VerticalContentAlignment() == VerticalAlignment::Center;
    if (message == WM_USER + 5) return fixture.VerticalAlignment() == VerticalAlignment::Stretch &&
        alignmentTarget.VerticalContentAlignment() == VerticalAlignment::Bottom;
    if (message == WM_USER + 3) {
        auto transform = fixture.RenderTransform().try_as<Media::TranslateTransform>();
        auto origin = fixture.RenderTransformOrigin();
        return transform && transform.X() == 8 && std::abs(origin.X - 0.2f) < 0.001f && std::abs(origin.Y - 0.3f) < 0.001f;
    }
    if (message == WM_CLOSE) { DestroyWindow(window); return 0; }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, wParam, lParam);
}

int wmain() {
    try {
        init_apartment(apartment_type::single_threaded);
        auto manager = WindowsXamlManager::InitializeForCurrentThread();
        WNDCLASSW definition{};
        definition.hInstance = GetModuleHandleW(nullptr);
        definition.lpfnWndProc = FixtureProc;
        definition.lpszClassName = L"WinForge.Shell.Fixture";
        RegisterClassW(&definition);
        parentWindow = CreateWindowExW(0, definition.lpszClassName, L"WinForge shell verification", WS_OVERLAPPEDWINDOW,
            100, 100, 400, 220, nullptr, nullptr, definition.hInstance, nullptr);
        if (!parentWindow) throw_last_error();
        DesktopWindowXamlSource source;
        auto native = source.as<IDesktopWindowXamlSourceNative>();
        check_hresult(native->AttachToWindow(parentWindow));
        HWND island{};
        check_hresult(native->get_WindowHandle(&island));
        SetWindowPos(island, nullptr, 0, 0, 380, 180, SWP_SHOWWINDOW);
        fixture = Border();
        fixture.Name(L"WinForgeVerificationTarget");
        bindingSource = Slider();
        bindingSource.Maximum(1000);
        bindingSource.Value(160);
        Data::Binding binding;
        binding.Source(bindingSource);
        binding.Path(PropertyPath(L"Value"));
        binding.Mode(Data::BindingMode::OneWay);
        fixture.SetBinding(FrameworkElement::WidthProperty(), binding);
        fixture.Height(80);
        Media::TranslateTransform originalTransform;
        originalTransform.X(8);
        fixture.RenderTransform(originalTransform);
        fixture.RenderTransformOrigin({0.2f, 0.3f});
        TextBlock label;
        label.Text(L"WinForge native XAML verification");
        alignmentTarget = Button();
        alignmentTarget.Name(L"WinForgeAlignmentTarget");
        alignmentTarget.VerticalContentAlignment(VerticalAlignment::Bottom);
        alignmentTarget.Content(label);
        fixture.Child(alignmentTarget);
        source.Content(fixture);
        ShowWindow(parentWindow, SW_SHOWNOACTIVATE);
        std::wprintf(L"%lu %llu\n", GetCurrentProcessId(), static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(parentWindow)));
        std::fflush(stdout);
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
        source.Close();
        manager.Close();
        return 0;
    } catch (hresult_error const& error) {
        std::fwprintf(stderr, L"XAML fixture error: 0x%08X %s\n", static_cast<unsigned int>(error.code().value), error.message().c_str());
        return 1;
    }
}
