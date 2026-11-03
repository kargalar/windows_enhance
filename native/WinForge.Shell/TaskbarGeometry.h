#pragma once

#include <windows.h>
#include <commctrl.h>
#include <shellapi.h>
#include <algorithm>
#include <cmath>
#include <string>

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "shell32.lib")

namespace winforge::shell {

// Lives on the taskbar's UI thread. The session owns the lifetime and calls
// Restore before destroying its UI context, including on heartbeat loss.
class TaskbarGeometry final {
public:
    struct Diagnostic {
        unsigned int stage{};
        RECT current{};
        RECT desired{};
        RECT actual{};
        RECT nativeProposal{};
        RECT adjustedProposal{};
        UINT flags{};
        UINT dpi{};
        DWORD callerThread{};
        DWORD windowThread{};
        unsigned int changingMessages{};
        unsigned int changedMessages{};
    };

private:
    HWND window_{};
    RECT original_{};
    RECT monitor_{};
    UINT originalDpi_{};
    int heightDips_{};
    bool restoring_{};
    RECT restoreDesired_{};
    HWND reservation_{};
    bool registered_{};
    int reservedPixels_{};
    Diagnostic diagnostic_{};
    static constexpr UINT ReservationNotification = WM_APP + 0x41;

    static bool Bounds(HWND window, RECT& rectangle) noexcept {
        return GetWindowRect(window, &rectangle) != FALSE;
    }

    static bool SameRect(RECT const& first, RECT const& second) noexcept {
        return EqualRect(&first, &second) != FALSE;
    }

    static int Pixels(int dips, UINT dpi) noexcept {
        return MulDiv(dips, static_cast<int>(dpi ? dpi : USER_DEFAULT_SCREEN_DPI), USER_DEFAULT_SCREEN_DPI);
    }

    bool ReadMonitor(MONITORINFO& information) const noexcept {
        information.cbSize = sizeof(information);
        return GetMonitorInfoW(MonitorFromWindow(window_, MONITOR_DEFAULTTONEAREST), &information) != FALSE;
    }

    static LRESULT CALLBACK Subclass(HWND window, UINT message, WPARAM wParam,
        LPARAM lParam, UINT_PTR identity, DWORD_PTR data) noexcept {
        auto self = reinterpret_cast<TaskbarGeometry*>(data);
        if (message == WM_NCDESTROY) {
            RemoveWindowSubclass(window, Subclass, identity);
            self->window_ = nullptr;
            self->ReleaseReservation();
            return DefSubclassProc(window, message, wParam, lParam);
        }
        auto result = DefSubclassProc(window, message, wParam, lParam);
        if (message == WM_WINDOWPOSCHANGED) ++self->diagnostic_.changedMessages;
        if (message == WM_WINDOWPOSCHANGING && (self->restoring_ || self->heightDips_ > 0)) {
            auto position = reinterpret_cast<WINDOWPOS*>(lParam);
            ++self->diagnostic_.changingMessages;
            self->diagnostic_.nativeProposal = RECT{position->x, position->y,
                position->x + position->cx, position->y + position->cy};
            self->diagnostic_.flags = position->flags;
            if (self->restoring_) {
                position->x = self->restoreDesired_.left;
                position->y = self->restoreDesired_.top;
                position->cx = self->restoreDesired_.right - self->restoreDesired_.left;
                position->cy = self->restoreDesired_.bottom - self->restoreDesired_.top;
                position->flags &= ~(SWP_NOMOVE | SWP_NOSIZE);
                self->diagnostic_.adjustedProposal = self->restoreDesired_;
                return result;
            }
            MONITORINFO information{};
            RECT current{};
            auto targetHeight = Pixels(self->heightDips_, GetDpiForWindow(window));
            if (self->ReadMonitor(information) && Bounds(window, current) &&
                (!(position->flags & SWP_NOSIZE) || !(position->flags & SWP_NOMOVE) || current.bottom - current.top != targetHeight)) {
                    auto left = (position->flags & SWP_NOMOVE) ? current.left : position->x;
                    auto top = (position->flags & SWP_NOMOVE) ? current.top : position->y;
                    auto width = (position->flags & SWP_NOSIZE) ? current.right - current.left : position->cx;
                    RECT proposed{left, top, left + width, top + position->cy};
                    if (position->flags & SWP_NOSIZE) {
                        auto nativeHeight = MulDiv(self->original_.bottom - self->original_.top,
                            static_cast<int>(GetDpiForWindow(window)), static_cast<int>(self->originalDpi_));
                        proposed = MoveOnlyProposal(proposed, information.rcMonitor, targetHeight, nativeHeight);
                    }
                    auto desired = ResizeBottom(proposed, information.rcMonitor, targetHeight);
                    position->y = desired.top;
                    position->cx = desired.right - desired.left;
                    position->cy = desired.bottom - desired.top;
                    if (desired.top != current.top) position->flags &= ~SWP_NOMOVE;
                    if (position->cy != current.bottom - current.top) position->flags &= ~SWP_NOSIZE;
                    self->diagnostic_.adjustedProposal = RECT{position->x, position->y,
                        position->x + position->cx, position->y + position->cy};
            }
        }
        return result;
    }

