using System.Diagnostics;
using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class BrowserFullscreenModule : ITweakModule
{
    private const string BrowserWindowClass = "Chrome_WidgetWin_1";
    private const string DefaultProcesses = "chrome;msedge;opera;brave;comet;Maxthon;whale;aloha;thorium";
    private const long PopupStyle = 0x80000000;
    private const long SystemMenuStyle = 0x00080000;
    private const long PopupExtendedStyles = 0x080000A8;
    private const int StyleIndex = -16;
    private const int Hide = 0;
    private const int ShowWithoutActivation = 4;
    private IModuleContext? _context;
    private WindowEventObserver? _events;
    private readonly HashSet<IntPtr> _pending = [];
    private readonly Dictionary<IntPtr, uint> _hidden = [];
    private HashSet<string> _processes = new(StringComparer.OrdinalIgnoreCase);
    private string? _error;

    public string Id => "browser-fullscreen";
    public string Name => "Chrome Enhance";
    public string Category => "Verimlilik";
    public string Description => "Chrome ve Chromium tabanlı tarayıcılar için geliştirmeler.";
    public string? Warning => _error;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [new() { Key = "hide-fullscreen-hint", Label = "Tam ekran çıkış uyarısını gizle", Kind = SettingKind.Toggle, Default = true,
        Section = "Tam ekran", Description = "Chrome tam ekrana geçtiğinde Esc ile çıkış uyarısını gizler." },
     new() { Key = "processes", Label = "Tarayıcı işlem adları", Kind = SettingKind.Text, Default = DefaultProcesses,
        Section = "Tam ekran", Description = "Noktalı virgülle ayırın. Uyarı gizleme bu işlemlere uygulanır." }];

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate()
    {
        if (IsActive) return;
        Apply();
        _events = new(WindowEventObserver.ObjectDestroy, WindowEventObserver.ObjectLocationChange, QueueWindow,
            exception => _context?.Log.Error("Tarayıcı pencere gözlemi başarısız.", exception));
        IsActive = true;
        Native.EnumWindows((window, _) => { QueueWindow(new(WindowEventObserver.ObjectShow, window)); return true; }, IntPtr.Zero);
    }
    public void Apply()
    {
        _pending.Clear();
        _error = null;
        _processes = new((_context?.Settings.GetString("processes", DefaultProcesses) ?? DefaultProcesses)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name), StringComparer.OrdinalIgnoreCase);
        RestoreHidden(keepMatchingTargets: IsFeatureEnabled);
        if (IsActive && IsFeatureEnabled) Native.EnumWindows((window, _) => { QueueWindow(new(WindowEventObserver.ObjectShow, window)); return true; }, IntPtr.Zero);
    }

    private bool IsFeatureEnabled => _context?.Settings.GetBool("hide-fullscreen-hint", true) ?? true;

    private void QueueWindow(WindowEventObserver.WindowEvent observed)
    {
        var window = observed.Window;
        if (observed.Kind == WindowEventObserver.ObjectDestroy) { _pending.Remove(window); _hidden.Remove(window); return; }
        if (!IsActive || !IsFeatureEnabled || Native.ClassNameOf(window) != BrowserWindowClass || _pending.Count >= 128 || !_pending.Add(window)) return;
        try { TryHide(window); }
        catch (Exception exception)
        {
            _error = exception.Message;
            _context?.Log.Error("Tarayıcı çıkış ipucu gizlenemedi.", exception);
        }
        finally { _pending.Remove(window); }
    }

    private bool TryHide(IntPtr window)
    {
        if (!Native.IsWindowVisible(window) || !IsExitBubble(window)) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        _hidden[window] = processId;
        Native.GetWindowThreadProcessId(window, out var currentProcess);
        if (!IsActive || currentProcess != processId || !IsExitBubble(window)) return false;
        if (!ShowWindow(window, Hide)) throw new InvalidOperationException("Tarayıcı uyarı penceresini gizleyemedi.");
        if (Native.IsWindowVisible(window)) throw new InvalidOperationException("Tam ekran uyarı penceresi gizlenemedi.");
        return true;
    }

    private bool IsExitBubble(IntPtr window)
    {
        if (!IsExitBubbleShape(window)) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        try { using var process = Process.GetProcessById((int)processId); return _processes.Contains(process.ProcessName); }
        catch (ArgumentException) { return false; }
    }

    private bool IsExitBubbleShape(IntPtr window)
    {
        if (Native.ClassNameOf(window) != BrowserWindowClass || !Native.GetWindowRect(window, out var bubble)) return false;
        var style = Native.GetWindowLongPtr(window, StyleIndex).ToInt64();
        var extended = Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE).ToInt64();
        if ((style & PopupStyle) == 0 || (style & SystemMenuStyle) != 0 || (extended & PopupExtendedStyles) != PopupExtendedStyles) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        var found = false;
        Native.EnumWindows((candidate, _) =>
        {
            if (candidate == window || !Native.IsWindowVisible(candidate) || Native.IsIconic(candidate) || Native.ClassNameOf(candidate) != BrowserWindowClass) return true;
            Native.GetWindowThreadProcessId(candidate, out var ownerProcess);
            if (ownerProcess != processId || !Native.GetWindowRect(candidate, out var browser)) return true;
            var display = DesktopDisplays.ForWindow(candidate).Bounds;
            if (!Covers(browser, display)) return true;
            var scale = Math.Max(1, Native.GetDpiForWindow(candidate)) / 96.0;
            var width = bubble.Right - bubble.Left;
            var height = bubble.Bottom - bubble.Top;
            var centerError = Math.Abs((bubble.Left + bubble.Right) / 2.0 - (browser.Left + browser.Right) / 2.0);
            found = width >= 80 * scale && width < (browser.Right - browser.Left) * 0.95 &&
                height >= 16 * scale && height <= 180 * scale && bubble.Top >= browser.Top &&
                bubble.Top < browser.Top + 180 * scale && centerError <= 24 * scale;
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    private static bool Covers(RECT window, RECT screen) => Math.Abs(window.Left - screen.Left) <= 2 &&
        Math.Abs(window.Top - screen.Top) <= 2 && Math.Abs(window.Right - screen.Right) <= 2 && Math.Abs(window.Bottom - screen.Bottom) <= 2;

    public void Deactivate()
    {
        IsActive = false;
        _events?.Dispose();
        _events = null;
        _pending.Clear();
        RestoreHidden();
    }

    private void RestoreHidden(bool keepMatchingTargets = false)
    {
        var stillHidden = new Dictionary<IntPtr, uint>();
        foreach (var (window, processId) in _hidden)
        {
            Native.GetWindowThreadProcessId(window, out var currentProcess);
            if (!Native.IsWindow(window) || currentProcess != processId || !IsExitBubbleShape(window)) continue;
            if (keepMatchingTargets)
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    if (_processes.Contains(process.ProcessName)) { stillHidden[window] = processId; continue; }
                }
                catch (ArgumentException) { continue; }
            }
            ShowWindow(window, ShowWithoutActivation);
        }
        _hidden.Clear();
        foreach (var pair in stillHidden) _hidden[pair.Key] = pair.Value;
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window, int command);
}
