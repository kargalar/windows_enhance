using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Provides a compact, keyboard-driven audio and media flyout.</summary>
public sealed class MediaFlyoutModule : ITweakModule
{
    private const uint VkM = 0x4D;
    private const uint VkV = 0x56;
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private IDisposable? _volumeHotkey;
    private IDisposable? _externalVolumeWatcher;
    private int _lastObservedVolume = -1;
    private bool? _lastObservedMuted;
    private LockKeysFlyoutHook? _lockKeysHook;
    private MediaFlyoutWindow? _window;
    private TaskbarMediaWidgetWindow? _taskbarWidget;
    private TaskbarAudioVisualizer? _taskbarVisualizer;
    private readonly SettingDescriptor[] _settings;

    public MediaFlyoutModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open-flyout",
                Label = "Medya ve ses flyout’unu aç",
                Kind = SettingKind.Action,
                Description = "Win+Alt+M kısayoluyla da açabilirsiniz.",
                Execute = OpenFlyout
            },
            new SettingDescriptor
            {
                Key = "hotkey-enabled",
                Label = "Win+Alt+M kısayolu etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Kısayol flyout’u açar veya ekrandaysa kapatır."
            },
            new SettingDescriptor
            {
                Key = "volume-hotkey-enabled",
                Label = "Ctrl+Alt+V ses flyout kısayolu etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Hızlı erişim için medya ve ses flyout’unu açar."
            },
            new SettingDescriptor
            {
                Key = "up-next-enabled",
                Label = "Müzik / parça değiştiğinde otomatik göster",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Çalan müzik veya parça değiştiğinde flyout kapalı olsa bile kısa süreliğine otomatik görünür ve yeni parça bilgisini gösterir."
            },
            new SettingDescriptor
            {
                Key = "flyout-pinned",
                Label = "Flyout penceresini ekranda sabitle (Pin)",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Etkinleştirildiğinde flyout otomatik kapanmaz ve ekranda sabit kalır. Bu ayarı kapatarak otomatik kapanmayı yeniden etkinleştirebilirsiniz.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "flyout-auto-dismiss-seconds",
                Label = "Otomatik açılışta görünür kalma süresi (saniye)",
                Kind = SettingKind.Number,
                Default = 3,
                Min = 1,
                Max = 30,
                Description = "Medya olayıyla otomatik açılan flyout’un ekranda kalacağı kısa süre.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "taskbar-media-widget-enabled",
                Label = "Küçük medya oynatıcısını görev çubuğunun üstünde göster",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Bir medya oturumu etkinken birincil görev çubuğunun hemen üstünde kompakt oynatıcı gösterir; tam ekran uygulamalarda gizlenir.",
                Section = "Medya"
            },
            new SettingDescriptor
            {
                Key = "taskbar-widget-scroll-mode",
                Label = "Mini oynatıcıda tekerlek davranışı",
                Kind = SettingKind.Choice,
                Default = "master-volume",
                Options = ["master-volume", "player-volume", "off"],
                OptionLabels = new Dictionary<string, string>
                {
                    ["master-volume"] = "Ana ses düzeyini değiştir",
                    ["player-volume"] = "Seçili oynatıcının sesini değiştir",
                    ["off"] = "Kapalı"
                },
                Description = "Fare tekerleği mini oynatıcı üzerindeyken ana çıkışın veya seçili medya uygulamasının sesini ayarlar.",
                Section = "Medya"
            },
            new SettingDescriptor
            {
                Key = "taskbar-visualizer-enabled",
                Label = "Görev çubuğu üstü ses görselleştiricisi etkin",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Varsayılan ses çıkışından gerçek zamanlı spektrum çizer. Yalnızca açıldığında ses akışını işler; ses kaydetmez.",
                Section = "Medya"
            },
            new SettingDescriptor
            {
                Key = "taskbar-visualizer-position",
                Label = "Ses görselleştiricisi konumu",
                Kind = SettingKind.Choice,
                Default = "center",
                Options = ["center", "near-widget"],
                OptionLabels = new Dictionary<string, string> { ["center"] = "Görev çubuğunun ortası", ["near-widget"] = "Mini oynatıcının solu" },
                Description = "Görselleştiriciyi birincil görev çubuğunun üzerinde konumlandırır.",
                Section = "Medya"
            },
            new SettingDescriptor
            {
                Key = "taskbar-visualizer-baseline",
                Label = "Sessizlikte taban çizgisini göster",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Görselleştirici sessizlikteyken taban çizgisini açık tutar.",
                Section = "Medya"
            },
            new SettingDescriptor
            {
                Key = "taskbar-visualizer-hide-when-silent",
                Label = "Sessizlikte görselleştiriciyi gizle",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Çıkış sesinde etkinlik yokken görselleştiriciyi gizler.",
                Section = "Medya"
            },
            new SettingDescriptor
            {
                Key = "media-app-filter-mode",
                Label = "Medya uygulaması filtreleme",
                Kind = SettingKind.Choice,
                Default = "all",
                Options = ["all", "allowlist", "blocklist"],
                OptionLabels = new Dictionary<string, string>
                {
                    ["all"] = "Tüm uygulamalar", ["allowlist"] = "Yalnızca izin verilenler", ["blocklist"] = "Belirtilenleri gizle"
                },
                Description = "Medya oturumu seçicisinde ve denetimlerinde hangi uygulamaların görüneceğini belirler.",
                Section = "Medya"
            },
            new SettingDescriptor
            {
                Key = "media-app-filter-list",
                Label = "Uygulama filtre listesi",
                Kind = SettingKind.Text,
                Default = "",
                Description = "Uygulama işlem adlarını veya paket kimliklerini noktalı virgülle ayırın (ör. Spotify; msedge). Uzantı ve büyük/küçük harf önemli değildir.",
                Section = "Medya"
            },
            new SettingDescriptor
            {
                Key = "lock-keys-flyout-enabled",
                Label = "Kilit tuşları OSD etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Caps Lock, Num Lock ve Scroll Lock değişince odağı çalmayan bir durum penceresi gösterir."
            },
            new SettingDescriptor
            {
                Key = "caps-lock-flyout-enabled",
                Label = "Caps Lock göstergesi etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Caps Lock durumu değiştiğinde kilit tuşu göstergesini açar.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "num-lock-flyout-enabled",
                Label = "Num Lock göstergesi etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Num Lock durumu değiştiğinde kilit tuşu göstergesini açar.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "scroll-lock-flyout-enabled",
                Label = "Scroll Lock göstergesi etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Scroll Lock durumu değiştiğinde kilit tuşu göstergesini açar.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "hardware-keys-flyout-enabled",
                Label = "Donanım ses ve medya tuşlarında bildirimleri göster",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Ses tuşlarını WinForge işler ve yalnızca ses OSD’sini gösterir; medya tuşlarında medya panelini açar.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "external-volume-flyout-enabled",
                Label = "Harici aygıt ses değişiminde ses OSD’sini göster",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Windows varsayılan ses çıkışındaki ses veya sessize alma değişimini bildirirse ekranın alt ortasında ayarlanabilir ses OSD’sini gösterir. Destek, ses sürücüsünün değişikliği Windows Core Audio’ya iletmesine bağlıdır.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "flyout-position",
                Label = "Flyout konumu",
                Kind = SettingKind.Choice,
                Default = "bottom-right",
                Options = ["bottom-right", "bottom-left", "top-right", "top-left"],
                OptionLabels = new Dictionary<string, string>
                {
                    ["bottom-right"] = "Sağ alt", ["bottom-left"] = "Sol alt",
                    ["top-right"] = "Sağ üst", ["top-left"] = "Sol üst"
                },
                Description = "Seçili monitörün çalışma alanına göre flyout köşesini belirler.",
                Section = "Görünüm"
            },
            new SettingDescriptor
            {
                Key = "flyout-monitor",
                Label = "Flyout monitörü",
                Kind = SettingKind.Choice,
                Default = "cursor",
                Options = ["cursor", "primary", "active"],
                OptionLabels = new Dictionary<string, string>
                {
                    ["cursor"] = "Fare imlecinin bulunduğu", ["primary"] = "Birincil", ["active"] = "Etkin pencerenin bulunduğu"
                },
                Description = "Birden fazla ekran kullanırken flyout’un hangi ekranda açılacağını belirler.",
                Section = "Görünüm"
            },
            new SettingDescriptor
            {
                Key = "flyout-animation",
                Label = "Açılış animasyonu",
                Kind = SettingKind.Choice,
                Default = "fade",
                Options = ["fade", "none"],
                OptionLabels = new Dictionary<string, string> { ["fade"] = "Yumuşak belirme", ["none"] = "Kapalı" },
                Description = "Flyout açılırken kısa bir belirme animasyonu kullanır.",
                Section = "Görünüm"
            },
            new SettingDescriptor
            {
                Key = "flyout-dismiss-seconds",
                Label = "Etkileşimden sonra kapanma (saniye)",
                Kind = SettingKind.Number,
                Default = 8,
                Min = 0,
                Max = 60,
                Description = "Fare veya klavye etkinliği durduktan sonra kapatır. 0 değeri otomatik kapatmayı devre dışı bırakır.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "flyout-fullscreen-suppressed",
                Label = "Tam ekranda flyout’u gösterme",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Ön plandaki pencere ekranı kaplıyorsa flyout’u açmaz.",
                Section = "Davranış"
            }
        ];
    }

    public string Id => "media-flyout";
    public string Name => "Fluent Medya Flyout’u";
    public string Category => "Sistem";
    public string Description => "FluentFlyout benzeri medya denetimi, ses değişimlerinde alt-orta ayarlanabilir ses OSD’si, medya tuşlarında açılan flyout, medya uygulaması filtreleri, oturum seçimi, parça konumu, shuffle/repeat, kilit tuşu OSD’si, konum seçimi ve çoklu ekran desteği.";
    public string? Warning => "Medya bilgisi Windows medya oturumu API’sine bağlıdır; bazı oynatıcılar albüm kapağı, konum veya karıştır/tekrar denetimlerini sağlamaz. Ses görselleştiricisi yalnızca etkinleştirildiğinde paylaşılan çıkış sesini işler ve kayıt oluşturmaz.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context)
    {
        _context = context;
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        try
        {
            RegisterHotkey();
            RegisterVolumeHotkey();
            RegisterExternalVolumeWatcher();
            RegisterLockKeysFlyout();
            ConfigureFlyoutWindow();
            ConfigureTaskbarWidget();
            ConfigureTaskbarVisualizer();
        }
        catch (Exception exception)
        {
            IsActive = false;
            _hotkey?.Dispose();
            _hotkey = null;
            _volumeHotkey?.Dispose();
            _volumeHotkey = null;
            _externalVolumeWatcher?.Dispose();
            _externalVolumeWatcher = null;
            _lockKeysHook?.Dispose();
            _lockKeysHook = null;
            _window?.Close();
            _window = null;
            _taskbarWidget?.Close();
            _taskbarWidget = null;
            _taskbarVisualizer?.Dispose();
            _taskbarVisualizer = null;
            _context?.Log.Error("Medya flyout kısayolu kaydedilemedi.", exception);
            throw;
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
        _volumeHotkey?.Dispose();
        _volumeHotkey = null;
        _externalVolumeWatcher?.Dispose();
        _externalVolumeWatcher = null;
        _lockKeysHook?.Dispose();
        _lockKeysHook = null;
        _taskbarWidget?.Close();
        _taskbarWidget = null;
        _taskbarVisualizer?.Dispose();
        _taskbarVisualizer = null;
        _window?.Close();
        _window = null;
    }

    public void Apply()
    {
        if (!IsActive) return;
        _hotkey?.Dispose();
        _hotkey = null;
        var primaryHotkeyEnabled = _context?.Settings.GetBool("hotkey-enabled", true) == true;
        var volumeHotkeyEnabled = _context?.Settings.GetBool("volume-hotkey-enabled", true) == true;
        RegisterHotkey();
        _volumeHotkey?.Dispose();
        _volumeHotkey = null;
        RegisterVolumeHotkey();
        NotifyHotkeyAvailability(primaryHotkeyEnabled, volumeHotkeyEnabled);
        RegisterExternalVolumeWatcher();
        _lockKeysHook?.Dispose();
        _lockKeysHook = null;
        RegisterLockKeysFlyout();
        ConfigureFlyoutWindow();
        ConfigureTaskbarWidget();
        ConfigureTaskbarVisualizer();
    }

    private void NotifyHotkeyAvailability(bool primaryHotkeyEnabled, bool volumeHotkeyEnabled)
    {
        if (_context is null) return;

        string? message = null;
        if (primaryHotkeyEnabled && _hotkey is null)
        {
            message = _volumeHotkey is not null
                ? "Win+Alt+M başka bir uygulama tarafından kullanılıyor. Medya Flyout'u açmak için Ctrl+Alt+V kısayolunu kullanabilirsiniz."
                : volumeHotkeyEnabled
                    ? "Win+Alt+M ve Ctrl+Alt+V başka uygulamalar tarafından kullanılıyor; Medya Flyout kısayolları etkinleştirilemedi."
                    : "Win+Alt+M başka bir uygulama tarafından kullanılıyor; Ctrl+Alt+V yedek kısayolu ayarlardan etkinleştirebilirsiniz.";
        }
        else if (!primaryHotkeyEnabled && volumeHotkeyEnabled && _volumeHotkey is null)
        {
            message = "Ctrl+Alt+V başka bir uygulama tarafından kullanılıyor; Medya Flyout kısayolu etkinleştirilemedi.";
        }

        if (message is null) return;
        _context.Notify(message);
    }

    private void RegisterHotkey()
    {
        if (_context?.Settings.GetBool("hotkey-enabled", true) == true)
            _hotkey = RegisterFlyoutHotkey(
                "Win+Alt+M",
                HotkeyModifiers.Win | HotkeyModifiers.Alt,
                VkM);
    }

    private void RegisterVolumeHotkey()
    {
        if (_context?.Settings.GetBool("volume-hotkey-enabled", true) == true)
            _volumeHotkey = RegisterFlyoutHotkey(
                "Ctrl+Alt+V",
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                VkV);
    }

    private IDisposable? RegisterFlyoutHotkey(string shortcut, uint modifiers, uint virtualKey)
    {
        if (_context is null) return null;

        try
        {
            return _context.Hotkeys.Register(modifiers, virtualKey, OpenFlyout);
        }
        catch (InvalidOperationException exception)
        {
            var message = $"{shortcut} başka bir uygulama tarafından kullanılıyor; Fluent Medya için bu kısayol etkinleştirilemedi.";
            _context.Log.Error(message, exception);
            return null;
        }
    }

    private void RegisterExternalVolumeWatcher()
    {
        _externalVolumeWatcher?.Dispose();
        _externalVolumeWatcher = null;
        if (_context?.Settings.GetBool("external-volume-flyout-enabled", true) != true) return;
        try
        {
            _lastObservedVolume = TryReadCurrentVolume();
            _lastObservedMuted = TryReadCurrentMuted();
            _externalVolumeWatcher = VolumeService.WatchDefaultEndpoint(change =>
            {
                if (VolumeService.IsWinForgeChange(change)) return;
                if (_lastObservedVolume == change.VolumePercent && _lastObservedMuted == change.IsMuted) return;
                _lastObservedVolume = change.VolumePercent;
                _lastObservedMuted = change.IsMuted;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (!IsActive) return;
                    ShowVolumeOsd(change.VolumePercent, change.IsMuted);
                });
            });
        }
        catch (Exception exception)
        {
            _context?.Log.Info("Harici ses değişimi izleyicisi başlatılamadı: " + exception.Message);
        }
    }

    private static int TryReadCurrentVolume()
    {
        try { return VolumeService.Current(); }
        catch { return -1; }
    }

    private static bool? TryReadCurrentMuted()
    {
        try { return VolumeService.IsMuted(); }
        catch { return null; }
    }

    private void HandleVolumeHardwareKey(int key)
    {
        if (!IsActive) return;
        try
        {
            const int volumeStepPercent = 2;
            if (key == LockKeysFlyoutHook.VolumeMuteKey)
                VolumeService.SetMutedFromFlyout(!VolumeService.IsMuted());
            else
                VolumeService.StepFromFlyout(key == LockKeysFlyoutHook.VolumeUpKey
                    ? volumeStepPercent : -volumeStepPercent);
            ShowVolumeOsd(VolumeService.Current(), VolumeService.IsMuted());
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Ses tuşu işlenemedi.", exception);
        }
    }

    private void ShowVolumeOsd(int percent, bool isMuted)
    {
        if (_context is IAudioVolumeOverlayContext overlay)
            overlay.ShowAudioVolume(percent, isMuted, null,
                Math.Clamp(_context.Settings.GetInt("flyout-auto-dismiss-seconds", 3), 1, 30));
        else
            _context?.Notify(isMuted ? $"Ses kapalı · {percent}%" : $"Ses {percent}%");
    }

    private void RegisterLockKeysFlyout()
    {
        LockKeysStatusWindow.Configure((_context as IFeatureUiContext)?.FeatureUi);
        var settings = _context?.Settings;
        var lockKeysEnabled = settings?.GetBool("lock-keys-flyout-enabled", true) == true;
        var hardwareKeysEnabled = _context?.Settings.GetBool("hardware-keys-flyout-enabled", true) == true;
        var capsLockEnabled = lockKeysEnabled && settings?.GetBool("caps-lock-flyout-enabled", true) == true;
        var numLockEnabled = lockKeysEnabled && settings?.GetBool("num-lock-flyout-enabled", true) == true;
        var scrollLockEnabled = lockKeysEnabled && settings?.GetBool("scroll-lock-flyout-enabled", true) == true;
        if (capsLockEnabled || numLockEnabled || scrollLockEnabled || hardwareKeysEnabled)
            _lockKeysHook = new LockKeysFlyoutHook(capsLockEnabled, numLockEnabled, scrollLockEnabled,
                hardwareKeysEnabled ? OpenFlyoutFromHardwareKey : null,
                hardwareKeysEnabled ? HandleVolumeHardwareKey : null);
    }

    private void ConfigureTaskbarWidget()
    {
        if (_context?.Settings.GetBool("taskbar-media-widget-enabled", false) == true)
        {
            _taskbarWidget ??= new TaskbarMediaWidgetWindow(OpenFlyout, (_context as IFeatureUiContext)?.FeatureUi);
            _taskbarWidget.ConfigureMediaFilter(
                _context?.Settings.GetString("media-app-filter-mode", "all") ?? "all",
                _context?.Settings.GetString("media-app-filter-list", "") ?? "",
                _context?.Settings.GetString("taskbar-widget-scroll-mode", "master-volume") ?? "master-volume",
                _context?.Settings.GetString("pinned-media-session", "") ?? "",
                appId => _context?.Settings.Set("pinned-media-session", appId ?? ""));
            if (!_taskbarWidget.IsVisible) _taskbarWidget.Show();
        }
        else
        {
            _taskbarWidget?.Close();
            _taskbarWidget = null;
        }
    }

    private void ConfigureTaskbarVisualizer()
    {
        _taskbarVisualizer?.Dispose();
        _taskbarVisualizer = null;
        var settings = _context?.Settings;
        if (settings?.GetBool("taskbar-visualizer-enabled", false) != true) return;
        _taskbarVisualizer = new TaskbarAudioVisualizer(
            (_context as IFeatureUiContext)?.FeatureUi,
            settings.GetString("taskbar-visualizer-position", "center"),
            settings.GetBool("taskbar-media-widget-enabled", false),
            settings.GetBool("taskbar-visualizer-baseline", false),
            settings.GetBool("taskbar-visualizer-hide-when-silent", true));
    }

    private void ConfigureFlyoutWindow()
    {
        if (!IsActive || Application.Current is null) return;
        try
        {
            var window = EnsureConfiguredWindow();
            window.StartBackgroundMonitoring();
            if (_context?.Settings.GetBool("flyout-pinned", false) == true && !window.IsVisible)
            {
                if (_context?.Settings.GetBool("flyout-fullscreen-suppressed", false) == true &&
                    MediaFlyoutWindow.IsForegroundFullscreen()) return;
                window.ShowOrRefresh(activateWindow: false, isAutoTrigger: false);
            }
        }
        catch (Exception exception)
        {
            _context?.Log.Info("Medya flyout arka plan izleyicisi başlatılamadı: " + exception.Message);
        }
    }

    private MediaFlyoutWindow EnsureConfiguredWindow()
    {
        if (_window is null)
        {
            _window = new MediaFlyoutWindow((_context as IFeatureUiContext)?.FeatureUi);
            _window.PresentationRequested += () => ShowVolumeOsd(VolumeService.Current(), VolumeService.IsMuted());
            _window.Closed += (_, _) => _window = null;
        }
        var settings = _context?.Settings;
        _window.ConfigureUpNext(settings?.GetBool("up-next-enabled", true) == true);
        _window.Configure(
            settings?.GetString("flyout-position", "bottom-right") ?? "bottom-right",
            settings?.GetString("flyout-monitor", "cursor") ?? "cursor",
            settings?.GetString("flyout-animation", "fade") ?? "fade",
            settings?.GetInt("flyout-dismiss-seconds", 8) ?? 8,
            settings?.GetString("media-app-filter-mode", "all") ?? "all",
            settings?.GetString("media-app-filter-list", "") ?? "",
            settings?.GetString("pinned-media-session", "") ?? "",
            settings?.GetBool("flyout-pinned", false) == true,
            pinned => settings?.Set("flyout-pinned", pinned),
            settings?.GetInt("flyout-auto-dismiss-seconds", 3) ?? 3,
            settings?.GetBool("flyout-fullscreen-suppressed", false) == true);
        return _window;
    }

    private void OpenFlyout() => PresentFlyout(toggle: true);

    private void OpenFlyoutFromHardwareKey() => PresentFlyout(toggle: false);

    private void PresentFlyout(bool toggle)
    {
        if (!IsActive) return;
        try
        {
            var settings = _context?.Settings;
            var window = EnsureConfiguredWindow();
            if (settings?.GetBool("flyout-fullscreen-suppressed", false) == true && MediaFlyoutWindow.IsForegroundFullscreen()) return;
            if (toggle) window.Toggle();
            else window.ShowOrRefresh(activateWindow: false, isAutoTrigger: true);
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Medya flyout’u açılamadı.", exception);
            _context?.Notify("Medya flyout’u açılamadı: " + exception.Message);
        }
    }
}

