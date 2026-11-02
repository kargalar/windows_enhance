using System.Runtime.InteropServices;

namespace WinForge.Core;

public sealed class GlobalMouseHook : IDisposable
{
    public readonly record struct Input(int Message, POINT Point, int WheelDelta, int XButton);
    private Native.LowLevelMouseProc? _callback;
    private IntPtr _hook;

    public event Action<POINT>? LeftButtonDown;
    public event Action<POINT>? RightButtonDown;
    public event Action<POINT>? MiddleButtonDown;
    public event Action<POINT, int>? XButtonDown;
    public event Action<POINT>? MiddleButtonUp;
    public event Action<POINT, int>? WheelMoved;

    /// <summary>Fast optional filter. True consumes native input after observers run; never perform blocking work here.</summary>
    public Func<Input, bool>? EventFilter { get; set; }

    /// <summary>Reports callback failures. Reporting must also remain fast and nonblocking.</summary>
    public Action<Exception>? ErrorHandler { get; set; }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;

        _callback = Handle;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Fare kancası kurulamadı.");
    }

    private IntPtr Handle(int nCode, IntPtr wParam, IntPtr lParam)
    {
        using var timing = PerformanceTrace.Measure("input.mouse.callback");
        if (nCode >= 0)
        {
            var message = wParam.ToInt64() & 0xffff;
            if (message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN
                or Native.WM_MBUTTONUP or Native.WM_MOUSEWHEEL)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (ProcessInput(new((int)message, info.pt, (short)((info.mouseData >> 16) & 0xffff), GetXButtonNumber(info.mouseData))))
                    return new IntPtr(1);
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    internal bool ProcessInput(Input input)
    {
        if (HotkeyService.IsCapturing && input.Message != Native.WM_MBUTTONUP) return false;
        var suppress = false;
        try { suppress = EventFilter?.Invoke(input) == true; }
        catch (Exception exception) { ReportError(exception); }
        switch (input.Message)
        {
            case Native.WM_LBUTTONDOWN: Notify(LeftButtonDown, input.Point); break;
            case Native.WM_RBUTTONDOWN: Notify(RightButtonDown, input.Point); break;
            case Native.WM_MBUTTONDOWN: Notify(MiddleButtonDown, input.Point); break;
            case Native.WM_XBUTTONDOWN:
                if (input.XButton != 0) Notify(XButtonDown, input.Point, input.XButton);
                break;
            case Native.WM_MBUTTONUP: Notify(MiddleButtonUp, input.Point); break;
            case Native.WM_MOUSEWHEEL: Notify(WheelMoved, input.Point, input.WheelDelta); break;
        }
        return suppress;
    }

    private void Notify<T>(Action<T>? observers, T value)
    {
        if (observers is null) return;
        foreach (Action<T> observer in observers.GetInvocationList())
        {
            try { observer(value); }
            catch (Exception exception) { ReportError(exception); }
        }
    }

    private void Notify<T1, T2>(Action<T1, T2>? observers, T1 first, T2 second)
    {
        if (observers is null) return;
        foreach (Action<T1, T2> observer in observers.GetInvocationList())
        {
            try { observer(first, second); }
            catch (Exception exception) { ReportError(exception); }
        }
    }

    private void ReportError(Exception exception)
    {
        try { ErrorHandler?.Invoke(exception); }
        catch { /* Logging cannot escape through the native hook or change input consumption. */ }
    }

    internal static int GetXButtonNumber(uint mouseData)
    {
        var button = (int)((mouseData >> 16) & 0xffff);
        return button is 1 or 2 ? button : 0;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _callback = null;
    }
}
