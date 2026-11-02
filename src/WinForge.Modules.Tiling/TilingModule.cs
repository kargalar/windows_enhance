using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WinForge.Core;
using RECT = WinForge.Core.RECT;

namespace WinForge.Modules.Tiling;

public partial class TilingModule : ITweakModule, IWindowZoneDragProvider
{
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const uint GW_OWNER = 4;
    private const long WS_POPUP = unchecked((int)0x80000000);
    private const long WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const long WS_THICKFRAME = 0x00040000;
    private const long WS_EX_TOPMOST = 0x00000008;
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
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int AutoTileMode = 0;
    private const int ZoneMoveMode = 1;
    private const int DefaultZoneMergeDistance = 24;
    private const int SW_MAXIMIZE = 3;
    private const string DefaultZoneActiveColor = "#4CC2FF";
    private const string DefaultZoneInactiveColor = "#6B6B6B";

    private static readonly IReadOnlyDictionary<string, string> ZoneColorPalette = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["#4CC2FF"] = "Turkuaz",
        ["#0078D4"] = "Mavi",
        ["#005A9E"] = "Lacivert",
        ["#107C10"] = "Yeşil",
        ["#498205"] = "Zeytin yeşili",
        ["#C19C00"] = "Altın sarısı",
        ["#CA5010"] = "Turuncu",
        ["#C50F1F"] = "Kırmızı",
        ["#8764B8"] = "Mor",
        ["#C30052"] = "Fuşya",
        ["#6B6B6B"] = "Gri"
    };

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
    private static readonly TimeSpan NewWindowPlacementDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan NewWindowPlacementTimeout = TimeSpan.FromSeconds(3);

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
    private DispatcherTimer? _zonePlacementTimer;
    private IDisposable? _nextZoneWindowHotkey;
    private IDisposable? _previousZoneWindowHotkey;
    private readonly List<IDisposable> _zoneMoveHotkeys = new();
    private readonly Dictionary<IntPtr, long> _pendingZoneWindows = new();
    private readonly object _pendingZoneWindowsLock = new();
    private int _gap = 8;
    private int _layout;
    private int _masterPercent = 55;
    private bool _noMinimize;
    private int _busy;
    private int _pulses;
    private bool _dragging;
    private readonly Dictionary<IntPtr, RECT> _placed = new();
    private readonly Dictionary<IntPtr, FullscreenWindowState> _fullscreenWindows = new();
    private readonly Dictionary<string, List<IntPtr>> _zoneCycleOrders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IntPtr> _order = new();
    private readonly object _autoTileLock = new();
    private readonly List<(IntPtr Handle, RECT Bounds)> _dragSlots = new();
    private RECT _dragStartBounds;
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
    private int _zoneMergeDistance = DefaultZoneMergeDistance;
    private bool _zoneShowNumbers = true;
    private string _zoneActiveColor = DefaultZoneActiveColor;
    private string _zoneInactiveColor = DefaultZoneInactiveColor;
    private int _zoneBorderThickness = 2;
    private bool _zoneAutoPlaceNewWindows = true;
    private bool _zoneDragUsesWindowControl;
    private string _dragStartMonitorName = string.Empty;
    private bool _dragStartedWithAllZonesOccupied;
    private readonly int _configuredMode;

    public TilingModule() : this(AutoTileMode) { }

    protected TilingModule(int mode)
    {
        _configuredMode = mode;
        _mode = mode;
        _handler = OnWinEvent;
        Settings = CreateSettings(mode);
    }

    public virtual string Id => "auto-tiling";
    public virtual string Name => "Tam Ekran Pencere Yönetimi";
    public virtual string Category => "Verimlilik";
    public virtual string Description => "Açık pencereleri otomatik olarak Ana + yığın, Izgara veya Sütunlar düzeninde yerleştirir.";
    public virtual string? Warning => "Otomatik döşeme bazı uygulamaların minimum boyut kısıtları nedeniyle boşluk bırakabilir. Tam ekran uygulamalar ve küçük araç pencereleri atlanır.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; }

    private static IReadOnlyList<SettingDescriptor> CreateSettings(int mode)
    {
        var settings = new[]
        {
        new SettingDescriptor
        {
            Key = "zone-auto-place-new-windows",
            Label = "Yeni pencereleri boş bölgelere yerleştir",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Yeni açılan uygun pencereleri aynı ekrandaki en az dolu bölgeye yerleştirir."
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
            Key = "zone-show-numbers",
            Label = "Bölge numaralarını göster",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Sürükleme önizlemesinde her bölgenin numarasını gösterir."
        },
        new SettingDescriptor
        {
            Key = "zone-active-color",
            Label = "Etkin alan rengi",
            Kind = SettingKind.Palette,
            Options = ZoneColorPalette.Keys.ToArray(),
            OptionLabels = ZoneColorPalette,
            Default = DefaultZoneActiveColor,
            Description = "Seçili alanın rengini paletten seçin."
        },
        new SettingDescriptor
        {
            Key = "zone-inactive-color",
            Label = "Pasif alan rengi",
            Kind = SettingKind.Palette,
            Options = ZoneColorPalette.Keys.ToArray(),
            OptionLabels = ZoneColorPalette,
            Default = DefaultZoneInactiveColor,
            Description = "Seçilmemiş alanların rengini paletten seçin."
        },
        new SettingDescriptor
        {
            Key = "zone-border-thickness",
            Label = "Bölge kenarlığı kalınlığı (px)",
            Kind = SettingKind.Number,
            Default = 2,
            Min = 1,
            Max = 12,
            Description = "Etkin ve pasif bölge kenarlıklarının kalınlığını ayarlar."
        },
        new SettingDescriptor
        {
            Key = "zone-merge-distance",
            Label = "Birleştirme algılama mesafesi (px)",
            Kind = SettingKind.Number,
            Default = DefaultZoneMergeDistance,
            Min = 0,
            Max = 200,
            Description = "İmleç bir bölme çizgisine bu kadar yaklaştığında komşu bölgeleri birleştirerek vurgular; ızgara kesişiminde dört alanı birlikte seçer."
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
            Description = "Otomatik döşemede pencere, FancyZones'ta bölge arası boşluk. Ekran kenarlarına sıfır boşluk uygulanır."
        }
        };

        return mode == ZoneMoveMode
            ? settings.Where(setting => setting.Key.StartsWith("zone-", StringComparison.Ordinal) || setting.Key == "gap").ToArray()
            : settings.Where(setting => setting.Key is "layout" or "master-percent" or "prevent-minimize" or "gap").ToArray();
    }

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        _timer = new Timer(_ => Retile(), null, Timeout.Infinite, Timeout.Infinite);
        _zonePlacementTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = NewWindowPlacementDelay
        };
        _zonePlacementTimer.Tick += (_, _) =>
        {
            PlacePendingWindowsInZones();
        };
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
        foreach (var window in _fullscreenWindows.Keys.ToArray()) RestoreFullscreenWindow(window, restorePlacement: true);
        _timer?.Dispose();
        _timer = null;
        _zonePlacementTimer?.Stop();
        _zonePlacementTimer = null;
        lock (_pendingZoneWindowsLock) _pendingZoneWindows.Clear();
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
        _dragging = false;
        _dragSlots.Clear();
        _zoneDragUsesWindowControl = false;
        _dragStartMonitorName = string.Empty;
        _dragStartedWithAllZonesOccupied = false;

        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();

        _context?.Log.Info(_mode == ZoneMoveMode ? "FancyZones kapatıldı." : "Otomatik döşeme kapatıldı.");
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
        _mode = _configuredMode;

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
        _zoneMergeDistance = Math.Clamp(settings?.GetInt("zone-merge-distance", DefaultZoneMergeDistance) ?? DefaultZoneMergeDistance, 0, 200);
        _zoneShowNumbers = settings?.GetBool("zone-show-numbers", true) ?? true;
        _zoneActiveColor = ResolvePaletteColor(settings?.GetString("zone-active-color", DefaultZoneActiveColor), DefaultZoneActiveColor);
        _zoneInactiveColor = ResolvePaletteColor(settings?.GetString("zone-inactive-color", DefaultZoneInactiveColor), DefaultZoneInactiveColor);
        _zoneBorderThickness = Math.Clamp(settings?.GetInt("zone-border-thickness", 2) ?? 2, 1, 12);
        _zoneAutoPlaceNewWindows = settings?.GetBool("zone-auto-place-new-windows", true) ?? true;

        _gap = Math.Clamp(_context?.Settings.GetInt("gap", 8) ?? 8, 0, 48);
        _masterPercent = Math.Clamp(_context?.Settings.GetInt("master-percent", 55) ?? 55, 30, 80);
        _noMinimize = _context?.Settings.GetBool("prevent-minimize", true) ?? true;

        var layout = _context?.Settings.GetString("layout", "Ana + yığın") ?? "Ana + yığın";
        _layout = layout.Equals("Izgara", StringComparison.CurrentCultureIgnoreCase) ? 1
            : layout.Equals("Sütunlar", StringComparison.CurrentCultureIgnoreCase) ? 2
            : 0;
    }

    private static string ResolvePaletteColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = value.Trim();
        if (normalized.Length != 7 || normalized[0] != '#') return fallback;
        for (var index = 1; index < normalized.Length; index++)
            if (!Uri.IsHexDigit(normalized[index])) return fallback;
        return normalized.ToUpperInvariant();
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
        var nextZone = currentZone < 0
            ? FindNearestZone(zones, new POINT { X = (bounds.Left + bounds.Right) / 2, Y = (bounds.Top + bounds.Bottom) / 2 })
            : FindAdjacentZoneForWindow(zones, bounds, direction);
        if (nextZone >= 0 && nextZone != currentZone) Move(activeWindow, zones[nextZone]);
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

        return true;
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

        _context?.Log.Info(_mode == ZoneMoveMode ? "FancyZones etkin." : "Otomatik döşeme etkin.");
    }

    private void Hook(uint min, uint max)
    {
        var flags = WINEVENT_OUTOFCONTEXT | (_mode == AutoTileMode ? WINEVENT_SKIPOWNPROCESS : 0);
        var hook = SetWinEventHook(min, max, IntPtr.Zero, _handler, 0, 0,
            flags);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
        else _context?.Log.Error($"WinEvent kancası kurulamadı: 0x{min:X}");
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint threadId, uint time)
    {
        if (idObject != 0) return;

        if (_mode == ZoneMoveMode && eventType == EVENT_OBJECT_DESTROY)
        {
            lock (_pendingZoneWindowsLock) _pendingZoneWindows.Remove(hwnd);
            _placed.Remove(hwnd);
            _fullscreenWindows.Remove(hwnd);
            return;
        }

        if (_mode == ZoneMoveMode && eventType == EVENT_OBJECT_SHOW && _zoneAutoPlaceNewWindows)
            QueueNewWindowForZonePlacement(hwnd);

        if (eventType == EVENT_SYSTEM_MOVESIZESTART)
        {
            BeginZoneDrag(hwnd, usesWindowControl: false);
            return;
        }

        if (eventType == EVENT_SYSTEM_MOVESIZEEND)
        {
            _ = EndZoneDrag(hwnd, canceled: false);

            if (_mode == ZoneMoveMode) return;
        }

        if (!IsActive) return;
        Interlocked.Exchange(ref _pulses, 5);
        Restart(40);
    }

    private void QueueNewWindowForZonePlacement(IntPtr hwnd)
    {
        if (!IsActive || hwnd == IntPtr.Zero) return;
        lock (_pendingZoneWindowsLock)
            _pendingZoneWindows.TryAdd(hwnd, Environment.TickCount64 + (long)NewWindowPlacementTimeout.TotalMilliseconds);
        // Do not debounce: unrelated SHOW events must not delay an opening window.
        _zonePlacementTimer?.Start();
    }

    public void BeginWindowZoneDrag(IntPtr window)
    {
        if (!IsActive) return;
        BeginZoneDrag(window, usesWindowControl: true);
    }

    public bool EndWindowZoneDrag(IntPtr window, bool canceled) => EndZoneDrag(window, canceled);

    private void BeginZoneDrag(IntPtr window, bool usesWindowControl)
    {
        if (!IsActive || !Native.IsWindow(window)) return;
        lock (_pendingZoneWindowsLock) _pendingZoneWindows.Remove(window);
        lock (_autoTileLock)
        {
            if (_dragging && _draggedWindow == window) return;
            RestoreFullscreenWindow(window, restorePlacement: true);
            _dragging = true;
            _draggedWindow = window;
            _zoneDragUsesWindowControl = usesWindowControl;
            _zoneDragArmed = false;
            _selectedZone = -1;
            _selectedZones.Clear();
            _keyboardZoneSelectionActive = false;
            _lastZoneDirectionMask = 0;
            _dragStartedWithAllZonesOccupied = false;
            _dragStartMonitorName = string.Empty;
            _dragSlots.Clear();
            if (_mode == AutoTileMode && _placed.ContainsKey(window) && Native.GetWindowRect(window, out _dragStartBounds))
            {
                var dragMonitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
                foreach (var handle in _order)
                    if (_placed.ContainsKey(handle) && Native.IsWindowVisible(handle) && !Native.IsIconic(handle) &&
                        MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST) == dragMonitor &&
                        TryGetVisibleFrameBounds(handle, out var bounds))
                        _dragSlots.Add((handle, bounds));
            }
            if (_mode == ZoneMoveMode && TryGetMonitorWorkArea(window, out var monitor, out var monitorName, out var workArea))
            {
                ApplyMonitorGridProfile(monitorName);
                _dragStartMonitorName = monitorName;
                _dragStartedWithAllZonesOccupied = AreAllZonesOccupied(
                    CountOccupiedZones(CreateZones(workArea), monitor, window));
                _zoneTimer?.Start();
            }
        }
    }

    private bool EndZoneDrag(IntPtr window, bool canceled)
    {
        if (!_dragging || _draggedWindow != window) return false;
        if (_mode == ZoneMoveMode && !canceled) UpdateZonePreview();
        var handled = _mode == ZoneMoveMode && !canceled && _zoneDragArmed && _selectedZone >= 0;
        lock (_autoTileLock)
        {
            if (_mode == AutoTileMode && !canceled && Native.GetCursorPos(out var cursor) &&
                Native.GetWindowRect(window, out var bounds) &&
                (bounds.Left != _dragStartBounds.Left || bounds.Top != _dragStartBounds.Top) &&
                bounds.Right - bounds.Left == _dragStartBounds.Right - _dragStartBounds.Left &&
                bounds.Bottom - bounds.Top == _dragStartBounds.Bottom - _dragStartBounds.Top)
            {
                var target = FindTileDropTarget(_dragSlots, cursor);
                if (Native.IsWindow(target) && Native.IsWindowVisible(target) && !Native.IsIconic(target))
                    handled = SwapTileOrder(_order, window, target);
            }
            _dragSlots.Clear();
            _dragging = false;
        }
        _zoneTimer?.Stop();
        if (handled && _mode == ZoneMoveMode)
            ApplySelectedZone(window);
        _zonePreview?.ClosePreview();
        _draggedWindow = IntPtr.Zero;
        _zoneDragUsesWindowControl = false;
        _zoneDragArmed = false;
        _selectedZone = -1;
        _selectedZones.Clear();
        _keyboardZoneSelectionActive = false;
        _lastZoneDirectionMask = 0;
        _dragStartedWithAllZonesOccupied = false;
        _dragStartMonitorName = string.Empty;
        if (_mode == AutoTileMode && IsActive)
        {
            Interlocked.Exchange(ref _pulses, 5);
            Restart(40);
        }
        return handled;
    }

    internal static IntPtr FindTileDropTarget(IReadOnlyList<(IntPtr Handle, RECT Bounds)> slots, POINT cursor)
    {
        foreach (var (handle, bounds) in slots)
            if (cursor.X >= bounds.Left && cursor.X < bounds.Right &&
                cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom)
                return handle;
        return IntPtr.Zero;
    }

    internal static bool SwapTileOrder(IList<IntPtr> order, IntPtr window, IntPtr target)
    {
        var sourceIndex = order.IndexOf(window);
        var targetIndex = order.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return false;
        (order[sourceIndex], order[targetIndex]) = (order[targetIndex], order[sourceIndex]);
        return true;
    }

    private void PlacePendingWindowsInZones()
    {
        if (!IsActive || _mode != ZoneMoveMode || !_zoneAutoPlaceNewWindows)
        {
            lock (_pendingZoneWindowsLock) _pendingZoneWindows.Clear();
            _zonePlacementTimer?.Stop();
            return;
        }

        KeyValuePair<IntPtr, long>[] pending;
        lock (_pendingZoneWindowsLock)
            pending = _pendingZoneWindows.ToArray();

        foreach (var (hwnd, deadline) in pending)
        {
            if (PlaceNewWindowInAvailableZone(hwnd) || Environment.TickCount64 >= deadline)
                lock (_pendingZoneWindowsLock) _pendingZoneWindows.Remove(hwnd);
        }
        lock (_pendingZoneWindowsLock)
            if (_pendingZoneWindows.Count == 0) _zonePlacementTimer?.Stop();
    }

    // False keeps an opening window queued while its title, frame or visibility initializes.
    private bool PlaceNewWindowInAvailableZone(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd) || _fullscreenWindows.ContainsKey(hwnd) || _placed.ContainsKey(hwnd)) return true;
        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || hwnd == _draggedWindow ||
            !IsResizableWindow(hwnd) || !TryTileableCore(hwnd, out var monitor, out var workArea, allowMaximized: true) ||
            !TryGetMonitorWorkArea(hwnd, out _, out var monitorName, out _)) return false;

        ApplyMonitorGridProfile(monitorName);
        var zones = CreateZones(workArea);
        if (zones.Count == 0) return false;

        var occupancy = CountOccupiedZones(zones, monitor, hwnd);
        if (AreAllZonesOccupied(occupancy))
        {
            EnterTrueFullscreen(hwnd, monitor);
            return _fullscreenWindows.ContainsKey(hwnd);
        }

        var targetZone = FindLeastOccupiedZone(occupancy);
        if (targetZone < 0) return false;
        if (IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        Move(hwnd, zones[targetZone]);
        return _placed.ContainsKey(hwnd);
    }

    private static bool IsResizableWindow(IntPtr hwnd) =>
        (Native.GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & WS_THICKFRAME) != 0;

    private int[] CountOccupiedZones(IReadOnlyList<RECT> zones, IntPtr monitor, IntPtr excludedWindow)
    {
        var occupancy = new int[zones.Count];
        Native.EnumWindows((candidate, state) =>
        {
            if (candidate == excludedWindow || !Native.IsWindowVisible(candidate) || Native.IsIconic(candidate) ||
                !TryTileable(candidate, out var candidateMonitor, out _) || candidateMonitor != monitor ||
                !TryGetVisibleFrameBounds(candidate, out var bounds)) return true;

            var occupiedZone = FindBestZone(zones, bounds);
            if (occupiedZone >= 0) occupancy[occupiedZone]++;
            return true;
        }, IntPtr.Zero);
        return occupancy;
    }

    private static bool AreAllZonesOccupied(IReadOnlyList<int> occupancy) =>
        occupancy.Count > 0 && occupancy.All(count => count > 0);

    internal static int FindLeastOccupiedZone(IReadOnlyList<int> occupancy)
    {
        if (occupancy.Count == 0) return -1;
        var leastOccupied = 0;
        for (var index = 1; index < occupancy.Count; index++)
            if (occupancy[index] < occupancy[leastOccupied]) leastOccupied = index;
        return leastOccupied;
    }

    private void Retile()
    {
        if (!IsActive || _mode != AutoTileMode || _dragging) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;

        try
        {
            lock (_autoTileLock)
            {
                if (_dragging) return;
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

        if (_requireShiftForZones && !_zoneDragUsesWindowControl && (GetAsyncKeyState(VkShift) & 0x8000) == 0)
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
        GetZoneGridDimensions(out var columns, out var rows);
        var directionMask = ReadZoneDirectionMask();
        var newlyPressedDirections = directionMask & ~_lastZoneDirectionMask;
        _lastZoneDirectionMask = directionMask;

        if (_keyboardZoneSelectionActive &&
            (Math.Abs(cursor.X - _lastZoneCursor.X) > 4 || Math.Abs(cursor.Y - _lastZoneCursor.Y) > 4))
            _keyboardZoneSelectionActive = false;

        if (!_keyboardZoneSelectionActive)
        {
            var hoveredZone = FindZone(zones, cursor);
            var selection = FindZonesNearDividers(zones, columns, rows, cursor, _zoneMergeDistance);
            if (selection.Count == 0 && hoveredZone >= 0) selection.Add(hoveredZone);
            var primaryZone = hoveredZone >= 0 ? hoveredZone : selection.FirstOrDefault(-1);
            if (primaryZone != _selectedZone || !_selectedZones.SetEquals(selection))
            {
                _selectedZone = primaryZone;
                SetZoneSelection(selection);
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
        _zonePreview ??= new ZonePreviewWindow((_context as IFeatureUiContext)?.FeatureUi);
        RECT? mergedBounds = _selectedZones.Count > 1 && TryGetSpanBounds(zones, _selectedZones, _gap, out var selectedBounds)
            ? selectedBounds
            : null;
        _zonePreview.ShowZones(workArea, zones, _selectedZones, scale, mergedBounds,
            _zoneShowNumbers, _zoneActiveColor, _zoneInactiveColor, _zoneBorderThickness);
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
        var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var wasFullAtDragStart = _dragStartedWithAllZonesOccupied &&
                                 string.Equals(_dragStartMonitorName, monitorName, StringComparison.OrdinalIgnoreCase);
        if (wasFullAtDragStart || AreAllZonesOccupied(CountOccupiedZones(zones, monitor, hwnd)) ||
            (_selectedZones.Count == zones.Count && zones.Count > 0))
        {
            EnterTrueFullscreen(hwnd, monitor);
            return;
        }
        if (_selectedZones.Count > 1 && TryGetSpanBounds(zones, _selectedZones, _gap, out var spanBounds))
        {
            Move(hwnd, spanBounds);
            return;
        }
        var zone = _selectedZone >= 0 && _selectedZone < zones.Count ? _selectedZone : FindNearestZone(zones, cursor);
        if (zone >= 0) Move(hwnd, zones[zone]);
    }

    public bool TryPlaceWindowInZone(IntPtr window, POINT point)
    {
        if (!IsActive || _mode != ZoneMoveMode || !Native.IsWindow(window) || !TryTileable(window, out _, out _) ||
            !TryGetWorkArea(point, out var workArea, out var monitorName)) return false;
        ApplyMonitorGridProfile(monitorName);
        var zones = CreateZones(workArea);
        var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        if (AreAllZonesOccupied(CountOccupiedZones(zones, monitor, window)))
        {
            EnterTrueFullscreen(window, monitor);
            return _fullscreenWindows.ContainsKey(window);
        }
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

    private void SetZoneSelection(IEnumerable<int> zones)
    {
        _selectedZones.Clear();
        foreach (var zone in zones) _selectedZones.Add(zone);
    }

    private void GetZoneGridDimensions(out int columns, out int rows)
    {
        columns = _zoneLayout switch { 1 => 3, 2 => 2, 3 => _zoneColumns, _ => 2 };
        rows = _zoneLayout switch { 2 => 2, 3 => _zoneRows, _ => 1 };
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
        => TryTileableCore(hwnd, out monitor, out work, allowMaximized: false);

    private bool TryTileableCore(IntPtr hwnd, out IntPtr monitor, out RECT work, bool allowMaximized)
    {
        monitor = IntPtr.Zero;
        work = default;

        if (!Native.IsWindowVisible(hwnd)) return false;

        Native.GetWindowThreadProcessId(hwnd, out var processId);
        if (_mode != ZoneMoveMode && processId == (uint)Environment.ProcessId) return false;

        var extended = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((extended & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return false;

        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if (IgnoredClasses.Contains(Native.ClassNameOf(hwnd))) return false;
        if (IsCloaked(hwnd)) return false;
        if (Native.GetWindowTextLength(hwnd) == 0) return false;
        if (Native.IsIconic(hwnd))
        {
            if (!_noMinimize || _mode == ZoneMoveMode) return false;
            ShowWindow(hwnd, SW_RESTORE);
            if (Native.IsIconic(hwnd)) return false;
        }
        var maximized = IsZoomed(hwnd);
        if (maximized && !allowMaximized) return false;

        monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        if (!Native.GetWindowRect(hwnd, out var rect)) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 80 || height < 80) return false;

        if (!(allowMaximized && maximized) && Covers(rect, info.rcMonitor) && !IsOurPlacement(hwnd, rect)) return false;

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
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

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