    static LRESULT CALLBACK ReservationProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) noexcept {
        if (message == ReservationNotification) return 0;
        if (message == WM_WINDOWPOSCHANGED) {
            APPBARDATA bar{sizeof(bar)};
            bar.hWnd = window;
            SHAppBarMessage(ABM_WINDOWPOSCHANGED, &bar);
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    void ReleaseReservation() noexcept {
        if (registered_) {
            APPBARDATA bar{sizeof(bar)};
            bar.hWnd = reservation_;
            SHAppBarMessage(ABM_REMOVE, &bar);
            registered_ = false;
        }
        if (reservation_) DestroyWindow(reservation_);
        reservation_ = nullptr;
        reservedPixels_ = 0;
    }

    bool ReserveAdditionalSpace(MONITORINFO const& information, int pixels) noexcept {
        APPBARDATA state{sizeof(state)};
        state.hWnd = window_;
        if (SHAppBarMessage(ABM_GETSTATE, &state) & ABS_AUTOHIDE) {
            ReleaseReservation();
            return true;
        }
        auto originalPixels = MulDiv(original_.bottom - original_.top,
            static_cast<int>(GetDpiForWindow(window_)), static_cast<int>(originalDpi_));
        auto additional = pixels - originalPixels;
        if (additional <= 0) {
            ReleaseReservation();
            // Shrinking requires Explorer to reserve the smaller native area.
            // An application appbar cannot subtract another appbar's space.
            if (additional < 0) { SetLastError(ERROR_NOT_SUPPORTED); return false; }
            return true;
        }
        auto expectedBottom = information.rcMonitor.bottom - pixels;
        if (!registered_ && information.rcWork.bottom == expectedBottom) return true;
        auto nativeBottom = information.rcMonitor.bottom - originalPixels;
        if ((!registered_ && information.rcWork.bottom != nativeBottom) ||
            (registered_ && information.rcWork.bottom != nativeBottom - reservedPixels_)) {
            // A second bottom appbar needs Explorer to relocate it against the
            // taskbar's own reservation. Do not overlap it with a larger HWND.
            SetLastError(ERROR_NOT_SUPPORTED);
            return false;
        }
        if (!reservation_) {
            HMODULE module{};
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(&ReservationProc), &module);
            WNDCLASSW definition{};
            definition.lpfnWndProc = ReservationProc;
            definition.hInstance = module;
            definition.lpszClassName = L"WinForge.Shell.TaskbarReservation";
            RegisterClassW(&definition);
            reservation_ = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, definition.lpszClassName,
                L"", WS_POPUP, information.rcMonitor.left, information.rcMonitor.bottom - additional,
                information.rcMonitor.right - information.rcMonitor.left, additional,
                nullptr, nullptr, module, nullptr);
            if (!reservation_) return false;
            APPBARDATA bar{sizeof(bar)};
            bar.hWnd = reservation_;
            bar.uCallbackMessage = ReservationNotification;
            registered_ = SHAppBarMessage(ABM_NEW, &bar) != 0;
            if (!registered_) { ReleaseReservation(); SetLastError(ERROR_NOT_SUPPORTED); return false; }
        }
        APPBARDATA bar{sizeof(bar)};
        bar.hWnd = reservation_;
        bar.uEdge = ABE_BOTTOM;
        bar.rc = information.rcMonitor;
        SHAppBarMessage(ABM_QUERYPOS, &bar);
        bar.rc.top = bar.rc.bottom - additional;
        SHAppBarMessage(ABM_SETPOS, &bar);
        reservedPixels_ = static_cast<int>(bar.rc.bottom - bar.rc.top);
        if (!SetWindowPos(reservation_, nullptr, bar.rc.left, bar.rc.top,
            bar.rc.right - bar.rc.left, bar.rc.bottom - bar.rc.top, SWP_NOACTIVATE | SWP_NOZORDER)) return false;
        MONITORINFO after{};
        if (!ReadMonitor(after)) return false;
        if (after.rcWork.bottom > information.rcMonitor.bottom - pixels) {
            SetLastError(ERROR_INVALID_DATA);
            return false;
        }
        return true;
    }

public:
    TaskbarGeometry() = default;
    TaskbarGeometry(TaskbarGeometry const&) = delete;
    TaskbarGeometry& operator=(TaskbarGeometry const&) = delete;
    ~TaskbarGeometry() { Restore(); }

