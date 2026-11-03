#include <windows.h>
#include <commctrl.h>
#include <algorithm>
#include <iterator>
#include <new>
#include <string>
#include <vector>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\NotificationsPlacement\\Runtime";
constexpr wchar_t FixtureKey[] = L"Software\\WinForge\\NotificationsPlacement\\Fixture";
constexpr wchar_t NativeProperty[] = L"WinForge.ToastPlacement.Native";
constexpr wchar_t ObserverProperty[] = L"WinForge.ToastPlacement.Observer";
constexpr UINT_PTR SubclassId = 0x57465450;
constexpr UINT_PTR TimerId = 0x57465451;
HMODULE Library{};
bool LocalFixture{};
thread_local bool Discovering{};
struct Options { int anchor{}, monitor{}, gap{12}; };
struct State { HWND window{}; RECT original{}; bool hasOriginal{}, moving{}; };
thread_local std::vector<State*> Windows;

DWORD Number(HKEY key, wchar_t const* name, DWORD fallback) {
    DWORD value{}, bytes = sizeof(value);
    return RegGetValueW(key, nullptr, name, RRF_RT_REG_DWORD, nullptr, &value, &bytes) == ERROR_SUCCESS ? value : fallback;
}
bool ReadOptions(Options& options) {
    HKEY key{};
    if (RegOpenKeyExW(HKEY_CURRENT_USER, LocalFixture ? FixtureKey : RuntimeKey, 0, KEY_READ, &key) != ERROR_SUCCESS) return false;
    ULONGLONG beat{};
    DWORD bytes = sizeof(beat);
    auto live = Number(key, L"active", 0) != 0 &&
        RegGetValueW(key, nullptr, L"heartbeat", RRF_RT_REG_QWORD, nullptr, &beat, &bytes) == ERROR_SUCCESS &&
        GetTickCount64() - beat < 6000;
    options.anchor = static_cast<int>(std::min(Number(key, L"anchor", 0), 6ul));
    options.monitor = static_cast<int>(std::min(Number(key, L"monitor", 0), 16ul));
    options.gap = static_cast<int>(std::min(Number(key, L"gap", 12), 200ul));
    RegCloseKey(key);
    return live;
}
void Report(DWORD error) {
    HKEY key{};
    if (RegOpenKeyExW(HKEY_CURRENT_USER, LocalFixture ? FixtureKey : RuntimeKey, 0, KEY_SET_VALUE, &key) == ERROR_SUCCESS) {
        RegSetValueExW(key, L"last-error", 0, REG_DWORD, reinterpret_cast<BYTE const*>(&error), sizeof(error));
        RegCloseKey(key);
    }
}
bool IsSystemHost() {
    static const bool host = [] {
        wchar_t path[MAX_PATH]{}, windows[MAX_PATH]{};
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        GetWindowsDirectoryW(windows, MAX_PATH);
        auto expected = std::wstring(windows) + L"\\SystemApps\\ShellExperienceHost_cw5n1h2txyewy\\ShellExperienceHost.exe";
        return _wcsicmp(path, expected.c_str()) == 0;
    }();
    return host;
}
bool IsToast(HWND window) {
    if (GetAncestor(window, GA_ROOT) != window) return false;
    wchar_t name[128]{}, title[128]{};
    GetClassNameW(window, name, static_cast<int>(std::size(name)));
    GetWindowTextW(window, title, static_cast<int>(std::size(title)));
    if (LocalFixture) return wcscmp(name, L"WinForge.ToastFixture") == 0 && wcscmp(title, L"New notification") == 0;
    return wcscmp(name, L"Windows.UI.Core.CoreWindow") == 0 &&
        (wcscmp(title, L"New notification") == 0 || wcscmp(title, L"Yeni bildirim") == 0);
}
struct Display { HMONITOR handle{}; MONITORINFOEXW info{}; };
BOOL CALLBACK AddDisplay(HMONITOR monitor, HDC, RECT*, LPARAM parameter) {
    auto displays = reinterpret_cast<std::vector<Display>*>(parameter);
    Display display; display.handle = monitor; display.info.cbSize = sizeof(display.info);
    try { if (GetMonitorInfoW(monitor, &display.info)) displays->push_back(display); }
    catch (...) { return FALSE; }
    return TRUE;
}
bool Target(State* state, RECT source, Options const& options, RECT& target) {
    std::vector<Display> displays;
    if (!EnumDisplayMonitors(nullptr, nullptr, AddDisplay, reinterpret_cast<LPARAM>(&displays))) return false;
    if (displays.empty()) return false;
    std::sort(displays.begin(), displays.end(), [](auto const& a, auto const& b) {
        bool primaryA = (a.info.dwFlags & MONITORINFOF_PRIMARY) != 0, primaryB = (b.info.dwFlags & MONITORINFOF_PRIMARY) != 0;
        return primaryA != primaryB ? primaryA : wcscmp(a.info.szDevice, b.info.szDevice) < 0;
    });
    auto index = std::clamp(options.monitor - 1, 0, static_cast<int>(displays.size()) - 1);
    POINT cursor{};
    if (!options.monitor && GetCursorPos(&cursor)) {
        auto monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        for (size_t i = 0; i < displays.size(); ++i) if (displays[i].handle == monitor) index = static_cast<int>(i);
    }
    auto const& display = displays[index];
    // Match the module's selected full-monitor bounds, including negative origins.
    auto work = display.info.rcMonitor;
    auto width = static_cast<long long>(source.right) - source.left, height = static_cast<long long>(source.bottom) - source.top;
    auto workWidth = static_cast<long long>(work.right) - work.left, workHeight = static_cast<long long>(work.bottom) - work.top;
    if (width <= 0 || height <= 0 || width > workWidth || height > workHeight) return false;
    UINT dpiX = 96, dpiY = 96;
    // Loaded by Windows on supported Windows 10/11 hosts; no change to host DPI awareness.
    using DpiForMonitor = HRESULT(WINAPI*)(HMONITOR, int, UINT*, UINT*);
    static auto shcore = LoadLibraryW(L"shcore.dll");
    static auto dpi = shcore ? reinterpret_cast<DpiForMonitor>(GetProcAddress(shcore, "GetDpiForMonitor")) : nullptr;
    if (dpi) dpi(display.handle, 0, &dpiX, &dpiY);
    auto gap = MulDiv(options.gap, static_cast<int>(dpiX), 96);
    long long stack = 0;
    for (auto other : Windows) {
        if (other == state) break;
        RECT bounds{};
        if (IsWindowVisible(other->window) && GetWindowRect(other->window, &bounds)) stack += bounds.bottom - bounds.top + 12;
    }
    auto anchor = options.anchor;
    auto left = anchor == 1 || anchor == 3 ? work.left + gap : anchor >= 4 ? work.left + (workWidth - width) / 2 : work.right - width - gap;
    auto top = anchor == 2 || anchor == 3 || anchor == 4 ? work.top + gap + stack : anchor == 6 ? work.top + (workHeight - height) / 2 + stack : work.bottom - height - gap - stack;
    if (stack > workHeight - height || (stack > 0 && (top < work.top || top + height > work.bottom))) return false;
    left = std::clamp(left, static_cast<long long>(work.left), work.right - width);
    top = std::clamp(top, static_cast<long long>(work.top), work.bottom - height);
    target = {static_cast<LONG>(left), static_cast<LONG>(top), static_cast<LONG>(left + width), static_cast<LONG>(top + height)};
    return true;
}
void PlaceNow(State* state, Options const& options) {
    RECT current{}, target{};
    if (state->moving || !IsWindowVisible(state->window) || !GetWindowRect(state->window, &current)) return;
    if (!Target(state, current, options, target)) { Report(ERROR_INVALID_DATA); return; }
    if (EqualRect(&current, &target)) return;
    if (!state->hasOriginal) { state->original = current; state->hasOriginal = true; }
    state->moving = true;
    if (!SetWindowPos(state->window, nullptr, target.left, target.top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE)) Report(GetLastError());
    state->moving = false;
}
LRESULT CALLBACK ToastProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam, UINT_PTR, DWORD_PTR reference) try {
    auto state = reinterpret_cast<State*>(reference);
    if (message == WM_NCDESTROY) {
        KillTimer(window, TimerId);
        RemoveWindowSubclass(window, ToastProc, SubclassId);
        RemovePropW(window, NativeProperty);
        std::erase(Windows, state);
        delete state;
        return DefSubclassProc(window, message, wparam, lparam);
    }
    if ((message == WM_TIMER && wparam == TimerId) || message == WM_NULL || message == WM_WINDOWPOSCHANGING) {
        Options options;
        if (!ReadOptions(options) || !IsToast(window)) {
            KillTimer(window, TimerId);
            RemoveWindowSubclass(window, ToastProc, SubclassId);
            RemovePropW(window, NativeProperty);
            std::erase(Windows, state);
            if (state->hasOriginal && IsToast(window) && !SetWindowPos(window, nullptr, state->original.left, state->original.top,
                state->original.right - state->original.left, state->original.bottom - state->original.top, SWP_NOZORDER | SWP_NOACTIVATE)) Report(GetLastError());
            delete state;
            return DefSubclassProc(window, message, wparam, lparam);
        }
        if (message == WM_WINDOWPOSCHANGING && !state->moving && lparam) {
            // Let the host validate its request, then replace coordinates in the
            // window procedure, before Windows commits or displays the move.
            auto result = DefSubclassProc(window, message, wparam, lparam);
            auto position = reinterpret_cast<WINDOWPOS*>(lparam);
            RECT current{}, target{};
            GetWindowRect(window, &current);
            RECT requested = current;
            if (!(position->flags & SWP_NOMOVE)) { requested.left = position->x; requested.top = position->y; }
            requested.right = requested.left + ((position->flags & SWP_NOSIZE) ? current.right - current.left : position->cx);
            requested.bottom = requested.top + ((position->flags & SWP_NOSIZE) ? current.bottom - current.top : position->cy);
            if (!(position->flags & SWP_HIDEWINDOW) && Target(state, requested, options, target)) {
                if (!state->hasOriginal) { state->original = requested; state->hasOriginal = true; }
                position->x = target.left; position->y = target.top;
                position->flags &= ~SWP_NOMOVE;
            }
            else if (!(position->flags & SWP_HIDEWINDOW) && requested.right > requested.left && requested.bottom > requested.top) Report(ERROR_INVALID_DATA);
            return result;
        }
        if (message != WM_WINDOWPOSCHANGING) PlaceNow(state, options);
    }
    return DefSubclassProc(window, message, wparam, lparam);
}
catch (...) { Report(ERROR_INVALID_DATA); return DefSubclassProc(window, message, wparam, lparam); }
void Attach(HWND window) {
    DWORD_PTR existing{};
    if (!IsToast(window) || GetPropW(window, ObserverProperty) || GetWindowSubclass(window, ToastProc, SubclassId, &existing)) return;
    Options options;
    if (!ReadOptions(options)) return;
    auto state = new(std::nothrow) State;
    if (!state) return;
    state->window = window;
    try { Windows.push_back(state); } catch (...) { delete state; throw; }
    if (!SetWindowSubclass(window, ToastProc, SubclassId, reinterpret_cast<DWORD_PTR>(state))) { std::erase(Windows, state); delete state; Report(ERROR_INVALID_FUNCTION); return; }
    HMODULE pinned{};
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, reinterpret_cast<LPCWSTR>(&ToastProc), &pinned);
    if (!SetTimer(window, TimerId, 500, nullptr)) {
        RemoveWindowSubclass(window, ToastProc, SubclassId);
        std::erase(Windows, state); delete state; Report(ERROR_NOT_ENOUGH_MEMORY); return;
    }
    if (!SetPropW(window, NativeProperty, reinterpret_cast<HANDLE>(1))) {
        KillTimer(window, TimerId); RemoveWindowSubclass(window, ToastProc, SubclassId);
        std::erase(Windows, state); delete state; Report(GetLastError()); return;
    }
    PlaceNow(state, options);
}
LRESULT CALLBACK Hook(int code, WPARAM wparam, LPARAM lparam) try {
    if (code >= 0 && !Discovering && (LocalFixture || IsSystemHost())) {
        auto message = reinterpret_cast<CWPRETSTRUCT const*>(lparam);
        if (message->message == WM_CREATE || message->message == WM_SETTEXT || message->message == WM_NULL) {
            Discovering = true;
            Attach(message->hwnd);
            Discovering = false;
        }
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
catch (...) { Discovering = false; Report(ERROR_INVALID_DATA); return CallNextHookEx(nullptr, code, wparam, lparam); }
}
extern "C" __declspec(dllexport) HHOOK WINAPI InstallNotificationsHook(DWORD thread) {
    return SetWindowsHookExW(WH_CALLWNDPROCRET, Hook, Library, thread);
}
extern "C" __declspec(dllexport) HHOOK WINAPI InstallNotificationsFixtureHook(DWORD thread) {
    LocalFixture = true;
    return InstallNotificationsHook(thread);
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { Library = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
