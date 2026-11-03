using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class DisplayBrightnessModule : ITweakModule
{
    private const string DefaultDimBrightnessHotkey = "Ctrl+Alt+F11";
    private const string DefaultBrightenBrightnessHotkey = "Ctrl+Alt+F12";
    private IModuleContext? _context;
    private DisplayBrightnessWindow? _window;
    private DispatcherTimer? _scheduleTimer;
    private IDisposable? _dimBrightnessHotkey;
    private IDisposable? _brightenBrightnessHotkey;
    private GlobalMouseHook? _brightnessMouseHook;
    private readonly SemaphoreSlim _brightnessAdjustmentGate = new(1, 1);
    private readonly object _hotkeySessionSync = new();
    private DisplayBrightnessSession? _hotkeyBrightnessSession;
    private Task<DisplayBrightnessSession>? _hotkeyBrightnessDiscovery;
    private int? _lastScheduledBrightness;
    private bool _scheduleApplying;

    public string Id => "display-brightness";
    public string Name => "Power Display · Ekran Denetimi";
    public string Category => "Sistem";
    public string Description => "Dahili panel parlaklığını Windows WMI ile, harici ekran parlaklığı, kontrast, monitör sesi, giriş kaynağı ve renk ön ayarlarını DDC/CI ile yönetin.";
    public string? Warning => "Harici ekranlarda monitör menüsünden DDC/CI açık olmalıdır. Yalnızca yetenek bildirgesinde sunulan VCP kontrolleri gösterilir; üretici DDC/CI uyumsuzluğu bazı donanım sonuçlarını etkileyebilir. Renk ön ayarı monitör renklerini değiştirebilir.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public DisplayBrightnessModule()
    {
        Settings = new[]
        {
            new SettingDescriptor
            {
                Key = "open-panel",
                Label = "Power Display panelini aç",
                Kind = SettingKind.Action,
                Description = "Uyumlu harici ekranların desteklenen DDC/CI denetimlerini gösterir.",
                Execute = OpenPanel
            },
            new SettingDescriptor
            {
                Key = "brightness-schedule-enabled",
                Label = "Zamanlanmış parlaklık etkin",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Gündüz ve gece başlangıç saatlerinde desteklenen ekran parlaklığını panel kapalıyken de uygular."
            },
            new SettingDescriptor
            {
                Key = "day-start-time",
                Label = "Gündüz başlangıcı (SS:dd)",
                Kind = SettingKind.Text,
                Default = "07:00",
                Description = "Yerel saatle gündüz parlaklık döneminin başlangıcı."
            },
            new SettingDescriptor
            {
                Key = "day-brightness",
                Label = "Gündüz parlaklığı (%)",
                Kind = SettingKind.Number,
                Default = 75,
                Min = 0,
                Max = 100
            },
            new SettingDescriptor
            {
                Key = "night-start-time",
                Label = "Gece başlangıcı (SS:dd)",
                Kind = SettingKind.Text,
                Default = "20:00",
                Description = "Yerel saatle gece parlaklık döneminin başlangıcı."
            },
            new SettingDescriptor
            {
                Key = "night-brightness",
                Label = "Gece parlaklığı (%)",
                Kind = SettingKind.Number,
                Default = 35,
                Min = 0,
                Max = 100
            },
            new SettingDescriptor
            {
                Key = "brightness-hotkeys-enabled",
                Label = "İmleçteki ekran için parlaklık kısayolları",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Ctrl+Alt+F11 parlaklığı azaltır, Ctrl+Alt+F12 artırır. Yalnızca imlecin bulunduğu ekran değiştirilir."
            },
            new SettingDescriptor
            {
                Key = "brightness-hotkey-step",
                Label = "Kısayol adımı (%)",
                Kind = SettingKind.Number,
                Default = 5,
                Min = 1,
                Max = 20,
                Description = "Her kısayol basışında uygulanacak parlaklık değişimi."
            },
            new SettingDescriptor
            {
                Key = "brightness-dim-hotkey",
                Label = "Parlaklığı azaltma kısayolu",
                Kind = SettingKind.Text,
                Default = DefaultDimBrightnessHotkey,
                Description = "Örnek: Ctrl+Alt+F11 veya Ctrl+Alt+MouseWheelDown. Ctrl, Alt, Shift ya da Win kullanın."
            },
            new SettingDescriptor
            {
                Key = "brightness-brighten-hotkey",
                Label = "Parlaklığı artırma kısayolu",
                Kind = SettingKind.Text,
                Default = DefaultBrightenBrightnessHotkey,
                Description = "Örnek: Ctrl+Alt+F12 veya Ctrl+Alt+MouseWheelUp. Azaltma kısayolundan farklı olmalıdır."
            }
        };
    }

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        RegisterBrightnessHotkeys();
        StartHotkeyBrightnessDiscovery();
        UpdateScheduleTimer();
    }

    public void Deactivate()
    {
        IsActive = false;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _scheduleTimer?.Stop();
        _scheduleTimer = null;
        _lastScheduledBrightness = null;
        ReleaseBrightnessHotkeys();
        DisposeHotkeyBrightnessSessionIfIdle();
        _window?.Close();
        _window = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => DisposeHotkeyBrightnessSessionIfIdle();

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            DisposeHotkeyBrightnessSessionIfIdle();
    }

    public void Apply()
    {
        _lastScheduledBrightness = null;
        if (IsActive)
        {
            RegisterBrightnessHotkeys();
            UpdateScheduleTimer();
        }
    }

    private void RegisterBrightnessHotkeys()
    {
        ReleaseBrightnessHotkeys();
        if (_context?.Settings.GetBool("brightness-hotkeys-enabled", false) != true) return;

        try
        {
            var dimHotkey = ParseBrightnessHotkey(_context.Settings.GetString("brightness-dim-hotkey", DefaultDimBrightnessHotkey));
            var brightenHotkey = ParseBrightnessHotkey(_context.Settings.GetString("brightness-brighten-hotkey", DefaultBrightenBrightnessHotkey));
            if (dimHotkey == brightenHotkey)
                throw new FormatException("Parlaklığı artırma ve azaltma kısayolları farklı olmalıdır.");

            RegisterBrightnessShortcut(dimHotkey, -1);
            RegisterBrightnessShortcut(brightenHotkey, 1);
            if (dimHotkey.WheelDirection != 0 || brightenHotkey.WheelDirection != 0)
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                _brightnessMouseHook = new GlobalMouseHook
                {
                    EventFilter = input => IsActive && _context is not null &&
                        HandleBrightnessWheel(input, GetPressedHotkeyModifiers(), dimHotkey, brightenHotkey,
                            direction => dispatcher.BeginInvoke(new Action(() => _ = AdjustBrightnessAsync(direction))))
                };
                _brightnessMouseHook.Install();
            }
        }
        catch (Exception exception)
        {
            ReleaseBrightnessHotkeys();
            _context.Log.Error("Power Display parlaklık kısayolları kaydedilemedi.", exception);
            _context.Notify($"Power Display parlaklık kısayolları kaydedilemedi: {exception.Message}");
        }
    }

    private void RegisterBrightnessShortcut((uint Modifiers, uint VirtualKey, int WheelDirection) shortcut, int direction)
    {
        if (shortcut.WheelDirection != 0) return;
        var registration = _context!.Hotkeys.Register(shortcut.Modifiers, shortcut.VirtualKey,
            () => _ = AdjustBrightnessAsync(direction));
        if (direction < 0) _dimBrightnessHotkey = registration;
        else _brightenBrightnessHotkey = registration;
    }

    internal static bool HandleBrightnessWheel(GlobalMouseHook.Input input, uint modifiers,
        (uint Modifiers, uint VirtualKey, int WheelDirection) dimShortcut,
        (uint Modifiers, uint VirtualKey, int WheelDirection) brightenShortcut, Action<int> adjust)
    {
        if (HotkeyService.IsCapturing || input.Message != Native.WM_MOUSEWHEEL || input.WheelDelta == 0) return false;
        var wheelDirection = Math.Sign(input.WheelDelta);

        if (dimShortcut.WheelDirection == wheelDirection && dimShortcut.Modifiers == modifiers)
            adjust(-1);
        else if (brightenShortcut.WheelDirection == wheelDirection && brightenShortcut.Modifiers == modifiers)
            adjust(1);
        else return false;
        return true;
    }

    private static uint GetPressedHotkeyModifiers()
    {
        uint modifiers = 0;
        if (IsKeyDown(0x11)) modifiers |= HotkeyModifiers.Control;
        if (IsKeyDown(0x12)) modifiers |= HotkeyModifiers.Alt;
        if (IsKeyDown(0x10)) modifiers |= HotkeyModifiers.Shift;
        if (IsKeyDown(0x5B) || IsKeyDown(0x5C)) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    internal static (uint Modifiers, uint VirtualKey, int WheelDirection) ParseBrightnessHotkey(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("Kısayolu Ctrl+Alt+F11 biçiminde yazın.");

        uint modifiers = 0;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = parts[index].ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => throw new FormatException($"'{parts[index]}' tanınan bir kısayol değiştiricisi değil.")
            };
            if ((modifiers & modifier) != 0)
                throw new FormatException("Kısayolda aynı değiştiriciyi birden fazla yazmayın.");
            modifiers |= modifier;
        }

        var wheelDirection = parts[^1].ToLowerInvariant() switch
        {
            "mousewheelup" or "wheelup" => 1,
            "mousewheeldown" or "wheeldown" => -1,
            _ => 0
        };
        if (wheelDirection != 0) return (modifiers, 0, wheelDirection);

        if (!Enum.TryParse<Key>(parts[^1], true, out var key) ||
            key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed or Key.System or Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            throw new FormatException($"'{parts[^1]}' geçerli bir işlem tuşu değil.");

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is < 1 or > 254)
            throw new FormatException($"'{parts[^1]}' eşlenebilir bir tuş değil.");
        return (modifiers, (uint)virtualKey, 0);
    }

    private void ReleaseBrightnessHotkeys()
    {
        _dimBrightnessHotkey?.Dispose();
        _dimBrightnessHotkey = null;
        _brightenBrightnessHotkey?.Dispose();
        _brightenBrightnessHotkey = null;
        _brightnessMouseHook?.Dispose();
        _brightnessMouseHook = null;
    }

    private async Task AdjustBrightnessAsync(int direction)
    {
        if (!IsActive || _context is null) return;
        var step = Math.Clamp(_context.Settings.GetInt("brightness-hotkey-step", 5), 1, 20);
        await _brightnessAdjustmentGate.WaitAsync();
        try
        {
            if (!IsActive || _context is null) return;
            var result = await Task.Run(() =>
            {
                var session = _hotkeyBrightnessSession;
                if (session is not null && session.NeedsBrightnessRediscovery)
                {
                    session.Dispose();
                    session = _hotkeyBrightnessSession = null;
                }
                if (session is null)
                {
                    Task<DisplayBrightnessSession> discovery;
                    lock (_hotkeySessionSync)
                        discovery = _hotkeyBrightnessDiscovery ??= Task.Run(DisplayBrightnessSession.Discover);
                    session = discovery.GetAwaiter().GetResult();
                    if (session.NeedsBrightnessRediscovery)
                    {
                        session.Dispose();
                        session = DisplayBrightnessSession.Discover();
                    }
                    _hotkeyBrightnessSession = session;
                    lock (_hotkeySessionSync)
                        if (ReferenceEquals(_hotkeyBrightnessDiscovery, discovery)) _hotkeyBrightnessDiscovery = null;
                }
                var outcome = session.AdjustBrightnessForAllDisplays(step * direction);
                var monitors = OverlayMonitors(session);
                if (outcome.Failed > 0 || outcome.Supported == 0)
                {
                    session.Dispose();
                    _hotkeyBrightnessSession = null;
                }
                return (Outcome: outcome, Monitors: monitors);
            });
            if (!IsActive) return;
            var monitors = result.Monitors;
            var outcome = result.Outcome;
            if (outcome.Supported == 0)
                _context.Notify("Bağlı ekranlarda desteklenen bir parlaklık denetimi bulunamadı.");
            else if (outcome.Failed > 0)
                _context.Notify($"Ekran parlaklığı: {outcome.Applied}/{outcome.Supported} ekrana uygulandı; {outcome.Failed} hata.");
            ShowBrightnessOutcome(outcome, monitors);
        }
        catch (Exception exception)
        {
            _context.Log.Error("Power Display kısayoluyla parlaklık değiştirilemedi.", exception);
            _context.Notify("Ekran parlaklığı değiştirilemedi: " + exception.Message);
        }
        finally
        {
            if (!IsActive)
                Interlocked.Exchange(ref _hotkeyBrightnessSession, null)?.Dispose();
            _brightnessAdjustmentGate.Release();
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _overlayBrightnessRequests = new();

    private static IReadOnlyList<BrightnessOverlayMonitor> OverlayMonitors(DisplayBrightnessSession session) =>
        session.Monitors.Where(m => m.SupportsBrightness || m.VcpBrightness is not null)
            .Select(m => new BrightnessOverlayMonitor(m.Id, m.Name, m.Percent)).ToArray();

    private async Task SetOverlayBrightnessAsync(string id, int percent)
    {
        var request = _overlayBrightnessRequests.AddOrUpdate(id, 1, (_, previous) => previous + 1);
        await _brightnessAdjustmentGate.WaitAsync();
        try
        {
            if (!IsActive || _overlayBrightnessRequests[id] != request) return;
            var result = await Task.Run(() =>
            {
                if (_hotkeyBrightnessSession is { } cached && cached.NeedsBrightnessRediscovery)
                {
                    cached.Dispose();
                    _hotkeyBrightnessSession = null;
                }
                var session = _hotkeyBrightnessSession ??= DisplayBrightnessSession.Discover();
                var success = session.SetBrightness(id, percent, out _);
                var monitors = OverlayMonitors(session);
                if (!success)
                {
                    session.Dispose();
                    _hotkeyBrightnessSession = null;
                }
                return (success, monitors);
            });
            if (_overlayBrightnessRequests[id] == request)
            {
                var status = result.success ? null : "Parlakl?k de?i?tirilemedi";
                if (_context is IDisplayBrightnessOverlayContext overlay)
                    overlay.ShowDisplayBrightness(percent, "all-displays", status: status, monitors: result.monitors,
                        onMonitorBrightnessChanged: (monitorId, value) => _ = SetOverlayBrightnessAsync(monitorId, value));
            }
        }
        catch (Exception exception)
        {
            _context?.Log.Error("G?sterge ?zerinden parlakl?k de?i?tirilemedi.", exception);
        }
        finally
        {
            if (!IsActive) Interlocked.Exchange(ref _hotkeyBrightnessSession, null)?.Dispose();
            _brightnessAdjustmentGate.Release();
        }
    }

    private void ShowBrightnessOutcome((int Supported, int Applied, int Failed, int? Brightness) result,
        IReadOnlyList<BrightnessOverlayMonitor> monitors)
    {
        if (!IsActive || _context is not IDisplayBrightnessOverlayContext overlay) return;
        var connected = DesktopDisplays.Enumerate().Count;
        var status = result.Supported == 1 && connected == 1 && result.Failed == 0 ? null
            : $"{result.Applied}/{Math.Max(connected, result.Supported)} monit?re uyguland?";
        if (result.Failed > 0) status += $" ? {result.Failed} hata";
        if (result.Supported > 0 && result.Supported < connected)
            status += $" ? {connected - result.Supported} monit?rde denetim bulunamad?";
        if (result.Supported == 0) status = "Parlakl?k denetimi desteklenmiyor";
        overlay.ShowDisplayBrightness(result.Brightness ?? 0, "all-displays", status: status, monitors: monitors,
            onMonitorBrightnessChanged: (id, value) => _ = SetOverlayBrightnessAsync(id, value));
    }

    private void DisposeHotkeyBrightnessSessionIfIdle()
    {
        if (!_brightnessAdjustmentGate.Wait(0)) return;
        try
        {
            Interlocked.Exchange(ref _hotkeyBrightnessSession, null)?.Dispose();
            Task<DisplayBrightnessSession>? discovery;
            lock (_hotkeySessionSync)
            {
                discovery = _hotkeyBrightnessDiscovery;
                _hotkeyBrightnessDiscovery = null;
            }
            if (discovery is not null) _ = DisposeDiscoveredSessionAsync(discovery);
        }
        finally { _brightnessAdjustmentGate.Release(); }
    }

    private void StartHotkeyBrightnessDiscovery()
    {
        lock (_hotkeySessionSync)
            _hotkeyBrightnessDiscovery ??= Task.Run(DisplayBrightnessSession.Discover);
    }

    private static async Task DisposeDiscoveredSessionAsync(Task<DisplayBrightnessSession> discovery)
    {
        try { (await discovery.ConfigureAwait(false)).Dispose(); }
        catch { }
    }

    private void UpdateScheduleTimer()
    {
        var settings = _context?.Settings;
        if (!IsActive || settings?.GetBool("brightness-schedule-enabled", false) != true)
        {
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            return;
        }

        if (!DisplayBrightnessSchedule.TryGetTarget(DateTime.Now,
                settings.GetString("day-start-time", "07:00"), settings.GetInt("day-brightness", 75),
                settings.GetString("night-start-time", "20:00"), settings.GetInt("night-brightness", 35), out _))
        {
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            _ = ApplyScheduledBrightnessAsync();
            return;
        }

        if (_scheduleTimer is null)
        {
            _scheduleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _scheduleTimer.Tick += async (_, _) => await ApplyScheduledBrightnessAsync();
        }
        if (!_scheduleTimer.IsEnabled) _scheduleTimer.Start();
        _ = ApplyScheduledBrightnessAsync();
    }

    private async Task ApplyScheduledBrightnessAsync()
    {
        var settings = _context?.Settings;
        if (!IsActive || _scheduleApplying || settings?.GetBool("brightness-schedule-enabled", false) != true) return;
        if (!DisplayBrightnessSchedule.TryGetTarget(DateTime.Now,
                settings.GetString("day-start-time", "07:00"), settings.GetInt("day-brightness", 75),
                settings.GetString("night-start-time", "20:00"), settings.GetInt("night-brightness", 35), out var target))
        {
            _context?.Log.Error("Power Display zamanlanmış parlaklığı uygulanmadı: saatler SS:dd biçiminde olmalıdır.");
            return;
        }
        if (_lastScheduledBrightness == target) return;

        _scheduleApplying = true;
        try
        {
            var result = await Task.Run(() =>
            {
                using var session = DisplayBrightnessSession.Discover();
                var supported = 0;
                var applied = 0;
                var failed = 0;
                foreach (var monitor in session.Monitors)
                {
                    if (!monitor.SupportsBrightness && monitor.VcpBrightness is null) continue;
                    supported++;
                    if (session.SetBrightness(monitor.Id, target, out _)) applied++;
                    else failed++;
                }
                return (supported, applied, failed);
            });

            if (!IsActive) return;
            if (result.applied > 0 || result.supported == 0) _lastScheduledBrightness = target;
            if (result.failed > 0)
                _context?.Log.Error($"Power Display zamanlanmış parlaklığı: {result.applied}/{result.supported} ekran uygulandı; {result.failed} hata.");
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Power Display zamanlanmış parlaklığı uygulanamadı.", exception);
        }
        finally
        {
            _scheduleApplying = false;
        }
    }

    private void OpenPanel()
    {
        try
        {
            var featureUi = (_context as IFeatureUiContext)?.FeatureUi
                ?? throw new InvalidOperationException("React arayüz altyapısı kullanılamıyor.");
            if (_window is null || !_window.IsVisible)
                _window = new DisplayBrightnessWindow(_context?.Settings, featureUi);
            else
                _window.Show();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Ekran parlaklığı paneli açılamadı.", exception);
            _context?.Notify("Ekran parlaklığı paneli açılamadı: " + exception.Message);
        }
    }
}