    Diagnostic const& LastDiagnostic() const noexcept { return diagnostic_; }

    std::wstring DescribeDiagnostic() const {
        wchar_t buffer[1024]{};
        auto const& data = diagnostic_;
        swprintf_s(buffer, L"stage=%u thread=%lu/%lu dpi=%u changing=%u changed=%u flags=%u "
            L"current=%ld,%ld,%ld,%ld desired=%ld,%ld,%ld,%ld actual=%ld,%ld,%ld,%ld "
            L"native=%ld,%ld,%ld,%ld adjusted=%ld,%ld,%ld,%ld",
            data.stage, data.callerThread, data.windowThread, data.dpi, data.changingMessages,
            data.changedMessages, data.flags, data.current.left, data.current.top, data.current.right,
            data.current.bottom, data.desired.left, data.desired.top, data.desired.right, data.desired.bottom,
            data.actual.left, data.actual.top, data.actual.right, data.actual.bottom,
            data.nativeProposal.left, data.nativeProposal.top, data.nativeProposal.right, data.nativeProposal.bottom,
            data.adjustedProposal.left, data.adjustedProposal.top, data.adjustedProposal.right, data.adjustedProposal.bottom);
        return buffer;
    }

    bool Refresh() noexcept {
        return !window_ || Update(window_, heightDips_);
    }

    // Explorer can reveal an auto-hidden bar using either its native height
    // or the already customized height. Do not treat the latter as an
    // intermediate animation frame when the customized bar is shorter.
    static RECT MoveOnlyProposal(RECT rectangle, RECT const& monitor, int targetHeight, int nativeHeight) noexcept {
        rectangle.bottom = rectangle.top +
            (rectangle.top == monitor.bottom - targetHeight ? targetHeight : nativeHeight);
        return rectangle;
    }

    // Keep the two-pixel autohide reveal strip unchanged. During animation,
    // preserve progress between fully hidden and fully shown positions.
    static RECT ResizeBottom(RECT rectangle, RECT const& monitor, int height) noexcept {
        constexpr int RevealPixels = 2;
        auto previousHeight = static_cast<int>(rectangle.bottom - rectangle.top);
        if (height <= 0 || previousHeight <= 0) return rectangle;
        if (rectangle.top <= monitor.bottom - previousHeight) {
            rectangle.top = monitor.bottom - height;
            rectangle.bottom = monitor.bottom;
            return rectangle;
        }
        auto visible = std::clamp(static_cast<int>(monitor.bottom - rectangle.top) - RevealPixels, 0,
            std::max(1, previousHeight - RevealPixels));
        auto progress = static_cast<double>(visible) / std::max(1, previousHeight - RevealPixels);
        rectangle.top -= static_cast<LONG>(std::lround((height - previousHeight) * progress));
        rectangle.bottom = rectangle.top + height;
        return rectangle;
    }

