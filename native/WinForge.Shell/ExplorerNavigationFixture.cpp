#include <windows.h>
#include <commctrl.h>
#include <cstdio>
#include <stdexcept>
#include <vector>
#include <string>
#include <chrono>
#include <thread>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\ExplorerNavigation\\Fixture";
struct SavedValue { std::wstring name; DWORD kind{}; std::vector<BYTE> bytes; bool exists{}; };
struct RegistryScope {
    HKEY key{};
    std::vector<SavedValue> values;
    RegistryScope() {
        if (RegCreateKeyExW(HKEY_CURRENT_USER, RuntimeKey, 0, nullptr, 0, KEY_READ | KEY_WRITE, nullptr, &key, nullptr)) throw std::runtime_error("Runtime key unavailable.");
        DWORD active{}, bytes = sizeof(active);
        if (RegGetValueW(key, nullptr, L"active", RRF_RT_REG_DWORD, nullptr, &active, &bytes) == ERROR_SUCCESS && active) {
            RegCloseKey(key); key = nullptr;
            throw std::runtime_error("Disable the navigation module before running its fixture.");
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
void Pump(DWORD milliseconds) {
    auto until = GetTickCount64() + milliseconds;
    do {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    } while (GetTickCount64() < until);
}
void Require(bool valid, char const* message) { if (!valid) throw std::runtime_error(message); }
LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) { return DefWindowProcW(window, message, wparam, lparam); }
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    try {
        RegistryScope registry;
        registry.Number(L"active", 1); registry.Heartbeat(GetTickCount64());
        registry.Number(L"revision", 1); registry.Number(L"font-size", 17); registry.Number(L"padding-vertical", 9);
        registry.Number(L"indent", 31); registry.Number(L"background", RGB(12,34,56));
        registry.Number(L"text", RGB(220,210,200));
        WNDCLASSW wc{}; wc.lpfnWndProc = WindowProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"NamespaceTreeControl";
        Require(RegisterClassW(&wc) != 0, "Namespace fixture registration failed.");
        INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_TREEVIEW_CLASSES}; InitCommonControlsEx(&controls);
        auto window = CreateWindowW(wc.lpszClassName, L"WinForge navigation fixture", WS_OVERLAPPEDWINDOW, 0, 0, 400, 400, nullptr, nullptr, wc.hInstance, nullptr);
        auto tree = CreateWindowW(WC_TREEVIEWW, L"", WS_CHILD | WS_VISIBLE | TVS_HASBUTTONS | TVS_LINESATROOT, 0, 0, 300, 300, window, nullptr, wc.hInstance, nullptr);
        Require(tree != nullptr, "Tree fixture creation failed.");
        auto originalHeight = TreeView_GetItemHeight(tree);
        auto originalIndent = TreeView_GetIndent(tree);
        auto originalBackground = TreeView_GetBkColor(tree);
        auto originalFont = SendMessageW(tree, WM_GETFONT, 0, 0);
        TVINSERTSTRUCTW insert{}; insert.hParent = TVI_ROOT; insert.hInsertAfter = TVI_LAST; insert.item.mask = TVIF_TEXT; wchar_t label[] = L"Padding and font color"; insert.item.pszText = label;
        auto root = TreeView_InsertItem(tree, &insert);
        insert.hParent = root;
        auto child = TreeView_InsertItem(tree, &insert);
        TreeView_Expand(tree, root, TVE_EXPAND);
        auto library = LoadLibraryW(argv[1]); Require(library != nullptr, "Navigation DLL load failed.");
        auto install = reinterpret_cast<HHOOK(WINAPI*)(DWORD)>(GetProcAddress(library, "InstallNavigationHook"));
        Require(install != nullptr, "Hook export missing.");
        auto hook = install(GetCurrentThreadId()); Require(hook != nullptr, "Hook installation failed.");
        SendMessageW(tree, WM_NULL, 0, 0);
        // This control did not exist when the hook was installed. Its completed
        // creation must already carry the style, without pumping or a timer tick.
        auto freshTree = CreateWindowW(WC_TREEVIEWW, L"", WS_CHILD | WS_VISIBLE, 0, 0, 100, 100, window, nullptr, wc.hInstance, nullptr);
        Require(TreeView_GetBkColor(freshTree) == RGB(12,34,56), "A new navigation tree retained the default style until a later frame.");
        DestroyWindow(freshTree);
        auto dpi = GetDpiForWindow(tree);
        Require(TreeView_GetItemHeight(tree) >= MulDiv(40, static_cast<int>(dpi), 96), "Vertical padding/font size not applied.");
        Require(TreeView_GetIndent(tree) == static_cast<UINT>(MulDiv(31, static_cast<int>(dpi), 96)), "Indent not applied.");
        Require(TreeView_GetBkColor(tree) == RGB(12,34,56), "Background not applied.");
        auto dc = GetDC(tree); auto memory = CreateCompatibleDC(dc);
        BITMAPINFO info{}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = 300; info.bmiHeader.biHeight = -300;
        info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32; info.bmiHeader.biCompression = BI_RGB;
        void* pixels{};
        auto bitmap = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &pixels, nullptr, 0);
        Require(bitmap && pixels, "Render bitmap allocation failed.");
        auto previous = SelectObject(memory, bitmap);
        auto pixel = [&](int x, int y) {
            auto value = static_cast<DWORD const*>(pixels)[y * 300 + x];
            return RGB((value >> 16) & 255, (value >> 8) & 255, value & 255);
        };
        SendMessageW(tree, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(memory), PRF_CLIENT);
        GdiFlush();
        Require(pixel(290, 100) == RGB(12,34,56), "Custom panel background was not rendered.");
        bool foundText = false;
        for (int y = 0; y < 60; ++y) for (int x = 0; x < 280; ++x) if (pixel(x, y) == RGB(220,210,200)) foundText = true;
        Require(foundText, "Custom font color was not rendered.");
        registry.Number(L"show-icons", 0);
        registry.Number(L"row-margin", 0);
        DWORD revision = 1;
        for (auto indent : {0, 24}) for (auto padding : {0, 15}) for (auto arrows : {0, 1}) {
            registry.Number(L"indent", indent);
            registry.Number(L"padding-horizontal", padding);
            registry.Number(L"show-arrows", arrows);
            registry.Number(L"revision", ++revision);
            registry.Heartbeat(GetTickCount64()); Pump(1100);
            SendMessageW(tree, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(memory), PRF_CLIENT);
            GdiFlush();
            for (auto item : {root, child}) {
                RECT row{}; TreeView_GetItemRect(tree, item, &row, FALSE);
                // Ignore the arrow at the root by looking beyond its reserved slot.
                auto expected = MulDiv(padding + (item == child ? indent : 0) + (arrows ? 12 : 0), static_cast<int>(dpi), 96);
                int first = 300;
                for (int y = row.top; y < row.bottom; ++y) for (int x = expected; x < 280; ++x)
                    if (pixel(x, y) == RGB(220,210,200) && x < first) first = x;
                Require(first >= expected && first <= expected + MulDiv(4, static_cast<int>(dpi), 96), "Unexpected native inset in custom content layout.");
            }
        }
        registry.Number(L"show-arrows", 1); registry.Number(L"padding-horizontal", 0);
        registry.Number(L"revision", ++revision); registry.Heartbeat(GetTickCount64()); Pump(1100);
        RECT rootRow{}; TreeView_GetItemRect(tree, root, &rootRow, FALSE);
        SendMessageW(tree, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(MulDiv(6, static_cast<int>(dpi), 96), (rootRow.top + rootRow.bottom) / 2));
        Require(!(TreeView_GetItemState(tree, root, TVIS_EXPANDED) & TVIS_EXPANDED), "Drawn arrow did not collapse its folder.");
        auto click = MAKELPARAM(MulDiv(16, static_cast<int>(dpi), 96), (rootRow.top + rootRow.bottom) / 2);
        PostMessageW(tree, WM_LBUTTONDOWN, MK_LBUTTON, click);
        PostMessageW(tree, WM_LBUTTONUP, 0, click);
        Pump(100);
        Require(TreeView_GetSelection(tree) == root, "Compact content click did not select its row.");
        SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(tree, dc);
        registry.Number(L"active", 0); Pump(1200);
        Require(TreeView_GetItemHeight(tree) == originalHeight && TreeView_GetIndent(tree) == originalIndent &&
            TreeView_GetBkColor(tree) == originalBackground && SendMessageW(tree, WM_GETFONT, 0, 0) == originalFont, "Original style was not restored.");
        registry.Number(L"active", 1); registry.Heartbeat(GetTickCount64());
        SendMessageW(tree, WM_NULL, 0, 0);
        Require(TreeView_GetBkColor(tree) == RGB(12,34,56), "Style could not be reattached.");
        registry.Heartbeat(GetTickCount64() - 10000); Pump(1200);
        Require(TreeView_GetBkColor(tree) == originalBackground, "Expired owner heartbeat did not restore Explorer.");
        UnhookWindowsHookEx(hook); DestroyWindow(window);
        std::puts("PASS: native navigation layout, custom background/text rendering, disable/restore, reattach and owner crash recovery.");
        return 0;
    } catch (std::exception const& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
}
