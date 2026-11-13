using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Keeps the open Start and Search surfaces on the pointer's display.</summary>
public sealed class StartMenuPlacementModule : IModuleComponent
{
    private const uint ObjectHide = 0x8003;
    private const uint ObjectCloaked = 0x8017;
    private const uint ObjectUncloaked = 0x8018;
    private readonly Func<IntPtr, bool> _isStart;
    private readonly Func<IntPtr, bool> _isSearch;
    private readonly Dictionary<IntPtr, Opening> _openings = [];
    private WindowEventObserver? _objects;
    private WindowEventObserver? _foreground;
    private WindowEventObserver? _cloaking;
    private DispatcherTimer? _timer;
    private IModuleContext? _context;
    public StartMenuPlacementModule() : this(IsStartWindow, IsSearchWindow) { }
    internal StartMenuPlacementModule(Func<IntPtr, bool> isStart) : this(isStart, IsSearchWindow) { }
    internal StartMenuPlacementModule(Func<IntPtr, bool> isStart, Func<IntPtr, bool> isSearch)
    {
        _isStart = isStart;
        _isSearch = isSearch;
    }
    public string Id => "start-menu-placement";
    public string Name => "Başlat Menüsü Konumu";
    public string Category => "Başlat";
    public string Description => "Başlat menüsü ve arama açıkken farenin bulunduğu monitörü takip eder.";
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
            _cloaking = new(ObjectCloaked, ObjectUncloaked, OnEvent, Report);
            IsActive = true;
            _timer.Start();
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
            finally
            {
                _foreground = null;
                try { _cloaking?.Dispose(); }
                finally { _cloaking = null; _openings.Clear(); Warning = null; }
            }
        }
    }

    private void OnEvent(WindowEventObserver.WindowEvent change)
    {
        if (!IsActive) return;
        // Focus can temporarily move to another shell window while Start is
        // still presented. Visibility/cloaking, not focus, ends tracking.
        if (change.Kind is WindowEventObserver.ObjectDestroy or ObjectHide or ObjectCloaked)
        {
            EndOpening(change.Window);
            return;
        }
        if (change.Kind is not (WindowEventObserver.ObjectShow or WindowEventObserver.Foreground or ObjectUncloaked)) return;
        TrackOpening(change.Window);
    }

    private void TrackOpening(IntPtr window)
    {
        if (window == IntPtr.Zero || _openings.ContainsKey(window) || !IsPresented(window)) return;
        if (!_isSearch(window) && !_isStart(window)) return;
        if (!Native.GetCursorPos(out var cursor)) return;
        _openings[window] = new();
        Move(window, DesktopDisplays.ForPoint(cursor));
    }

    private void EndOpening(IntPtr window)
    {
        _openings.Remove(window);
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (!IsActive) return;
        DesktopDisplay? target = null;
        try { if (Native.GetCursorPos(out var cursor)) target = DesktopDisplays.ForPoint(cursor); }
        catch (Exception error) { Report(error); }
        foreach (var (window, opening) in _openings.ToArray())
        {
            if (!Native.IsWindow(window) || !IsPresented(window)) { EndOpening(window); continue; }
            if (opening.Blocked) continue;
            try { if (target is not null) Move(window, target); }
            catch (Exception error) { opening.Blocked = true; Report(error); }
        }
        // Foreground polling also catches shell openings with no SHOW notification.
        try { TrackOpening(Native.GetForegroundWindow()); }
        catch (Exception error) { Report(error); }
    }

    private static bool IsPresented(IntPtr window) => Native.IsWindowVisible(window) &&
        (DwmGetWindowAttribute(window, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(int)) != 0 || cloaked == 0);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    private void Move(IntPtr window, DesktopDisplay targetDisplay)
    {
        if (!Native.GetWindowRect(window, out var source) || source.Right <= source.Left || source.Bottom <= source.Top) return;
        var display = DesktopDisplays.ForWindow(window);
        if (display.Handle == targetDisplay.Handle) return;
        var target = CalculateBounds(source, display.WorkArea, targetDisplay.WorkArea);
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
        => IsShellWindow(window, "StartMenuExperienceHost");

    private static bool IsSearchWindow(IntPtr window)
        => IsShellWindow(window, "SearchHost", "SearchApp", "SearchUI");

    private static bool IsShellWindow(IntPtr window, params string[] processNames)
    {
        if (Native.GetAncestor(window, Native.GA_ROOT) != window || Native.ClassNameOf(window) != "Windows.UI.Core.CoreWindow") return false;
        Native.GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    private void Report(Exception error)
    {
        Warning = "Başlat menüsü farenin monitörüne taşınamadı: " + error.Message;
        _context?.Log.Error(Warning, error);
    }

    private sealed class Opening
    {
        internal bool Blocked { get; set; }
    }
}