internal sealed class LockKeysFlyoutHook : IDisposable
{
    internal const int VolumeMuteKey = 0xAD;
    internal const int VolumeDownKey = 0xAE;
    internal const int VolumeUpKey = 0xAF;
    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly Action? _showMediaFlyout;
    private readonly Action<int>? _handleVolumeKey;
    private bool _muteKeyDown;
    private readonly bool _showCapsLock;
    private readonly bool _showNumLock;
    private readonly bool _showScrollLock;
    private readonly IntPtr _hook;

    public LockKeysFlyoutHook(bool showCapsLock, bool showNumLock, bool showScrollLock, Action? showMediaFlyout,
        Action<int>? handleVolumeKey)
    {
        _showCapsLock = showCapsLock;
        _showNumLock = showNumLock;
        _showScrollLock = showScrollLock;
        _showMediaFlyout = showMediaFlyout;
        _handleVolumeKey = handleVolumeKey;
        _callback = HandleKeyboardEvent;
        _hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Kilit tuşları klavye izleyicisi başlatılamadı.");
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
    }

    private IntPtr HandleKeyboardEvent(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var isKeyUp = message == (IntPtr)Native.WM_KEYUP || message == (IntPtr)Native.WM_SYSKEYUP;
            var isKeyDown = message == (IntPtr)Native.WM_KEYDOWN || message == (IntPtr)Native.WM_SYSKEYDOWN;
            if (isKeyUp || isKeyDown)
            {
                var key = (int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data).vkCode;
                if (IsVolumeHardwareKey(key) && _handleVolumeKey is not null &&
                    Application.Current?.Dispatcher is { HasShutdownStarted: false } dispatcher)
                {
                    // Consume both edges so Windows cannot open its own volume OSD.
                    // Repeated volume presses still adjust the level; mute toggles once per press.
                    if (isKeyDown && (key != VolumeMuteKey || !_muteKeyDown))
                        dispatcher.BeginInvoke(() => _handleVolumeKey(key));
                    if (key == VolumeMuteKey) _muteKeyDown = isKeyDown;
                    return (IntPtr)1;
                }
                if (isKeyUp)
                {
                    var label = GetEnabledLockKeyLabel(key, _showCapsLock, _showNumLock, _showScrollLock);
                    if (label is not null)
                    {
                        var enabled = (GetKeyState(key) & 1) != 0;
                        Application.Current?.Dispatcher.BeginInvoke(() => LockKeysStatusWindow.Show(label, enabled));
                    }
                }
                if (isKeyUp && IsMediaFlyoutKey(key) && !IsVolumeHardwareKey(key) && _showMediaFlyout is not null)
                    Application.Current?.Dispatcher.BeginInvoke(_showMediaFlyout);
            }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    internal static bool IsMediaFlyoutKey(int key) => key is >= 0xAD and <= 0xB3;

    private static bool IsVolumeHardwareKey(int key) => key is >= VolumeMuteKey and <= VolumeUpKey;

    internal static string? GetEnabledLockKeyLabel(int key, bool showCapsLock, bool showNumLock, bool showScrollLock) => key switch
    {
        0x14 when showCapsLock => "Caps Lock",
        0x90 when showNumLock => "Num Lock",
        0x91 when showScrollLock => "Scroll Lock",
        _ => null
    };

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}

internal static class LockKeysStatusWindow
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private static IFeatureUiService? _featureUi;
    private static IFeatureUiWindow? _instance;

