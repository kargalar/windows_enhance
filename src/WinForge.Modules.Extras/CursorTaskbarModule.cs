using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Shows the existing Explorer taskbar on the pointer's display only.</summary>
public sealed class CursorTaskbarModule : IModuleComponent
{
    private readonly Dictionary<IntPtr, uint> _hidden = [];
    private readonly HashSet<IntPtr> _autoHideRecovered = [];
    private DispatcherTimer? _timer;
    private IModuleContext? _context;
    public string Id => "cursor-taskbar";
    public string Name => "Görev Çubuğu Konumu";
    public string Category => "Başlat";
    public string Description => "Görev çubuğunu yalnızca farenin bulunduğu monitörde gösterir.";
    public string? Warning { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => [];
    public void Initialize(IModuleContext context) => _context = context;
    public void Activate()
    {
        if (IsActive) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTick;
        IsActive = true;
        _timer.Start();
        OnTick(null, EventArgs.Empty);
    }
    public void Apply() { }
    public void Deactivate()
    {
        IsActive = false;
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTick;
        _timer = null;
        Restore();
        _autoHideRecovered.Clear();
        Warning = null;
    }

    private void OnTick(object? sender, EventArgs args)
    {
        try
        {
            if (!Native.GetCursorPos(out var cursor)) return;
            var target = DesktopDisplays.ForPoint(cursor).Handle;
            var bars = new List<(IntPtr Window, uint Process, IntPtr Display)>();
            Native.EnumWindows((window, _) =>
            {
                if (!IsTaskbar(window)) return true;
                Native.GetWindowThreadProcessId(window, out var pid);
                bars.Add((window, pid, DesktopDisplays.ForWindow(window).Handle));
                return true;
            }, IntPtr.Zero);
            foreach (var window in _hidden.Keys.ToArray())
                if (!bars.Any(bar => bar.Window == window && bar.Process == _hidden[window])) _hidden.Remove(window);

            // In auto-hide mode Explorer controls the edge-reveal windows. Hiding
            // the taskbar HWND interferes with those windows even after it is shown.
            // Let Explorer reveal the bar under the pointer, while recovering any
            // windows left hidden by an interrupted earlier WinForge session.
            if (IsAutoHide())
            {
                Restore();
                _autoHideRecovered.RemoveWhere(window => bars.All(bar => bar.Window != window));
                foreach (var bar in bars)
                    if (_autoHideRecovered.Add(bar.Window) && !Native.IsWindowVisible(bar.Window))
                        ShowWindowAsync(bar.Window, 4);
                Warning = "Windows otomatik gizleme açık; görev çubuğunun görünürlüğünü Windows yönetiyor.";
                return;
            }
            _autoHideRecovered.Clear();

            // Never hide the last taskbar if Explorer has not created one on this display.
            if (!bars.Any(bar => bar.Display == target))
            {
                Restore();
                Warning = "Windows ayarlarında görev çubuğunun tüm ekranlarda gösterilmesini açın.";
                return;
            }
            // A previous WinForge process may have stopped before restoring its
            // hidden taskbars. Adopt those windows so they can be shown again.
            foreach (var bar in bars.Where(bar => !Native.IsWindowVisible(bar.Window)))
                _hidden.TryAdd(bar.Window, bar.Process);
            foreach (var bar in bars.Where(bar => bar.Display == target))
                if (_hidden.ContainsKey(bar.Window) && ShowWindowAsync(bar.Window, 4)) // SW_SHOWNOACTIVATE
                    _hidden.Remove(bar.Window);
            foreach (var bar in bars.Where(bar => bar.Display != target))
                if (Native.IsWindowVisible(bar.Window) && ShowWindowAsync(bar.Window, 0))
                    _hidden[bar.Window] = bar.Process;
            Warning = null;
        }
        catch (Exception error)
        {
            Restore();
            Warning = "Görev çubuğu monitöre göre gösterilemedi: " + error.Message;
            _context?.Log.Error(Warning, error);
        }
    }

    private void Restore()
    {
        foreach (var (window, process) in _hidden)
        {
            Native.GetWindowThreadProcessId(window, out var current);
            if (current == process && IsTaskbar(window)) ShowWindowAsync(window, 4);
        }
        _hidden.Clear();
    }
    private static bool IsTaskbar(IntPtr window) => Native.ClassNameOf(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    private static bool IsAutoHide()
    {
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        return (SHAppBarMessage(4 /* ABM_GETSTATE */, ref data).ToInt64() & 1 /* ABS_AUTOHIDE */) != 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public RECT Rectangle;
        public IntPtr Parameter;
    }
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
}
