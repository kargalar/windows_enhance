#include <windows.h>
#include <commctrl.h>
#include <shlobj.h>
#include <wrl/client.h>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\FolderDialog\\Runtime";
constexpr wchar_t HandledProperty[] = L"WinForge.FolderDialog.Handled";
constexpr wchar_t PendingProperty[] = L"WinForge.FolderDialog.Pending";
constexpr wchar_t RejectedProperty[] = L"WinForge.FolderDialog.Rejected";
constexpr wchar_t PreviousProperty[] = L"WinForge.FolderDialog.Previous";
constexpr UINT_PTR SubclassId = 0x57464644;
constexpr UINT_PTR ConfirmTimer = SubclassId;
constexpr int LegacyTreeControl = 0x3741;
constexpr ULONGLONG LeaseMilliseconds = 6000;
constexpr UINT LeaseCheckMilliseconds = 500;
constexpr UINT ConfirmationDelayMilliseconds = 50;
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
    if (!container) {
        auto tree = GetDlgItem(dialog, LegacyTreeControl);
        return ClassIs(tree, WC_TREEVIEWW) ? tree : nullptr;
    }
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
    if (GetPropW(dialog, PendingProperty) && message == BFFM_ENABLEOK && !lparam)
        SetPropW(dialog, RejectedProperty, reinterpret_cast<HANDLE>(1));
    if (message == WM_TIMER && wparam == ConfirmTimer) {
        KillTimer(dialog, ConfirmTimer);
        auto accepted = Enabled() && !GetPropW(dialog, RejectedProperty) && IsWindowEnabled(GetDlgItem(dialog, IDOK));
        auto previous = reinterpret_cast<HTREEITEM>(RemovePropW(dialog, PreviousProperty));
        RemovePropW(dialog, PendingProperty);
        RemovePropW(dialog, RejectedProperty);
        if (accepted) PostMessageW(dialog, WM_COMMAND, IDOK, 0);
        else {
            auto tree = FindTree(dialog);
            if (tree) TreeView_SelectItem(tree, previous);
            ShowWindow(dialog, SW_SHOW);
        }
        return 0;
    }
    if (message == OpenMessage && !Opening && Enabled()) {
        auto initiallyAllowed = IsWindowEnabled(GetDlgItem(dialog, IDOK));
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
                auto timer = SetTimer(nullptr, 0, LeaseCheckMilliseconds, CheckLease);
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
                    SetPropW(dialog, PendingProperty, reinterpret_cast<HANDLE>(1));
                    SetPropW(dialog, PreviousProperty, reinterpret_cast<HANDLE>(previous));
                    PIDLIST_ABSOLUTE selectedPidl{};
                    if (SUCCEEDED(SHParseDisplayName(path, nullptr, &selectedPidl, 0, nullptr))) {
                        SendMessageW(dialog, BFFM_SETSELECTIONW, FALSE, reinterpret_cast<LPARAM>(selectedPidl));
                        CoTaskMemFree(selectedPidl);
                    }
                    accepted = initiallyAllowed && tree && TreeView_GetSelection(tree);
                    if (!accepted) {
                        RemovePropW(dialog, PendingProperty);
                        RemovePropW(dialog, PreviousProperty);
                        RemovePropW(dialog, RejectedProperty);
                        if (tree) TreeView_SelectItem(tree, previous);
                    }
                    CoTaskMemFree(path);
                }
            }
        }
        modern.Reset();
        if (SUCCEEDED(initialized)) CoUninitialize();
        Opening = false;
        if (IsWindow(dialog)) {
            // Let the original shell process queued selection callbacks before checking validation.
            if (accepted) {
                if (!SetTimer(dialog, ConfirmTimer, ConfirmationDelayMilliseconds, nullptr)) ShowWindow(dialog, SW_SHOW);
            }
            else if (cancelled) PostMessageW(dialog, WM_COMMAND, IDCANCEL, 0);
            else ShowWindow(dialog, SW_SHOW);
        }
        return 0;
    }
    if (message == WM_NCDESTROY) {
        if (picker) picker->Close(HRESULT_FROM_WIN32(ERROR_CANCELLED));
        RemovePropW(dialog, HandledProperty);
        RemovePropW(dialog, PendingProperty);
        RemovePropW(dialog, RejectedProperty);
        RemovePropW(dialog, PreviousProperty);
        RemoveWindowSubclass(dialog, DialogProc, SubclassId);
    }
    return DefSubclassProc(dialog, message, wparam, lparam);
}

LRESULT CALLBACK Hook(int code, WPARAM wparam, LPARAM lparam) {
    if (code >= 0 && wparam == PM_REMOVE && !Opening && !Discovering && Enabled()) {
        Discovering = true;
        auto event = reinterpret_cast<MSG const*>(lparam);
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
    return SetWindowsHookExW(WH_GETMESSAGE, Hook, Library, thread);
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { Library = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
