#include "ContextMenuEngine.h"
#include <memory>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\ContextMenu\\Runtime";
constexpr wchar_t CatalogKey[] = L"Software\\WinForge\\ContextMenu\\Catalog";
HMODULE Library{};
thread_local bool Processing{};
thread_local std::map<HMENU, std::unique_ptr<WinForgeMenu::Transaction>> Transactions;
std::wstring Text(HKEY key, wchar_t const* name) {
    wchar_t buffer[4096]{}; DWORD bytes = sizeof(buffer);
    return RegGetValueW(key, nullptr, name, RRF_RT_REG_SZ, nullptr, buffer, &bytes) == ERROR_SUCCESS ? buffer : L"";
}
DWORD Number(HKEY key, wchar_t const* name, DWORD fallback) {
    DWORD value{}, bytes = sizeof(value); return RegGetValueW(key, nullptr, name, RRF_RT_REG_DWORD, nullptr, &value, &bytes) == ERROR_SUCCESS ? value : fallback;
}
bool ReadRules(std::vector<WinForgeMenu::Rule>& rules, std::wstring& directory) {
    HKEY runtime{}; if (RegOpenKeyExW(HKEY_CURRENT_USER, RuntimeKey, 0, KEY_READ, &runtime) != ERROR_SUCCESS) return false;
    ULONGLONG heartbeat{}; DWORD bytes = sizeof(heartbeat);
    bool active = Number(runtime, L"active", 0) && RegGetValueW(runtime, nullptr, L"heartbeat", RRF_RT_REG_QWORD, nullptr, &heartbeat, &bytes) == ERROR_SUCCESS && GetTickCount64() - heartbeat < 6000;
    if (active) {
        directory = Text(runtime, L"icon-directory"); HKEY root{};
        if (RegOpenKeyExW(runtime, L"Rules", 0, KEY_READ, &root) == ERROR_SUCCESS) {
            for (DWORD i = 0; i < 1024; ++i) {
                wchar_t name[64]{}; DWORD length = static_cast<DWORD>(std::size(name));
                if (RegEnumKeyExW(root, i, name, &length, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS) break;
                HKEY key{}; if (RegOpenKeyExW(root, name, 0, KEY_READ, &key) != ERROR_SUCCESS) continue;
                WinForgeMenu::Rule rule; rule.match = Text(key, L"match"); rule.name = Text(key, L"name"); rule.group = Text(key, L"group"); rule.icon = Text(key, L"icon"); rule.visible = Number(key, L"visible", 1) != 0;
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
        auto label = WinForgeMenu::Normalize(item.text); if (label.empty() || (item.info.fType & MFT_SEPARATOR)) continue;
        unsigned long long hash = 14695981039346656037ull;
        for (auto character : label) { hash ^= static_cast<unsigned short>(character); hash *= 1099511628211ull; }
        wchar_t id[32]{}; swprintf_s(id, L"%016llX", hash);
        auto keyPath = std::wstring(CatalogKey) + L"\\" + id; HKEY key{};
        if (RegCreateKeyExW(HKEY_CURRENT_USER, keyPath.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) != ERROR_SUCCESS) continue;
        auto name = item.text; name.erase(std::remove(name.begin(), name.end(), L'&'), name.end()); auto tab = name.find(L'\t'); if (tab != std::wstring::npos) name.resize(tab);
        WriteText(key, L"label", name); WriteText(key, L"canonical", WinForgeMenu::Canonical(item.text, item.parent));
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
                std::vector<WinForgeMenu::Rule> rules; std::wstring directory;
                if (!nested && ReadRules(rules, directory)) {
                    auto items = WinForgeMenu::ReadItems(message->hwnd, menu, true);
                    Capture(items, directory);
                    auto transaction = std::make_unique<WinForgeMenu::Transaction>(); transaction->Apply(menu, items, rules); Transactions[menu] = std::move(transaction);
                } else if (nested && ReadRules(rules, directory)) {
                    Capture(WinForgeMenu::ReadItems(message->hwnd, menu, false), directory);
                }
            } catch (...) { /* No exception may escape into Explorer. */ }
            Processing = false;
        } else if (message->message == WM_UNINITMENUPOPUP) {
            auto menu = reinterpret_cast<HMENU>(message->wParam); auto found = Transactions.find(menu);
            if (found != Transactions.end()) { Processing = true; found->second->Restore(); Transactions.erase(found); Processing = false; }
        } else if (message->message == WM_EXITMENULOOP) {
            Processing = true; for (auto& entry : Transactions) entry.second->Restore(); Transactions.clear(); Processing = false;
        }
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
}
extern "C" __declspec(dllexport) HHOOK WINAPI InstallContextMenuHook(DWORD thread) { return SetWindowsHookExW(WH_CALLWNDPROCRET, Hook, Library, thread); }
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) Library = instance; return TRUE; }
