#include <windows.h>
#undef GetCurrentTime
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Markup.h>
#include <cstdio>
#include <cmath>

using namespace winrt;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Hosting;

static Border fixture{nullptr};
static Slider bindingSource{nullptr};
static Button alignmentTarget{nullptr};
static ColumnDefinition widthTarget{nullptr};
static TextBlock clockText{nullptr};
static ItemsControl overflowItems{nullptr};
static WrapGrid FindOverflowGrid(DependencyObject const& parent) {
    if (auto panel = parent.try_as<WrapGrid>()) return panel;
    for (int index = 0; index < Media::VisualTreeHelper::GetChildrenCount(parent); ++index)
        if (auto panel = FindOverflowGrid(Media::VisualTreeHelper::GetChild(parent, index))) return panel;
    return nullptr;
}
static HWND parentWindow{};
static LRESULT CALLBACK FixtureProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_USER + 1) return static_cast<LRESULT>(fixture.Width());
    if (message == WM_USER + 2) { bindingSource.Value(static_cast<double>(wParam)); return 1; }
    if (message == WM_USER + 7) return static_cast<LRESULT>(widthTarget.Width().Value);
    if (message == WM_USER + 8) {
        clockText.Text(L"Windows default");
        // Read back before returning to the message loop: no polling can hide
        // a temporary reversion from this assertion.
        return clockText.Text() == L"14:05 custom";
    }
    if (message == WM_USER + 9) return clockText.Text() == L"Windows default";
    if (message == WM_USER + 10 || message == WM_USER + 11) {
        auto panel = FindOverflowGrid(overflowItems);
        if (!panel) return 0;
        if (message == WM_USER + 10) return panel.MaximumRowsOrColumns() == 3 && panel.Orientation() == Orientation::Horizontal &&
            std::isnan(panel.ItemWidth()) && std::isnan(panel.ItemHeight());
        return panel.MaximumRowsOrColumns() == 5 && panel.Orientation() == Orientation::Vertical &&
            panel.ItemWidth() == 40 && panel.ItemHeight() == 40;
    }
    if (message == WM_USER + 4) return fixture.VerticalAlignment() == VerticalAlignment::Center &&
        alignmentTarget.VerticalContentAlignment() == VerticalAlignment::Center &&
        fixture.HorizontalAlignment() == HorizontalAlignment::Center && alignmentTarget.HorizontalContentAlignment() == HorizontalAlignment::Center;
    if (message == WM_USER + 5) return fixture.VerticalAlignment() == VerticalAlignment::Stretch &&
        alignmentTarget.VerticalContentAlignment() == VerticalAlignment::Bottom &&
        fixture.HorizontalAlignment() == HorizontalAlignment::Stretch && alignmentTarget.HorizontalContentAlignment() == HorizontalAlignment::Right;
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
        clockText = label;
        label.Name(L"TimeInnerTextBlock");
        label.Text(L"WinForge native XAML verification");
        alignmentTarget = Button();
        alignmentTarget.Name(L"WinForgeAlignmentTarget");
        alignmentTarget.VerticalContentAlignment(VerticalAlignment::Bottom);
        alignmentTarget.HorizontalContentAlignment(HorizontalAlignment::Right);
        Grid iconPanel;
        iconPanel.Name(L"WinForgeColumnTarget");
        widthTarget = ColumnDefinition();
        widthTarget.Width(GridLength{44, GridUnitType::Pixel});
        iconPanel.ColumnDefinitions().Append(widthTarget);
        iconPanel.Children().Append(label);
        overflowItems = ItemsControl();
        overflowItems.ItemsPanel(Markup::XamlReader::Load(L"<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><WrapGrid x:Name=\"WinForgeOverflowGrid\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" MaximumRowsOrColumns=\"5\" Orientation=\"Vertical\" ItemWidth=\"40\" ItemHeight=\"40\" /></ItemsPanelTemplate>").as<ItemsPanelTemplate>());
        iconPanel.Children().Append(overflowItems);
        alignmentTarget.Content(iconPanel);
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
