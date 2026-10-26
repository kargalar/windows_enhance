using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
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
    private int _generation;

    public string Id => "browser-fullscreen";
    public string Name => "Tarayıcı Tam Ekran İpucu";
    public string Category => "Verimlilik";
    public string Description => "Chromium tarayıcılarının tam ekrandan çıkış ipucunu gizler.";
    public string? Warning => _error;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [new() { Key = "processes", Label = "Tarayıcı işlem adları", Kind = SettingKind.Text, Default = DefaultProcesses,
        Description = "Noktalı virgülle ayırın. Yalnızca tam ekran tarayıcının üst ortasındaki etkileşimsiz çıkış ipucu hedeflenir." }];

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
        _generation++;
        RestoreHidden();
        _pending.Clear();
        _error = null;
        _processes = new((_context?.Settings.GetString("processes", DefaultProcesses) ?? DefaultProcesses)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name), StringComparer.OrdinalIgnoreCase);
        if (IsActive) Native.EnumWindows((window, _) => { QueueWindow(new(WindowEventObserver.ObjectShow, window)); return true; }, IntPtr.Zero);
    }

    private void QueueWindow(WindowEventObserver.WindowEvent observed)
    {
        var window = observed.Window;
        if (observed.Kind == WindowEventObserver.ObjectDestroy) { _pending.Remove(window); _hidden.Remove(window); return; }
        if (!IsActive || Native.ClassNameOf(window) != BrowserWindowClass || _pending.Count >= 128 || !_pending.Add(window)) return;
        var generation = _generation;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            try { if (IsActive && generation == _generation) await TryHideAsync(window, generation); }
            catch (Exception exception)
            {
                _error = exception.Message;
                _context?.Log.Error("Tarayıcı çıkış ipucu gizlenemedi.", exception);
            }
            finally { if (generation == _generation) _pending.Remove(window); }
        }));
    }

    private async Task<bool> TryHideAsync(IntPtr window, int generation)
    {
        if (!Native.IsWindowVisible(window) || !IsExitBubble(window)) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        _hidden[window] = processId;
        var deadline = Environment.TickCount64 + 500;
        do
        {
            if (!IsActive || generation != _generation || !Native.IsWindow(window)) return false;
            Native.GetWindowThreadProcessId(window, out var currentProcess);
            if (currentProcess != processId || !IsExitBubble(window)) return false;
            if (!ShowWindowAsync(window, Hide)) throw new InvalidOperationException("Tarayıcı gizleme isteğini kabul etmedi.");
            await Task.Delay(20);
            if (!Native.IsWindowVisible(window)) return true;
        } while (Environment.TickCount64 < deadline);
        throw new InvalidOperationException("Tam ekran çıkış ipucunun gizlendiği doğrulanamadı.");
    }

    private bool IsExitBubble(IntPtr window)
    {
        if (Native.ClassNameOf(window) != BrowserWindowClass || !Native.GetWindowRect(window, out var bubble)) return false;
        var style = Native.GetWindowLongPtr(window, StyleIndex).ToInt64();
        var extended = Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE).ToInt64();
        if ((style & PopupStyle) == 0 || (style & SystemMenuStyle) != 0 || (extended & PopupExtendedStyles) != PopupExtendedStyles) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        try { using var process = Process.GetProcessById((int)processId); if (!_processes.Contains(process.ProcessName)) return false; }
        catch (ArgumentException) { return false; }
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
        _generation++;
        _events?.Dispose();
        _events = null;
        _pending.Clear();
        RestoreHidden();
    }

    private void RestoreHidden()
    {
        foreach (var (window, processId) in _hidden)
        {
            Native.GetWindowThreadProcessId(window, out var currentProcess);
            if (Native.IsWindow(window) && currentProcess == processId && IsExitBubble(window)) ShowWindowAsync(window, ShowWithoutActivation);
        }
        _hidden.Clear();
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindowAsync(IntPtr window, int command);
}
