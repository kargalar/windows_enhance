using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ExplorerMouseNavigationModule : ITweakModule
{
    private IModuleContext _context = null!;
    private Native.LowLevelMouseProc? _callback;
    private IntPtr _hook;
    private bool _held;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    public string Id => "explorer-mouse-navigation";
    public string Name => "Gezgin Fare Gezinmesi";
    public string Category => "Gezgin";
    public string Description => "Dosya Gezgini’nde orta fare tuşuyla bir üst klasöre çıkın.";
    public string? Warning => null;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } = [new()
    {
        Key = "middle-up", Label = "Orta fare tuşuyla üst klasöre çık", Kind = SettingKind.Toggle, Default = true,
        Description = "Yalnızca odaktaki Dosya Gezgini penceresinde çalışır. Ctrl, Alt, Shift veya Win ile yapılan tıklamalar korunur."
    }];

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() { IsActive = true; try { Apply(); } catch { Deactivate(); throw; } }
    public void Deactivate()
    {
        IsActive = false;
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero; _callback = null; _held = false;
    }
    public void Apply()
    {
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero; _callback = null; _held = false;
        if (!IsActive || !_context.Settings.GetBool("middle-up", true)) return;
        _callback = Handle;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Gezgin fare gezinmesi başlatılamadı.");
    }
    private IntPtr Handle(int code, IntPtr message, IntPtr pointer)
    {
        if (code >= 0 && message.ToInt32() is Native.WM_MBUTTONDOWN or Native.WM_MBUTTONUP)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(pointer);
            if ((info.flags & 1) == 0 && ProcessInput(message.ToInt32(), info.pt)) return new(1);
        }
        return Native.CallNextHookEx(_hook, code, message, pointer);
    }
    internal bool ProcessInput(int message, POINT point)
    {
        if (message == Native.WM_MBUTTONUP && _held) { _held = false; return true; }
        if (!IsActive || HotkeyService.IsCapturing || message != Native.WM_MBUTTONDOWN) return false;
        if (_held) return true;
        var target = Native.GetForegroundWindow();
        var hovered = Native.GetAncestor(Native.WindowFromPoint(point), 2);
        var windowClass = new StringBuilder(256);
        Native.GetClassName(target, windowClass, windowClass.Capacity);
        if (!ShouldNavigate(windowClass.ToString(), target, hovered, ActionInput.Modifiers)) return false;
        Native.GetWindowThreadProcessId(target, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (!process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch { return false; }
        _held = true;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive || _hook == IntPtr.Zero || HotkeyService.IsCapturing || Native.GetForegroundWindow() != target || ActionInput.Modifiers != 0) return;
            try { ActionInput.Chord(0x12, 0x26); }
            catch (Exception error) { _context.Log.Error("Gezgin üst klasöre geçemedi.", error); }
        }));
        return true;
    }
    internal static bool ShouldNavigate(string windowClass, IntPtr foreground, IntPtr hovered, uint modifiers) =>
        windowClass is "CabinetWClass" or "ExploreWClass" && foreground != IntPtr.Zero && foreground == hovered && modifiers == 0;
}
