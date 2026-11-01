// Explorer's WinUI island reserves a native header rectangle independently of
// collapsed XAML controls. Resize that island and its sibling file-view host.
struct ExplorerCommandBarLayout {
    HWND root{}, bridge{}, tab{};
    int removed{};
    bool updating{};
    static constexpr UINT_PTR Id = 0x57464342;
    static std::wstring Class(HWND window) { wchar_t name[128]{}; GetClassNameW(window, name, 128); return name; }
    static RECT Bounds(HWND window, HWND parent) {
        RECT bounds{}; GetWindowRect(window, &bounds);
        MapWindowPoints(nullptr, parent, reinterpret_cast<POINT*>(&bounds), 2);
        return bounds;
    }
    void Update() {
        if (updating || !IsWindow(bridge) || !IsWindow(tab) || IsIconic(root)) return;
        updating = true;
        auto content = Bounds(tab, root), header = Bounds(bridge, root);
        auto target = MulDiv(87, static_cast<int>(GetDpiForWindow(root)), 96);
        auto delta = content.top - target;
        if (delta > 0 && delta < MulDiv(80, static_cast<int>(GetDpiForWindow(root)), 96)) {
            removed = delta;
            SetWindowPos(bridge, nullptr, 0, 0, header.right - header.left, header.bottom - header.top - delta, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            SetWindowPos(tab, nullptr, content.left, target, content.right - content.left, content.bottom - target, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        updating = false;
    }
    static LRESULT CALLBACK Proc(HWND window, UINT message, WPARAM wp, LPARAM lp, UINT_PTR, DWORD_PTR data) {
        auto state = reinterpret_cast<ExplorerCommandBarLayout*>(data);
        auto result = DefSubclassProc(window, message, wp, lp);
        if (message == WM_SIZE || message == WM_DPICHANGED) state->Update();
        return result;
    }
    explicit ExplorerCommandBarLayout(HWND window) : root(window) {
        for (auto child = GetWindow(root, GW_CHILD); child; child = GetWindow(child, GW_HWNDNEXT)) {
            auto type = Class(child);
            if (type == L"ShellTabWindowClass") tab = child;
            if (type == L"Microsoft.UI.Content.DesktopChildSiteBridge") bridge = child;
        }
        if (tab && bridge && SetWindowSubclass(root, Proc, Id, reinterpret_cast<DWORD_PTR>(this))) Update();
    }
    ~ExplorerCommandBarLayout() {
        if (!IsWindow(root)) return;
        RemoveWindowSubclass(root, Proc, Id);
        if (removed && IsWindow(tab) && IsWindow(bridge)) {
            auto content = Bounds(tab, root), header = Bounds(bridge, root);
            SetWindowPos(bridge, nullptr, 0, 0, header.right - header.left, header.bottom - header.top + removed, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            SetWindowPos(tab, nullptr, content.left, content.top + removed, content.right - content.left, content.bottom - content.top - removed, SWP_NOZORDER | SWP_NOACTIVATE);
            RECT client{}; GetClientRect(root, &client);
            SendMessageW(root, WM_SIZE, SIZE_RESTORED, MAKELPARAM(client.right, client.bottom));
        }
    }
};