    static LockKeysStatusWindow()
    {
        Timer.Tick += (_, _) =>
        {
            Timer.Stop();
            _instance?.Close();
            _instance = null;
        };
    }

    public static void Configure(IFeatureUiService? featureUi) => _featureUi = featureUi;

    public static void Show(string key, bool enabled)
    {
        if (_featureUi is null) return;
        var bounds = SystemParameters.WorkArea;
        _instance = _featureUi.Open("lock-keys-status", "lock-keys-status", "Kilit tuşu durumu",
            new FeatureUiWindowOptions(230, 76, Topmost: true, ActivateOnShow: false, Transparent: true));
        _instance.SetBounds(bounds.Left + (bounds.Width - 230) / 2, bounds.Top + 48, 230, 76);
        _instance.SetState(new { key, enabled });
        Timer.Stop();
        Timer.Start();
    }
}

internal sealed class MediaFlyoutWindow : Window
{
    private const double MinimumContentHeight = 160;
    private const double MaximumContentHeight = 400;
    private const double InitialContentHeight = 204;
    public event Action? PresentationRequested;
    private bool _presenting;
    private bool _preparingPresentation;
    private TaskCompletionSource? _mediaRefreshCompletion;
    private bool _uiHovered;
    private enum MediaAppType
    {
        Generic,
        Spotify,
        YouTube
    }

    private readonly TextBlock _trackInfo;
    private readonly TextBlock _artistInfo;
    private readonly Image _artwork;
    private readonly Button _shuffleButton;
    private readonly Button _repeatButton;
    private readonly ComboBox _sessionSelector;
    private readonly Slider _timelineSlider;
    private readonly TextBlock _timelineText;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _backgroundMediaTimer;
    private readonly DispatcherTimer _postHardwareKeyTimer;
    private readonly DispatcherTimer _dismissTimer;
    private readonly IFeatureUiSurface _uiSurface;
    private string? _artworkDataUri;

    // Kaynak uygulama tespiti ve yönlendirme
    private readonly Button _sourceAppButton;
    private readonly ContentControl _sourceAppIcon;
    private readonly TextBlock _sourceAppName;
    private readonly TextBlock _sourceAppBadge;
    private MediaAppType _currentMediaType = MediaAppType.Generic;
    private string _currentMediaAppId = "";

    // Medya denetim butonları (Önceki, Oynat/Duraklat, Sonraki)
    private readonly Button _prevButton;
    private readonly Button _playPauseButton;
    private readonly Button _nextButton;

    // Görsel Progress Bar (İlerleme Çubuğu)
    private readonly Grid _progressBarContainer;
    private readonly Border _progressTrack;
    private readonly Border _progressFill;
    private readonly Border _progressThumb;
    private readonly TextBlock _currentTimeText;
    private readonly TextBlock _totalTimeText;
    private SolidColorBrush _progressBrush;
    private TimeSpan _currentDuration = TimeSpan.Zero;
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private bool _isDraggingProgress;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _selectedSession;
    private GlobalSystemMediaTransportControlsSession? _observedSession;
    private bool _updatingTimeline;
    private bool _updatingSessionChoices;
    private readonly MediaSessionTracker _sessionTracker = new();
    private GlobalSystemMediaTransportControlsSession? _manualSession;
    private bool _seeking;
    private bool _refreshingMedia;
    private bool _mediaRefreshPending;
    private bool _shuffleEnabled;
    private MediaPlaybackAutoRepeatMode? _repeatMode;
    private string? _artworkKey;
    private string? _lastMediaKey;
    private bool _hasInitialMediaSnapshot;
    private bool _wasPlaying;
    private bool _isClosed;
    private bool _isFadingOut;
    private bool _showUpNext;
    private bool _isWindowPinned;
    private bool _hasCustomPosition;
    private bool _lastTriggerWasAuto;
    private bool _suppressOnFullscreen;
    private string _position = "bottom-right";
    private string _monitor = "cursor";
    private string _animation = "fade";
    private int _dismissSeconds = 8;
    private int _autoDismissSeconds = 3;
    private string _mediaAppFilterMode = "all";
    private HashSet<string> _mediaAppFilterList = new(StringComparer.OrdinalIgnoreCase);
    private string _pinnedSessionId = "";
    private Action<bool>? _saveWindowPinned;

