#include <windows.h>
#include <shobjidl.h>
#include <shlobj.h>
#undef GetCurrentTime
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <cstdio>

using namespace winrt;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Hosting;

static com_ptr<IExplorerBrowser> browser;
static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_APP + 1 && browser) {
        com_ptr<IShellView> view;
        if (SUCCEEDED(browser->GetCurrentView(IID_PPV_ARGS(view.put())))) return view->Refresh();
        return E_FAIL;
    }
    if (message == WM_CLOSE) { DestroyWindow(window); return 0; }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, wParam, lParam);
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    try {
        init_apartment(apartment_type::single_threaded);
        auto manager = WindowsXamlManager::InitializeForCurrentThread();
        WNDCLASSW definition{};
        definition.hInstance = GetModuleHandleW(nullptr);
        definition.lpfnWndProc = WindowProc;
        definition.lpszClassName = L"WinForge.FolderSize.Fixture";
        RegisterClassW(&definition);
        auto window = CreateWindowExW(0, definition.lpszClassName, L"WinForge folder-size verification", WS_OVERLAPPEDWINDOW,
            120, 120, 800, 500, nullptr, nullptr, definition.hInstance, nullptr);
        if (!window) throw_last_error();
        DesktopWindowXamlSource xaml;
        auto native = xaml.as<IDesktopWindowXamlSourceNative>();
        check_hresult(native->AttachToWindow(window));
        HWND island{};
        check_hresult(native->get_WindowHandle(&island));
        SetWindowPos(island, nullptr, 0, 0, 1, 1, SWP_SHOWWINDOW);
        Controls::TextBlock label;
        label.Name(L"WinForgeFolderSizeCoexist");
        xaml.Content(label);
        check_hresult(CoCreateInstance(CLSID_ExplorerBrowser, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(browser.put())));
        RECT rectangle{0, 2, 780, 450};
        FOLDERSETTINGS settings{FVM_DETAILS, FWF_FULLROWSELECT};
        check_hresult(browser->Initialize(window, &rectangle, &settings));
        PIDLIST_ABSOLUTE target{};
        check_hresult(SHParseDisplayName(argv[1], nullptr, &target, 0, nullptr));
        auto browse = browser->BrowseToIDList(target, SBSP_ABSOLUTE);
        CoTaskMemFree(target);
        check_hresult(browse);
        ShowWindow(window, SW_SHOWNOACTIVATE);
        std::wprintf(L"%lu %llu\n", GetCurrentProcessId(), static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(window)));
        std::fflush(stdout);
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
        browser->Destroy();
        browser = nullptr;
        xaml.Close();
        manager.Close();
        return 0;
    } catch (hresult_error const& error) {
        std::fwprintf(stderr, L"Folder-size fixture error: 0x%08X %s\n", static_cast<unsigned int>(error.code().value), error.message().c_str());
        return 1;
    }
}
