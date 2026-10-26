using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WinForge.Core;
using RECT = WinForge.Core.RECT;

namespace WinForge.Modules.Tiling;

public sealed class TilingModule : ITweakModule, IWindowZoneProvider
{
    private const int GWL_EXSTYLE = -20;
    private const uint GW_OWNER = 4;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int SW_RESTORE = 9;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const uint VkPageUp = 0x21;
    private const uint VkPageDown = 0x22;
    private const uint VkLeft = 0x25;
    private const uint VkUp = 0x26;
    private const uint VkRight = 0x27;
    private const uint VkDown = 0x28;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 0x0004;
    private const int AutoTileMode = 0;
    private const int ZoneMoveMode = 1;

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
    private IDisposable? _nextZoneWindowHotkey;
    private IDisposable? _previousZoneWindowHotkey;
    private readonly List<IDisposable> _zoneMoveHotkeys = new();
    private int _gap = 8;
    private int _layout;
    private int _masterPercent = 55;
    private bool _noMinimize;
    private int _busy;
    private int _pulses;
    private bool _dragging;
    private readonly Dictionary<IntPtr, RECT> _placed = new();
    private readonly Dictionary<string, List<IntPtr>> _zoneCycleOrders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IntPtr> _order = new();
    private readonly HashSet<IntPtr> _orderedHandles = new();
    private ZonePreviewWindow? _zonePreview;
    private DispatcherTimer? _zoneTimer;
    private IntPtr _draggedWindow;
    private bool _zoneDragArmed;
    private int _selectedZone = -1;
    private readonly HashSet<int> _selectedZones = new();
    private int _mode;
    private int _zoneLayout;
    private int _zoneColumns = 3;
    private int _zoneRows = 2;
    private int _defaultZoneLayout;
    private int _defaultZoneColumns = 3;
    private int _defaultZoneRows = 2;
    private Dictionary<string, (int Columns, int Rows)> _monitorGridProfiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _requireShiftForZones = true;
    private bool _keyboardZoneSelectionActive;
    private POINT _lastZoneCursor;
    private int _lastZoneDirectionMask;

    public TilingModule() => _handler = OnWinEvent;

