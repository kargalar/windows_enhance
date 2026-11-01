using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Captures a display once per Start opening and briefly counters shell layout resets.</summary>
public sealed class StartMenuPlacementModule : IModuleComponent
{
    private const uint ObjectHide = 0x8003;
    private readonly Func<IntPtr, bool> _isStart;
    private readonly Dictionary<IntPtr, Opening> _openings = [];
    private WindowEventObserver? _objects;
    private WindowEventObserver? _foreground;
    private DispatcherTimer? _timer;
    private IModuleContext? _context;
    public StartMenuPlacementModule() : this(IsStartWindow) { }
    internal StartMenuPlacementModule(Func<IntPtr, bool> isStart) => _isStart = isStart;
    public string Id => "start-menu-placement";
    public string Name => "Başlat Menüsü Konumu";
    public string Category => "Başlat";
    public string Description => "Başlat menüsünü açıldığı anda farenin bulunduğu monitöre taşır.";
    public string? Warning { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => [];
    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        try
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(60) };
            _timer.Tick += OnTick;
            _objects = new(WindowEventObserver.ObjectDestroy, WindowEventObserver.ObjectLocationChange, OnEvent, Report);
            _foreground = new(WindowEventObserver.Foreground, WindowEventObserver.Foreground, OnEvent, Report);
            IsActive = true;
        }
        catch { Deactivate(); throw; }
    }

    public void Apply() { }
    public void Deactivate()
    {
        IsActive = false;
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTick;
        _timer = null;
        try { _objects?.Dispose(); }
        finally
        {
            _objects = null;
            try { _foreground?.Dispose(); }
            finally { _foreground = null; _openings.Clear(); Warning = null; }
        }
    }

    private void OnEvent(WindowEventObserver.WindowEvent change)
    {
        if (!IsActive) return;
        if (change.Kind is WindowEventObserver.ObjectDestroy or ObjectHide)
        {
            _openings.Remove(change.Window);
            return;
        }
        if (change.Kind is not (WindowEventObserver.ObjectShow or WindowEventObserver.Foreground)) return;
        if (_openings.ContainsKey(change.Window) || !Native.IsWindowVisible(change.Window) || !_isStart(change.Window)) return;
        if (!Native.GetCursorPos(out var cursor)) return;
        _openings[change.Window] = new(DesktopDisplays.ForPoint(cursor), Stopwatch.StartNew());
        _timer?.Start();
        Move(change.Window, _openings[change.Window]);
    }

    private void OnTick(object? sender, EventArgs args)
    {
        var pending = false;
        foreach (var (window, opening) in _openings.ToArray())
        {
            if (!Native.IsWindow(window) || !Native.IsWindowVisible(window)) { _openings.Remove(window); continue; }
            if (opening.Clock.ElapsedMilliseconds > 750 || opening.Blocked) continue;
            pending = true;
            try { Move(window, opening); }
            catch (Exception error) { opening.Blocked = true; Report(error); }
        }
        if (!pending) _timer?.Stop();
    }

    private void Move(IntPtr window, Opening opening)
    {
        if (!Native.GetWindowRect(window, out var source) || source.Right <= source.Left || source.Bottom <= source.Top) return;
        var display = DesktopDisplays.ForWindow(window);
        if (display.Handle == opening.Display.Handle) return;
        var target = CalculateBounds(source, display.WorkArea, opening.Display.WorkArea);
        if (!Native.SetWindowPos(window, IntPtr.Zero, target.Left, target.Top, target.Right - target.Left, target.Bottom - target.Top,
                0x0004 | Native.SWP_NOACTIVATE))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Warning = null;
    }

    internal static RECT CalculateBounds(RECT source, RECT from, RECT to)
    {
        var width = Math.Min(source.Right - source.Left, to.Right - to.Left);
        var height = Math.Min(source.Bottom - source.Top, to.Bottom - to.Top);
        var available = from.Right - from.Left - (source.Right - source.Left);
        var fraction = available > 0 ? Math.Clamp((source.Left - from.Left) / (double)available, 0, 1) : 0.5;
        var left = to.Left + (int)Math.Round((to.Right - to.Left - width) * fraction);
        var gap = Math.Clamp(from.Bottom - source.Bottom, 0, 32);
        var top = Math.Max(to.Top, to.Bottom - height - gap);
        return new() { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    private static bool IsStartWindow(IntPtr window)
    {
        if (Native.GetAncestor(window, Native.GA_ROOT) != window || Native.ClassNameOf(window) != "Windows.UI.Core.CoreWindow") return false;
        Native.GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    private void Report(Exception error)
    {
        Warning = "Başlat menüsü farenin monitörüne taşınamadı: " + error.Message;
        _context?.Log.Error(Warning, error);
    }

    private sealed class Opening(DesktopDisplay display, Stopwatch clock)
    {
        internal DesktopDisplay Display { get; } = display;
        internal Stopwatch Clock { get; } = clock;
        internal bool Blocked { get; set; }
    }
}
