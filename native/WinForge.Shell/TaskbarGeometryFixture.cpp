#include "TaskbarGeometry.h"
#include <cstdio>
#include <thread>

using winforge::shell::TaskbarGeometry;

static bool rejectNativeSize{};
static LRESULT CALLBACK FixtureProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_WINDOWPOSCHANGING) {
        auto position = reinterpret_cast<WINDOWPOS*>(lParam);
        if (!(position->flags & SWP_NOSIZE)) position->cy = 48;
        if (rejectNativeSize) position->flags |= SWP_NOSIZE | SWP_NOMOVE;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

int wmain() {
    int failed{};
    auto check = [&failed](bool condition, char const* name) {
        std::printf("%s %s\n", condition ? "PASS" : "FAIL", name);
        if (!condition) ++failed;
    };
    RECT monitor{0, 0, 1920, 1080};
    RECT shown{0, 1032, 1920, 1080};
    auto resized = TaskbarGeometry::ResizeBottom(shown, monitor, 52);
    check(resized.top == 1028 && resized.bottom == 1080, "shown bottom anchor");
    RECT hidden{0, 1078, 1920, 1126};
    resized = TaskbarGeometry::ResizeBottom(hidden, monitor, 52);
    check(resized.top == 1078 && resized.bottom == 1130, "autohide reveal strip");
    RECT negativeMonitor{-1920, -1080, 0, 0};
    RECT negativeShown{-1920, -48, 0, 0};
    resized = TaskbarGeometry::ResizeBottom(negativeShown, negativeMonitor, 78);
    check(resized.top == -78 && resized.bottom == 0, "negative monitor coordinates and DPI-sized pixels");
    RECT partial{0, 1055, 1920, 1103};
    resized = TaskbarGeometry::ResizeBottom(partial, monitor, 52);
    check(resized.top == 1053 && resized.bottom == 1105, "autohide animation progress");
    RECT startReveal{0, 1040, 1920, 1080};
    auto startProposal = TaskbarGeometry::MoveOnlyProposal(startReveal, monitor, 40, 48);
    resized = TaskbarGeometry::ResizeBottom(startProposal, monitor, 40);
    check(resized.top == 1040 && resized.bottom == 1080, "Start reveal using shortened height stays anchored");
    RECT mouseReveal{0, 1032, 1920, 1080};
    auto mouseProposal = TaskbarGeometry::MoveOnlyProposal(mouseReveal, monitor, 40, 48);
    resized = TaskbarGeometry::ResizeBottom(mouseProposal, monitor, 40);
    check(resized.top == 1040 && resized.bottom == 1080, "native-height mouse reveal stays anchored");

    WNDCLASSW definition{};
    definition.lpfnWndProc = FixtureProc;
    definition.hInstance = GetModuleHandleW(nullptr);
    definition.lpszClassName = L"WinForge.Shell.GeometryFixture";
    RegisterClassW(&definition);
    auto window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, definition.lpszClassName, L"",
        WS_POPUP, 0, 1032, 300, 48, nullptr, nullptr, definition.hInstance, nullptr);
    check(window != nullptr, "owned fixture window created");
    if (!window) return 1;
    TaskbarGeometry geometry;
    RECT before{};
    GetWindowRect(window, &before);
    MONITORINFO workBefore{sizeof(workBefore)};
    GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &workBefore);
    APPBARDATA barState{sizeof(barState)};
    barState.hWnd = FindWindowW(L"Shell_TrayWnd", nullptr);
    auto stateBefore = SHAppBarMessage(ABM_GETSTATE, &barState);
    check(geometry.Update(window, 52), "owner-thread geometry update");
    RECT after{};
    GetWindowRect(window, &after);
    check(after.bottom - after.top == 52, "subclass wins over fixture native 48px constraint");
    SetWindowPos(window, nullptr, 0, 1032, 300, 48, SWP_NOZORDER | SWP_NOACTIVATE);
    GetWindowRect(window, &after);
    check(after.bottom - after.top == 52 && after.bottom == 1080, "later native layout remains requested size");
    SetWindowPos(window, nullptr, 0, 1078, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
    GetWindowRect(window, &after);
    check(after.top == 1078 && after.bottom - after.top == 52, "native move-only autohide preserves reveal strip");
    SetWindowPos(window, nullptr, 0, 1032, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
    GetWindowRect(window, &after);
    check(after.top == 1028 && after.bottom == 1080, "native move-only reveal anchors enlarged taskbar");
    bool rejected{};
    std::thread thread([&] { rejected = !geometry.Update(window, 52) && GetLastError() == ERROR_INVALID_THREAD_ID; });
    thread.join();
    check(rejected, "cross-thread update rejected");
    check(geometry.Restore(), "restore verified");
    GetWindowRect(window, &after);
    check(EqualRect(&before, &after) != FALSE, "exact original rectangle restored");
    check(geometry.Restore(), "restore idempotent");
    MONITORINFO workAfter{sizeof(workAfter)};
    GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &workAfter);
    check(EqualRect(&workBefore.rcWork, &workAfter.rcWork) != FALSE, "original monitor workarea preserved");
    check(SHAppBarMessage(ABM_GETSTATE, &barState) == stateBefore, "taskbar autohide state preserved");
    check(!geometry.Update(window, 500) && GetLastError() == ERROR_INVALID_PARAMETER, "invalid height rejected");
    rejectNativeSize = true;
    check(geometry.Update(window, 52), "native SWP_NOSIZE and SWP_NOMOVE veto overridden");
    GetWindowRect(window, &after);
    check(after.bottom - after.top == 52, "actual 52px after native resize veto");
    check(geometry.Restore(), "restore overrides native resize veto before removing subclass");
    GetWindowRect(window, &after);
    check(EqualRect(&before, &after) != FALSE, "exact original rectangle after vetoed native restore");
    check(geometry.Update(window, 52), "activation after vetoed native restore");
    DestroyWindow(window);
    check(geometry.Restore(), "window destruction removes subclass without dangling context");
    return failed ? 1 : 0;
}
