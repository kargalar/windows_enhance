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
                Options = ["cursor"],
                OptionLabels = new Dictionary<string, string>
                {
                    ["cursor"] = "Fare imlecinin bulunduğu"
                },
                Description = "Tüm açılır paneller fare imlecinin bulunduğu ekranda açılır.",
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
                if (_lastObservedVolume == change.VolumePercent && _lastObservedMuted == change.IsMuted) return;
                _lastObservedVolume = change.VolumePercent;
                _lastObservedMuted = change.IsMuted;
                if (VolumeService.IsWinForgeChange(change)) return;
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
