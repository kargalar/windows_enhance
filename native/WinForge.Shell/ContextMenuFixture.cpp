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
void SeparatorAndPathChecks() {
    auto menu = CreatePopupMenu();
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, 11, L"Copy");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, 12, L"Delete");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, 13, L"Properties");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    auto original = WinForgeMenu::ReadItems(nullptr, menu, false);
    {
        WinForgeMenu::Transaction transaction;
        transaction.Apply(menu, original, {{L"builtin:delete", L"", L"", L"", false}});
        Check(GetMenuItemCount(menu) == 3 && GetMenuItemID(menu, 0) == 11 && GetMenuItemID(menu, 2) == 13,
            "Hiding the only command in a section removes leading, trailing and duplicate separators");
        transaction.Restore();
        auto restored = WinForgeMenu::ReadItems(nullptr, menu, false);
        Check(restored.size() == original.size(), "Separator cleanup restores the complete native menu");
        for (size_t i = 0; i < original.size(); ++i)
            Check(original[i].info.fType == restored[i].info.fType && original[i].info.wID == restored[i].info.wID && original[i].text == restored[i].text,
                "Original separator position and command identity are restored");
        WinForgeMenu::Options options; options.cleanSeparators = false;
        auto identity = WinForgeMenu::SeparatorIdentity(original, original[2]);
        transaction.Apply(menu, original, {{identity, L"", L"", L"", false, 0}}, 1, options);
        Check(GetMenuItemCount(menu) == 7 && GetMenuItemID(menu, 2) == 12, "A captured separator can be hidden independently");
        transaction.Restore();
        options.showSeparators = false;
        transaction.Apply(menu, original, {}, 1, options);
        Check(GetMenuItemCount(menu) == 3, "All original separators can be turned off without hiding commands");
        transaction.Restore();
        Check(GetMenuItemCount(menu) == 8, "Showing native separators again preserves their original layout");
    }
    DestroyMenu(menu);
    menu = CreatePopupMenu(); AppendMenuW(menu, MF_STRING, 11, L"Copy"); AppendMenuW(menu, MF_STRING, 13, L"Properties");
    {
        WinForgeMenu::Transaction transaction;
        WinForgeMenu::Rule copy{L"builtin:copy"}, properties{L"builtin:properties"}; copy.separatorAfter = true; properties.separatorBefore = true;
        transaction.Apply(menu, WinForgeMenu::ReadItems(nullptr, menu, false), {copy, properties});
        Check(GetMenuItemCount(menu) == 3 && (GetMenuState(menu, 1, MF_BYPOSITION) & MF_SEPARATOR), "New separators can be added at item boundaries without duplicates");
        transaction.Restore(); Check(GetMenuItemCount(menu) == 2, "Custom separators disappear on restoration");
        copy.contexts = 32;
        transaction.Apply(menu, WinForgeMenu::ReadItems(nullptr, menu, false), {copy}, 32);
        Check(GetMenuItemID(menu, 0) == 11, "Shortcut context can show an item independently of ordinary files"); transaction.Restore();
        transaction.Apply(menu, WinForgeMenu::ReadItems(nullptr, menu, false), {copy}, 1);
        Check(GetMenuItemCount(menu) == 1, "Ordinary-file context can hide an item shown on shortcuts"); transaction.Restore();
        transaction.Apply(menu, WinForgeMenu::ReadItems(nullptr, menu, false), {copy}, 33);
        Check(GetMenuItemCount(menu) == 1, "Mixed selections require visibility in every selected context"); transaction.Restore();
    }
    DestroyMenu(menu);
    Check(WinForgeMenu::SelectionScope(L"C:\\Folder\\File.LNK", false, false, true) == 32, "LNK files have a dedicated shortcut scope");
    Check(WinForgeMenu::SelectionScope(L"C:\\Folder\\File.url", false, true, true) == 32, "Internet shortcuts have the same dedicated scope");
    Check(WinForgeMenu::SelectionScope(L"C:\\Folder\\App.exe", false, false, true) == 64, "Executables have an application scope");
    Check(WinForgeMenu::SelectionScope(L"C:\\Folder\\script.ps1", false, false, true) == 64, "Scripts have an application scope");
    Check(WinForgeMenu::SelectionScope(L"C:\\Folder.lnk", true, false, true) == 2, "A folder with a shortcut suffix remains a folder");
    Check(WinForgeMenu::SelectionScope(L"", true, false, false) == 128, "Non-filesystem shell items have a virtual scope");
    Check(WinForgeMenu::SelectionScope(L"C:\\", true, false, true) == 16, "Drive roots retain their scope");
    auto path = std::wstring(L"C:\\Folder With Spaces\\test.txt");
    Check(WinForgeMenu::Canonical(L"Copy as path") == L"builtin:copy-path" && WinForgeMenu::Canonical(L"Yol olarak kopyala") == L"builtin:copy-path", "Copy as path has a stable English and Turkish identity");
    Check(WinForgeMenu::PlainCopiedPaths(L"\"" + path + L"\"", {path}) == path, "Copy as path returns an unquoted path while preserving spaces");
    Check(WinForgeMenu::PlainCopiedPaths(L"\"" + path + L"\"\r\n\"D:\\Other.lnk\"\r\n", {path, L"D:\\Other.lnk"}) == path + L"\r\nD:\\Other.lnk", "Multiple copied paths retain their line breaks and shortcut paths");
    Check(WinForgeMenu::PlainCopiedPaths(L"\"unrelated text\"", {path}).empty(), "Unrelated quoted clipboard text is never rewritten");
    Check(WinForgeMenu::PlainCopiedPaths(L"\"C:\\different.txt\"", {path}).empty(), "A path from a different selection is never rewritten");
    Check(WinForgeMenu::PlainCopiedPaths(L"\"" + path + L"\"", {path, L""}).empty(), "Incomplete virtual selections cannot rewrite the clipboard");
}
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
        SeparatorAndPathChecks();
        Check(WinForgeMenu::Canonical(L"Masa\u00fcst\u00fc k\u0131sayolu olu\u015ftur") == L"prepared:desktop-shortcut", "Prepared shortcut catalog identity remains stable");
        Check(WinForgeMenu::Canonical(L"Dosyay\u0131 / k\u0131sayolu gizle veya g\u00f6ster") == L"prepared:toggle-hidden", "Prepared visibility catalog identity remains stable");
        Check(WinForgeMenu::Canonical(L"Klas\u00f6r simgesini de\u011fi\u015ftir") == L"prepared:folder-icon", "Prepared folder icon catalog identity remains stable");

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
        MENUITEMINFOW header{}; header.cbSize = sizeof(header); header.fMask = MIIM_FTYPE | MIIM_DATA;
        Check(GetMenuItemInfoW(root, 2, TRUE, &header) && (header.fType & MFT_OWNERDRAW), "Group header uses theme-aware drawing");
        MEASUREITEMSTRUCT measure{}; measure.CtlType = ODT_MENU; measure.itemData = header.dwItemData;
        Check(transaction.HandleGroupMessage(GetDesktopWindow(), WM_MEASUREITEM, reinterpret_cast<LPARAM>(&measure)) && measure.itemWidth > 56 && measure.itemHeight >= 28, "Group header measures menu font and icon space");
        measure.itemData = 0;
        Check(!transaction.HandleGroupMessage(GetDesktopWindow(), WM_MEASUREITEM, reinterpret_cast<LPARAM>(&measure)), "Extension-owned drawing is left untouched");
        Check(WinForgeMenu::GroupTextColor(true, false) == RGB(255, 255, 255) && WinForgeMenu::GroupTextColor(true, true) == RGB(255, 255, 255), "Dark group text stays white both normally and on hover");
        Check(WinForgeMenu::GroupTextColor(false, false) == GetSysColor(COLOR_MENUTEXT), "Light and high contrast menus use system text color");
        auto screen = GetDC(nullptr), dc = CreateCompatibleDC(screen);
        auto canvas = CreateCompatibleBitmap(screen, 320, 40); auto previous = SelectObject(dc, canvas);
        DRAWITEMSTRUCT draw{}; draw.CtlType = ODT_MENU; draw.itemData = header.dwItemData; draw.hDC = dc; draw.rcItem = {0, 0, 320, 40};
        Check(transaction.HandleGroupMessage(GetDesktopWindow(), WM_DRAWITEM, reinterpret_cast<LPARAM>(&draw)), "Group header draws into a real device context");
        int textPixels{};
        for (int y = 0; y < 40; ++y) for (int x = 32; x < 150; ++x)
            if (GetPixel(dc, x, y) == WinForgeMenu::GroupTextColor(WinForgeMenu::DarkMenus(), false)) ++textPixels;
        Check(textPixels > 0, "Rendered group label contains readable theme text pixels");
        SelectObject(dc, previous); DeleteObject(canvas); DeleteDC(dc); ReleaseDC(nullptr, screen);

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
        std::vector<WinForgeMenu::Rule> scoped = {{L"builtin:properties", L"", L"", L"", true, 31u & ~8u}};
        transaction.Apply(root, WinForgeMenu::ReadItems(nullptr, root, false), scoped, 8);
        Check(GetMenuItemCount(root) == 3, "Desktop switch hides command only on desktop"); transaction.Restore();
        transaction.Apply(root, WinForgeMenu::ReadItems(nullptr, root, false), scoped, 2);
        Check(GetMenuItemCount(root) == 4, "Folder switch remains visible independently"); transaction.Restore();
        wchar_t temporary[MAX_PATH]{}, iconPath[MAX_PATH]{}; GetTempPathW(MAX_PATH, temporary);
        Check(GetTempFileNameW(temporary, L"WFI", 0, iconPath) != 0, "Temporary group icon path created");
        BITMAPFILEHEADER fileHeader{}; fileHeader.bfType = 0x4D42; fileHeader.bfOffBits = sizeof(fileHeader) + sizeof(BITMAPINFOHEADER); fileHeader.bfSize = fileHeader.bfOffBits + 4;
        BITMAPINFOHEADER bitmapHeader{}; bitmapHeader.biSize = sizeof(bitmapHeader); bitmapHeader.biWidth = 1; bitmapHeader.biHeight = 1; bitmapHeader.biPlanes = 1; bitmapHeader.biBitCount = 32; bitmapHeader.biCompression = BI_RGB;
        auto file = CreateFileW(iconPath, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        DWORD written{}, pixel = 0xFFFF0000;
        Check(file != INVALID_HANDLE_VALUE, "Group icon fixture opens");
        WriteFile(file, &fileHeader, sizeof(fileHeader), &written, nullptr); WriteFile(file, &bitmapHeader, sizeof(bitmapHeader), &written, nullptr); WriteFile(file, &pixel, sizeof(pixel), &written, nullptr); CloseHandle(file);
        WinForgeMenu::Rule iconRule{L"builtin:properties", L"", L"Icon group", L"", true}; iconRule.groupIcon = iconPath;
        transaction.Apply(root, WinForgeMenu::ReadItems(nullptr, root, false), {iconRule});
        header.fMask = MIIM_DATA; GetMenuItemInfoW(root, static_cast<UINT>(GetMenuItemCount(root) - 1), TRUE, &header);
        screen = GetDC(nullptr); dc = CreateCompatibleDC(screen); canvas = CreateCompatibleBitmap(screen, 320, 40); previous = SelectObject(dc, canvas);
        draw.itemData = header.dwItemData; draw.hDC = dc;
        Check(transaction.HandleGroupMessage(GetDesktopWindow(), WM_DRAWITEM, reinterpret_cast<LPARAM>(&draw)), "Group header with a custom bitmap draws");
        Check(GetPixel(dc, 15, 20) == RGB(255, 0, 0), "Custom group icon is loaded and alpha blended into the icon column");
        SelectObject(dc, previous); DeleteObject(canvas); DeleteDC(dc); ReleaseDC(nullptr, screen);
        transaction.Restore(); Check(DeleteFileW(iconPath) != FALSE, "Group icon fixture file released");
        DestroyMenu(root); root = nullptr;
        if (argc > 1 && wcscmp(argv[1], L"--shell") == 0) ShellChecks();
        std::printf("PASS: %d native context menu checks.\n", Checks); return 0;
    } catch (std::exception const& error) { std::fprintf(stderr, "FAIL: %s\n", error.what()); if (IsMenu(root)) DestroyMenu(root); return 1; }
}
