#include <windows.h>
#include <commctrl.h>
#include <shlobj.h>
#include <wrl/client.h>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\FolderDialog\\Runtime";
constexpr wchar_t HandledProperty[] = L"WinForge.FolderDialog.Handled";
constexpr UINT_PTR SubclassId = 0x57464644;
constexpr ULONGLONG LeaseMilliseconds = 6000;
const UINT OpenMessage = RegisterWindowMessageW(L"WinForge.FolderDialog.Open.v1");
HMODULE Library{};
thread_local bool Opening{};
thread_local bool Discovering{};
thread_local IFileDialog* CurrentPicker{};

bool Enabled() {
    DWORD active{}, size = sizeof(active);
    if (RegGetValueW(HKEY_CURRENT_USER, RuntimeKey, L"active", RRF_RT_REG_DWORD, nullptr, &active, &size) != ERROR_SUCCESS || !active) return false;
    ULONGLONG heartbeat{};
    size = sizeof(heartbeat);
    return RegGetValueW(HKEY_CURRENT_USER, RuntimeKey, L"heartbeat", RRF_RT_REG_QWORD, nullptr, &heartbeat, &size) == ERROR_SUCCESS &&
        GetTickCount64() - heartbeat < LeaseMilliseconds;
}
bool ClassIs(HWND window, wchar_t const* expected) {
    wchar_t name[128]{};
    GetClassNameW(window, name, ARRAYSIZE(name));
    return lstrcmpW(name, expected) == 0;
}
HWND FindTree(HWND dialog) {
    auto container = FindWindowExW(dialog, nullptr, L"SHBrowseForFolder ShellNameSpace Control", nullptr);
    if (!container) return nullptr;
    return FindWindowExW(container, nullptr, WC_TREEVIEWW, nullptr);
}
void CALLBACK CheckLease(HWND, UINT, UINT_PTR, DWORD) {
    if (CurrentPicker && !Enabled()) {
        Microsoft::WRL::ComPtr<IOleWindow> window;
        HWND handle{};
        if (SUCCEEDED(CurrentPicker->QueryInterface(IID_PPV_ARGS(&window))) && SUCCEEDED(window->GetWindow(&handle)))
            PostMessageW(handle, WM_CLOSE, 0, 0);
    }
}

LRESULT CALLBACK DialogProc(HWND dialog, UINT message, WPARAM wparam, LPARAM lparam, UINT_PTR, DWORD_PTR data) {
    auto picker = reinterpret_cast<IFileDialog*>(data);
    if (message == OpenMessage && !Opening && Enabled()) {
        Opening = true;
        Microsoft::WRL::ComPtr<IFileOpenDialog> modern;
        auto initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        auto result = FAILED(initialized) ? initialized : CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&modern));
        bool cancelled = false, accepted = false;
        if (SUCCEEDED(result)) {
            FILEOPENDIALOGOPTIONS options{};
            result = modern->GetOptions(&options);
            if (SUCCEEDED(result)) result = modern->SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            wchar_t title[512]{};
            GetWindowTextW(dialog, title, ARRAYSIZE(title));
            if (title[0]) modern->SetTitle(title);
            if (SUCCEEDED(result)) {
                SetWindowSubclass(dialog, DialogProc, SubclassId, reinterpret_cast<DWORD_PTR>(modern.Get()));
                CurrentPicker = modern.Get();
                auto timer = SetTimer(nullptr, 0, 500, CheckLease);
                ShowWindow(dialog, SW_HIDE);
                result = modern->Show(dialog);
                KillTimer(nullptr, timer);
                CurrentPicker = nullptr;
                SetWindowSubclass(dialog, DialogProc, SubclassId, 0);
                cancelled = result == HRESULT_FROM_WIN32(ERROR_CANCELLED) && Enabled();
                Microsoft::WRL::ComPtr<IShellItem> item;
                PWSTR path{};
                if (SUCCEEDED(result) && Enabled() && SUCCEEDED(modern->GetResult(&item)) && SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path))) {
                    auto tree = FindTree(dialog);
                    auto previous = tree ? TreeView_GetSelection(tree) : nullptr;
                    // Clear the previous choice so a rejected path cannot accept an old folder.
                    if (tree) TreeView_SelectItem(tree, nullptr);
                    EnableWindow(GetDlgItem(dialog, IDOK), FALSE);
                    SendMessageW(dialog, BFFM_SETSELECTIONW, TRUE, reinterpret_cast<LPARAM>(path));
                    accepted = tree && TreeView_GetSelection(tree) && IsWindowEnabled(GetDlgItem(dialog, IDOK));
                    if (!accepted && tree) TreeView_SelectItem(tree, previous);
                    CoTaskMemFree(path);
                }
            }
        }
        modern.Reset();
        if (SUCCEEDED(initialized)) CoUninitialize();
        Opening = false;
        if (IsWindow(dialog)) {
            if (accepted || cancelled) PostMessageW(dialog, WM_COMMAND, accepted ? IDOK : IDCANCEL, 0);
            else ShowWindow(dialog, SW_SHOW);
        }
        return 0;
    }
    if (message == WM_NCDESTROY) {
        if (picker) picker->Close(HRESULT_FROM_WIN32(ERROR_CANCELLED));
        RemovePropW(dialog, HandledProperty);
        RemoveWindowSubclass(dialog, DialogProc, SubclassId);
    }
    return DefSubclassProc(dialog, message, wparam, lparam);
}

LRESULT CALLBACK Hook(int code, WPARAM wparam, LPARAM lparam) {
    if (code >= 0 && !Opening && !Discovering && Enabled()) {
        Discovering = true;
        auto event = reinterpret_cast<CWPSTRUCT const*>(lparam);
        auto dialog = event->hwnd;
        if (ClassIs(dialog, L"#32770") && IsWindowVisible(dialog) && !GetPropW(dialog, HandledProperty) &&
            GetDlgItem(dialog, IDOK) && GetDlgItem(dialog, IDCANCEL) && FindTree(dialog)) {
            if (SetWindowSubclass(dialog, DialogProc, SubclassId, 0)) {
                HMODULE pinned{};
                GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                    reinterpret_cast<LPCWSTR>(&DialogProc), &pinned);
                SetPropW(dialog, HandledProperty, reinterpret_cast<HANDLE>(1));
                PostMessageW(dialog, OpenMessage, 0, 0);
            }
        }
        Discovering = false;
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
}
extern "C" __declspec(dllexport) HHOOK WINAPI InstallFolderDialogHook(DWORD thread) {
    return SetWindowsHookExW(WH_CALLWNDPROC, Hook, Library, thread);
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { Library = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