    public string Id => "auto-tiling";
    public string Name => "Pencere Yönetimi";
    public string Category => "Verimlilik";
    public string Description => "Otomatik tam ekran döşeme ile FancyZones benzeri sürükle-bırak bölge yerleşimi arasında seçim yapın.";
    public string? Warning => "Otomatik döşeme bazı uygulamaların minimum boyut kısıtları nedeniyle boşluk bırakabilir. Tam ekran uygulamalar ve küçük araç pencereleri atlanır.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "mode",
            Label = "Çalışma modu",
            Kind = SettingKind.Choice,
            Options = new[] { "Otomatik tam ekran döşeme", "Bölgeli pencere taşıma (FancyZones)" },
            Default = "Otomatik tam ekran döşeme",
            Description = "Otomatik döşeme açık pencereleri yerleştirir. Bölgeli modda pencereyi sürüklerken Shift tuşunu basılı tutup vurgulanan bölgeye bırakın; ok tuşlarıyla seçin, Ctrl+ok ile bitişik bölgeleri birleştirip pencereyi genişletin."
        },
        new SettingDescriptor
        {
            Key = "zone-activation",
            Label = "Bölge etkinleştirme",
            Kind = SettingKind.Choice,
            Options = new[] { "Shift tuşuyla etkinleştir", "Sürüklerken otomatik göster" },
            Default = "Shift tuşuyla etkinleştir",
            Description = "İkinci seçenek bölgeleri pencereyi sürüklemeye başlar başlamaz gösterir."
        },
        new SettingDescriptor
        {
            Key = "zone-window-switching",
            Label = "Aynı bölgedeki pencereler arasında geçiş",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Bölgeli modda Win+PageDown ile sonraki, Win+PageUp ile önceki pencereyi etkinleştirir."
        },
        new SettingDescriptor
        {
            Key = "zone-keyboard-move-enabled",
            Label = "Etkin pencereyi kısayolla komşu bölgeye taşı",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Win+Ctrl+Alt+ok tuşları etkin pencereyi aynı monitördeki bitişik bölgeye taşır; bir sürükleme başlatmanız gerekmez."
        },
        new SettingDescriptor
        {
            Key = "zone-layout",
            Label = "Bölge düzeni",
            Kind = SettingKind.Choice,
            Options = new[] { "İki sütun", "Üç sütun", "Dört bölge", "Özel ızgara" },
            Default = "İki sütun",
            Description = "Bölgeli modda kullanılacak hazır ya da özel ızgara düzeni; ok komşu bölgeyi seçer, Ctrl+ok bitişik bölgeleri birleştirir."
        },
        new SettingDescriptor
        {
            Key = "zone-columns",
            Label = "Özel düzen sütunları",
            Kind = SettingKind.Number,
            Default = 3,
            Min = 1,
            Max = 6,
            Description = "Özel ızgaranın yatay bölge sayısı (monitör başına en fazla 6 sütun)."
        },
        new SettingDescriptor
        {
            Key = "zone-rows",
            Label = "Özel düzen satırları",
            Kind = SettingKind.Number,
            Default = 2,
            Min = 1,
            Max = 4,
            Description = "Özel ızgaranın dikey bölge sayısı (monitör başına en fazla 4 satır)."
        },
        new SettingDescriptor
        {
            Key = "zone-monitor-grid-profiles",
            Label = "Monitör başına ızgara profilleri",
            Kind = SettingKind.Text,
            Default = "",
            Description = "İsteğe bağlı özel ızgaraları ekrana göre tanımlayın: DISPLAY1=3x2; DISPLAY2=2x2. Windows Ekran Ayarları'ndaki ekran kimliğini kullanın; tanımlanmayan ekranlar genel bölge düzenini kullanır."
        },
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
        _zoneTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _zoneTimer.Tick += (_, _) => UpdateZonePreview();
        _order.Clear();
        _orderedHandles.Clear();
        _zoneCycleOrders.Clear();
        ReadSettings();
        RegisterZoneWindowSwitchingHotkeys();
        InstallHooks();
        Retile();
    }

    public void Deactivate()
    {
        IsActive = false;
        _timer?.Dispose();
        _timer = null;
        _nextZoneWindowHotkey?.Dispose();
        _nextZoneWindowHotkey = null;
        _previousZoneWindowHotkey?.Dispose();
        _previousZoneWindowHotkey = null;
        foreach (var hotkey in _zoneMoveHotkeys) hotkey.Dispose();
        _zoneMoveHotkeys.Clear();
        _zoneTimer?.Stop();
        _zoneTimer = null;
        _zoneCycleOrders.Clear();
        _zonePreview?.ClosePreview();
        _zonePreview?.Close();
        _zonePreview = null;
        _draggedWindow = IntPtr.Zero;

        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();

        _context?.Log.Info("Otomatik döşeme kapatıldı.");
    }

    public void Apply()
    {
        ReadSettings();
        _zoneCycleOrders.Clear();
        RegisterZoneWindowSwitchingHotkeys();
        if (_mode != ZoneMoveMode)
        {
            _zoneTimer?.Stop();
            _zonePreview?.ClosePreview();
        }
        Retile();
    }

    private void ReadSettings()
    {
        var settings = _context?.Settings;
        var mode = settings?.GetString("mode", "Otomatik tam ekran döşeme") ?? "Otomatik tam ekran döşeme";
        _mode = mode.Equals("Bölgeli pencere taşıma (FancyZones)", StringComparison.CurrentCultureIgnoreCase) ||
                mode.Equals("Shift ile bölgeye taşı", StringComparison.CurrentCultureIgnoreCase)
            ? ZoneMoveMode
            : AutoTileMode;

        var zoneLayout = settings?.GetString("zone-layout", "İki sütun") ?? "İki sütun";
        _zoneLayout = zoneLayout.Equals("Üç sütun", StringComparison.CurrentCultureIgnoreCase) ? 1
            : zoneLayout.Equals("Dört bölge", StringComparison.CurrentCultureIgnoreCase) ? 2
            : zoneLayout.Equals("Özel ızgara", StringComparison.CurrentCultureIgnoreCase) ? 3
            : 0;
        _zoneColumns = Math.Clamp(settings?.GetInt("zone-columns", 3) ?? 3, 1, 6);
        _zoneRows = Math.Clamp(settings?.GetInt("zone-rows", 2) ?? 2, 1, 4);
        _defaultZoneLayout = _zoneLayout;
        _defaultZoneColumns = _zoneColumns;
        _defaultZoneRows = _zoneRows;
        _monitorGridProfiles = ParseMonitorGridProfiles(settings?.GetString("zone-monitor-grid-profiles", "") ?? "");
        var zoneActivation = settings?.GetString("zone-activation", "Shift tuşuyla etkinleştir") ?? "Shift tuşuyla etkinleştir";
        _requireShiftForZones = !zoneActivation.Equals("Sürüklerken otomatik göster", StringComparison.CurrentCultureIgnoreCase);

        _gap = Math.Clamp(_context?.Settings.GetInt("gap", 8) ?? 8, 0, 48);
        _masterPercent = Math.Clamp(_context?.Settings.GetInt("master-percent", 55) ?? 55, 30, 80);
        _noMinimize = _context?.Settings.GetBool("prevent-minimize", true) ?? true;

        var layout = _context?.Settings.GetString("layout", "Ana + yığın") ?? "Ana + yığın";
        _layout = layout.Equals("Izgara", StringComparison.CurrentCultureIgnoreCase) ? 1
            : layout.Equals("Sütunlar", StringComparison.CurrentCultureIgnoreCase) ? 2
            : 0;
    }

    private void RegisterZoneWindowSwitchingHotkeys()
    {
        _nextZoneWindowHotkey?.Dispose();
        _nextZoneWindowHotkey = null;
        _previousZoneWindowHotkey?.Dispose();
        _previousZoneWindowHotkey = null;
        foreach (var hotkey in _zoneMoveHotkeys) hotkey.Dispose();
        _zoneMoveHotkeys.Clear();
        if (_mode != ZoneMoveMode) return;

        try
        {
            if (_context?.Settings.GetBool("zone-window-switching", true) == true)
            {
                _nextZoneWindowHotkey = _context.Hotkeys.Register(
                    HotkeyModifiers.Win, VkPageDown, () => CycleZoneWindows(direction: 1));
                _previousZoneWindowHotkey = _context.Hotkeys.Register(
                    HotkeyModifiers.Win, VkPageUp, () => CycleZoneWindows(direction: -1));
            }
            if (_context?.Settings.GetBool("zone-keyboard-move-enabled", true) == true)
            {
                var modifiers = HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.Alt;
                _zoneMoveHotkeys.Add(_context.Hotkeys.Register(modifiers, VkLeft, () => MoveActiveWindowToAdjacentZone(1)));
                _zoneMoveHotkeys.Add(_context.Hotkeys.Register(modifiers, VkUp, () => MoveActiveWindowToAdjacentZone(2)));
                _zoneMoveHotkeys.Add(_context.Hotkeys.Register(modifiers, VkRight, () => MoveActiveWindowToAdjacentZone(4)));
                _zoneMoveHotkeys.Add(_context.Hotkeys.Register(modifiers, VkDown, () => MoveActiveWindowToAdjacentZone(8)));
            }
        }
        catch (Exception exception)
        {
            _nextZoneWindowHotkey?.Dispose();
            _nextZoneWindowHotkey = null;
            _previousZoneWindowHotkey?.Dispose();
            _previousZoneWindowHotkey = null;
            foreach (var hotkey in _zoneMoveHotkeys) hotkey.Dispose();
            _zoneMoveHotkeys.Clear();
            _context?.Log.Error("Bölge yönetimi kısayolları kaydedilemedi.", exception);
            _context?.Notify("Bölge yönetimi kısayolları kullanılamıyor; başka bir uygulama bu tuşları kullanıyor olabilir.");
        }
    }

    private void MoveActiveWindowToAdjacentZone(int direction)
    {
        if (!IsActive || _mode != ZoneMoveMode) return;
        var activeWindow = Native.GetForegroundWindow();
        if (activeWindow == IntPtr.Zero || !TryGetMonitorWorkArea(activeWindow, out var monitor, out var monitorName, out var workArea) ||
            !IsZoneCycleCandidate(activeWindow, monitor) || !TryGetVisibleFrameBounds(activeWindow, out var bounds)) return;

        ApplyMonitorGridProfile(monitorName);
        var zones = CreateZones(workArea);
        var currentZone = FindBestZone(zones, bounds);
        var nextZone = FindAdjacentZoneForWindow(zones, bounds, direction);
        if (nextZone != currentZone) Move(activeWindow, zones[nextZone]);
    }

    internal static int FindAdjacentZoneForWindow(IReadOnlyList<RECT> zones, RECT windowBounds, int direction)
    {
        var currentZone = FindBestZone(zones, windowBounds);
        if (currentZone < 0)
            currentZone = FindNearestZone(zones, new POINT
            {
                X = (windowBounds.Left + windowBounds.Right) / 2,
                Y = (windowBounds.Top + windowBounds.Bottom) / 2
            });
        return FindAdjacentZone(zones, currentZone, direction);
    }

    private void CycleZoneWindows(int direction)
    {
        if (!IsActive || _mode != ZoneMoveMode || direction is not (-1 or 1)) return;
        var activeWindow = Native.GetForegroundWindow();
        if (activeWindow == IntPtr.Zero || !TryGetMonitorWorkArea(activeWindow, out var monitor, out var monitorName, out var workArea)) return;

        ApplyMonitorGridProfile(monitorName);
        var zones = CreateZones(workArea);
        if (!TryGetVisibleFrameBounds(activeWindow, out var activeBounds)) return;
        var activeZone = FindBestZone(zones, activeBounds);
        if (activeZone < 0) return;

        var windows = new List<IntPtr>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!IsZoneCycleCandidate(hwnd, monitor) ||
                !TryGetCycleFrameBounds(hwnd, out var bounds) || FindBestZone(zones, bounds) != activeZone) return true;
            windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        if (!windows.Contains(activeWindow)) return;
        var zoneKey = MakeZoneCycleKey(monitorName, zones[activeZone]);
        if (!_zoneCycleOrders.TryGetValue(zoneKey, out var order))
        {
            order = new List<IntPtr>(windows);
            _zoneCycleOrders[zoneKey] = order;
        }
        else
        {
            UpdateWindowCycleOrder(order, windows);
        }

        if (_zoneCycleOrders.Count > 64)
            foreach (var staleKey in _zoneCycleOrders.Keys
                         .Where(key => !string.Equals(key, zoneKey, StringComparison.OrdinalIgnoreCase))
                         .Take(_zoneCycleOrders.Count - 64).ToArray())
                _zoneCycleOrders.Remove(staleKey);

        var nextIndex = FindCycleWindowIndex(order, activeWindow, direction);
        if (nextIndex < 0 || nextIndex >= order.Count) return;
        var nextWindow = order[nextIndex];
        ShowWindow(nextWindow, SW_RESTORE);
        SetForegroundWindow(nextWindow);
    }

    private static bool TryGetMonitorWorkArea(IntPtr hwnd, out IntPtr monitor, out string monitorName, out RECT workArea)
    {
        monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
        if (monitor == IntPtr.Zero || !GetMonitorInfoEx(monitor, ref info))
        {
            monitorName = string.Empty;
            workArea = default;
            return false;
        }
        monitorName = info.szDevice.TrimEnd('\0');
        workArea = info.rcWork;
        return workArea.Right - workArea.Left >= 200 && workArea.Bottom - workArea.Top >= 200;
    }

    private static bool IsZoneCycleCandidate(IntPtr hwnd, IntPtr targetMonitor)
    {
        if (!Native.IsWindowVisible(hwnd) || IsZoomed(hwnd) ||
            (Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0 ||
            GetWindow(hwnd, GW_OWNER) != IntPtr.Zero || IgnoredClasses.Contains(Native.ClassNameOf(hwnd)) ||
            IsCloaked(hwnd) || Native.GetWindowTextLength(hwnd) == 0 ||
            MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) != targetMonitor)
            return false;

        Native.GetWindowThreadProcessId(hwnd, out var processId);
        return processId != (uint)Environment.ProcessId;
    }

    private bool TryGetCycleFrameBounds(IntPtr hwnd, out RECT bounds)
    {
        if (Native.IsIconic(hwnd) && _placed.TryGetValue(hwnd, out bounds)) return true;
        return TryGetVisibleFrameBounds(hwnd, out bounds);
    }

    private static bool TryGetVisibleFrameBounds(IntPtr hwnd, out RECT bounds)
    {
        if (DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf<RECT>()) == 0 &&
            bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
            return true;
        return Native.GetWindowRect(hwnd, out bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    internal static int FindBestZone(IReadOnlyList<RECT> zones, RECT windowBounds)
    {
        if (windowBounds.Right <= windowBounds.Left || windowBounds.Bottom <= windowBounds.Top) return -1;
        var bestZone = -1;
        var bestCoverage = 0.80;
        for (var index = 0; index < zones.Count; index++)
        {
            var zone = zones[index];
            var zoneArea = (long)(zone.Right - zone.Left) * (zone.Bottom - zone.Top);
            if (zoneArea <= 0) continue;
            var overlapWidth = Math.Max(0, Math.Min(zone.Right, windowBounds.Right) - Math.Max(zone.Left, windowBounds.Left));
            var overlapHeight = Math.Max(0, Math.Min(zone.Bottom, windowBounds.Bottom) - Math.Max(zone.Top, windowBounds.Top));
            var coverage = (double)overlapWidth * overlapHeight / zoneArea;
            if (coverage <= bestCoverage) continue;
            bestZone = index;
            bestCoverage = coverage;
        }
        return bestZone;
    }

    internal static int FindCycleWindowIndex(IReadOnlyList<IntPtr> windows, IntPtr activeWindow, int direction)
    {
        if (windows.Count < 2 || direction is not (-1 or 1)) return -1;
        var activeIndex = -1;
        for (var index = 0; index < windows.Count; index++)
            if (windows[index] == activeWindow) { activeIndex = index; break; }
        if (activeIndex < 0) return -1;
        return (activeIndex + direction + windows.Count) % windows.Count;
    }

    internal static void UpdateWindowCycleOrder(List<IntPtr> order, IReadOnlyCollection<IntPtr> currentWindows)
    {
        var currentSet = currentWindows.ToHashSet();
        order.RemoveAll(window => !currentSet.Contains(window));
        var knownWindows = order.ToHashSet();
        foreach (var window in currentWindows)
            if (knownWindows.Add(window)) order.Add(window);
    }

    private static string MakeZoneCycleKey(string monitorName, RECT zone) =>
        $"{monitorName}|{zone.Left},{zone.Top},{zone.Right},{zone.Bottom}";

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
            _draggedWindow = hwnd;
            _zoneDragArmed = false;
            _selectedZone = -1;
            _selectedZones.Clear();
            _keyboardZoneSelectionActive = false;
            _lastZoneDirectionMask = 0;
            if (_mode == ZoneMoveMode) _zoneTimer?.Start();
            return;
        }

        if (eventType == EVENT_SYSTEM_MOVESIZEEND)
        {
            _dragging = false;
            _zoneTimer?.Stop();
            if (_mode == ZoneMoveMode && _zoneDragArmed && _selectedZone >= 0)
                ApplySelectedZone(hwnd);
            _zonePreview?.ClosePreview();
            _draggedWindow = IntPtr.Zero;
            _zoneDragArmed = false;
            _selectedZone = -1;
            _selectedZones.Clear();
            _keyboardZoneSelectionActive = false;
            _lastZoneDirectionMask = 0;

            if (_mode == ZoneMoveMode) return;
        }

        if (!IsActive) return;
        Interlocked.Exchange(ref _pulses, 5);
        Restart(40);
    }

    private void Retile()
    {
        if (!IsActive || _mode != AutoTileMode || _dragging) return;
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

    private void UpdateZonePreview()
    {
        if (!IsActive || !_dragging || _mode != ZoneMoveMode || _draggedWindow == IntPtr.Zero)
            return;

        if (_requireShiftForZones && (GetAsyncKeyState(VkShift) & 0x8000) == 0)
        {
            ClearZoneSelection();
            return;
        }

        if (!Native.GetCursorPos(out var cursor) || !TryGetWorkArea(cursor, out var workArea, out var monitorName))
        {
            ClearZoneSelection();
            return;
        }

        ApplyMonitorGridProfile(monitorName);
        var zones = CreateZones(workArea);
        var directionMask = ReadZoneDirectionMask();
        var newlyPressedDirections = directionMask & ~_lastZoneDirectionMask;
        _lastZoneDirectionMask = directionMask;

        if (_keyboardZoneSelectionActive &&
            (Math.Abs(cursor.X - _lastZoneCursor.X) > 4 || Math.Abs(cursor.Y - _lastZoneCursor.Y) > 4))
            _keyboardZoneSelectionActive = false;

        if (!_keyboardZoneSelectionActive)
        {
            var hoveredZone = FindZone(zones, cursor);
            if (hoveredZone != _selectedZone)
            {
                _selectedZone = hoveredZone;
                SetSingleZoneSelection(hoveredZone);
            }
            _lastZoneCursor = cursor;
        }

        if (newlyPressedDirections != 0)
        {
            if (_selectedZone < 0)
            {
                _selectedZone = FindNearestZone(zones, cursor);
                SetSingleZoneSelection(_selectedZone);
            }
            var adjacentZone = FindAdjacentZone(zones, _selectedZone, FirstDirection(newlyPressedDirections));
            var controlDown = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
            if (controlDown)
            {
                if (TryExtendZoneSelection(zones, _selectedZones, adjacentZone, _gap))
                    _selectedZone = adjacentZone;
            }
            else
            {
                _selectedZone = adjacentZone;
                SetSingleZoneSelection(_selectedZone);
            }
            _keyboardZoneSelectionActive = _selectedZone >= 0;
        }

        _zoneDragArmed = true;

        var dpi = Native.GetDpiForWindow(_draggedWindow);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        _zonePreview ??= new ZonePreviewWindow();
        RECT? mergedBounds = _selectedZones.Count > 1 && TryGetSpanBounds(zones, _selectedZones, _gap, out var selectedBounds)
            ? selectedBounds
            : null;
        _zonePreview.ShowZones(workArea, zones, _selectedZones, scale, mergedBounds);
    }

    private void ClearZoneSelection()
    {
        _selectedZone = -1;
        _selectedZones.Clear();
        _zoneDragArmed = false;
        _keyboardZoneSelectionActive = false;
        _lastZoneDirectionMask = 0;
        _zonePreview?.ClosePreview();
    }

    private void ApplySelectedZone(IntPtr hwnd)
    {
        if (!Native.GetCursorPos(out var cursor) || !TryGetWorkArea(cursor, out var workArea, out var monitorName)) return;
        ApplyMonitorGridProfile(monitorName);
        var zones = CreateZones(workArea);
        if (_selectedZones.Count > 1 && TryGetSpanBounds(zones, _selectedZones, _gap, out var spanBounds))
        {
            Move(hwnd, spanBounds);
            return;
        }
        var zone = _keyboardZoneSelectionActive ? _selectedZone : FindZone(zones, cursor);
        if (zone < 0 && _keyboardZoneSelectionActive) zone = FindNearestZone(zones, cursor);
        if (zone >= 0) Move(hwnd, zones[zone]);
    }

    public bool TryPlaceWindowInZone(IntPtr window, POINT point)
    {
        if (!IsActive || !Native.IsWindow(window) || !TryTileable(window, out _, out _) ||
            !TryGetWorkArea(point, out var workArea, out var monitorName)) return false;
        ApplyMonitorGridProfile(monitorName);
        var zones = CreateZones(workArea);
        var zone = FindZone(zones, point);
        if (zone < 0) zone = FindNearestZone(zones, point);
        if (zone < 0) return false;
        Move(window, zones[zone]);
        return Native.GetWindowRect(window, out var actual) && _placed.TryGetValue(window, out var expected) &&
            Math.Abs(actual.Left - expected.Left) <= 2 && Math.Abs(actual.Top - expected.Top) <= 2 &&
            Math.Abs(actual.Right - expected.Right) <= 2 && Math.Abs(actual.Bottom - expected.Bottom) <= 2;
    }

    private void SetSingleZoneSelection(int zone)
    {
        _selectedZones.Clear();
        if (zone >= 0) _selectedZones.Add(zone);
    }

    private static bool TryExtendZoneSelection(IReadOnlyList<RECT> zones, ISet<int> selection, int candidate, int gap)
    {
        if (candidate < 0 || candidate >= zones.Count || selection.Contains(candidate)) return false;
        if (!TryGetSpanBounds(zones, selection.ToHashSet(), gap, out var currentBounds) ||
            !selection.Any(index => AreZonesAdjacent(zones[index], zones[candidate], gap))) return false;

        var candidateBounds = zones[candidate];
        var extendedBounds = new RECT
        {
            Left = Math.Min(currentBounds.Left, candidateBounds.Left),
            Top = Math.Min(currentBounds.Top, candidateBounds.Top),
            Right = Math.Max(currentBounds.Right, candidateBounds.Right),
            Bottom = Math.Max(currentBounds.Bottom, candidateBounds.Bottom)
        };
        var expanded = Enumerable.Range(0, zones.Count).Where(index =>
        {
            var zone = zones[index];
            return zone.Left >= extendedBounds.Left && zone.Top >= extendedBounds.Top &&
                   zone.Right <= extendedBounds.Right && zone.Bottom <= extendedBounds.Bottom;
        }).ToHashSet();
        if (!expanded.Contains(candidate) || !selection.All(expanded.Contains)) return false;
        selection.Clear();
        foreach (var index in expanded) selection.Add(index);
        return true;
    }

    private static bool TryGetSpanBounds(IReadOnlyList<RECT> zones, IReadOnlySet<int> selection, int gap, out RECT bounds)
    {
        bounds = default;
        if (selection.Count == 0 || selection.Any(index => index < 0 || index >= zones.Count)) return false;
        var selected = selection.ToHashSet();
        var first = zones[selected.First()];
        bounds = selected.Skip(1).Select(index => zones[index]).Aggregate(first, (current, zone) => new RECT
        {
            Left = Math.Min(current.Left, zone.Left),
            Top = Math.Min(current.Top, zone.Top),
            Right = Math.Max(current.Right, zone.Right),
            Bottom = Math.Max(current.Bottom, zone.Bottom)
        });
        var spanBounds = bounds;

        var enclosedZones = Enumerable.Range(0, zones.Count).Where(index =>
        {
            var zone = zones[index];
            return zone.Left >= spanBounds.Left && zone.Top >= spanBounds.Top &&
                   zone.Right <= spanBounds.Right && zone.Bottom <= spanBounds.Bottom;
        }).ToHashSet();
        if (!selected.SetEquals(enclosedZones)) return false;

        var connected = new HashSet<int> { selected.First() };
        var pending = new Queue<int>(connected);
        while (pending.TryDequeue(out var current))
        {
            foreach (var candidate in selected)
                if (!connected.Contains(candidate) && AreZonesAdjacent(zones[current], zones[candidate], gap))
                {
                    connected.Add(candidate);
                    pending.Enqueue(candidate);
                }
        }
        return connected.Count == selected.Count;
    }

    private static bool AreZonesAdjacent(RECT first, RECT second, int gap)
    {
        var verticalOverlap = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top);
        var horizontalOverlap = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);
        var horizontalDistance = Math.Min(Math.Abs(first.Right - second.Left), Math.Abs(second.Right - first.Left));
        var verticalDistance = Math.Min(Math.Abs(first.Bottom - second.Top), Math.Abs(second.Bottom - first.Top));
        return (horizontalDistance <= gap && verticalOverlap > 0) || (verticalDistance <= gap && horizontalOverlap > 0);
    }

    private bool TryGetWorkArea(POINT cursor, out RECT workArea, out string monitorName)
    {
        workArea = default;
        monitorName = string.Empty;
        var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
        if (monitor == IntPtr.Zero || !GetMonitorInfoEx(monitor, ref info)) return false;

        workArea = info.rcWork;
        monitorName = info.szDevice.TrimEnd('\0');
        return workArea.Right - workArea.Left >= 200 && workArea.Bottom - workArea.Top >= 200;
    }

    private void ApplyMonitorGridProfile(string monitorName)
    {
        _zoneLayout = _defaultZoneLayout;
        _zoneColumns = _defaultZoneColumns;
        _zoneRows = _defaultZoneRows;
        if (!_monitorGridProfiles.TryGetValue(monitorName, out var profile)) return;
        _zoneLayout = 3;
        _zoneColumns = profile.Columns;
        _zoneRows = profile.Rows;
    }

    internal static Dictionary<string, (int Columns, int Rows)> ParseMonitorGridProfiles(string value)
    {
        var profiles = new Dictionary<string, (int Columns, int Rows)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !IsValidMonitorName(pair[0])) continue;
            var dimensions = pair[1].Split('x', 2, StringSplitOptions.TrimEntries);
            if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out var columns) || !int.TryParse(dimensions[1], out var rows)) continue;
            profiles[pair[0]] = (Math.Clamp(columns, 1, 6), Math.Clamp(rows, 1, 4));
        }
        return profiles;
    }

    private static bool IsValidMonitorName(string value) =>
        value.Length is > 0 and <= 32 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private List<RECT> CreateZones(RECT workArea)
    {
        if (_zoneLayout == 3) return CreateGridZones(workArea, _zoneColumns, _zoneRows, _gap);
        var zones = new List<RECT>();
        var width = workArea.Right - workArea.Left;
        var height = workArea.Bottom - workArea.Top;

        if (_zoneLayout == 2)
        {
            var widths = SplitSizes(width, 2, _gap);
            var heights = SplitSizes(height, 2, _gap);
            var top = workArea.Top;
            for (var row = 0; row < 2; row++)
            {
                var left = workArea.Left;
                for (var column = 0; column < 2; column++)
                {
                    zones.Add(new RECT
                    {
                        Left = left,
                        Top = top,
                        Right = left + widths[column],
                        Bottom = top + heights[row]
                    });
                    left += widths[column] + _gap;
                }
                top += heights[row] + _gap;
            }
            return zones;
        }

        var count = _zoneLayout == 1 ? 3 : 2;
        var columnWidths = SplitSizes(width, count, _gap);
        var x = workArea.Left;
        foreach (var columnWidth in columnWidths)
        {
            zones.Add(new RECT
            {
                Left = x,
                Top = workArea.Top,
                Right = x + columnWidth,
                Bottom = workArea.Bottom
            });
            x += columnWidth + _gap;
        }

        return zones;
    }

    private static List<RECT> CreateGridZones(RECT workArea, int columns, int rows, int gap)
    {
        columns = Math.Clamp(columns, 1, 6);
        rows = Math.Clamp(rows, 1, 4);
        gap = Math.Clamp(gap, 0, 48);
        var width = Math.Max(1, workArea.Right - workArea.Left);
        var height = Math.Max(1, workArea.Bottom - workArea.Top);
        columns = Math.Min(columns, width);
        rows = Math.Min(rows, height);
        var horizontalGap = columns == 1 ? 0 : Math.Min(gap, Math.Max(0, (width - columns) / (columns - 1)));
        var verticalGap = rows == 1 ? 0 : Math.Min(gap, Math.Max(0, (height - rows) / (rows - 1)));
        var widths = SplitSizes(width, columns, horizontalGap);
        var heights = SplitSizes(height, rows, verticalGap);
        var zones = new List<RECT>(columns * rows);
        var top = workArea.Top;
        for (var row = 0; row < rows; row++)
        {
            var left = workArea.Left;
            for (var column = 0; column < columns; column++)
            {
                zones.Add(new RECT
                {
                    Left = left,
                    Top = top,
                    Right = left + widths[column],
                    Bottom = top + heights[row]
                });
                left += widths[column] + horizontalGap;
            }
            top += heights[row] + verticalGap;
        }
        return zones;
    }

    private static int FindZone(IReadOnlyList<RECT> zones, POINT point)
    {
        for (var index = 0; index < zones.Count; index++)
        {
            var zone = zones[index];
            if (point.X >= zone.Left && point.X < zone.Right && point.Y >= zone.Top && point.Y < zone.Bottom)
                return index;
        }

        return -1;
    }

    private static int FindNearestZone(IReadOnlyList<RECT> zones, POINT point)
    {
        if (zones.Count == 0) return -1;
        var nearest = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < zones.Count; index++)
        {
            var zone = zones[index];
            var deltaX = (zone.Left + zone.Right) / 2.0 - point.X;
            var deltaY = (zone.Top + zone.Bottom) / 2.0 - point.Y;
            var distance = deltaX * deltaX + deltaY * deltaY;
            if (distance >= bestDistance) continue;
            nearest = index;
            bestDistance = distance;
        }
        return nearest;
    }

    private static int FindAdjacentZone(IReadOnlyList<RECT> zones, int current, int direction)
    {
        if (current < 0 || current >= zones.Count || direction is not (1 or 2 or 4 or 8)) return current;
        var origin = zones[current];
        var originX = (origin.Left + origin.Right) / 2.0;
        var originY = (origin.Top + origin.Bottom) / 2.0;
        var bestIndex = current;
        var bestScore = double.MaxValue;
        for (var index = 0; index < zones.Count; index++)
        {
            if (index == current) continue;
            var candidate = zones[index];
            var deltaX = (candidate.Left + candidate.Right) / 2.0 - originX;
            var deltaY = (candidate.Top + candidate.Bottom) / 2.0 - originY;
            var primary = direction switch { 1 => -deltaX, 2 => -deltaY, 4 => deltaX, _ => deltaY };
            if (primary <= 0) continue;
            var secondary = direction is 1 or 4 ? Math.Abs(deltaY) : Math.Abs(deltaX);
            var score = primary + secondary * 1.5;
            if (score >= bestScore) continue;
            bestIndex = index;
            bestScore = score;
        }
        return bestIndex;
    }

    private static int ReadZoneDirectionMask()
    {
        var mask = 0;
        if ((GetAsyncKeyState(0x25) & 0x8000) != 0) mask |= 1;
        if ((GetAsyncKeyState(0x26) & 0x8000) != 0) mask |= 2;
        if ((GetAsyncKeyState(0x27) & 0x8000) != 0) mask |= 4;
        if ((GetAsyncKeyState(0x28) & 0x8000) != 0) mask |= 8;
        return mask;
    }

    private static int FirstDirection(int mask) => (mask & 1) != 0 ? 1
        : (mask & 2) != 0 ? 2
        : (mask & 4) != 0 ? 4 : 8;

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

        for (var i = _order.Count - 1; i >= 0; i--)
        {
            var handle = _order[i];
            if (Native.IsWindow(handle)) continue;

            _order.RemoveAt(i);
            _orderedHandles.Remove(handle);
        }

        foreach (var window in result)
            if (_orderedHandles.Add(window.Handle))
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

        Native.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == (uint)Environment.ProcessId) return false;

        var extended = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((extended & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return false;

        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if (IgnoredClasses.Contains(Native.ClassNameOf(hwnd))) return false;
        if (IsCloaked(hwnd)) return false;
        if (Native.GetWindowTextLength(hwnd) == 0) return false;
        if (Native.IsIconic(hwnd))
        {
            if (!_noMinimize) return false;
            ShowWindow(hwnd, SW_RESTORE);
            if (Native.IsIconic(hwnd)) return false;
        }
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoEx(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attribute, out RECT rect, int size);
}