    public bool IsWindowPinned => _isWindowPinned;

    public MediaFlyoutWindow(IFeatureUiService? featureUi = null)
    {
        _uiSurface = featureUi?.CreateSurface("media-flyout")
            ?? throw new InvalidOperationException("Medya flyout'u için React arayüzü gerekli.");
        _uiSurface.MessageReceived += OnUiMessage;
        Title = "Medya ve Ses";
        Width = 360;
        Height = InitialContentHeight;
        SizeToContent = SizeToContent.Manual;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        WindowStartupLocation = WindowStartupLocation.Manual;

        var content = new StackPanel { Margin = new Thickness(15) };
        _sourceAppButton = new Button
        {
            Name = "MediaSourceAppButton",
            ToolTip = "Uygulamayı aç / öne getir",
            Height = 28,
            Padding = new Thickness(4, 2, 6, 2),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var appHeaderStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _sourceAppIcon = new ContentControl
        {
            Content = CreateMediaAppIcon(MediaAppType.Generic),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        _sourceAppName = new TextBlock
        {
            Text = "Medya",
            Foreground = Brush("#F5F5F5"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _sourceAppBadge = new TextBlock
        {
            Text = " ↗",
            Foreground = Brush("#888888"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        appHeaderStack.Children.Add(_sourceAppIcon);
        appHeaderStack.Children.Add(_sourceAppName);
        appHeaderStack.Children.Add(_sourceAppBadge);
        _sourceAppButton.Content = appHeaderStack;
        _sourceAppButton.Click += (_, _) => OpenCurrentMediaApp();

        _trackInfo = new TextBlock
        {
            Name = "MediaSessionInfo",
            Text = "Medya oturumu bekleniyor",
            Foreground = Brush("#F5F5F5"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220
        };
        _artistInfo = new TextBlock
        {
            Name = "MediaArtistInfo",
            Foreground = Brush("#A0A0A0"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 0),
            MaxWidth = 220
        };
        var trackDetails = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        trackDetails.Children.Add(_trackInfo);
        trackDetails.Children.Add(_artistInfo);
        var metadataRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        metadataRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        metadataRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _artwork = new Image
        {
            Name = "MediaArtwork",
            Width = 56,
            Height = 56,
            Stretch = Stretch.UniformToFill,
            Clip = new RectangleGeometry(new Rect(0, 0, 56, 56), 10, 10),
            ToolTip = "Albüm kapağı"
        };
        var artworkBorder = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brush("#202020"),
            Child = _artwork
        };
        Grid.SetColumn(artworkBorder, 0);
        metadataRow.Children.Add(artworkBorder);
        var metadataDetails = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(metadataDetails, 1);
        metadataDetails.Children.Add(trackDetails);
        metadataRow.Children.Add(metadataDetails);
        content.Children.Add(metadataRow);

        _sessionSelector = new ComboBox
        {
            Name = "MediaSessionSelector",
            MinHeight = 28,
            Margin = new Thickness(0, 0, 0, 6),
            Background = Brush("#222222"),
            Foreground = Brush("#F5F5F5"),
            ToolTip = "Denetlenecek medya uygulamasını seçin",
            Visibility = Visibility.Collapsed
        };
        _sessionSelector.SelectionChanged += SessionSelectionChanged;
        content.Children.Add(_sessionSelector);
        // Görsel Progress Bar (İlerleme Çubuğu)
        _progressBrush = Brush("#1DB954");
        _progressBarContainer = new Grid
        {
            Height = 16,
            Margin = new Thickness(0, 4, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Şarkıda/videoda konumu değiştir (Seek)",
            Background = Brushes.Transparent
        };
        _progressTrack = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Brush("#33FFFFFF"),
            VerticalAlignment = VerticalAlignment.Center
        };
        _progressFill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = _progressBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 0
        };
        _progressThumb = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(-5, 0, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _progressBarContainer.Children.Add(_progressTrack);
        _progressBarContainer.Children.Add(_progressFill);
        _progressBarContainer.Children.Add(_progressThumb);

        _progressBarContainer.MouseEnter += (_, _) =>
        {
            _progressTrack.Height = 6;
            _progressFill.Height = 6;
            _progressThumb.Visibility = Visibility.Visible;
        };
        _progressBarContainer.MouseLeave += (_, _) =>
        {
            if (!_isDraggingProgress)
            {
                _progressTrack.Height = 4;
                _progressFill.Height = 4;
                _progressThumb.Visibility = Visibility.Collapsed;
            }
        };
        _progressBarContainer.PreviewMouseLeftButtonDown += async (sender, args) =>
        {
            _isDraggingProgress = true;
            _progressBarContainer.CaptureMouse();
            var pos = args.GetPosition(_progressBarContainer);
            await HandleSeekByPosition(pos.X);
        };
        _progressBarContainer.PreviewMouseMove += async (sender, args) =>
        {
            if (_isDraggingProgress && args.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                var pos = args.GetPosition(_progressBarContainer);
                await HandleSeekByPosition(pos.X);
            }
        };
        _progressBarContainer.PreviewMouseLeftButtonUp += (sender, args) =>
        {
            _isDraggingProgress = false;
            _progressBarContainer.ReleaseMouseCapture();
            if (!_progressBarContainer.IsMouseOver)
            {
                _progressTrack.Height = 4;
                _progressFill.Height = 4;
                _progressThumb.Visibility = Visibility.Collapsed;
            }
        };
        var timeRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 6) };
        _currentTimeText = new TextBlock
        {
            Text = "0:00",
            Foreground = Brush("#BCC6D6"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(_currentTimeText, Dock.Left);
        _totalTimeText = new TextBlock
        {
            Text = "0:00",
            Foreground = Brush("#758092"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        timeRow.Children.Add(_currentTimeText);
        timeRow.Children.Add(_totalTimeText);
        metadataDetails.Children.Add(timeRow);

        var playbackOptions = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        playbackOptions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        playbackOptions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        playbackOptions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        _progressBarContainer.Margin = new Thickness(0, 0, 3, 0);
        Grid.SetColumn(_progressBarContainer, 0);
        playbackOptions.Children.Add(_progressBarContainer);
        _shuffleButton = CreateButton("🔀", "Karıştır: kapalı", ToggleShuffle);
        _repeatButton = CreateButton("🔁", "Tekrar: kapalı", ToggleRepeat);
        foreach (var button in new[] { _shuffleButton, _repeatButton })
        {
            button.Width = 24;
            button.Height = 24;
            button.Margin = new Thickness(1, 0, 1, 0);
            button.Padding = new Thickness(0);
            button.FontSize = 13;
        }
        Grid.SetColumn(_shuffleButton, 1);
        Grid.SetColumn(_repeatButton, 2);
        playbackOptions.Children.Add(_shuffleButton);
        playbackOptions.Children.Add(_repeatButton);
        metadataDetails.Children.Add(playbackOptions);

        _timelineText = new TextBlock
        {
            Name = "MediaTimelineText",
            Text = "0:00 / 0:00",
            Visibility = Visibility.Collapsed
        };
        _timelineSlider = new Slider
        {
            Name = "MediaTimelineSlider",
            Minimum = 0,
            Maximum = 1,
            Visibility = Visibility.Collapsed
        };
        _timelineSlider.ValueChanged += (_, _) =>
        {
            if (!_updatingTimeline && (_timelineSlider.IsMouseCaptureWithin || _timelineSlider.IsKeyboardFocusWithin)) _seeking = true;
        };
        content.Children.Add(_timelineText);
        content.Children.Add(_timelineSlider);

        var mediaControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8)
        };
        mediaControls.Children.Add(_sourceAppButton);
        _prevButton = CreateMediaButton("⏮", "Önceki parça", SkipPrevious, 46, 36);
        _playPauseButton = CreateMediaButton("▶", "Oynat veya duraklat", TogglePlayback, 56, 38, isPrimary: true);
        _nextButton = CreateMediaButton("⏭", "Sonraki parça", SkipNext, 46, 36);
        mediaControls.Children.Add(_prevButton);
        mediaControls.Children.Add(_playPauseButton);
        mediaControls.Children.Add(_nextButton);
        content.Children.Add(mediaControls);

        Content = _uiSurface.Element;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
        };
        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
            try
            {
                DragMove();
                _hasCustomPosition = true;
            }
            catch { }
        };
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) =>
        {
            UpdateTimeline(_selectedSession);
            PushUiState();
        };
        _backgroundMediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _backgroundMediaTimer.Tick += async (_, _) =>
        {
            if (!_isClosed && _showUpNext && !IsVisible)
                await RefreshAsync();
        };
        _postHardwareKeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _postHardwareKeyTimer.Tick += async (_, _) =>
        {
            _postHardwareKeyTimer.Stop();
            if (!_isClosed && IsVisible)
                await RefreshAsync();
        };
        _dismissTimer = new DispatcherTimer();
        _dismissTimer.Tick += (_, _) =>
        {
            if (_isWindowPinned || IsMouseOver) return;
            _dismissTimer.Stop();
            Dismiss();
        };
        AudioVolumeOverlayLifecycle.Shown += OnVolumeShown;
        AudioVolumeOverlayLifecycle.Hidden += HideWithVolume;
        AudioVolumeOverlayLifecycle.KeepOpenRequested += KeepVolumeOpen;
        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) => ResetDismissTimer(_lastTriggerWasAuto);
        PreviewMouseMove += (_, _) => ResetDismissTimer(_lastTriggerWasAuto);
        PreviewMouseDown += (_, _) => ResetDismissTimer(isAutoTrigger: false);
        PreviewKeyDown += (_, _) => ResetDismissTimer(isAutoTrigger: false);
        Closed += (_, _) =>
        {
            AudioVolumeOverlayLifecycle.Shown -= OnVolumeShown;
            AudioVolumeOverlayLifecycle.Hidden -= HideWithVolume;
            AudioVolumeOverlayLifecycle.KeepOpenRequested -= KeepVolumeOpen;
            AudioVolumeOverlayLifecycle.RequestDismiss();
            _isClosed = true;
            _refreshTimer.Stop();
            _backgroundMediaTimer.Stop();
            _postHardwareKeyTimer.Stop();
            _dismissTimer.Stop();
            DetachMediaEventHandlers();
            _uiSurface.Dispose();
        };
        Loaded += async (_, _) => await RefreshAsync();
    }

    public void StartBackgroundMonitoring()
    {
        if (_isClosed) return;
        if (!_backgroundMediaTimer.IsEnabled)
            _backgroundMediaTimer.Start();
        _ = RefreshAsync();
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Dismiss();
            return;
        }
        ShowOrRefresh(activateWindow: true, isAutoTrigger: false);
    }

    public void ShowOrRefresh(bool activateWindow = false, bool isAutoTrigger = false)
    {
        if (_isClosed || _presenting) return;
        if (_sessionManager is null)
        {
            if (!_preparingPresentation)
                _ = PreparePresentationAsync(activateWindow, isAutoTrigger, AudioVolumeOverlayLifecycle.IsVisible);
            return;
        }
        RefreshSessionChoices();
        if (_selectedSession is null) return;
        _presenting = true;
        try
        {
            if (!AudioVolumeOverlayLifecycle.IsVisible) PresentationRequested?.Invoke();
            var animate = _animation == "fade" && !AudioVolumeOverlayLifecycle.IsVisible;
            _lastTriggerWasAuto = isAutoTrigger;
            var wasVisible = IsVisible;
            if (!wasVisible || !_hasCustomPosition)
                PositionFlyout();
            _ = RefreshAsync();
            if (isAutoTrigger)
            {
                _postHardwareKeyTimer.Stop();
                _postHardwareKeyTimer.Start();
            }
            if (!wasVisible)
            {
                Opacity = animate ? 0 : 1;
                Show();
                if (animate)
                    BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else if (_isFadingOut)
            {
                _isFadingOut = false;
                BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            }
            if (activateWindow) Activate();
            _refreshTimer.Start();
            ResetDismissTimer(isAutoTrigger);
        }
        finally { _presenting = false; }
    }

    private async Task PreparePresentationAsync(bool activateWindow, bool isAutoTrigger, bool requiresVolume)
    {
        _preparingPresentation = true;
        try
        {
            await RefreshAsync();
            if (!_isClosed && _sessionManager is not null && (!requiresVolume || AudioVolumeOverlayLifecycle.IsVisible))
                ShowOrRefresh(activateWindow, isAutoTrigger);
        }
        finally { _preparingPresentation = false; }
    }

    private void OnVolumeShown() => ShowOrRefresh(isAutoTrigger: true);

    private bool KeepVolumeOpen() => IsVisible && (_isWindowPinned || _uiHovered || IsMouseOver);

    private void HideWithVolume()
    {
        _isFadingOut = false;
        _uiHovered = false;
        _refreshTimer.Stop();
        _dismissTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Hide();
    }

    private void Dismiss()
    {
        if (AudioVolumeOverlayLifecycle.IsVisible)
        {
            AudioVolumeOverlayLifecycle.RequestDismiss();
            return;
        }
        if (!IsVisible || _isFadingOut) return;

        _refreshTimer.Stop();
        _dismissTimer.Stop();
        if (_animation != "fade")
        {
            Hide();
            return;
        }

        _isFadingOut = true;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (!_isFadingOut || _isClosed) return;
            _isFadingOut = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    public void ConfigureUpNext(bool enabled) => _showUpNext = enabled;

    public void Configure(string position, string monitor, string animation, int dismissSeconds,
        string mediaAppFilterMode = "all", string mediaAppFilterList = "", string pinnedSessionId = "",
        bool windowPinned = false, Action<bool>? saveWindowPinned = null,
        int autoDismissSeconds = 3, bool suppressOnFullscreen = false)
    {
        var newPosition = position is "bottom-left" or "top-right" or "top-left" ? position : "bottom-right";
        var newMonitor = monitor is "primary" or "active" ? monitor : "cursor";
        if (newPosition != _position || newMonitor != _monitor)
            _hasCustomPosition = false;
        _position = newPosition;
        _monitor = newMonitor;
        _animation = animation == "none" ? "none" : "fade";
        _dismissSeconds = Math.Clamp(dismissSeconds, 0, 60);
        _autoDismissSeconds = Math.Clamp(autoDismissSeconds, 1, 30);
        _suppressOnFullscreen = suppressOnFullscreen;
        _mediaAppFilterMode = mediaAppFilterMode is "allowlist" or "blocklist" ? mediaAppFilterMode : "all";
        _mediaAppFilterList = ParseMediaAppFilterList(mediaAppFilterList);
        _pinnedSessionId = pinnedSessionId.Trim();
        _isWindowPinned = windowPinned;
        _saveWindowPinned = saveWindowPinned;
        ApplyTheme();
        if (_isWindowPinned) _dismissTimer.Stop();
        else if (IsVisible) ResetDismissTimer(_lastTriggerWasAuto);
        PushUiState();
    }

    public void SetWindowPinned(bool pinned)
    {
        _isWindowPinned = pinned;
        _saveWindowPinned?.Invoke(_isWindowPinned);
        if (_isWindowPinned) _dismissTimer.Stop();
        else ResetDismissTimer(isAutoTrigger: false);
        PushUiState();
    }

    private async void OnUiMessage(string command, JsonElement payload)
    {
        if (command != "media.resize") AudioVolumeOverlayLifecycle.NotifyInteraction();
        switch (command)
        {
            case "media.resize":
                if (payload.TryGetProperty("height", out var contentHeight) &&
                    contentHeight.TryGetDouble(out var height) && double.IsFinite(height))
                {
                    Height = Math.Clamp(Math.Ceiling(height), MinimumContentHeight, MaximumContentHeight);
                    if (IsVisible && !_hasCustomPosition) PositionFlyout();
                }
                break;
            case "media.previous": SkipPrevious(null, new RoutedEventArgs()); break;
            case "media.next": SkipNext(null, new RoutedEventArgs()); break;
            case "media.toggle": TogglePlayback(null, new RoutedEventArgs()); break;
            case "media.shuffle": ToggleShuffle(null, new RoutedEventArgs()); break;
            case "media.repeat": ToggleRepeat(null, new RoutedEventArgs()); break;
            case "media.seek":
                if (payload.TryGetProperty("position", out var position) && position.TryGetDouble(out var seconds))
                    await SeekToPositionAsync(seconds);
                break;
            case "media.select-session":
                if (payload.TryGetProperty("id", out var id))
                {
                    var choice = (_sessionSelector.ItemsSource as IEnumerable<MediaSessionChoice>)?
                        .FirstOrDefault(item => string.Equals(item.Session.SourceAppUserModelId, id.GetString(), StringComparison.Ordinal));
                    if (choice is not null) _sessionSelector.SelectedItem = choice;
                }
                break;
            case "media.open-source": OpenCurrentMediaApp(); break;
            case "media.pin":
                if (payload.TryGetProperty("value", out var pinned) && pinned.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    SetWindowPinned(pinned.GetBoolean());
                break;
            case "media.dismiss": Dismiss(); break;
            case "media.hover":
                _uiHovered = payload.TryGetProperty("value", out var hoverValue) && hoverValue.ValueKind == JsonValueKind.True;
                if (payload.TryGetProperty("value", out var hovering) && hovering.ValueKind == JsonValueKind.True)
                    _dismissTimer.Stop();
                else if (!_isWindowPinned)
                    ResetDismissTimer(_lastTriggerWasAuto);
                break;
        }
    }

    private async Task SeekToPositionAsync(double seconds)
    {
        if (_selectedSession is null || _currentDuration <= TimeSpan.Zero || !double.IsFinite(seconds)) return;
        var target = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _currentDuration.TotalSeconds));
        try
        {
            var timeline = _selectedSession.GetTimelineProperties();
            await _selectedSession.TryChangePlaybackPositionAsync((timeline.StartTime + target).Ticks);
            UpdateTimeline(_selectedSession);
            PushUiState();
        }
        catch { }
    }

    private void PushUiState()
    {
        if (_isClosed) return;
        var sessions = (_sessionSelector.ItemsSource as IEnumerable<MediaSessionChoice>)?.Select(choice => new
        {
            id = choice.Session.SourceAppUserModelId,
            label = choice.DisplayName
        }).ToArray() ?? [];
        var selected = _selectedSession;
        var isPlaying = false;
        if (selected is not null)
        {
            try { isPlaying = selected.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { }
        }
        _uiSurface.SetState(new
        {
            title = _trackInfo.Text,
            artist = _artistInfo.Text,
            artwork = _artworkDataUri,
            sourceName = _sourceAppName.Text,
            sourceType = _currentMediaType.ToString(),
            sourceBadge = _sourceAppBadge.Text,
            sessions,
            selectedSessionId = selected?.SourceAppUserModelId ?? "",
            showSessionSelector = _sessionSelector.Visibility == Visibility.Visible,
            position = _currentPosition.TotalSeconds,
            duration = _currentDuration.TotalSeconds,
            currentTime = _currentTimeText.Text,
            totalTime = _totalTimeText.Text,
            isPlaying,
            canPlay = _playPauseButton.IsEnabled,
            canPrevious = _prevButton.IsEnabled,
            canNext = _nextButton.IsEnabled,
            canShuffle = _shuffleButton.IsEnabled,
            canRepeat = _repeatButton.IsEnabled,
            shuffleEnabled = _shuffleEnabled,
            repeatMode = _repeatMode switch
            {
                MediaPlaybackAutoRepeatMode.List => "list",
                MediaPlaybackAutoRepeatMode.Track => "track",
                _ => "off"
            },
            pinned = _isWindowPinned
        });
    }

    internal static HashSet<string> ParseMediaAppFilterList(string value) => value
        .Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizeMediaAppId)
        .Where(item => item.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static string NormalizeMediaAppId(string value)
    {
        var normalized = value.Trim().Trim('"', '\'');
        var separator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
        if (separator >= 0) normalized = normalized[(separator + 1)..];
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^4];
        return normalized.Trim();
    }

    internal static bool IsMediaAppAllowed(string appId, string mode, IReadOnlySet<string> filterList)
    {
        if (mode is not ("allowlist" or "blocklist") || filterList.Count == 0 && mode == "blocklist") return true;
        var candidate = NormalizeMediaAppId(appId);
        var matches = filterList.Any(item => candidate.Contains(item, StringComparison.OrdinalIgnoreCase));
        return mode == "allowlist" ? matches : !matches;
    }

    public static bool IsForegroundFullscreen()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !GetWindowRect(foreground, out var window)) return false;
        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var info)) return false;
        const int tolerance = 2;
        return window.Left <= info.Monitor.Left + tolerance && window.Top <= info.Monitor.Top + tolerance &&
               window.Right >= info.Monitor.Right - tolerance && window.Bottom >= info.Monitor.Bottom - tolerance;
    }

    private void PositionFlyout()
    {
        var cursor = GetCursorPoint();
        var monitor = _monitor switch
        {
            "primary" => GetPrimaryMonitor(),
            "active" => MonitorFromWindow(GetForegroundWindow(), MonitorDefaultToNearest),
            _ => MonitorFromPoint(cursor, MonitorDefaultToNearest)
        };
        if (!TryGetMonitorInfo(monitor, out var info))
        {
            var workArea = SystemParameters.WorkArea;
            PlaceInArea(workArea.Left, workArea.Top, workArea.Right, workArea.Bottom);
            return;
        }
        var scaleX = 1.0;
        var scaleY = 1.0;
        if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
        {
            scaleX = dpiX / 96.0;
            scaleY = dpiY / 96.0;
        }
        PlaceInArea(info.Work.Left / scaleX, info.Work.Top / scaleY,
            info.Work.Right / scaleX, info.Work.Bottom / scaleY);
    }

    private void PlaceInArea(double left, double top, double right, double bottom)
    {
        const double margin = 16;
        var height = Height;
        Left = _position.EndsWith("left", StringComparison.Ordinal) ? left + margin : right - Width - margin;
        Top = _position.StartsWith("top", StringComparison.Ordinal) ? top + margin : bottom - height - margin;
        Left = Math.Max(left + margin, Math.Min(Left, right - Width - margin));
        Top = Math.Max(top + margin, Math.Min(Top, bottom - height - margin));
    }

    private void ResetDismissTimer(bool isAutoTrigger = false)
    {
        _dismissTimer.Stop();
        if (AudioVolumeOverlayLifecycle.IsVisible)
        {
            AudioVolumeOverlayLifecycle.NotifyInteraction();
            return;
        }
        if (_isWindowPinned || !IsVisible) return;
        var seconds = isAutoTrigger && !IsMouseOver ? _autoDismissSeconds : _dismissSeconds;
        if (seconds > 0)
        {
            _dismissTimer.Interval = TimeSpan.FromSeconds(seconds);
            _dismissTimer.Start();
        }
    }

    private void ApplyTheme()
    {
        var background = Brush("#1B1B1D");
        var foreground = Brushes.White;
        if (Content is Border card)
        {
            card.Background = background;
            card.BorderBrush = Brush("#424246");
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(19);
        }
        foreach (var element in Descendants(this))
        {
            if (element is TextBlock text)
            {
                text.Foreground = foreground;
            }
            else if (element is Border border)
            {
                if (border == _progressTrack) border.Background = Brush("#57575B");
                else if (border == _progressFill) { /* kaynağa özel renk korunur */ }
                else if (border == _progressThumb) border.Background = Brushes.White;
                else border.Background = Brush("#252527");
                border.BorderBrush = Brush("#39393D");
                border.BorderThickness = new Thickness(1);
            }
            else if (element is Button button)
            {
                var isSourceButton = button == _sourceAppButton;
                button.SetResourceReference(FrameworkElement.StyleProperty, "PopupButton");
                button.Foreground = isSourceButton ? Brushes.White : Brushes.Black;
                button.Background = isSourceButton ? Brushes.Transparent : Brushes.White;
                button.BorderBrush = isSourceButton ? Brushes.Transparent : Brush("#F5F5F5");
                button.BorderThickness = new Thickness(isSourceButton ? 0 : 1);
                button.FontWeight = FontWeights.SemiBold;
            }
            else if (element is ComboBox combo)
            {
                combo.Foreground = Brushes.Black;
                combo.Background = Brush("#F5F5F5");
                combo.BorderBrush = Brush("#E5E5E5");
                combo.BorderThickness = new Thickness(1);
            }
        }
        _sourceAppName.Foreground = Brushes.White;
        _sourceAppBadge.Foreground = Brushes.White;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshingMedia)
        {
            _mediaRefreshPending = true;
            if (_mediaRefreshCompletion is not null) await _mediaRefreshCompletion.Task;
            return;
        }
        _refreshingMedia = true;
        _mediaRefreshCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (_sessionManager is null)
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _sessionManager.SessionsChanged += SessionManagerSessionsChanged;
                _sessionManager.CurrentSessionChanged += SessionManagerCurrentSessionChanged;
            }
            RefreshSessionChoices();
            var session = _selectedSession;
            _selectedSession = session;
            ObserveSelectedSession(session);
            if (session is null)
            {
                if (AudioVolumeOverlayLifecycle.IsVisible) HideWithVolume();
                _trackInfo.Text = "Medya oturumu bekleniyor";
                _artistInfo.Text = "";
                _lastMediaKey = null;
                _wasPlaying = false;
                _hasInitialMediaSnapshot = true;
                UpdateSourceApp(null, null);
                UpdatePlaybackButtons(null);
                UpdateTimeline(null);
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            var hasTrackTitle = !string.IsNullOrWhiteSpace(properties.Title);
            var title = hasTrackTitle ? properties.Title : "Bilinmeyen parça";
            var mediaKey = $"{session.SourceAppUserModelId}|{title}|{properties.Artist}";
            var isPlaying = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var trackChanged = _lastMediaKey is not null && !string.Equals(_lastMediaKey, mediaKey, StringComparison.Ordinal);
            var newlyStartedTrack = _hasInitialMediaSnapshot && _lastMediaKey is null && hasTrackTitle && isPlaying;
            if (_showUpNext && isPlaying && (trackChanged || newlyStartedTrack))
            {
                if (!IsVisible && (!_suppressOnFullscreen || !IsForegroundFullscreen()))
                    ShowOrRefresh(activateWindow: false, isAutoTrigger: true);
                else if (IsVisible)
                    ResetDismissTimer(_lastTriggerWasAuto);
            }
            _lastMediaKey = mediaKey;
            _wasPlaying = isPlaying;
            _hasInitialMediaSnapshot = true;
            _trackInfo.Text = title;
            _artistInfo.Text = properties.Artist;
            await RefreshArtworkAsync(properties, session);
            RefreshPlaybackOptions(session);
            UpdateSourceApp(session, properties);
            UpdatePlaybackButtons(session);
            UpdateTimeline(session);
        }
        catch
        {
            _trackInfo.Text = "Medya oturumu bekleniyor";
            _artistInfo.Text = "";
            _artwork.Source = null;
            _artworkKey = null;
            _shuffleButton.IsEnabled = false;
            _repeatButton.IsEnabled = false;
            UpdateSourceApp(null, null);
            UpdatePlaybackButtons(null);
            UpdateTimeline(null);
        }
        finally
        {
            _refreshingMedia = false;
            _mediaRefreshCompletion.TrySetResult();
            PushUiState();
            if (_mediaRefreshPending && !_isClosed)
            {
                _mediaRefreshPending = false;
                QueueMediaRefresh();
            }
        }
    }

    private void SessionManagerSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        QueueMediaRefresh();

    private void SessionManagerCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        QueueMediaRefresh();

    private void SessionMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        QueueMediaRefresh();

    private void SessionPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        QueueMediaRefresh();

    private void SessionTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        if (_isClosed || !IsVisible) return;
        Dispatcher.BeginInvoke(() => UpdateTimeline(_selectedSession));
    }

    private void QueueMediaRefresh()
    {
        if (_isClosed) return;
        Dispatcher.BeginInvoke(async () =>
        {
            if (_isClosed) return;
            await RefreshAsync();
        });
    }

    private void ObserveSelectedSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_observedSession, session)) return;
        if (_observedSession is not null)
        {
            _observedSession.MediaPropertiesChanged -= SessionMediaPropertiesChanged;
            _observedSession.PlaybackInfoChanged -= SessionPlaybackInfoChanged;
            _observedSession.TimelinePropertiesChanged -= SessionTimelinePropertiesChanged;
        }
        _observedSession = session;
        if (_observedSession is null) return;
        _observedSession.MediaPropertiesChanged += SessionMediaPropertiesChanged;
        _observedSession.PlaybackInfoChanged += SessionPlaybackInfoChanged;
        _observedSession.TimelinePropertiesChanged += SessionTimelinePropertiesChanged;
    }

    private void DetachMediaEventHandlers()
    {
        ObserveSelectedSession(null);
        if (_sessionManager is null) return;
        _sessionManager.SessionsChanged -= SessionManagerSessionsChanged;
        _sessionManager.CurrentSessionChanged -= SessionManagerCurrentSessionChanged;
    }

    private async Task RefreshArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties,
        GlobalSystemMediaTransportControlsSession session)
    {
        var key = $"{session.SourceAppUserModelId}|{properties.AlbumTitle}|{properties.Title}|{properties.Artist}";
        if (string.Equals(_artworkKey, key, StringComparison.Ordinal)) return;
        _artworkKey = key;
        _artwork.Source = null;
        _artworkDataUri = null;
        if (properties.Thumbnail is null) return;

        try
        {
            using var stream = await properties.Thumbnail.OpenReadAsync();
            const ulong maximumArtworkBytes = 4 * 1024 * 1024;
            if (stream.Size is 0 or > maximumArtworkBytes) return;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var byteCount = checked((uint)stream.Size);
            await reader.LoadAsync(byteCount);
            var bytes = new byte[byteCount];
            reader.ReadBytes(bytes);
            using var memory = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 152;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            _artwork.Source = bitmap;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            _artworkDataUri = $"data:image/png;base64,{Convert.ToBase64String(encoded.ToArray())}";
        }
        catch
        {
            _artwork.Source = null;
            _artworkDataUri = null;
        }
    }

    private void RefreshPlaybackOptions(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var playback = session.GetPlaybackInfo();
            _shuffleEnabled = playback.IsShuffleActive ?? false;
            _repeatMode = playback.AutoRepeatMode;
            _shuffleButton.Content = "🔀";
            _shuffleButton.ToolTip = $"Karıştır: {(_shuffleEnabled ? "açık" : "kapalı")}";
            _shuffleButton.Foreground = Brushes.Black;
            _repeatButton.Content = _repeatMode == MediaPlaybackAutoRepeatMode.Track ? "🔂" : "🔁";
            _repeatButton.ToolTip = $"Tekrar: {RepeatLabel(_repeatMode)}";
            _repeatButton.Foreground = Brushes.Black;
            _shuffleButton.IsEnabled = playback.Controls.IsShuffleEnabled;
            _repeatButton.IsEnabled = playback.Controls.IsRepeatEnabled;
        }
        catch
        {
            _shuffleButton.IsEnabled = false;
            _repeatButton.IsEnabled = false;
        }
    }

    private async void ToggleShuffle(object? sender, RoutedEventArgs args)
    {
        await TryControlSession(async session => await session.TryChangeShuffleActiveAsync(!_shuffleEnabled));
        if (_selectedSession is not null) RefreshPlaybackOptions(_selectedSession);
    }

    private async void ToggleRepeat(object? sender, RoutedEventArgs args)
    {
        var nextMode = _repeatMode switch
        {
            MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None
        };
        await TryControlSession(async session => await session.TryChangeAutoRepeatModeAsync(nextMode));
        if (_selectedSession is not null) RefreshPlaybackOptions(_selectedSession);
    }

    private static string RepeatLabel(MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        MediaPlaybackAutoRepeatMode.List => "tümü",
        MediaPlaybackAutoRepeatMode.Track => "parça",
        _ => "kapalı"
    };

    private void RefreshSessionChoices()
    {
        if (_sessionManager is null) return;
        var allowed = _sessionManager.GetSessions()
            .Where(session => IsMediaAppAllowed(session.SourceAppUserModelId, _mediaAppFilterMode, _mediaAppFilterList))
            .ToArray();
        if (_sessionTracker.Update(allowed)) _manualSession = null;
        var sessions = _sessionTracker.Order(allowed);
        var pinned = sessions.FirstOrDefault(session => string.Equals(
            session.SourceAppUserModelId, _pinnedSessionId, StringComparison.OrdinalIgnoreCase));
        var manual = _manualSession is not null && sessions.Any(session => ReferenceEquals(session, _manualSession))
            ? _manualSession
            : null;
        _manualSession = manual;
        var current = pinned ?? manual ?? sessions.FirstOrDefault();
        var choices = sessions.Select(session => new MediaSessionChoice(
            session,
            (MediaSessionTracker.IsPlaying(session) ? "▶ " : "❚❚ ") + FriendlySessionName(session))).ToArray();

        _updatingSessionChoices = true;
        _sessionSelector.ItemsSource = choices;
        _sessionSelector.DisplayMemberPath = nameof(MediaSessionChoice.DisplayName);
        var selected = choices.FirstOrDefault(choice => ReferenceEquals(choice.Session, current)) ?? choices.FirstOrDefault();
        _sessionSelector.SelectedItem = selected;
        _selectedSession = selected?.Session;
        _updatingSessionChoices = false;
        _sessionSelector.Visibility = choices.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (choices.Length == 0)
        {
            _selectedSession = null;
            _trackInfo.Text = "Medya oturumu bekleniyor";
            _artistInfo.Text = "";
            UpdateSourceApp(null, null);
            UpdatePlaybackButtons(null);
        }
    }

    private void SessionSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updatingSessionChoices) return;
        if (_sessionSelector.SelectedItem is MediaSessionChoice choice)
        {
            _selectedSession = choice.Session;
            _manualSession = choice.Session;
            _ = RefreshAsync();
        }
    }

    private static string FriendlySessionName(GlobalSystemMediaTransportControlsSession session)
    {
        var name = NormalizeMediaAppId(session.SourceAppUserModelId);
        return string.IsNullOrWhiteSpace(name) ? "Medya uygulaması" : name;
    }

    private void UpdateTimeline(GlobalSystemMediaTransportControlsSession? session)
    {
        _updatingTimeline = true;
        try
        {
            if (session is null)
            {
                _timelineSlider.IsEnabled = false;
                _timelineSlider.Value = 0;
                _timelineSlider.Maximum = 1;
                _timelineText.Text = "0:00 / 0:00";

                _currentDuration = TimeSpan.Zero;
                _currentPosition = TimeSpan.Zero;
                _progressFill.Width = 0;
                _progressThumb.Margin = new Thickness(-5, 0, 0, 0);
                _currentTimeText.Text = "0:00";
                _totalTimeText.Text = "0:00";
                _progressBarContainer.IsEnabled = false;
                return;
            }

            var timeline = session.GetTimelineProperties();
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero)
            {
                _timelineSlider.IsEnabled = false;
                _timelineSlider.Value = 0;
                _timelineSlider.Maximum = 1;
                _timelineText.Text = "0:00 / 0:00";

                _currentDuration = TimeSpan.Zero;
                _currentPosition = TimeSpan.Zero;
                _progressFill.Width = 0;
                _progressThumb.Margin = new Thickness(-5, 0, 0, 0);
                _currentTimeText.Text = "0:00";
                _totalTimeText.Text = "0:00";
                _progressBarContainer.IsEnabled = false;
                return;
            }

            _currentDuration = duration;
            _progressBarContainer.IsEnabled = true;

            var position = timeline.Position;
            if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                position += DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));
            _currentPosition = position;

            _timelineSlider.Maximum = duration.TotalSeconds;
            if (!_seeking) _timelineSlider.Value = Math.Clamp(position.TotalSeconds, 0, duration.TotalSeconds);
            _timelineSlider.IsEnabled = timeline.MaxSeekTime > timeline.MinSeekTime;
            _timelineText.Text = $"{FormatTime(position)} / {FormatTime(duration)}";

            _currentTimeText.Text = FormatTime(position);
            _totalTimeText.Text = FormatTime(duration);

            if (!_isDraggingProgress)
            {
                var totalWidth = _progressBarContainer.ActualWidth;
                if (totalWidth <= 0) totalWidth = 292;
                var ratio = duration.TotalSeconds > 0 ? (position.TotalSeconds / duration.TotalSeconds) : 0;
                ratio = Math.Clamp(ratio, 0.0, 1.0);
                var fillWidth = ratio * totalWidth;
                _progressFill.Width = fillWidth;
                _progressThumb.Margin = new Thickness(Math.Max(-5, fillWidth - 5), 0, 0, 0);
            }
        }
        catch
        {
            _timelineSlider.IsEnabled = false;
            _timelineText.Text = "Konum bilgisi yok";
            _progressBarContainer.IsEnabled = false;
            _currentTimeText.Text = "0:00";
            _totalTimeText.Text = "0:00";
        }
        finally { _updatingTimeline = false; }
    }

    private async Task HandleSeekByPosition(double x)
    {
        if (_selectedSession is null || _currentDuration <= TimeSpan.Zero) return;
        var totalWidth = _progressBarContainer.ActualWidth;
        if (totalWidth <= 0) totalWidth = 292;
        var ratio = Math.Clamp(x / totalWidth, 0.0, 1.0);
        var targetSeconds = ratio * _currentDuration.TotalSeconds;

        _progressFill.Width = ratio * totalWidth;
        _progressThumb.Margin = new Thickness((ratio * totalWidth) - 5, 0, 0, 0);
        _currentTimeText.Text = FormatTime(TimeSpan.FromSeconds(targetSeconds));

        try
        {
            var timeline = _selectedSession.GetTimelineProperties();
            var requestedPosition = timeline.StartTime + TimeSpan.FromSeconds(targetSeconds);
            await _selectedSession.TryChangePlaybackPositionAsync(requestedPosition.Ticks);
        }
        catch { }
    }

    private async Task SeekAsync()
    {
        if (!_seeking || _selectedSession is null || !_timelineSlider.IsEnabled) return;
        _seeking = false;
        try
        {
            var timeline = _selectedSession.GetTimelineProperties();
            var requestedPosition = timeline.StartTime + TimeSpan.FromSeconds(_timelineSlider.Value);
            await _selectedSession.TryChangePlaybackPositionAsync(requestedPosition.Ticks);
            UpdateTimeline(_selectedSession);
        }
        catch { _timelineText.Text = "Konum değiştirilemedi"; }
    }

    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1
        ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
        : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    private sealed record MediaSessionChoice(GlobalSystemMediaTransportControlsSession Session, string DisplayName);

    private async void SkipPrevious(object? sender, RoutedEventArgs args)
    {
        if (await TryControlSession(async session => await session.TrySkipPreviousAsync())) return;
        SendMediaKey(0xB1);
    }

    private async void SkipNext(object? sender, RoutedEventArgs args)
    {
        if (await TryControlSession(async session => await session.TrySkipNextAsync())) return;
        SendMediaKey(0xB0);
    }

    private async void TogglePlayback(object? sender, RoutedEventArgs args)
    {
        if (await TryControlSession(async session =>
        {
            var playback = session.GetPlaybackInfo();
            return playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                ? await session.TryPauseAsync()
                : await session.TryPlayAsync();
        })) return;
        SendMediaKey(0xB3);
    }

    private void UpdatePlaybackButtons(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null)
        {
            _prevButton.IsEnabled = false;
            _nextButton.IsEnabled = false;
            _playPauseButton.IsEnabled = false;
            _playPauseButton.Content = "▶";
            _playPauseButton.ToolTip = "Oynat";
            return;
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            var isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _playPauseButton.Content = isPlaying ? "❚❚" : "▶";
            _playPauseButton.ToolTip = isPlaying ? "Duraklat" : "Oynat";

            var controls = playback.Controls;
            _prevButton.IsEnabled = controls.IsPreviousEnabled;
            _nextButton.IsEnabled = controls.IsNextEnabled;
            _playPauseButton.IsEnabled = controls.IsPlayEnabled || controls.IsPauseEnabled || controls.IsPlayPauseToggleEnabled;
        }
        catch
        {
            _prevButton.IsEnabled = true;
            _nextButton.IsEnabled = true;
            _playPauseButton.IsEnabled = true;
        }
    }

    private void UpdateSourceApp(
        GlobalSystemMediaTransportControlsSession? session,
        GlobalSystemMediaTransportControlsSessionMediaProperties? properties)
    {
        _currentMediaType = DetectMediaType(session, properties, out var appDisplayName, out var colorHex);
        _currentMediaAppId = session?.SourceAppUserModelId ?? "";

        _sourceAppName.Text = appDisplayName;
        _sourceAppIcon.Content = CreateMediaAppIcon(_currentMediaType);
        _sourceAppButton.ToolTip = _currentMediaType switch
        {
            MediaAppType.Spotify => "Spotify'ı aç veya öne getir",
            MediaAppType.YouTube => "YouTube'u aç veya sekmeyi öne getir",
            _ => $"{appDisplayName} uygulamasını öne getir"
        };

        _progressBrush = Brush(colorHex);
        _progressFill.Background = _progressBrush;
    }

    private void OpenCurrentMediaApp()
    {
        OpenMediaApp(_currentMediaType, _currentMediaAppId);
    }

    private static MediaAppType DetectMediaType(
        GlobalSystemMediaTransportControlsSession? session,
        GlobalSystemMediaTransportControlsSessionMediaProperties? properties,
        out string displayName,
        out string colorHex)
    {
        var appId = session?.SourceAppUserModelId ?? "";
        var title = properties?.Title ?? "";
        var artist = properties?.Artist ?? "";
        var album = properties?.AlbumTitle ?? "";

        if (appId.Contains("spotify", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            displayName = "Spotify";
            colorHex = "#1DB954";
            return MediaAppType.Spotify;
        }

        var isYouTube = appId.Contains("youtube", StringComparison.OrdinalIgnoreCase) ||
                        artist.Contains("youtube", StringComparison.OrdinalIgnoreCase) ||
                        album.Contains("youtube", StringComparison.OrdinalIgnoreCase) ||
                        title.EndsWith("- YouTube", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("YouTube", StringComparison.OrdinalIgnoreCase);

        if (!isYouTube && IsBrowserApp(appId) && HasVisibleWindowWithTitle("YouTube"))
        {
            isYouTube = true;
        }

        if (isYouTube)
        {
            displayName = "YouTube";
            colorHex = "#FF0000";
            return MediaAppType.YouTube;
        }

        if (!string.IsNullOrWhiteSpace(appId))
        {
            var normalized = NormalizeMediaAppId(appId);
            displayName = CleanAppDisplayName(normalized);
        }
        else
        {
            displayName = "Medya";
        }

        colorHex = "#60CDFF";
        return MediaAppType.Generic;
    }

    private static bool IsBrowserApp(string appId)
    {
        var lower = appId.ToLowerInvariant();
        return lower.Contains("chrome") ||
               lower.Contains("msedge") ||
               lower.Contains("firefox") ||
               lower.Contains("brave") ||
               lower.Contains("opera") ||
               lower.Contains("vivaldi") ||
               lower.Contains("arc") ||
               lower.Contains("browser");
    }

    private static string CleanAppDisplayName(string id)
    {
        if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (id.Contains("youtube", StringComparison.OrdinalIgnoreCase)) return "YouTube";
        if (id.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (id.Contains("msedge", StringComparison.OrdinalIgnoreCase) || id.Contains("edge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (id.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (id.Contains("brave", StringComparison.OrdinalIgnoreCase)) return "Brave";
        if (id.Contains("vlc", StringComparison.OrdinalIgnoreCase)) return "VLC";
        return id;
    }

    private static FrameworkElement CreateMediaAppIcon(MediaAppType type)
    {
        switch (type)
        {
            case MediaAppType.Spotify:
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M12 2C6.477 2 2 6.477 2 12c0 5.523 4.477 10 10 10s10-4.477 10-10c0-5.523-4.477-10-10-10zm4.586 14.424c-.18.295-.563.387-.857.207-2.35-1.435-5.308-1.76-8.793-.963-.335.077-.67-.133-.746-.468-.077-.334.132-.67.467-.746 3.809-.871 7.077-.506 9.722 1.113.294.18.386.563.207.857zm1.226-2.724c-.227.368-.711.485-1.08.258-2.69-1.654-6.79-2.133-9.972-1.168-.415.127-.857-.107-.983-.522-.127-.416.107-.857.522-.983 3.633-1.103 8.147-.568 11.255 1.335.369.227.486.712.258 1.08zm.105-2.835C14.692 8.95 9.375 8.775 6.297 9.71c-.495.15-1.022-.128-1.172-.623-.15-.495.128-1.022.623-1.172 3.53-1.07 9.404-.863 13.115 1.34.444.263.59.837.327 1.28-.263.444-.838.59-1.282.328z"),
                    Fill = Brush("#1DB954"),
                    Stretch = Stretch.Uniform,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return path;
            }
            case MediaAppType.YouTube:
            {
                var grid = new Grid { Width = 18, Height = 14, VerticalAlignment = VerticalAlignment.Center };
                var redBg = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814z"),
                    Fill = Brush("#FF0000"),
                    Stretch = Stretch.Uniform
                };
                var whiteTriangle = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M9.545 15.568V8.432L15.818 12l-6.273 3.568z"),
                    Fill = Brushes.White,
                    Stretch = Stretch.Uniform,
                    Width = 6,
                    Height = 6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                grid.Children.Add(redBg);
                grid.Children.Add(whiteTriangle);
                return grid;
            }
            default:
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"),
                    Fill = Brush("#60CDFF"),
                    Stretch = Stretch.Uniform,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return path;
            }
        }
    }

    private static Button CreateMediaButton(string label, string tooltip, RoutedEventHandler action, double width, double height, bool isPrimary = false)
    {
        var button = new Button
        {
            Content = label,
            ToolTip = tooltip,
            Width = width,
            Height = height,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(0),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = isPrimary ? 16 : 14,
            FontWeight = isPrimary ? FontWeights.Bold : FontWeights.Normal,
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = isPrimary ? "primary" : "secondary"
        };
        button.Click += action;
        return button;
    }

    private static void OpenMediaApp(MediaAppType type, string appId)
    {
        switch (type)
        {
            case MediaAppType.Spotify:
                NavigateToSpotify();
                break;
            case MediaAppType.YouTube:
                NavigateToYouTube();
                break;
            default:
                NavigateToGenericApp(appId);
                break;
        }
    }

    private static void NavigateToSpotify()
    {
        try
        {
            var window = FindWindowForProcess("Spotify");
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, 9);
                SetForegroundWindow(window);
                return;
            }

            var processes = Process.GetProcessesByName("Spotify");
            var proc = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (proc != null)
            {
                ShowWindow(proc.MainWindowHandle, 9);
                SetForegroundWindow(proc.MainWindowHandle);
                return;
            }

            Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
            }
            catch { }
        }
    }

    private static void NavigateToYouTube()
    {
        try
        {
            var ytWindow = FindWindowWithTitle("YouTube");
            if (ytWindow != IntPtr.Zero)
            {
                ShowWindow(ytWindow, 9);
                SetForegroundWindow(ytWindow);
                return;
            }

            Process.Start(new ProcessStartInfo("https://www.youtube.com") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.youtube.com") { UseShellExecute = true });
            }
            catch { }
        }
    }

    private static void NavigateToGenericApp(string appId)
    {
        try
        {
            var processName = Path.GetFileNameWithoutExtension(NormalizeMediaAppId(appId));
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var window = FindWindowForProcess(processName);
                if (window != IntPtr.Zero)
                {
                    ShowWindow(window, 9);
                    SetForegroundWindow(window);
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(appId))
            {
                Process.Start(new ProcessStartInfo($"shell:AppsFolder\\{appId}") { UseShellExecute = true });
            }
        }
        catch { }
    }

    private static bool HasVisibleWindowWithTitle(string partialTitle) =>
        FindWindowWithTitle(partialTitle) != IntPtr.Zero;

    private static IntPtr FindWindowWithTitle(string partialTitle)
    {
        var found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new StringBuilder(256);
            if (GetWindowText(hWnd, sb, sb.Capacity) > 0)
            {
                var title = sb.ToString();
                if (title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static IntPtr FindWindowForProcess(string processName)
    {
        var found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (string.Equals(proc.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    var sb = new StringBuilder(256);
                    if (GetWindowText(hWnd, sb, sb.Capacity) > 0 && !string.IsNullOrWhiteSpace(sb.ToString()))
                    {
                        found = hWnd;
                        return false;
                    }
                }
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private async Task<bool> TryControlSession(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> control)
    {
        try
        {
            _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _selectedSession ?? _sessionManager.GetCurrentSession();
            return session is not null && await control(session);
        }
        catch { return false; }
    }

    private static Button CreateButton(string label, string tooltip, RoutedEventHandler action)
    {
        var button = new Button
        {
            Content = label,
            ToolTip = tooltip,
            Height = 36,
            Margin = new Thickness(2),
            Padding = new Thickness(8, 0, 8, 0),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += action;
        return button;
    }

    private static void SendMediaKey(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, 2, UIntPtr.Zero);
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color)!);

    private static POINT GetCursorPoint()
    {
        GetCursorPos(out var point);
        return point;
    }

    private static IntPtr GetPrimaryMonitor()
    {
        IntPtr primary = IntPtr.Zero;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            if (TryGetMonitorInfo(monitor, out var info) && (info.Flags & MonitorInfoPrimary) != 0)
            {
                primary = monitor;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return primary;
    }

    private static bool TryGetMonitorInfo(IntPtr monitor, out MONITORINFO info)
    {
        info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info);
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MONITORINFO { public int Size; public RECT Monitor; public RECT Work; public uint Flags; }
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