    bool Update(HWND window, int heightDips) noexcept {
        constexpr int MinimumHeightDips = 24;
        constexpr int MaximumHeightDips = 160;
        if (!window || heightDips < MinimumHeightDips || heightDips > MaximumHeightDips) {
            SetLastError(ERROR_INVALID_PARAMETER); return false;
        }
        DWORD process{};
        auto windowThread = GetWindowThreadProcessId(window, &process);
        if (windowThread != GetCurrentThreadId() || process != GetCurrentProcessId()) {
            SetLastError(ERROR_INVALID_THREAD_ID); return false;
        }
        diagnostic_ = Diagnostic{};
        diagnostic_.callerThread = GetCurrentThreadId();
        diagnostic_.windowThread = windowThread;
        diagnostic_.dpi = GetDpiForWindow(window);
        if (window_ && window_ != window && !Restore()) return false;
        if (!window_) {
            if (!Bounds(window, original_)) return false;
            window_ = window;
            originalDpi_ = GetDpiForWindow(window);
            MONITORINFO information{};
            if (!originalDpi_ || !ReadMonitor(information)) { window_ = nullptr; return false; }
            monitor_ = information.rcMonitor;
            if (original_.right - original_.left <= original_.bottom - original_.top ||
                original_.top < monitor_.bottom - (original_.bottom - original_.top) - 2 ||
                original_.top >= monitor_.bottom) {
                window_ = nullptr;
                SetLastError(ERROR_NOT_SUPPORTED);
                return false;
            }
            if (!SetWindowSubclass(window, Subclass, reinterpret_cast<UINT_PTR>(this), reinterpret_cast<DWORD_PTR>(this))) {
                window_ = nullptr; return false;
            }
        }
        heightDips_ = heightDips;
        MONITORINFO information{};
        RECT current{};
        if (!ReadMonitor(information) || !Bounds(window_, current)) return false;
        auto height = Pixels(heightDips, GetDpiForWindow(window_));
        auto desired = ResizeBottom(current, information.rcMonitor, height);
        diagnostic_.stage = 1;
        diagnostic_.current = current;
        diagnostic_.desired = desired;
        // Submit an actual size change. Explorer may ignore a no-op resize
        // before delivering the message to the subclass chain.
        if (!SameRect(current, desired) && !SetWindowPos(window_, nullptr, desired.left, desired.top,
            desired.right - desired.left, height, SWP_NOACTIVATE | SWP_NOZORDER)) return false;
        RECT actual{};
        auto readback = Bounds(window_, actual);
        diagnostic_.stage = 2;
        diagnostic_.actual = actual;
        if (!readback || !SameRect(actual, desired)) { SetLastError(ERROR_INVALID_DATA); return false; }
        diagnostic_.stage = 3;
        auto reserved = ReserveAdditionalSpace(information, height);
        if (reserved) diagnostic_.stage = 4;
        return reserved;
    }

    bool Restore() noexcept {
        if (!window_) { ReleaseReservation(); return true; }
        if (GetWindowThreadProcessId(window_, nullptr) != GetCurrentThreadId()) {
            SetLastError(ERROR_INVALID_THREAD_ID); return false;
        }
        ReleaseReservation();
        restoring_ = true;
        heightDips_ = 0;
        auto window = window_;
        RECT desired = original_;
        MONITORINFO information{};
        RECT current{};
        if (ReadMonitor(information) && Bounds(window, current) &&
            (!SameRect(information.rcMonitor, monitor_) || GetDpiForWindow(window) != originalDpi_)) {
            desired = ResizeBottom(current, information.rcMonitor,
                MulDiv(original_.bottom - original_.top, static_cast<int>(GetDpiForWindow(window)), static_cast<int>(originalDpi_)));
        }
        restoreDesired_ = desired;
        diagnostic_.stage = 5;
        diagnostic_.current = current;
        diagnostic_.desired = desired;
        auto succeeded = !IsWindow(window) || SetWindowPos(window, nullptr, desired.left, desired.top,
            desired.right - desired.left, desired.bottom - desired.top, SWP_NOACTIVATE | SWP_NOZORDER) != FALSE;
        auto failure = succeeded ? ERROR_SUCCESS : GetLastError();
        RECT actual{};
        auto verified = !IsWindow(window) || (Bounds(window, actual) && SameRect(actual, desired));
        diagnostic_.stage = 6;
        diagnostic_.actual = actual;
        RemoveWindowSubclass(window, Subclass, reinterpret_cast<UINT_PTR>(this));
        window_ = nullptr;
        restoring_ = false;
        if (!verified) failure = ERROR_INVALID_DATA;
        if (!succeeded && !failure) failure = ERROR_GEN_FAILURE;
        if (failure) SetLastError(failure);
        if (succeeded && verified) diagnostic_.stage = 7;
        return succeeded && verified;
    }
};
}
