#include "ContextMenuEngine.h"
#include <cstdio>
#include <stdexcept>
#include <shlobj.h>
#include <wrl/client.h>

namespace {
int Checks{};
void Check(bool condition, char const* label) { if (!condition) throw std::runtime_error(label); ++Checks; }
std::wstring Label(HMENU menu, UINT pos) { wchar_t text[256]{}; GetMenuStringW(menu, pos, text, static_cast<int>(std::size(text)), MF_BYPOSITION); return text; }
Microsoft::WRL::ComPtr<IContextMenu3> Active3;
Microsoft::WRL::ComPtr<IContextMenu2> Active2;
LRESULT CALLBACK ShellOwner(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_INITMENUPOPUP || message == WM_DRAWITEM || message == WM_MEASUREITEM || message == WM_MENUCHAR) {
        LRESULT result{};
        if (Active3 && SUCCEEDED(Active3->HandleMenuMsg2(message, wparam, lparam, &result))) return result;
        if (Active2 && SUCCEEDED(Active2->HandleMenuMsg(message, wparam, lparam))) return 0;
    }
    return DefWindowProcW(window, message, wparam, lparam);
}
void ShellChecks() {
    Check(SUCCEEDED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)), "Shell COM initializes");
    wchar_t temporary[MAX_PATH]{}; GetTempPathW(MAX_PATH, temporary);
    auto path = std::wstring(temporary) + L"WinForge.ContextMenuFixture." + std::to_wstring(GetCurrentProcessId());
    Check(CreateDirectoryW(path.c_str(), nullptr) != FALSE, "Temporary shell test directory created");
    WNDCLASSW type{}; type.hInstance = GetModuleHandleW(nullptr); type.lpszClassName = L"WinForgeMenuFixture"; type.lpfnWndProc = ShellOwner; RegisterClassW(&type);
    auto owner = CreateWindowW(type.lpszClassName, L"Menu fixture", WS_OVERLAPPED, 0, 0, 400, 300, nullptr, nullptr, type.hInstance, nullptr);
    PIDLIST_ABSOLUTE pidl{};
    try {
        Check(SUCCEEDED(SHParseDisplayName(path.c_str(), nullptr, &pidl, 0, nullptr)), "Shell parses fixture directory");
        Microsoft::WRL::ComPtr<IShellFolder> parent; PCUITEMID_CHILD child{};
        Check(SUCCEEDED(SHBindToParent(pidl, IID_PPV_ARGS(&parent), &child)), "Shell binds parent folder");
        Microsoft::WRL::ComPtr<IShellFolder> folder;
        Check(SUCCEEDED(parent->BindToObject(child, nullptr, IID_PPV_ARGS(&folder))), "Shell binds directory background");
        for (bool background : {false, true}) {
            Microsoft::WRL::ComPtr<IContextMenu> context;
            auto hr = background ? folder->CreateViewObject(owner, IID_PPV_ARGS(&context)) : parent->GetUIObjectOf(owner, 1, &child, IID_IContextMenu, nullptr, reinterpret_cast<void**>(context.GetAddressOf()));
            Check(SUCCEEDED(hr), background ? "Real background context menu created" : "Real file-item context menu created");
            context.As(&Active3); context.As(&Active2);
            auto menu = CreatePopupMenu();
            Check(SUCCEEDED(context->QueryContextMenu(menu, 0, 1, 0x7FFF, CMF_NORMAL)), "Windows shell populates menu");
            auto items = WinForgeMenu::ReadItems(owner, menu, true);
            auto wanted = background ? L"builtin:new-folder" : L"builtin:properties";
            auto item = std::find_if(items.begin(), items.end(), [&](auto const& row) { return WinForgeMenu::Canonical(row.text, row.parent) == wanted; });
            Check(item != items.end(), background ? "Real New Folder command found" : "Real Properties command found");
            auto command = item->info.wID;
            WinForgeMenu::Transaction transaction;
            transaction.Apply(menu, items, {{wanted, L"", L"Di\x011fer", L"", true}});
            HMENU group{};
            for (int i = 0; i < GetMenuItemCount(menu); ++i) if (Label(menu, static_cast<UINT>(i)) == L"Di\x011fer") group = GetSubMenu(menu, i);
            Check(group && GetMenuItemID(group, 0) == command, "Real Windows command moves into group retaining invocation ID");
            transaction.Restore(); DestroyMenu(menu); Active3.Reset(); Active2.Reset();
        }
    } catch (...) { if (pidl) CoTaskMemFree(pidl); Active3.Reset(); Active2.Reset(); DestroyWindow(owner); RemoveDirectoryW(path.c_str()); CoUninitialize(); throw; }
    CoTaskMemFree(pidl); DestroyWindow(owner); Check(RemoveDirectoryW(path.c_str()) != FALSE, "Temporary directory removed"); CoUninitialize();
}
}
int wmain(int argc, wchar_t** argv) {
    HMENU root = CreatePopupMenu(), create = CreatePopupMenu();
    try {
        Check(WinForgeMenu::Canonical(L"Masa\u00fcst\u00fc k\u0131sayolu olu\u015ftur") == L"prepared:desktop-shortcut", "Prepared shortcut catalog identity remains stable");
        Check(WinForgeMenu::Canonical(L"Dosyay\u0131 / k\u0131sayolu gizle veya g\u00f6ster") == L"prepared:toggle-hidden", "Prepared visibility catalog identity remains stable");

        AppendMenuW(root, MF_STRING | MF_CHECKED, 101, L"&Properties");
        AppendMenuW(create, MF_STRING, 201, L"&Folder"); AppendMenuW(create, MF_STRING, 202, L"Shortcut");
        AppendMenuW(root, MF_POPUP, reinterpret_cast<UINT_PTR>(create), L"&New");
        AppendMenuW(root, MF_STRING | MF_GRAYED, 102, L"&Copy\tCtrl+C"); AppendMenuW(root, MF_STRING, 103, L"Delete");
        auto items = WinForgeMenu::ReadItems(nullptr, root, false);
        Check(items.size() == 6, "Recursive menu enumeration");
        Check(WinForgeMenu::Canonical(L"&Properties") == L"builtin:properties", "Properties canonical verb");
        Check(WinForgeMenu::Canonical(L"Klas\x00f6r", L"Yeni") == L"builtin:new-folder", "Turkish New Folder canonical verb");
        Check(WinForgeMenu::Canonical(L"Folder", L"Other").empty(), "Unrelated Folder labels are not mistaken for New Folder");
        std::vector<WinForgeMenu::Rule> rules = {
            {L"builtin:properties", L"", L"Other", L"", true}, {L"builtin:new-folder", L"", L"Other", L"", true},
            {L"builtin:copy", L"Copy renamed", L"", L"", true}, {L"builtin:delete", L"", L"", L"", false}
        };
        WinForgeMenu::Transaction transaction; transaction.Apply(root, items, rules);
        Check(GetMenuItemCount(root) == 3, "Original items hidden/moved from root");
        Check(Label(root, 2) == L"Other", "Group appears in actual HMENU");
        auto group = GetSubMenu(root, 2);
        Check(GetMenuItemCount(group) == 2, "Properties and New Folder share group");
        Check(GetMenuItemID(group, 0) == 101 && GetMenuItemID(group, 1) == 201, "Moved commands retain their original invocation IDs and order");
        Check((GetMenuState(group, 0, MF_BYPOSITION) & MF_CHECKED) != 0, "Moved item retains checked state");
        Check(GetMenuItemCount(create) == 1 && GetMenuItemID(create, 0) == 202, "New Folder removed from original New submenu; other commands preserved");
        Check(Label(root, 1) == L"Copy renamed", "Existing command display name can change");
        Check((GetMenuState(root, 1, MF_BYPOSITION) & MF_GRAYED) != 0, "Renamed command retains disabled state");
        transaction.Restore();
        Check(GetMenuItemCount(root) == 4 && GetMenuItemID(root, 0) == 101 && GetMenuItemID(root, 3) == 103, "Root order and hidden commands restored on close");
        Check(GetMenuItemCount(create) == 2 && GetMenuItemID(create, 0) == 201, "Promoted child restored to original submenu");
        Check(Label(root, 2) == L"&Copy\tCtrl+C", "Original title and accelerator restored");
        Check(!IsMenu(group), "Temporary groups released without destroying original submenu");
        { WinForgeMenu::Transaction temporary; temporary.Apply(root, WinForgeMenu::ReadItems(nullptr, root, false), rules); }
        Check(GetMenuItemCount(root) == 4 && GetMenuItemCount(create) == 2, "Transaction cleanup also restores on scope exit");
        DestroyMenu(root); root = nullptr;
        if (argc > 1 && wcscmp(argv[1], L"--shell") == 0) ShellChecks();
        std::printf("PASS: %d native context menu checks.\n", Checks); return 0;
    } catch (std::exception const& error) { std::fprintf(stderr, "FAIL: %s\n", error.what()); if (IsMenu(root)) DestroyMenu(root); return 1; }
}
