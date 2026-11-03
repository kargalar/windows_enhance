#include <windows.h>
#include <cstdio>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\NotificationsPlacement\\Fixture";
struct SavedValue { std::wstring name; DWORD kind{}; std::vector<BYTE> bytes; bool exists{}; };
struct RegistryScope {
    HKEY key{};
    std::vector<SavedValue> values;
    RegistryScope() {
        if (RegCreateKeyExW(HKEY_CURRENT_USER, RuntimeKey, 0, nullptr, 0, KEY_READ | KEY_WRITE, nullptr, &key, nullptr)) throw std::runtime_error("Runtime key unavailable.");
        DWORD active{}, bytes = sizeof(active);
        if (RegGetValueW(key, nullptr, L"active", RRF_RT_REG_DWORD, nullptr, &active, &bytes) == ERROR_SUCCESS && active) {
            RegCloseKey(key); key = nullptr;
            throw std::runtime_error("Disable notification placement before running its fixture.");
        }
    }
    void Write(wchar_t const* name, DWORD kind, void const* data, DWORD length) {
        bool saved = false;
        for (auto const& value : values) if (value.name == name) saved = true;
        if (!saved) {
            SavedValue value; value.name = name;
            DWORD bytes{};
            value.exists = RegQueryValueExW(key, name, nullptr, &value.kind, nullptr, &bytes) == ERROR_SUCCESS;
            if (value.exists) { value.bytes.resize(bytes); RegQueryValueExW(key, name, nullptr, nullptr, value.bytes.data(), &bytes); }
            values.push_back(value);
        }
        if (RegSetValueExW(key, name, 0, kind, static_cast<BYTE const*>(data), length)) throw std::runtime_error("Runtime write failed.");
    }
    void Number(wchar_t const* name, DWORD value) { Write(name, REG_DWORD, &value, sizeof(value)); }
    void Heartbeat(ULONGLONG value) { Write(L"heartbeat", REG_QWORD, &value, sizeof(value)); }
    ~RegistryScope() {
        if (!key) return;
        for (auto const& value : values) {
            if (value.exists) RegSetValueExW(key, value.name.c_str(), 0, value.kind, value.bytes.data(), static_cast<DWORD>(value.bytes.size()));
            else RegDeleteValueW(key, value.name.c_str());
        }
        RegCloseKey(key);
    }
};
void Require(bool valid, char const* message) { if (!valid) throw std::runtime_error(message); }
bool FirstVisiblePositionCorrect = true;
LONG ExpectedLeft{}, ExpectedTop{};
bool InspectFirstFrame{};
LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (InspectFirstFrame && message == WM_WINDOWPOSCHANGED && IsWindowVisible(window)) {
        RECT rect{}; GetWindowRect(window, &rect);
        FirstVisiblePositionCorrect &= rect.left == ExpectedLeft && rect.top == ExpectedTop;
    }
    return DefWindowProcW(window, message, wparam, lparam);
}
void Pump(DWORD milliseconds) {
    auto until = GetTickCount64() + milliseconds;
    do {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        Sleep(10);
    } while (GetTickCount64() < until);
}
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    try {
        RegistryScope registry;
        registry.Number(L"active", 1); registry.Heartbeat(GetTickCount64());
        registry.Number(L"anchor", 3); registry.Number(L"monitor", 1); registry.Number(L"gap", 12);
        registry.Number(L"last-error", 0);
        WNDCLASSW wc{}; wc.lpfnWndProc = WindowProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"WinForge.ToastFixture";
        Require(RegisterClassW(&wc) != 0, "Toast fixture registration failed.");
        auto library = LoadLibraryW(argv[1]); Require(library != nullptr, "Toast DLL load failed.");
        auto install = reinterpret_cast<HHOOK(WINAPI*)(DWORD)>(GetProcAddress(library, "InstallNotificationsFixtureHook"));
        Require(install != nullptr, "Toast hook export missing.");
        auto hook = install(GetCurrentThreadId()); Require(hook != nullptr, "Toast hook installation failed.");
        auto window = CreateWindowW(wc.lpszClassName, L"New notification", WS_POPUP, 100, 200, 360, 150, nullptr, nullptr, wc.hInstance, nullptr);
        Require(window != nullptr, "Toast fixture creation failed.");
        Require(GetPropW(window, L"WinForge.ToastPlacement.Native") != nullptr, "A new toast was not attached before first display.");
        MONITORINFO monitor{sizeof(monitor)};
        GetMonitorInfoW(MonitorFromPoint({0,0}, MONITOR_DEFAULTTOPRIMARY), &monitor);
        auto gap = MulDiv(12, static_cast<int>(GetDpiForWindow(window)), 96);
        ExpectedLeft = monitor.rcMonitor.left + gap; ExpectedTop = monitor.rcMonitor.top + gap;
        auto foreground = GetForegroundWindow();
        InspectFirstFrame = true;
        SetWindowPos(window, nullptr, 700, 600, 360, 150, SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
        RECT placed{}; GetWindowRect(window, &placed);
        Require(placed.left == ExpectedLeft && placed.top == ExpectedTop && FirstVisiblePositionCorrect,
            "Toast showed a default-position frame before placement.");
        Require(GetForegroundWindow() == foreground, "Placement activated the notification.");
        SetWindowPos(window, nullptr, 900, 800, 400, 180, SWP_NOACTIVATE | SWP_NOZORDER);
        GetWindowRect(window, &placed);
        Require(placed.left == ExpectedLeft && placed.top == ExpectedTop && placed.right - placed.left == 400 && FirstVisiblePositionCorrect,
            "Shell-style moves or resize escaped the pre-display placement.");
        InspectFirstFrame = false;
        registry.Number(L"anchor", 6);
        SendMessageW(window, WM_NULL, 0, 0);
        RECT centered{}; GetWindowRect(window, &centered);
        Require(centered.left == monitor.rcMonitor.left + (monitor.rcMonitor.right - monitor.rcMonitor.left - 400) / 2 &&
            centered.top == monitor.rcMonitor.top + (monitor.rcMonitor.bottom - monitor.rcMonitor.top - 180) / 2,
            "Settings did not move an existing toast to the monitor center synchronously.");
        registry.Number(L"anchor", 0); registry.Number(L"gap", 0);
        SendMessageW(window, WM_NULL, 0, 0);
        RECT flush{}; GetWindowRect(window, &flush);
        Require(flush.right == monitor.rcMonitor.right && flush.bottom == monitor.rcMonitor.bottom,
            "A zero edge gap failed to place a toast flush at the bottom right.");
        registry.Number(L"anchor", 3); registry.Number(L"gap", 12);
        SendMessageW(window, WM_NULL, 0, 0);
        auto other = CreateWindowW(wc.lpszClassName, L"Action center", WS_POPUP, 50, 60, 360, 150, nullptr, nullptr, wc.hInstance, nullptr);
        ShowWindow(other, SW_SHOWNOACTIVATE);
        RECT unrelated{}; GetWindowRect(other, &unrelated);
        Require(unrelated.left == 50 && unrelated.top == 60, "An unrelated shell window was repositioned.");
        auto observed = CreateWindowW(wc.lpszClassName, L"Pending notification", WS_POPUP, 50, 60, 360, 150, nullptr, nullptr, wc.hInstance, nullptr);
        SetPropW(observed, L"WinForge.ToastPlacement.Observer", reinterpret_cast<HANDLE>(1));
        SetWindowTextW(observed, L"New notification"); ShowWindow(observed, SW_SHOWNOACTIVATE);
        GetWindowRect(observed, &unrelated);
        Require(!GetPropW(observed, L"WinForge.ToastPlacement.Native") && unrelated.left == 50 && unrelated.top == 60,
            "Native placement took over an observer-owned toast and lost its original position.");
        DestroyWindow(observed);
        auto second = CreateWindowW(wc.lpszClassName, L"New notification", WS_POPUP, 100, 200, 360, 150, nullptr, nullptr, wc.hInstance, nullptr);
        ShowWindow(second, SW_SHOWNOACTIVATE);
        RECT stacked{}; GetWindowRect(second, &stacked);
        Require(stacked.left == placed.left && stacked.top == placed.bottom + 12, "Toasts overlap on first display.");
        DestroyWindow(second);
        registry.Number(L"active", 0);
        SendMessageW(window, WM_NULL, 0, 0);
        RECT restored{}; GetWindowRect(window, &restored);
        Require(!GetPropW(window, L"WinForge.ToastPlacement.Native"), "Disable left a stale native-ownership marker.");
        Require(restored.left == 700 && restored.top == 600 && restored.right - restored.left == 360 && restored.bottom - restored.top == 150,
            "Disable failed to restore the original requested bounds.");
        registry.Number(L"active", 1); registry.Heartbeat(GetTickCount64());
        SendMessageW(window, WM_NULL, 0, 0);
        GetWindowRect(window, &placed);
        Require(placed.left == ExpectedLeft, "Existing toast failed to reattach synchronously.");
        registry.Heartbeat(GetTickCount64() - 10000); Pump(700);
        GetWindowRect(window, &restored);
        Require(restored.left == 700 && restored.top == 600, "Expired lease failed to restore toast bounds.");
        UnhookWindowsHookEx(hook); DestroyWindow(window); DestroyWindow(other);
        std::puts("PASS: toast first visible position, shell moves/resize, focus, stack, identity exclusion, restore, reattach and crash recovery.");
        return 0;
    } catch (std::exception const& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
}
