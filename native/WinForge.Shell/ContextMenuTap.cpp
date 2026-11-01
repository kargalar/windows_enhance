#include "ContextMenuEngine.h"
#include <memory>
#include <commctrl.h>
#include <shlobj.h>
#include <exdisp.h>
#include <shlguid.h>
#include <wrl/client.h>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\ContextMenu\\Runtime";
constexpr wchar_t CatalogKey[] = L"Software\\WinForge\\ContextMenu\\Catalog";
HMODULE Library{};
thread_local bool Processing{};
thread_local std::map<HMENU, std::unique_ptr<WinForgeMenu::Transaction>> Transactions;
constexpr UINT_PTR ClipboardTimer = 0x57464D43;
struct ClipboardRequest { std::vector<std::wstring> paths; DWORD sequence{}; ULONGLONG expires{}; };
thread_local ClipboardRequest CopySelection, PendingCopy;
thread_local bool SelectedCopyPath{};
bool RewriteCopiedPaths(HWND owner) {
    if (GetClipboardSequenceNumber() == PendingCopy.sequence || !OpenClipboard(owner)) return false;
    bool finished = false;
    auto handle = GetClipboardData(CF_UNICODETEXT);
    auto bytes = handle ? GlobalSize(handle) : 0;
    if (bytes && bytes <= 4 * 1024 * 1024) {
        auto data = static_cast<wchar_t const*>(GlobalLock(handle));
        if (data) {
            auto length = wcsnlen_s(data, bytes / sizeof(wchar_t));
            auto plain = WinForgeMenu::PlainCopiedPaths(std::wstring(data, length), PendingCopy.paths);
            GlobalUnlock(handle);
            if (!plain.empty()) {
                auto output = GlobalAlloc(GMEM_MOVEABLE, (plain.size() + 1) * sizeof(wchar_t));
                if (output) {
                    auto destination = GlobalLock(output);
                    if (destination) {
                        memcpy(destination, plain.c_str(), (plain.size() + 1) * sizeof(wchar_t)); GlobalUnlock(output);
                        if (EmptyClipboard() && SetClipboardData(CF_UNICODETEXT, output)) { output = nullptr; finished = true; }
                    }
                    if (output) GlobalFree(output);
                }
            }
        }
    }
    CloseClipboard(); return finished;
}
LRESULT CALLBACK GroupOwner(HWND window, UINT message, WPARAM wparam, LPARAM lparam, UINT_PTR id, DWORD_PTR) {
    if (message == WM_MENUSELECT && !(HIWORD(wparam) == 0xFFFF && !lparam)) {
        SelectedCopyPath = false;
        if (!(HIWORD(wparam) & (MF_POPUP | MF_SEPARATOR | MF_DISABLED | MF_GRAYED)))
            for (auto const& transaction : Transactions) if (transaction.second->IsCopyPath(LOWORD(wparam))) SelectedCopyPath = true;
    }
    if (message == WM_CANCELMODE) SelectedCopyPath = false;
    if (message == WM_EXITMENULOOP && SelectedCopyPath && !CopySelection.paths.empty()) {
        PendingCopy = CopySelection; PendingCopy.expires = GetTickCount64() + 2000;
        SetTimer(window, ClipboardTimer, 15, nullptr); SelectedCopyPath = false;
    }
    if (message == WM_TIMER && wparam == ClipboardTimer) {
        if (RewriteCopiedPaths(window) || GetTickCount64() >= PendingCopy.expires) {
            KillTimer(window, ClipboardTimer); PendingCopy = {};
            if (Transactions.empty()) RemoveWindowSubclass(window, GroupOwner, id);
        }
        return 0;
    }
    for (auto const& transaction : Transactions)
        if (transaction.second->HandleGroupMessage(window, message, lparam)) return TRUE;
    if (message == WM_NCDESTROY) { KillTimer(window, ClipboardTimer); PendingCopy = {}; RemoveWindowSubclass(window, GroupOwner, id); }
    return DefSubclassProc(window, message, wparam, lparam);
}
unsigned MenuContext(HWND owner, std::vector<WinForgeMenu::Item> const& items, std::vector<std::wstring>& paths) {
    using Microsoft::WRL::ComPtr;
    auto root = GetAncestor(owner, GA_ROOT);
    wchar_t type[128]{}; GetClassNameW(root, type, 128);
    bool desktop = wcscmp(type, L"Progman") == 0 || wcscmp(type, L"WorkerW") == 0;
    // Refresh belongs to the background menu even if the view retains a selection.
    if (std::any_of(items.begin(), items.end(), [](auto const& item) { return WinForgeMenu::Canonical(item.text) == L"builtin:refresh"; })) return desktop ? 8u : 4u;
    ComPtr<IShellWindows> windows;
    if (FAILED(CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&windows)))) return 0;
    ComPtr<IDispatch> dispatch;
    if (desktop) {
        VARIANT location{}; long handle{};
        windows->FindWindowSW(&location, &location, SWC_DESKTOP, &handle, SWFO_NEEDDISPATCH, &dispatch);
    } else {
        long count{}; windows->get_Count(&count);
        for (long i = 0; i < count; ++i) {
            VARIANT index{}; index.vt = VT_I4; index.lVal = i;
            ComPtr<IDispatch> candidate; windows->Item(index, &candidate);
            ComPtr<IWebBrowserApp> app;
            if (!candidate || FAILED(candidate.As(&app))) continue;
            SHANDLE_PTR handle{}; app->get_HWND(&handle);
            if (reinterpret_cast<HWND>(handle) == root) { dispatch = candidate; break; }
        }
    }
    ComPtr<IServiceProvider> services; ComPtr<IShellBrowser> browser; ComPtr<IShellView> view; ComPtr<IFolderView2> folder;
    if (!dispatch || FAILED(dispatch.As(&services)) || FAILED(services->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser))) ||
        FAILED(browser->QueryActiveShellView(&view)) || FAILED(view.As(&folder))) return 0;
    ComPtr<IShellItemArray> selection;
    if (FAILED(folder->GetSelection(FALSE, &selection))) return 0;
    DWORD count{}; selection->GetCount(&count);
    if (!count) return desktop ? 8u : 4u;
    unsigned context{};
    for (DWORD i = 0; i < count; ++i) {
        ComPtr<IShellItem> item; if (FAILED(selection->GetItemAt(i, &item))) continue;
        SFGAOF attributes{}; if (FAILED(item->GetAttributes(SFGAO_FOLDER | SFGAO_LINK | SFGAO_FILESYSTEM, &attributes))) continue;
        std::wstring selectedPath;
        PWSTR path{};
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path))) {
            selectedPath = path;
            CoTaskMemFree(path);
        }
        paths.push_back(selectedPath);
        context |= WinForgeMenu::SelectionScope(selectedPath, (attributes & SFGAO_FOLDER) != 0, (attributes & SFGAO_LINK) != 0, (attributes & SFGAO_FILESYSTEM) != 0);
    }
    return context;
}
std::wstring Text(HKEY key, wchar_t const* name) {
    wchar_t buffer[4096]{}; DWORD bytes = sizeof(buffer);
    return RegGetValueW(key, nullptr, name, RRF_RT_REG_SZ, nullptr, buffer, &bytes) == ERROR_SUCCESS ? buffer : L"";
}
DWORD Number(HKEY key, wchar_t const* name, DWORD fallback) {
    DWORD value{}, bytes = sizeof(value); return RegGetValueW(key, nullptr, name, RRF_RT_REG_DWORD, nullptr, &value, &bytes) == ERROR_SUCCESS ? value : fallback;
}
bool ReadRules(std::vector<WinForgeMenu::Rule>& rules, std::wstring& directory, WinForgeMenu::Options& options) {
    HKEY runtime{}; if (RegOpenKeyExW(HKEY_CURRENT_USER, RuntimeKey, 0, KEY_READ, &runtime) != ERROR_SUCCESS) return false;
    ULONGLONG heartbeat{}; DWORD bytes = sizeof(heartbeat);
    bool active = Number(runtime, L"active", 0) && RegGetValueW(runtime, nullptr, L"heartbeat", RRF_RT_REG_QWORD, nullptr, &heartbeat, &bytes) == ERROR_SUCCESS && GetTickCount64() - heartbeat < 6000;
    if (active) {
        options.cleanSeparators = Number(runtime, L"clean-separators", 1) != 0;
        options.showSeparators = Number(runtime, L"show-separators", 1) != 0;
        options.unquotedCopyPath = Number(runtime, L"unquoted-copy-path", 1) != 0;
        directory = Text(runtime, L"icon-directory"); HKEY root{};
        if (RegOpenKeyExW(runtime, L"Rules", 0, KEY_READ, &root) == ERROR_SUCCESS) {
            for (DWORD i = 0; i < 1024; ++i) {
                wchar_t name[64]{}; DWORD length = static_cast<DWORD>(std::size(name));
                if (RegEnumKeyExW(root, i, name, &length, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS) break;
                HKEY key{}; if (RegOpenKeyExW(root, name, 0, KEY_READ, &key) != ERROR_SUCCESS) continue;
                WinForgeMenu::Rule rule; rule.match = Text(key, L"match"); rule.name = Text(key, L"name"); rule.group = Text(key, L"group"); rule.icon = Text(key, L"icon"); rule.visible = Number(key, L"visible", 1) != 0; rule.groupIcon = Text(key, L"group-icon");
                rule.contexts = 0; unsigned bit = 1;
                for (auto scope : {L"file", L"folder", L"background", L"desktop", L"drive", L"shortcut", L"application", L"virtual"}) {
                    auto fallback = bit == 32 || bit == 64 ? Number(key, L"visible-file", rule.visible ? 1 : 0) : rule.visible ? 1u : 0u;
                    if (Number(key, (std::wstring(L"visible-") + scope).c_str(), fallback)) rule.contexts |= bit;
                    bit <<= 1;
                }
                rule.separatorBefore = Number(key, L"separator-before", 0) != 0; rule.separatorAfter = Number(key, L"separator-after", 0) != 0;
                rules.push_back(rule); RegCloseKey(key);
            }
            RegCloseKey(root);
        }
    }
    RegCloseKey(runtime); return active;
}
void WriteText(HKEY key, wchar_t const* name, std::wstring const& value) { RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<BYTE const*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))); }
std::wstring SaveBitmap(HBITMAP bitmap, std::wstring const& path) {
    if (reinterpret_cast<UINT_PTR>(bitmap) <= 12) return {};
    BITMAP source{}; if (!GetObjectW(bitmap, sizeof(source), &source) || source.bmWidth < 1 || source.bmHeight < 1 || source.bmWidth > 128 || source.bmHeight > 128) return {};
    BITMAPINFO info{}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER); info.bmiHeader.biWidth = source.bmWidth;
    info.bmiHeader.biHeight = -source.bmHeight; info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32; info.bmiHeader.biCompression = BI_RGB;
    auto bytes = static_cast<DWORD>(source.bmWidth * source.bmHeight * 4); std::vector<BYTE> pixels(bytes);
    auto dc = GetDC(nullptr); auto read = GetDIBits(dc, bitmap, 0, static_cast<UINT>(source.bmHeight), pixels.data(), &info, DIB_RGB_COLORS); ReleaseDC(nullptr, dc);
    if (!read) return {};
    bool alpha = false; for (size_t i = 3; i < pixels.size(); i += 4) if (pixels[i]) { alpha = true; break; }
    if (!alpha) for (size_t i = 3; i < pixels.size(); i += 4) pixels[i] = 255;
    BITMAPFILEHEADER header{}; header.bfType = 0x4D42; header.bfOffBits = sizeof(header) + sizeof(BITMAPINFOHEADER); header.bfSize = header.bfOffBits + bytes;
    auto file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr); if (file == INVALID_HANDLE_VALUE) return {};
    DWORD written{}; bool saved = WriteFile(file, &header, sizeof(header), &written, nullptr) && WriteFile(file, &info.bmiHeader, sizeof(info.bmiHeader), &written, nullptr) && WriteFile(file, pixels.data(), bytes, &written, nullptr);
    CloseHandle(file); return saved ? path : L"";
}
void Capture(std::vector<WinForgeMenu::Item> const& items, std::wstring const& directory) {
    for (auto const& item : items) {
        bool separator = (item.info.fType & MFT_SEPARATOR) != 0;
        auto canonical = separator ? WinForgeMenu::SeparatorIdentity(items, item) : WinForgeMenu::Canonical(item.text, item.parent);
        auto label = separator ? canonical : WinForgeMenu::Normalize(item.text); if (label.empty()) continue;
        unsigned long long hash = 14695981039346656037ull;
        for (auto character : label) { hash ^= static_cast<unsigned short>(character); hash *= 1099511628211ull; }
        wchar_t id[32]{}; swprintf_s(id, L"%016llX", hash);
        auto keyPath = std::wstring(CatalogKey) + L"\\" + id; HKEY key{};
        if (RegCreateKeyExW(HKEY_CURRENT_USER, keyPath.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) != ERROR_SUCCESS) continue;
        auto name = item.text;
        if (separator) {
            std::wstring previous, next;
            auto display = [](std::wstring text) { auto accelerator = text.find(L'\t'); if (accelerator != std::wstring::npos) text.resize(accelerator); return text; };
            for (auto const& neighbor : items) if (neighbor.menu == item.menu && !(neighbor.info.fType & MFT_SEPARATOR)) {
                if (neighbor.position < item.position) previous = display(neighbor.text);
                else if (neighbor.position > item.position) { next = display(neighbor.text); break; }
            }
            name = L"Ay\u0131r\u0131c\u0131: " + previous + L" / " + next;
        }
        name.erase(std::remove(name.begin(), name.end(), L'&'), name.end()); auto tab = name.find(L'\t'); if (tab != std::wstring::npos) name.resize(tab);
        WriteText(key, L"label", name); WriteText(key, L"canonical", canonical);
        if (!directory.empty() && reinterpret_cast<UINT_PTR>(item.info.hbmpItem) > 12) {
            auto path = SaveBitmap(item.info.hbmpItem, directory + L"\\" + id + L".bmp"); if (!path.empty()) WriteText(key, L"icon", path);
        }
        RegCloseKey(key);
    }
}
LRESULT CALLBACK Hook(int code, WPARAM wparam, LPARAM lparam) {
    if (code >= 0 && !Processing && lparam) {
        auto message = reinterpret_cast<CWPRETSTRUCT const*>(lparam);
        if (message->message == WM_INITMENUPOPUP && HIWORD(message->lParam) == 0) {
            Processing = true;
            try {
                auto menu = reinterpret_cast<HMENU>(message->wParam);
                bool nested = Transactions.contains(menu);
                for (auto const& transaction : Transactions) if (transaction.second->IsGroup(menu)) nested = true;
                std::vector<WinForgeMenu::Rule> rules; std::wstring directory; WinForgeMenu::Options options;
                if (!nested && ReadRules(rules, directory, options)) {
                    auto items = WinForgeMenu::ReadItems(message->hwnd, menu, true);
                    Capture(items, directory);
                    std::vector<std::wstring> paths;
                    auto context = MenuContext(message->hwnd, items, paths);
                    CopySelection = {options.unquotedCopyPath ? paths : std::vector<std::wstring>{}, GetClipboardSequenceNumber(), 0};
                    SelectedCopyPath = false;
                    SetWindowSubclass(message->hwnd, GroupOwner, reinterpret_cast<UINT_PTR>(GroupOwner), 0);
                    auto transaction = std::make_unique<WinForgeMenu::Transaction>(); transaction->Apply(menu, items, rules, context, options); Transactions[menu] = std::move(transaction);
                } else if (nested && ReadRules(rules, directory, options)) {
                    Capture(WinForgeMenu::ReadItems(message->hwnd, menu, false), directory);
                }
            } catch (...) { /* No exception may escape into Explorer. */ }
            Processing = false;
        } else if (message->message == WM_UNINITMENUPOPUP) {
            auto menu = reinterpret_cast<HMENU>(message->wParam); auto found = Transactions.find(menu);
            if (found != Transactions.end()) { Processing = true; found->second->Restore(); Transactions.erase(found); Processing = false; }
        } else if (message->message == WM_EXITMENULOOP) {
            Processing = true; for (auto& entry : Transactions) entry.second->Restore(); Transactions.clear(); if (!PendingCopy.expires) RemoveWindowSubclass(message->hwnd, GroupOwner, reinterpret_cast<UINT_PTR>(GroupOwner)); Processing = false;
        }
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
}
extern "C" __declspec(dllexport) HHOOK WINAPI InstallContextMenuHook(DWORD thread) { return SetWindowsHookExW(WH_CALLWNDPROCRET, Hook, Library, thread); }
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) Library = instance; return TRUE; }
