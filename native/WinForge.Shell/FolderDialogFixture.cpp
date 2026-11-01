#include <windows.h>
#include <shlobj.h>
#include <cstdio>
#include <string>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\FolderDialog\\Runtime";
HWND Legacy{};
bool SawModern{}, TimedOut{};
int Mode{}, Ticks{}, Stage{};
std::wstring Target;
constexpr int MaximumTicks = 40;
constexpr int FolderNameControl = 1152;
void Active(DWORD value) {
    RegSetKeyValueW(HKEY_CURRENT_USER, RuntimeKey, L"active", REG_DWORD, &value, sizeof(value));
    auto heartbeat = GetTickCount64();
    RegSetKeyValueW(HKEY_CURRENT_USER, RuntimeKey, L"heartbeat", REG_QWORD, &heartbeat, sizeof(heartbeat));
}
BOOL CALLBACK FindModern(HWND window, LPARAM parameter) {
    DWORD process{};
    GetWindowThreadProcessId(window, &process);
    wchar_t name[64]{};
    GetClassNameW(window, name, ARRAYSIZE(name));
    if (process == GetCurrentProcessId() && window != Legacy && IsWindowVisible(window) && lstrcmpW(name, L"#32770") == 0)
        *reinterpret_cast<HWND*>(parameter) = window;
    return TRUE;
}
BOOL CALLBACK FindEdit(HWND window, LPARAM parameter) {
    if (GetDlgCtrlID(window) == FolderNameControl) *reinterpret_cast<HWND*>(parameter) = window;
    return TRUE;
}
void CALLBACK Tick(HWND, UINT, UINT_PTR, DWORD) {
    if (++Ticks > MaximumTicks) {
        TimedOut = true;
        Active(0);
        HWND modern{};
        EnumWindows(FindModern, reinterpret_cast<LPARAM>(&modern));
        if (modern) PostMessageW(modern, WM_CLOSE, 0, 0);
        if (Legacy) PostMessageW(Legacy, WM_COMMAND, IDCANCEL, 0);
        return;
    }
    if (Mode != 2) Active(1);
    HWND modern{};
    EnumWindows(FindModern, reinterpret_cast<LPARAM>(&modern));
    if (!modern) {
        if (Mode >= 2 && SawModern && IsWindowVisible(Legacy)) PostMessageW(Legacy, WM_COMMAND, IDCANCEL, 0);
        return;
    }
    SawModern = true;
    if (Mode == 1) { PostMessageW(modern, WM_COMMAND, IDCANCEL, 0); return; }
    if (Mode == 2) { Active(0); return; }
    HWND edit{};
    EnumChildWindows(modern, FindEdit, reinterpret_cast<LPARAM>(&edit));
    if (!Stage && edit) { SetWindowTextW(edit, Target.c_str()); Stage = 1; }
    if (Stage) PostMessageW(modern, WM_COMMAND, IDOK, 0);
}
int CALLBACK Browse(HWND window, UINT message, LPARAM, LPARAM) {
    if (message == BFFM_INITIALIZED) {
        Legacy = window;
    }
    if (Mode == 4 && message == BFFM_SELCHANGED) SendMessageW(window, BFFM_ENABLEOK, 0, FALSE);
    return 0;
}
}
int wmain(int count, wchar_t** arguments) {
    setvbuf(stdout, nullptr, _IONBF, 0);
    bool external = count == 3 && lstrcmpW(arguments[2], L"--external") == 0;
    if ((count != 2 && !external) || FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 2;
    DWORD active{}, bytes = sizeof(active);
    RegGetValueW(HKEY_CURRENT_USER, RuntimeKey, L"active", RRF_RT_REG_DWORD, nullptr, &active, &bytes);
    if (active && !external) { std::puts("Disable Modern Folder Dialog before running fixture."); return 2; }
    HHOOK hook{};
    if (!external) {
        auto library = LoadLibraryW(arguments[1]);
        if (!library) return 3;
        auto install = reinterpret_cast<HHOOK(WINAPI*)(DWORD)>(GetProcAddress(library, "InstallFolderDialogHook"));
        hook = install ? install(GetCurrentThreadId()) : nullptr;
        if (!hook) return 4;
    }
    wchar_t temporary[MAX_PATH]{};
    GetTempPathW(ARRAYSIZE(temporary), temporary);
    Target = std::wstring(temporary) + L"WinForge Folder Dialog Fixture";
    CreateDirectoryW(Target.c_str(), nullptr);
    int failures{};
    for (Mode = 0; Mode < (external ? 1 : 6); ++Mode) {
        Legacy = nullptr; SawModern = false; TimedOut = false; Ticks = 0; Stage = 0;
        Active(1);
        auto timer = SetTimer(nullptr, 0, 250, Tick);
        BROWSEINFOW info{};
        PIDLIST_ABSOLUTE root{};
        if (Mode == 3) SHParseDisplayName(Target.c_str(), nullptr, &root, 0, nullptr);
        info.pidlRoot = root;
        info.lpszTitle = L"WinForge folder replacement verification";
        info.ulFlags = BIF_RETURNONLYFSDIRS | (Mode == 5 ? 0 : BIF_NEWDIALOGSTYLE);
        info.lpfn = Browse;
        auto originalTarget = Target;
        if (Mode == 3) Target = temporary; // Outside the application's allowed root.
        auto selected = SHBrowseForFolderW(&info);
        KillTimer(nullptr, timer);
        wchar_t path[MAX_PATH]{};
        bool passed = SawModern && !TimedOut && (Mode == 0 ? selected && SHGetPathFromIDListW(selected, path) && Target == path : selected == nullptr);
        constexpr char const* Labels[] = { "selected path reaches SHBrowseForFolder caller", "modern cancellation cancels classic dialog", "deactivation restores classic dialog", "root restriction restores classic dialog", "initially disabled confirmation restores classic dialog", "unsupported old-style selection safely restores classic dialog" };
        std::printf("%s: %s\n", passed ? "PASS" : "FAIL", Labels[Mode]);
        if (!passed) { ++failures; std::printf("saw=%d timeout=%d selected=%p stage=%d ticks=%d\n", SawModern, TimedOut, selected, Stage, Ticks); }
        CoTaskMemFree(selected);
        CoTaskMemFree(root);
        Target = originalTarget;
    }
    Active(0);
    if (hook) UnhookWindowsHookEx(hook);
    RemoveDirectoryW(Target.c_str());
    CoUninitialize();
    return failures ? 1 : 0;
}
