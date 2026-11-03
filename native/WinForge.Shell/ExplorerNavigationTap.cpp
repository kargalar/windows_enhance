#include <windows.h>
#include <commctrl.h>
#include <algorithm>
#include <string>
#include <iterator>
#include <new>

namespace {
constexpr wchar_t RuntimeKey[] = L"Software\\WinForge\\ExplorerNavigation\\Runtime";
constexpr wchar_t FixtureKey[] = L"Software\\WinForge\\ExplorerNavigation\\Fixture";
constexpr UINT_PTR SubclassId = 0x57464E41;
constexpr UINT_PTR TimerId = 0x57464E42;
constexpr ULONGLONG LeaseMilliseconds = 6000;
HMODULE Library{};
thread_local bool Discovering{};
bool LocalFixture{};
bool IsExplorer() {
    static const bool explorer = [] {
        wchar_t path[MAX_PATH]{};
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        auto name = wcsrchr(path, L'\\');
        return _wcsicmp(name ? name + 1 : path, L"explorer.exe") == 0;
    }();
    return explorer;
}

DWORD Number(HKEY key, wchar_t const* name, DWORD fallback) {
    DWORD value{}, bytes = sizeof(value);
    return RegGetValueW(key, nullptr, name, RRF_RT_REG_DWORD, nullptr, &value, &bytes) == ERROR_SUCCESS ? value : fallback;
}
struct Options {
    DWORD revision{};
    int fontSize{13}, weight{400}, horizontal{8}, vertical{5}, indent{18}, icon{16}, gap{6}, radius{6}, margin{4};
    COLORREF background{RGB(25,25,25)}, text{RGB(240,240,240)}, selected{RGB(45,45,45)}, selectedText{RGB(255,255,255)}, hover{RGB(38,38,38)};
    wchar_t family[LF_FACESIZE]{L"Segoe UI"};
    bool icons{true}, arrows{true};
};
bool ReadOptions(Options& value) {
    HKEY key{};
    if (RegOpenKeyExW(HKEY_CURRENT_USER, LocalFixture ? FixtureKey : RuntimeKey, 0, KEY_READ, &key) != ERROR_SUCCESS) return false;
    ULONGLONG beat{};
    DWORD bytes = sizeof(beat);
    auto live = Number(key, L"active", 0) != 0 &&
        RegGetValueW(key, nullptr, L"heartbeat", RRF_RT_REG_QWORD, nullptr, &beat, &bytes) == ERROR_SUCCESS &&
        GetTickCount64() - beat < LeaseMilliseconds;
    if (live) {
        value.revision = Number(key, L"revision", 0);
        auto read = [&](wchar_t const* name, int fallback, int low, int high) { return std::clamp(static_cast<int>(Number(key, name, static_cast<DWORD>(fallback))), low, high); };
        value.fontSize = read(L"font-size", 13, 9, 32);
        value.weight = read(L"font-weight", 400, 100, 900);
        value.horizontal = read(L"padding-horizontal", 8, 0, 48);
        value.vertical = read(L"padding-vertical", 5, 0, 24);
        value.indent = read(L"indent", 18, 0, 64);
        value.icon = read(L"icon-size", 16, 12, 32);
        value.gap = read(L"icon-gap", 6, 0, 32);
        value.radius = read(L"corner-radius", 6, 0, 24);
        value.margin = read(L"row-margin", 4, 0, 24);
        value.background = Number(key, L"background", value.background);
        value.text = Number(key, L"text", value.text);
        value.selected = Number(key, L"selected", value.selected);
        value.selectedText = Number(key, L"selected-text", value.selectedText);
        value.hover = Number(key, L"hover", value.hover);
        value.icons = Number(key, L"show-icons", 1) != 0;
        value.arrows = Number(key, L"show-arrows", 1) != 0;
        bytes = sizeof(value.family);
        RegGetValueW(key, nullptr, L"font-family", RRF_RT_REG_SZ, nullptr, value.family, &bytes);
        value.family[LF_FACESIZE - 1] = 0;
    }
    RegCloseKey(key);
    return live;
}
struct TreeState {
    HWND tree{};
    HFONT originalFont{}, font{};
    COLORREF originalBackground{}, originalText{};
    int originalHeight{}, originalIndent{};
    UINT dpi{};
    Options options;
    int Scale(int value) const { return MulDiv(value, static_cast<int>(GetDpiForWindow(tree)), 96); }
};
int ContentLeft(TreeState const* state, HTREEITEM item) {
    int depth = 0;
    for (auto parent = TreeView_GetParent(state->tree, item); parent; parent = TreeView_GetParent(state->tree, parent)) ++depth;
    return state->Scale(state->options.horizontal + depth * state->options.indent) - GetScrollPos(state->tree, SB_HORZ);
}
void Restore(TreeState* state) {
    SendMessageW(state->tree, WM_SETFONT, reinterpret_cast<WPARAM>(state->originalFont), TRUE);
    TreeView_SetItemHeight(state->tree, state->originalHeight);
    TreeView_SetIndent(state->tree, state->originalIndent);
    TreeView_SetBkColor(state->tree, state->originalBackground);
    TreeView_SetTextColor(state->tree, state->originalText);
    if (state->font) DeleteObject(state->font);
    InvalidateRect(state->tree, nullptr, TRUE);
}
void Apply(TreeState* state, Options const& options) {
    state->options = options;
    state->dpi = GetDpiForWindow(state->tree);
    LOGFONTW font{};
    font.lfHeight = -state->Scale(options.fontSize);
    font.lfWeight = options.weight;
    font.lfCharSet = DEFAULT_CHARSET;
    wcscpy_s(font.lfFaceName, options.family);
    auto replacement = CreateFontIndirectW(&font);
    if (replacement) {
        auto previous = state->font;
        state->font = replacement;
        SendMessageW(state->tree, WM_SETFONT, reinterpret_cast<WPARAM>(replacement), FALSE);
        if (previous) DeleteObject(previous);
    }
    // Font ascent/descent needs extra room beyond its em size.
    TreeView_SetItemHeight(state->tree, state->Scale(std::max(options.fontSize + 5, options.icons ? options.icon : 0) + options.vertical * 2));
    TreeView_SetIndent(state->tree, state->Scale(options.indent));
    TreeView_SetBkColor(state->tree, options.background);
    TreeView_SetTextColor(state->tree, options.text);
    InvalidateRect(state->tree, nullptr, TRUE);
}
LRESULT Paint(TreeState* state, NMTVCUSTOMDRAW* draw) {
    if (draw->nmcd.dwDrawStage == CDDS_PREPAINT) return CDRF_NOTIFYITEMDRAW;
    if (draw->nmcd.dwDrawStage != CDDS_ITEMPREPAINT) return CDRF_DODEFAULT;
    auto tree = state->tree;
    auto const& options = state->options;
    auto item = reinterpret_cast<HTREEITEM>(draw->nmcd.dwItemSpec);
    wchar_t text[2048]{};
    TVITEMW data{};
    data.mask = TVIF_TEXT | TVIF_IMAGE | TVIF_SELECTEDIMAGE | TVIF_CHILDREN | TVIF_STATE;
    data.hItem = item;
    data.pszText = text;
    data.cchTextMax = static_cast<int>(std::size(text));
    data.stateMask = TVIS_EXPANDED | TVIS_SELECTED | TVIS_CUT | TVIS_BOLD | TVIS_OVERLAYMASK;
    if (!TreeView_GetItem(tree, &data)) return CDRF_DODEFAULT;
    RECT row = draw->nmcd.rc, client{};
    GetClientRect(tree, &client);
    row.left = client.left;
    row.right = client.right;
    auto dc = draw->nmcd.hdc;
    auto background = CreateSolidBrush(options.background);
    FillRect(dc, &row, background);
    DeleteObject(background);
    POINT cursor{};
    GetCursorPos(&cursor);
    ScreenToClient(tree, &cursor);
    TVHITTESTINFO hit{};
    hit.pt = cursor;
    auto hovered = TreeView_HitTest(tree, &hit) == item && PtInRect(&client, cursor);
    bool selected = (data.state & TVIS_SELECTED) != 0;
    auto fill = selected ? options.selected : hovered ? options.hover : options.background;
    RECT selection = row;
    selection.left += state->Scale(options.margin);
    selection.right -= state->Scale(options.margin);
    auto brush = CreateSolidBrush(fill);
    auto oldBrush = SelectObject(dc, brush);
    auto oldPen = SelectObject(dc, GetStockObject(NULL_PEN));
    auto radius = state->Scale(options.radius * 2);
    RoundRect(dc, selection.left, selection.top, selection.right, selection.bottom, radius, radius);
    SelectObject(dc, oldPen);
    SelectObject(dc, oldBrush);
    DeleteObject(brush);
    // Native label rectangles already include Explorer's root inset, image and
    // expander slots. Lay out our own content once, keeping only hierarchy and scroll.
    int left = ContentLeft(state, item);
    int arrowWidth = options.arrows ? state->Scale(12) : 0;
    int iconSize = state->Scale(options.icon);
    if (options.arrows && data.cChildren != 0) {
        auto x = left + arrowWidth / 2;
        auto y = (row.top + row.bottom) / 2;
        POINT points[3];
        if (data.state & TVIS_EXPANDED) { points[0] = {x-3,y-2}; points[1] = {x+3,y-2}; points[2] = {x,y+2}; }
        else { points[0] = {x-2,y-3}; points[1] = {x-2,y+3}; points[2] = {x+2,y}; }
        auto arrowBrush = CreateSolidBrush(options.text);
        oldBrush = SelectObject(dc, arrowBrush);
        oldPen = SelectObject(dc, GetStockObject(NULL_PEN));
        Polygon(dc, points, 3);
        SelectObject(dc, oldPen); SelectObject(dc, oldBrush); DeleteObject(arrowBrush);
    }
    left += arrowWidth;
    if (options.icons) {
        auto images = TreeView_GetImageList(tree, TVSIL_NORMAL);
        auto icon = images ? ImageList_GetIcon(images, selected ? data.iSelectedImage : data.iImage, ILD_TRANSPARENT) : nullptr;
        if (icon) { DrawIconEx(dc, left, (row.top + row.bottom - iconSize) / 2, icon, iconSize, iconSize, 0, nullptr, DI_NORMAL); DestroyIcon(icon); }
        left += iconSize + state->Scale(options.gap);
    }
    RECT content{left, row.top, row.right - state->Scale(options.horizontal + options.margin), row.bottom};
    auto oldFont = SelectObject(dc, state->font ? state->font : state->originalFont);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, selected ? options.selectedText : options.text);
    DrawTextW(dc, text, -1, &content, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX);
    SelectObject(dc, oldFont);
    if (selected && GetFocus() == tree && !(SendMessageW(tree, WM_QUERYUISTATE, 0, 0) & UISF_HIDEFOCUS)) DrawFocusRect(dc, &selection);
    return CDRF_SKIPDEFAULT;
}
LRESULT CALLBACK TreeProc(HWND, UINT, WPARAM, LPARAM, UINT_PTR, DWORD_PTR);
LRESULT CALLBACK ParentProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam, UINT_PTR, DWORD_PTR) {
    if (message == WM_NOTIFY && lparam) {
        auto header = reinterpret_cast<NMHDR*>(lparam);
        DWORD_PTR reference{};
        if (header->code == NM_CUSTOMDRAW && GetWindowSubclass(header->hwndFrom, TreeProc, SubclassId, &reference))
            return Paint(reinterpret_cast<TreeState*>(reference), reinterpret_cast<NMTVCUSTOMDRAW*>(lparam));
    }
    if (message == WM_NCDESTROY) RemoveWindowSubclass(window, ParentProc, SubclassId);
    return DefSubclassProc(window, message, wparam, lparam);
}
LRESULT CALLBACK TreeProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam, UINT_PTR, DWORD_PTR reference) {
    auto state = reinterpret_cast<TreeState*>(reference);
    if (message == WM_LBUTTONDOWN || message == WM_LBUTTONUP || message == WM_LBUTTONDBLCLK ||
        message == WM_RBUTTONDOWN || message == WM_RBUTTONUP) {
        TVHITTESTINFO hit{};
        hit.pt = {static_cast<short>(LOWORD(lparam)), static_cast<short>(HIWORD(lparam))};
        auto item = TreeView_HitTest(window, &hit);
        RECT label{};
        if (item && TreeView_GetItemRect(window, item, &label, TRUE)) {
            auto left = ContentLeft(state, item);
            auto arrowWidth = state->options.arrows ? state->Scale(12) : 0;
            if (arrowWidth && hit.pt.x >= left && hit.pt.x < left + arrowWidth &&
                (message == WM_LBUTTONDOWN || message == WM_LBUTTONUP || message == WM_LBUTTONDBLCLK)) {
                if (message == WM_LBUTTONDOWN) TreeView_Expand(window, item, TVE_TOGGLE);
                return 0;
            }
            // Native hit testing must follow the content we drew, rather than
            // treating its former icon/expander space as the clickable content.
            if (hit.pt.x >= left + arrowWidth)
                lparam = MAKELPARAM(label.left + 1, hit.pt.y);
        }
    }
    if (message == TVM_SETBKCOLOR) lparam = static_cast<LPARAM>(state->options.background);
    if (message == TVM_SETTEXTCOLOR) lparam = static_cast<LPARAM>(state->options.text);
    if (message == WM_SETFONT && state->font) wparam = reinterpret_cast<WPARAM>(state->font);
    if (message == TVM_SETINDENT) wparam = static_cast<WPARAM>(state->Scale(state->options.indent));
    if (message == TVM_SETITEMHEIGHT) {
        auto const& options = state->options;
        wparam = static_cast<WPARAM>(state->Scale(std::max(options.fontSize + 5, options.icons ? options.icon : 0) + options.vertical * 2));
    }
    if (message == WM_TIMER && wparam == TimerId) {
        Options options;
        if (ReadOptions(options)) {
            if (options.revision != state->options.revision || state->dpi != GetDpiForWindow(window)) Apply(state, options);
            else InvalidateRect(window, nullptr, FALSE);
            return 0;
        }
        KillTimer(window, TimerId);
        RemoveWindowSubclass(window, TreeProc, SubclassId);
        Restore(state);
        delete state;
        return 0;
    }
    if (message == WM_NCDESTROY) {
        KillTimer(window, TimerId);
        RemoveWindowSubclass(window, TreeProc, SubclassId);
        if (state->font) DeleteObject(state->font);
        delete state;
    }
    return DefSubclassProc(window, message, wparam, lparam);
}
bool ClassIs(HWND window, wchar_t const* expected) {
    wchar_t name[128]{};
    GetClassNameW(window, name, static_cast<int>(std::size(name)));
    return wcscmp(name, expected) == 0;
}
BOOL CALLBACK FindTree(HWND window, LPARAM) {
    if (GetWindowThreadProcessId(window, nullptr) != GetCurrentThreadId()) return TRUE;
    if (!ClassIs(window, WC_TREEVIEWW)) return TRUE;
    auto parent = GetParent(window);
    bool navigation = false;
    for (auto ancestor = parent; ancestor; ancestor = GetParent(ancestor))
        if (ClassIs(ancestor, L"NamespaceTreeControl")) { navigation = true; break; }
    DWORD_PTR existing{};
    if (!navigation || GetWindowSubclass(window, TreeProc, SubclassId, &existing)) return TRUE;
    Options options;
    if (!ReadOptions(options)) return TRUE;
    auto state = new(std::nothrow) TreeState;
    if (!state) return TRUE;
    state->tree = window;
    state->originalFont = reinterpret_cast<HFONT>(SendMessageW(window, WM_GETFONT, 0, 0));
    state->originalHeight = TreeView_GetItemHeight(window);
    state->originalIndent = static_cast<int>(TreeView_GetIndent(window));
    state->originalBackground = TreeView_GetBkColor(window);
    state->originalText = TreeView_GetTextColor(window);
    // Callback addresses remain valid even if a parent or queued message outlives the hook.
    HMODULE pinned{};
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, reinterpret_cast<LPCWSTR>(&TreeProc), &pinned);
    if (!SetWindowSubclass(parent, ParentProc, SubclassId, 0) || !SetWindowSubclass(window, TreeProc, SubclassId, reinterpret_cast<DWORD_PTR>(state))) { delete state; return TRUE; }
    Apply(state, options);
    SetTimer(window, TimerId, 1000, nullptr);
    return TRUE;
}
LRESULT CALLBACK Hook(int code, WPARAM wparam, LPARAM lparam) {
    // Inspect completed creation, before the control can paint. A desktop hook
    // also covers Explorer threads/windows that do not exist at activation time.
    if (code >= 0 && !Discovering && (LocalFixture || IsExplorer())) {
        auto message = reinterpret_cast<CWPRETSTRUCT const*>(lparam);
        if (message->message == WM_CREATE || message->message == WM_NULL) {
            Discovering = true;
            FindTree(message->hwnd, 0);
            Discovering = false;
        }
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
}
extern "C" __declspec(dllexport) HHOOK WINAPI InstallNavigationHook(DWORD thread) {
    HMODULE module = Library;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&Hook), &module)) return nullptr;
    LocalFixture = thread != 0;
    return SetWindowsHookExW(WH_CALLWNDPROCRET, Hook, module, thread);
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) Library = instance; return TRUE; }
