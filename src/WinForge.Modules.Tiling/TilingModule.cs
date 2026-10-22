using System.Runtime.InteropServices;
using WinForge.Core;
using RECT = WinForge.Core.RECT;

namespace WinForge.Modules.Tiling;

public sealed class TilingModule : ITweakModule
{
    private const int GWL_EXSTYLE = -20;
    private const uint GW_OWNER = 4;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int SW_RESTORE = 9;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 0x0004;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow"
    };

    private readonly List<IntPtr> _hooks = new();
    private readonly WinEventDelegate _handler;
    private IModuleContext? _context;
    private Timer? _timer;
    private int _gap = 8;
    private int _layout;
    private int _masterPercent = 55;
    private bool _noMinimize;
    private int _busy;
    private int _pulses;
    private bool _dragging;
    private readonly Dictionary<IntPtr, RECT> _placed = new();
    private readonly List<IntPtr> _order = new();

    public TilingModule() => _handler = OnWinEvent;

    public string Id => "auto-tiling";
    public string Name => "Otomatik Döşeme";
    public string Category => "Verimlilik";
    public string Description => "Pencereleri Linux tiling WM gibi ekranı dolduracak şekilde dizer; yeni pencere açılınca düzen kendini yeniler.";
    public string? Warning => "Bazı uygulamalar minimum boyut kısıtı yüzünden boşluk bırakır. Tam ekran uygulamalar ve küçük araç pencereleri atlanır.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "layout",
            Label = "Yerleşim",
            Kind = SettingKind.Choice,
            Options = new[] { "Ana + yığın", "Izgara", "Sütunlar" },
            Default = "Ana + yığın",
            Description = "Ana + yığın: ilk pencere solda büyük, kalanlar sağda alt alta. Izgara: eşit hücreler. Sütunlar: pencereler yan yana eşit sütunlara bölünür."
        },
        new SettingDescriptor
        {
            Key = "master-percent",
            Label = "Ana pencere genişliği (%)",
            Kind = SettingKind.Number,
            Default = 55,
            Min = 30,
            Max = 80,
            Description = "Yalnızca Ana + yığın yerleşiminde geçerlidir."
        },
        new SettingDescriptor
        {
            Key = "prevent-minimize",
            Label = "Küçültmeyi engelle",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Küçültülen pencereler hemen geri açılır; tüm açık pencereler her zaman görünür kalır."
        },
        new SettingDescriptor
        {
            Key = "gap",
            Label = "Boşluk (px)",
            Kind = SettingKind.Number,
            Default = 8,
            Min = 0,
            Max = 48,
            Description = "Yalnızca pencereler arasındaki boşluk; ekran kenarlarına pencereler sıfıra sıfır oturur."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        _timer = new Timer(_ => Retile(), null, Timeout.Infinite, Timeout.Infinite);
        _order.Clear();
        ReadSettings();
        InstallHooks();
        Retile();
    }

    public void Deactivate()
    {
        IsActive = false;
        _timer?.Dispose();
        _timer = null;

        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();

        _context?.Log.Info("Otomatik döşeme kapatıldı.");
    }

    public void Apply()
    {
        ReadSettings();
        Retile();
    }

    private void ReadSettings()
    {
        _gap = Math.Clamp(_context?.Settings.GetInt("gap", 8) ?? 8, 0, 48);
        _masterPercent = Math.Clamp(_context?.Settings.GetInt("master-percent", 55) ?? 55, 30, 80);
        _noMinimize = _context?.Settings.GetBool("prevent-minimize", true) ?? true;

        var layout = _context?.Settings.GetString("layout", "Ana + yığın") ?? "Ana + yığın";
        _layout = layout.Equals("Izgara", StringComparison.CurrentCultureIgnoreCase) ? 1
            : layout.Equals("Sütunlar", StringComparison.CurrentCultureIgnoreCase) ? 2
            : 0;
    }

    private void InstallHooks()
    {
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_SHOW);
        Hook(EVENT_OBJECT_HIDE, EVENT_OBJECT_HIDE);

        _context?.Log.Info("Otomatik döşeme etkin.");
    }

    private void Hook(uint min, uint max)
    {
        var hook = SetWinEventHook(min, max, IntPtr.Zero, _handler, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
        else _context?.Log.Error($"WinEvent kancası kurulamadı: 0x{min:X}");
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint threadId, uint time)
    {
        if (idObject != 0) return;

        if (eventType == EVENT_SYSTEM_MOVESIZESTART)
        {
            _dragging = true;
            return;
        }

        if (eventType == EVENT_SYSTEM_MOVESIZEEND) _dragging = false;

        if (!IsActive) return;
        Interlocked.Exchange(ref _pulses, 5);
        Restart(40);
    }

    private void Retile()
    {
        if (!IsActive || _dragging) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;

        try
        {
            var windows = Collect();

            foreach (var group in windows.GroupBy(w => w.Monitor))
            {
                var handles = group.Select(w => w.Handle).ToList();
                var work = group.First().Work;
                if (_layout == 1) TileGrid(handles, work, _gap);
                else if (_layout == 2) TileColumns(handles, work, _gap);
                else TileMaster(handles, work, _gap);
            }
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Döşeme sırasında hata", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            if (IsActive && Interlocked.Decrement(ref _pulses) > 0) Restart(150);
        }
    }

    private void Restart(int delay)
    {
        try
        {
            _timer?.Change(delay, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private List<(IntPtr Handle, IntPtr Monitor, RECT Work)> Collect()
    {
        var result = new List<(IntPtr Handle, IntPtr Monitor, RECT Work)>();

        Native.EnumWindows((hwnd, _) =>
        {
            if (TryTileable(hwnd, out var monitor, out var work))
                result.Add((hwnd, monitor, work));
            return true;
        }, IntPtr.Zero);

        _order.RemoveAll(handle => !Native.IsWindow(handle));
        foreach (var window in result)
            if (!_order.Contains(window.Handle))
                _order.Add(window.Handle);

        var rank = new Dictionary<IntPtr, int>();
        for (var i = 0; i < _order.Count; i++) rank[_order[i]] = i;
        result.Sort((a, b) => rank[a.Handle].CompareTo(rank[b.Handle]));

        return result;
    }

    private bool TryTileable(IntPtr hwnd, out IntPtr monitor, out RECT work)
    {
        monitor = IntPtr.Zero;
        work = default;

        if (!Native.IsWindowVisible(hwnd)) return false;

        if (Native.IsIconic(hwnd))
        {
            if (!_noMinimize) return false;
            ShowWindow(hwnd, SW_RESTORE);
            if (Native.IsIconic(hwnd)) return false;
        }

        Native.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == (uint)Environment.ProcessId) return false;

        var extended = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((extended & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return false;

        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if (IgnoredClasses.Contains(Native.ClassNameOf(hwnd))) return false;
        if (IsCloaked(hwnd)) return false;
        if (Native.GetWindowTextLength(hwnd) == 0) return false;
        if (IsZoomed(hwnd)) return false;

        monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        if (!Native.GetWindowRect(hwnd, out var rect)) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 80 || height < 80) return false;

        if (Covers(rect, info.rcMonitor) && !IsOurPlacement(hwnd, rect)) return false;

        work = info.rcWork;
        return work.Right - work.Left >= 200 && work.Bottom - work.Top >= 200;
    }

    private bool IsOurPlacement(IntPtr hwnd, RECT rect) =>
        _placed.TryGetValue(hwnd, out var placed) &&
        placed.Left == rect.Left && placed.Top == rect.Top &&
        placed.Right == rect.Right && placed.Bottom == rect.Bottom;

    private static bool Covers(RECT window, RECT monitor) =>
        window.Left <= monitor.Left + 2 && window.Top <= monitor.Top + 2 &&
        window.Right >= monitor.Right - 2 && window.Bottom >= monitor.Bottom - 2;

    private void TileMaster(IReadOnlyList<IntPtr> windows, RECT work, int gap)
    {
        var area = work;
        if (windows.Count == 1)
        {
            Move(windows[0], area);
            return;
        }

        var width = area.Right - area.Left;
        var height = area.Bottom - area.Top;
        var masterWidth = (int)((long)(width - gap) * _masterPercent / 100);
        if (masterWidth < 160) masterWidth = (width - gap) / 2;
        if (masterWidth < 80) return;

        Move(windows[0], new RECT
        {
            Left = area.Left,
            Top = area.Top,
            Right = area.Left + masterWidth,
            Bottom = area.Bottom
        });

        var stackLeft = area.Left + masterWidth + gap;
        var stackWidth = area.Right - stackLeft;
        if (stackWidth < 80) return;

        var heights = SplitSizes(height, windows.Count - 1, gap);
        var top = area.Top;
        for (var i = 1; i < windows.Count; i++)
        {
            var bottom = top + heights[i - 1];
            Move(windows[i], new RECT
            {
                Left = stackLeft,
                Top = top,
                Right = stackLeft + stackWidth,
                Bottom = bottom
            });
            top = bottom + gap;
        }
    }

    private void TileColumns(IReadOnlyList<IntPtr> windows, RECT work, int gap)
    {
        var widths = SplitSizes(work.Right - work.Left, windows.Count, gap);
        var left = work.Left;

        for (var i = 0; i < windows.Count; i++)
        {
            var right = left + widths[i];
            Move(windows[i], new RECT
            {
                Left = left,
                Top = work.Top,
                Right = right,
                Bottom = work.Bottom
            });
            left = right + gap;
        }
    }

    private void TileGrid(IReadOnlyList<IntPtr> windows, RECT work, int gap)
    {
        var area = work;
        var count = windows.Count;
        var width = area.Right - area.Left;
        var height = area.Bottom - area.Top;
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        if (columns < 1 || rows < 1) return;

        var heights = SplitSizes(height, rows, gap);
        var top = area.Top;
        var index = 0;

        for (var row = 0; row < rows; row++)
        {
            var inRow = Math.Min(columns, count - index);
            var widths = SplitSizes(width, inRow, gap);
            var left = area.Left;

            for (var column = 0; column < inRow; column++)
            {
                var right = left + widths[column];
                Move(windows[index++], new RECT
                {
                    Left = left,
                    Top = top,
                    Right = right,
                    Bottom = top + heights[row]
                });
                left = right + gap;
            }

            top += heights[row] + gap;
        }
    }

    private static int[] SplitSizes(int total, int count, int gap)
    {
        var sizes = new int[count];
        if (count <= 0) return sizes;

        var available = total - gap * (count - 1);
        if (available < count) available = count;

        var previous = 0;
        for (var i = 0; i < count; i++)
        {
            var edge = (int)((long)available * (i + 1) / count);
            sizes[i] = edge - previous;
            previous = edge;
        }

        return sizes;
    }

    private void Move(IntPtr hwnd, RECT target)
    {
        if (!Native.GetWindowRect(hwnd, out var current)) return;

        var windowTarget = target;
        if (DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame,
                Marshal.SizeOf<RECT>()) == 0 && frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            windowTarget.Left -= frame.Left - current.Left;
            windowTarget.Top -= frame.Top - current.Top;
            windowTarget.Right += current.Right - frame.Right;
            windowTarget.Bottom += current.Bottom - frame.Bottom;
        }

        if (_placed.Count > 64)
        {
            foreach (var stale in _placed.Keys.Where(h => !Native.IsWindow(h)).ToList())
                _placed.Remove(stale);
        }

        if (current.Left == windowTarget.Left && current.Top == windowTarget.Top &&
            current.Right == windowTarget.Right && current.Bottom == windowTarget.Bottom)
        {
            _placed[hwnd] = current;
            return;
        }

        Native.SetWindowPos(hwnd, IntPtr.Zero, windowTarget.Left, windowTarget.Top,
            windowTarget.Right - windowTarget.Left, windowTarget.Bottom - windowTarget.Top,
            SWP_NOZORDER | Native.SWP_NOACTIVATE);

        _placed[hwnd] = windowTarget;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint threadId, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attribute, out RECT rect, int size);
}
