using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class SquareCornersModule : ITweakModule
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDoNotRound = 1;
    private const int DwmwcpRoundSmall = 3;

    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private readonly List<IntPtr> _hooks = new();
    private readonly Dictionary<IntPtr, int> _applied = new();
    private readonly WinEventDelegate _handler;
    private IModuleContext? _context;
    private int _preference = DwmwcpDoNotRound;

    public SquareCornersModule() => _handler = OnWinEvent;

    public string Id => "square-corners";
    public string Name => "Pencere Köşeleri";
    public string Category => "Görünüm";
    public string Description => "Windows 11'in yuvarlak pencere köşelerini kapatır veya küçük yuvarlak köşelere çevirir.";
    public string? Warning => "Bazı uygulamalar kendi köşelerini çizer; onlar etkilenmez.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "style",
            Label = "Köşe biçimi",
            Kind = SettingKind.Choice,
            Options = new[] { "Kapalı", "Küçük yuvarlak" },
            Default = "Kapalı"
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        _preference = ReadPreference();

        Hook(EventObjectCreate, EventObjectShow);
        Hook(EventSystemForeground, EventSystemForeground);
        ApplyAll();

        _context?.Log.Info("Pencere köşesi ayarı etkin.");
    }

    public void Deactivate()
    {
        IsActive = false;

        foreach (var (window, original) in _applied)
            if (Native.IsWindow(window)) SetPreference(window, original);
        _applied.Clear();

        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
    }

    public void Apply()
    {
        if (!IsActive) return;

        _preference = ReadPreference();
        foreach (var window in _applied.Keys.ToList())
        {
            if (!Native.IsWindow(window))
            {
                _applied.Remove(window);
                continue;
            }

            SetPreference(window, _preference);
        }

        ApplyAll();
    }

    private int ReadPreference() =>
        string.Equals(_context?.Settings.GetString("style", "Kapalı"), "Küçük yuvarlak",
            StringComparison.CurrentCultureIgnoreCase)
            ? DwmwcpRoundSmall
            : DwmwcpDoNotRound;

    private void ApplyAll()
    {
        var ownProcess = (uint)Environment.ProcessId;

        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId != ownProcess) Apply(hwnd);
            return true;
        }, IntPtr.Zero);
    }

    private void Apply(IntPtr hwnd)
    {
        if (!IsActive || hwnd == IntPtr.Zero) return;
        if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd)) return;
        if (Native.GetWindowTextLength(hwnd) == 0) return;
        if (IgnoredClasses.Contains(Native.ClassNameOf(hwnd))) return;

        if (!_applied.ContainsKey(hwnd))
        {
            if (DwmGetWindowAttribute(hwnd, DwmwaWindowCornerPreference, out var original, sizeof(int)) < 0) return;
            if (SetPreference(hwnd, _preference) >= 0) _applied.Add(hwnd, original);
        }
        else SetPreference(hwnd, _preference);
    }

    private static int SetPreference(IntPtr hwnd, int value) =>
        Native.DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref value, sizeof(int));

    private void Hook(uint min, uint max)
    {
        var hook = SetWinEventHook(min, max, IntPtr.Zero, _handler, 0, 0,
            WineventOutOfContext | WineventSkipOwnProcess);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
        else _context?.Log.Error($"Pencere köşesi kancası kurulamadı: 0x{min:X}");
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint threadId, uint time)
    {
        if (!IsActive || idObject != 0 || hwnd == IntPtr.Zero) return;
        if (eventType == EventObjectDestroy)
        {
            _applied.Remove(hwnd);
            return;
        }

        var root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        Apply(root == IntPtr.Zero ? hwnd : root);
    }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint threadId, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int valueSize);
}
