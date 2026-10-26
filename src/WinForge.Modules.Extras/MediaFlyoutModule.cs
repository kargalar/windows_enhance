using System.IO;
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
using Microsoft.Win32;
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
    private TaskbarAudioVisualizerWindow? _taskbarVisualizer;
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
                Label = "Müzik / parça değiştiğinde otomatik göster (Up Next)",
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
                Description = "Etkinleştirildiğinde flyout otomatik kapanmaz ve ekranda sabit kalır. Flyout üzerindeki 📌 Sabitle düğmesiyle de değiştirilebilir.",
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
                Description = "Ses veya müzik değiştiğinde otomatik açılan flyout’un ekranda kalacağı kısa süre.",
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
                Label = "Donanım ses ve medya tuşlarında flyout’u göster",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Ses açma/kısma/sessiz ve medya oynat/duraklat/önceki/sonraki tuşlarını izler; tuşların normal Windows davranışını engellemez.",
                Section = "Davranış"
            },
            new SettingDescriptor
            {
                Key = "external-volume-flyout-enabled",
                Label = "Harici aygıt ses değişiminde flyout’u göster",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Windows varsayılan ses çıkışındaki ses veya sessize alma değişimini bildirirse Bluetooth kulaklık, aygıt tekerleği ve diğer denetimlerde flyout’u açar. Destek, ses sürücüsünün değişikliği Windows Core Audio’ya iletmesine bağlıdır.",
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
                Key = "flyout-theme",
                Label = "Flyout teması",
                Kind = SettingKind.Choice,
                Default = "dark",
                Options = ["dark", "light", "system"],
                OptionLabels = new Dictionary<string, string>
                {
                    ["dark"] = "Koyu", ["light"] = "Açık", ["system"] = "Windows ile aynı"
                },
                Description = "Flyout yüzeyinin renk temasını seçer.",
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
    public string Description => "FluentFlyout benzeri medya/ana ses denetimi, donanım ses ve medya tuşlarından açılan OSD, uygulama mikseri ve filtreleri, oturum seçimi, parça konumu, shuffle/repeat, kilit tuşları durumu, Up Next bildirimi, tema/konum seçimi ve çoklu ekran desteği.";
    public string? Warning => "Medya bilgisi Windows medya oturumu API’sine bağlıdır; bazı oynatıcılar albüm kapağı, konum veya karıştır/tekrar denetimlerini sağlamaz. Up Next parça değişimini flyout açıkken izler. Ses görselleştiricisi yalnızca etkinleştirildiğinde paylaşılan çıkış sesini işler ve kayıt oluşturmaz.";
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
            _taskbarVisualizer?.Close();
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
        _taskbarVisualizer?.Close();
        _taskbarVisualizer = null;
        _window?.Close();
        _window = null;
    }

    public void Apply()
    {
        if (!IsActive) return;
        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
        _volumeHotkey?.Dispose();
        _volumeHotkey = null;
        RegisterVolumeHotkey();
        RegisterExternalVolumeWatcher();
        _lockKeysHook?.Dispose();
        _lockKeysHook = null;
        RegisterLockKeysFlyout();
        ConfigureFlyoutWindow();
        ConfigureTaskbarWidget();
        ConfigureTaskbarVisualizer();
    }

    private void RegisterHotkey()
    {
        if (_context?.Settings.GetBool("hotkey-enabled", true) == true)
            _hotkey = _context.Hotkeys.Register(HotkeyModifiers.Win | HotkeyModifiers.Alt, VkM, OpenFlyout);
    }

    private void RegisterVolumeHotkey()
    {
        if (_context?.Settings.GetBool("volume-hotkey-enabled", true) == true)
            _volumeHotkey = _context.Hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkV, OpenFlyout);
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
                    if (IsActive) OpenFlyoutFromHardwareKey();
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

    private void RegisterLockKeysFlyout()
    {
        var settings = _context?.Settings;
        var lockKeysEnabled = settings?.GetBool("lock-keys-flyout-enabled", true) == true;
        var hardwareKeysEnabled = _context?.Settings.GetBool("hardware-keys-flyout-enabled", true) == true;
        var capsLockEnabled = lockKeysEnabled && settings?.GetBool("caps-lock-flyout-enabled", true) == true;
        var numLockEnabled = lockKeysEnabled && settings?.GetBool("num-lock-flyout-enabled", true) == true;
        var scrollLockEnabled = lockKeysEnabled && settings?.GetBool("scroll-lock-flyout-enabled", true) == true;
        if (capsLockEnabled || numLockEnabled || scrollLockEnabled || hardwareKeysEnabled)
            _lockKeysHook = new LockKeysFlyoutHook(capsLockEnabled, numLockEnabled, scrollLockEnabled,
                hardwareKeysEnabled ? OpenFlyoutFromHardwareKey : null);
    }

    private void ConfigureTaskbarWidget()
    {
        if (_context?.Settings.GetBool("taskbar-media-widget-enabled", false) == true)
        {
            _taskbarWidget ??= new TaskbarMediaWidgetWindow(OpenFlyout);
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
        _taskbarVisualizer?.Close();
        _taskbarVisualizer = null;
        var settings = _context?.Settings;
        if (settings?.GetBool("taskbar-visualizer-enabled", false) != true) return;
        _taskbarVisualizer = new TaskbarAudioVisualizerWindow(
            settings.GetString("taskbar-visualizer-position", "center"),
            settings.GetBool("taskbar-media-widget-enabled", false),
            settings.GetBool("taskbar-visualizer-baseline", false),
            settings.GetBool("taskbar-visualizer-hide-when-silent", true));
        _taskbarVisualizer.Show();
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
            _window = new MediaFlyoutWindow();
            _window.Closed += (_, _) => _window = null;
        }
        var settings = _context?.Settings;
        _window.ConfigureUpNext(settings?.GetBool("up-next-enabled", true) == true);
        _window.Configure(
            settings?.GetString("flyout-position", "bottom-right") ?? "bottom-right",
            settings?.GetString("flyout-monitor", "cursor") ?? "cursor",
            settings?.GetString("flyout-theme", "dark") ?? "dark",
            settings?.GetString("flyout-animation", "fade") ?? "fade",
            settings?.GetInt("flyout-dismiss-seconds", 8) ?? 8,
            settings?.GetString("media-app-filter-mode", "all") ?? "all",
            settings?.GetString("media-app-filter-list", "") ?? "",
            settings?.GetString("pinned-media-session", "") ?? "",
            appId => settings?.Set("pinned-media-session", appId ?? ""),
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
    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly Action? _showMediaFlyout;
    private readonly bool _showCapsLock;
    private readonly bool _showNumLock;
    private readonly bool _showScrollLock;
    private readonly IntPtr _hook;

    public LockKeysFlyoutHook(bool showCapsLock, bool showNumLock, bool showScrollLock, Action? showMediaFlyout)
    {
        _showCapsLock = showCapsLock;
        _showNumLock = showNumLock;
        _showScrollLock = showScrollLock;
        _showMediaFlyout = showMediaFlyout;
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
                if (isKeyUp)
                {
                    var label = GetEnabledLockKeyLabel(key, _showCapsLock, _showNumLock, _showScrollLock);
                    if (label is not null)
                    {
                        var enabled = (GetKeyState(key) & 1) != 0;
                        Application.Current?.Dispatcher.BeginInvoke(() => LockKeysStatusWindow.Show(label, enabled));
                    }
                }
                if (_showMediaFlyout is not null && IsMediaFlyoutKey(key) && (isKeyUp || IsVolumeHardwareKey(key)))
                    Application.Current?.Dispatcher.BeginInvoke(_showMediaFlyout);
            }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    internal static bool IsMediaFlyoutKey(int key) => key is >= 0xAD and <= 0xB3;

    private static bool IsVolumeHardwareKey(int key) => key is >= 0xAD and <= 0xAF;

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

internal sealed class LockKeysStatusWindow : Window
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private static LockKeysStatusWindow? _instance;

    static LockKeysStatusWindow()
    {
        Timer.Tick += (_, _) =>
        {
            Timer.Stop();
            _instance?.Close();
            _instance = null;
        };
    }

    private LockKeysStatusWindow(string key, bool enabled)
    {
        Width = 230;
        Height = 76;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Top + 48;
        var highContrast = SystemParameters.HighContrast;
        Content = new Border
        {
            Background = highContrast ? SystemColors.WindowBrush : Brush("#F2181E28"),
            BorderBrush = highContrast ? SystemColors.WindowTextBrush : Brush("#55465263"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 10, 16, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = enabled ? "●" : "○", FontSize = 23, Foreground = Brush(enabled ? "#72D49B" : "#A8B3C7"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center },
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = key, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F1F4F9") },
                            new TextBlock { Text = enabled ? "Açık" : "Kapalı", FontSize = 11, Foreground = Brush("#BCC6D6") }
                        }
                    }
                }
            }
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
        };
    }

    public static void Show(string key, bool enabled)
    {
        _instance?.Close();
        _instance = new LockKeysStatusWindow(key, enabled);
        _instance.Show();
        Timer.Stop();
        Timer.Start();
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color)!);
}

internal sealed class MediaFlyoutWindow : Window
{
    private readonly Slider _volumeSlider;
    private readonly TextBlock _volumeText;
    private readonly TextBlock _muteButton;
    private readonly TextBlock _trackInfo;
    private readonly TextBlock _lockState;
    private readonly Image _artwork;
    private readonly Button _shuffleButton;
    private readonly Button _repeatButton;
    private readonly ComboBox _sessionSelector;
    private readonly Button _pinSessionButton;
    private readonly Button _pinWindowButton;
    private readonly Slider _timelineSlider;
    private readonly TextBlock _timelineText;
    private readonly StackPanel _applicationMixer;
    private readonly ScrollViewer _applicationMixerScroll;
    private readonly Button _applicationMixerToggle;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _backgroundMediaTimer;
    private readonly DispatcherTimer _postHardwareKeyTimer;
    private readonly DispatcherTimer _upNextTimer;
    private readonly DispatcherTimer _dismissTimer;
    private readonly TextBlock _upNextText;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _selectedSession;
    private GlobalSystemMediaTransportControlsSession? _observedSession;
    private bool _updatingTimeline;
    private bool _updatingSessionChoices;
    private bool _updatingPinButton;
    private bool _updatingWindowPinButton;
    private bool _seeking;
    private bool _updatingSlider;
    private bool _updatingMixer;
    private bool _applicationMixerExpanded;
    private bool _refreshingApplicationMixer;
    private bool _refreshingMedia;
    private bool _mediaRefreshPending;
    private bool _muted;
    private bool _shuffleEnabled;
    private MediaPlaybackAutoRepeatMode? _repeatMode;
    private string? _artworkKey;
    private string? _lastMediaKey;
    private bool _hasInitialMediaSnapshot;
    private bool _wasPlaying;
    private bool _isClosed;
    private bool _showUpNext;
    private bool _isWindowPinned;
    private bool _hasCustomPosition;
    private bool _lastTriggerWasAuto;
    private bool _suppressOnFullscreen;
    private string _position = "bottom-right";
    private string _monitor = "cursor";
    private string _theme = "dark";
    private string _animation = "fade";
    private int _dismissSeconds = 8;
    private int _autoDismissSeconds = 3;
    private string _mediaAppFilterMode = "all";
    private HashSet<string> _mediaAppFilterList = new(StringComparer.OrdinalIgnoreCase);
    private string _pinnedSessionId = "";
    private Action<string?>? _savePinnedSession;
    private Action<bool>? _saveWindowPinned;

    public bool IsWindowPinned => _isWindowPinned;

    public MediaFlyoutWindow()
    {
        Title = "Medya ve Ses";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        WindowStartupLocation = WindowStartupLocation.Manual;

        var content = new StackPanel { Margin = new Thickness(20) };
        _upNextText = new TextBlock
        {
            Name = "UpNextNotification",
            Foreground = Brush("#F1F4F9"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brush("#303B4B"),
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(_upNextText);
        var headerRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 14) };
        _pinWindowButton = new Button
        {
            Name = "PinFlyoutButton",
            Content = "📌 Sabitle",
            ToolTip = "Flyout penceresini ekranda sabitle (otomatik kapanmasın)",
            Height = 26,
            Padding = new Thickness(10, 2, 10, 2),
            Background = Brush("#252D3A"),
            Foreground = Brush("#F1F4F9"),
            BorderBrush = Brush("#374253"),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _pinWindowButton.Click += ToggleWindowPinned;
        DockPanel.SetDock(_pinWindowButton, Dock.Right);
        headerRow.Children.Add(_pinWindowButton);
        headerRow.Children.Add(new TextBlock
        {
            Text = "HIZLI DENETİMLER",
            Foreground = Brush("#A8B3C7"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(headerRow);

        _trackInfo = new TextBlock
        {
            Name = "MediaSessionInfo",
            Text = "Medya oturumu bekleniyor",
            Foreground = Brush("#E3E9F2"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var metadataRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
        _artwork = new Image
        {
            Name = "MediaArtwork",
            Width = 76,
            Height = 76,
            Stretch = Stretch.UniformToFill,
            Margin = new Thickness(0, 0, 12, 0),
            ToolTip = "Albüm kapağı"
        };
        DockPanel.SetDock(_artwork, Dock.Left);
        metadataRow.Children.Add(_artwork);
        _trackInfo.Margin = new Thickness(0, 0, 0, 0);
        _trackInfo.VerticalAlignment = VerticalAlignment.Center;
        metadataRow.Children.Add(_trackInfo);
        content.Children.Add(metadataRow);

        _sessionSelector = new ComboBox
        {
            Name = "MediaSessionSelector",
            MinHeight = 30,
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brush("#252D3A"),
            Foreground = Brush("#F1F4F9"),
            ToolTip = "Denetlenecek medya uygulamasını seçin"
        };
        _sessionSelector.SelectionChanged += SessionSelectionChanged;
        content.Children.Add(_sessionSelector);
        _pinSessionButton = CreateButton("Oturumu sabitle", "Seçili medya oturumunu sonraki açılışlarda da tercih et", TogglePinnedSession);
        _pinSessionButton.Margin = new Thickness(0, 0, 0, 10);
        content.Children.Add(_pinSessionButton);

        var soundRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        _volumeText = new TextBlock { Foreground = Brush("#F1F4F9"), FontSize = 13, Width = 48, VerticalAlignment = VerticalAlignment.Center };
        _muteButton = ButtonText("Sessiz");
        _muteButton.Cursor = System.Windows.Input.Cursors.Hand;
        _muteButton.MouseLeftButtonUp += (_, _) => ToggleMute();
        DockPanel.SetDock(_muteButton, Dock.Right);
        DockPanel.SetDock(_volumeText, Dock.Left);
        soundRow.Children.Add(_muteButton);
        soundRow.Children.Add(_volumeText);
        content.Children.Add(soundRow);

        _volumeSlider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1, IsMoveToPointEnabled = true, Margin = new Thickness(0, 0, 0, 16) };
        _volumeSlider.ValueChanged += (_, args) => SetVolume((int)Math.Round(args.NewValue));
        content.Children.Add(_volumeSlider);

        content.Children.Add(CreateButton("Ses çıkış aygıtlarını seç", "Windows Ses Ayarları", (_, _) => OpenSoundSettings()));

        _applicationMixerToggle = CreateButton("Uygulama sesleri göster", "Uygulama ses karıştırıcısını aç/kapat", ToggleApplicationMixer);
        content.Children.Add(_applicationMixerToggle);
        _applicationMixer = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 10) };
        _applicationMixerScroll = new ScrollViewer
        {
            Content = _applicationMixer,
            MaxHeight = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(_applicationMixerScroll);

        var mediaRow = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 18) };
        mediaRow.Children.Add(CreateButton("⏮", "Önceki parça", SkipPrevious));
        mediaRow.Children.Add(CreateButton("▶ / ❚❚", "Oynat veya duraklat", TogglePlayback));
        mediaRow.Children.Add(CreateButton("⏭", "Sonraki parça", SkipNext));
        content.Children.Add(mediaRow);

        var playbackOptions = new UniformGrid { Columns = 2, Margin = new Thickness(0, -10, 0, 10) };
        _shuffleButton = CreateButton("Karıştır: kapalı", "Karıştırmayı değiştir", ToggleShuffle);
        _repeatButton = CreateButton("Tekrar: kapalı", "Tekrar modunu değiştir", ToggleRepeat);
        playbackOptions.Children.Add(_shuffleButton);
        playbackOptions.Children.Add(_repeatButton);
        content.Children.Add(playbackOptions);

        var timelineRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
        _timelineText = new TextBlock
        {
            Name = "MediaTimelineText",
            Text = "0:00 / 0:00",
            Foreground = Brush("#BCC6D6"),
            FontSize = 11,
            Width = 88,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(_timelineText, Dock.Right);
        _timelineSlider = new Slider
        {
            Name = "MediaTimelineSlider",
            Minimum = 0,
            Maximum = 1,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Parça konumu"
        };
        _timelineSlider.ValueChanged += (_, _) =>
        {
            if (!_updatingTimeline && (_timelineSlider.IsMouseCaptureWithin || _timelineSlider.IsKeyboardFocusWithin)) _seeking = true;
        };
        _timelineSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(async (_, _) => await SeekAsync()));
        _timelineSlider.PreviewMouseLeftButtonUp += async (_, _) => await SeekAsync();
        _timelineSlider.PreviewKeyUp += async (_, _) => await SeekAsync();
        DockPanel.SetDock(_timelineSlider, Dock.Left);
        timelineRow.Children.Add(_timelineText);
        timelineRow.Children.Add(_timelineSlider);
        content.Children.Add(timelineRow);

        _lockState = new TextBlock { Foreground = Brush("#BCC6D6"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        content.Children.Add(new Border
        {
            Background = Brush("#202633"),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11, 9, 11, 9),
            Child = _lockState
        });

        Content = new Border
        {
            Background = Brush("#F2181E28"),
            BorderBrush = Brush("#55465263"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 5, Opacity = 0.35, Color = Colors.Black }
        };

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
            Refresh();
            UpdateTimeline(_selectedSession);
            if (_applicationMixerExpanded) RefreshApplicationMixer();
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
            {
                Refresh();
                await RefreshAsync();
            }
        };
        _upNextTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _upNextTimer.Tick += (_, _) =>
        {
            _upNextTimer.Stop();
            _upNextText.Visibility = Visibility.Collapsed;
        };
        _dismissTimer = new DispatcherTimer();
        _dismissTimer.Tick += (_, _) =>
        {
            if (_isWindowPinned || IsMouseOver) return;
            _dismissTimer.Stop();
            Hide();
        };
        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) => ResetDismissTimer(_lastTriggerWasAuto);
        PreviewMouseMove += (_, _) => ResetDismissTimer(_lastTriggerWasAuto);
        PreviewMouseDown += (_, _) => ResetDismissTimer(isAutoTrigger: false);
        PreviewKeyDown += (_, _) => ResetDismissTimer(isAutoTrigger: false);
        Closed += (_, _) =>
        {
            _isClosed = true;
            _refreshTimer.Stop();
            _backgroundMediaTimer.Stop();
            _postHardwareKeyTimer.Stop();
            _upNextTimer.Stop();
            _dismissTimer.Stop();
            DetachMediaEventHandlers();
        };
        Loaded += async (_, _) => await RefreshAsync();
        Refresh();
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
            _refreshTimer.Stop();
            _dismissTimer.Stop();
            Hide();
            return;
        }
        ShowOrRefresh(activateWindow: true, isAutoTrigger: false);
    }

    public void ShowOrRefresh(bool activateWindow = false, bool isAutoTrigger = false)
    {
        _lastTriggerWasAuto = isAutoTrigger;
        var wasVisible = IsVisible;
        if (!wasVisible || !_hasCustomPosition)
            PositionFlyout();
        Refresh();
        _ = RefreshAsync();
        if (isAutoTrigger)
        {
            _postHardwareKeyTimer.Stop();
            _postHardwareKeyTimer.Start();
        }
        if (!wasVisible)
        {
            Opacity = _animation == "fade" ? 0 : 1;
            Show();
            if (_animation == "fade")
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        if (activateWindow) Activate();
        _refreshTimer.Start();
        ResetDismissTimer(isAutoTrigger);
    }

    public void ConfigureUpNext(bool enabled) => _showUpNext = enabled;

    public void Configure(string position, string monitor, string theme, string animation, int dismissSeconds,
        string mediaAppFilterMode = "all", string mediaAppFilterList = "", string pinnedSessionId = "",
        Action<string?>? savePinnedSession = null, bool windowPinned = false, Action<bool>? saveWindowPinned = null,
        int autoDismissSeconds = 3, bool suppressOnFullscreen = false)
    {
        var newPosition = position is "bottom-left" or "top-right" or "top-left" ? position : "bottom-right";
        var newMonitor = monitor is "primary" or "active" ? monitor : "cursor";
        if (newPosition != _position || newMonitor != _monitor)
            _hasCustomPosition = false;
        _position = newPosition;
        _monitor = newMonitor;
        _theme = theme is "light" or "system" ? theme : "dark";
        _animation = animation == "none" ? "none" : "fade";
        _dismissSeconds = Math.Clamp(dismissSeconds, 0, 60);
        _autoDismissSeconds = Math.Clamp(autoDismissSeconds, 1, 30);
        _suppressOnFullscreen = suppressOnFullscreen;
        _mediaAppFilterMode = mediaAppFilterMode is "allowlist" or "blocklist" ? mediaAppFilterMode : "all";
        _mediaAppFilterList = ParseMediaAppFilterList(mediaAppFilterList);
        _pinnedSessionId = pinnedSessionId.Trim();
        _savePinnedSession = savePinnedSession;
        _isWindowPinned = windowPinned;
        _saveWindowPinned = saveWindowPinned;
        ApplyTheme();
        UpdateWindowPinButton();
        if (_isWindowPinned) _dismissTimer.Stop();
        else if (IsVisible) ResetDismissTimer(_lastTriggerWasAuto);
    }

    public void SetWindowPinned(bool pinned)
    {
        _isWindowPinned = pinned;
        _saveWindowPinned?.Invoke(_isWindowPinned);
        UpdateWindowPinButton();
        if (_isWindowPinned) _dismissTimer.Stop();
        else ResetDismissTimer(isAutoTrigger: false);
    }

    private void ToggleWindowPinned(object? sender, RoutedEventArgs args)
    {
        if (_updatingWindowPinButton) return;
        SetWindowPinned(!_isWindowPinned);
    }

    private void UpdateWindowPinButton()
    {
        _updatingWindowPinButton = true;
        try
        {
            _pinWindowButton.Content = _isWindowPinned ? "📌 Sabitlendi" : "📌 Sabitle";
            _pinWindowButton.ToolTip = _isWindowPinned
                ? "Flyout sabitlemesini kaldır (otomatik kapanmayı etkinleştir)"
                : "Flyout penceresini ekranda sabitle (otomatik kapanmasın)";
            if (!SystemParameters.HighContrast)
            {
                var light = _theme == "light" || (_theme == "system" && IsWindowsLightTheme());
                _pinWindowButton.Background = _isWindowPinned
                    ? Brush(light ? "#D0E4FF" : "#2E4A70")
                    : Brush(light ? "#E8EDF4" : "#252D3A");
                _pinWindowButton.BorderBrush = _isWindowPinned
                    ? Brush(light ? "#639CE8" : "#5A90D6")
                    : Brush(light ? "#CBD3DF" : "#374253");
            }
        }
        finally
        {
            _updatingWindowPinButton = false;
        }
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
        Measure(new Size(Width, double.PositiveInfinity));
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
        PlaceInArea(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    private void PlaceInArea(double left, double top, double right, double bottom)
    {
        const double margin = 16;
        var height = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        Left = _position.EndsWith("left", StringComparison.Ordinal) ? left + margin : right - Width - margin;
        Top = _position.StartsWith("top", StringComparison.Ordinal) ? top + margin : bottom - height - margin;
        Left = Math.Max(left + margin, Math.Min(Left, right - Width - margin));
        Top = Math.Max(top + margin, Math.Min(Top, bottom - height - margin));
    }

    private void ResetDismissTimer(bool isAutoTrigger = false)
    {
        _dismissTimer.Stop();
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
        var light = _theme == "light" || (_theme == "system" && IsWindowsLightTheme());
        var background = light ? Brush("#F8FAFC") : Brush("#F2181E28");
        var foreground = Brush(light ? "#17202C" : "#F1F4F9");
        if (Content is Border card)
        {
            card.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : background;
            card.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brush(light ? "#CBD3DF" : "#55465263");
        }
        foreach (var element in Descendants(this))
        {
            if (element is TextBlock text) text.Foreground = foreground;
            else if (element is Border border) border.Background = Brush(light ? "#E8EDF4" : "#202633");
            else if (element is Button button)
            {
                button.Foreground = foreground;
                button.Background = Brush(light ? "#E8EDF4" : "#252D3A");
                button.BorderBrush = Brush(light ? "#CBD3DF" : "#374253");
            }
            else if (element is ComboBox combo)
            {
                combo.Foreground = foreground;
                combo.Background = Brush(light ? "#E8EDF4" : "#252D3A");
            }
        }
        if (SystemParameters.HighContrast)
        {
            if (Content is Border highContrastCard) highContrastCard.Background = SystemColors.WindowBrush;
            foreach (var element in Descendants(this))
            {
                if (element is TextBlock text) text.Foreground = SystemColors.WindowTextBrush;
                if (element is Control control) { control.Foreground = SystemColors.WindowTextBrush; control.Background = SystemColors.WindowBrush; }
                if (element is Border border) { border.Background = SystemColors.WindowBrush; border.BorderBrush = SystemColors.WindowTextBrush; }
            }
        }
        UpdateWindowPinButton();
    }

    private static bool IsWindowsLightTheme()
    {
        try { return Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0)) != 0; }
        catch { return false; }
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

    private void Refresh()
    {
        try
        {
            _updatingSlider = true;
            var volume = VolumeService.Current();
            _volumeSlider.Value = volume;
            _volumeText.Text = $"{volume}%";
            _muted = VolumeService.IsMuted();
            _muteButton.Text = _muted ? "Sesi aç" : "Sessiz";
        }
        catch
        {
            _volumeText.Text = "—";
        }
        finally
        {
            _updatingSlider = false;
        }

        _lockState.Text = $"Caps Lock: {StateText(0x14)}   ·   Num Lock: {StateText(0x90)}   ·   Scroll Lock: {StateText(0x91)}";
    }

    private async Task RefreshAsync()
    {
        if (_refreshingMedia)
        {
            _mediaRefreshPending = true;
            return;
        }
        _refreshingMedia = true;
        Refresh();
        try
        {
            if (_sessionManager is null)
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _sessionManager.SessionsChanged += SessionManagerSessionsChanged;
                _sessionManager.CurrentSessionChanged += SessionManagerCurrentSessionChanged;
            }
            RefreshSessionChoices();
            var session = _selectedSession ?? _sessionManager.GetCurrentSession();
            _selectedSession = session;
            ObserveSelectedSession(session);
            if (session is null)
            {
                _trackInfo.Text = "Medya oturumu bekleniyor";
                _lastMediaKey = null;
                _wasPlaying = false;
                _hasInitialMediaSnapshot = true;
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
                _upNextText.Text = $"SIRADAKİ PARÇA\n{title}" +
                    (string.IsNullOrWhiteSpace(properties.Artist) ? "" : $" · {properties.Artist}");
                _upNextText.Visibility = Visibility.Visible;
                _upNextTimer.Stop();
                _upNextTimer.Start();
                if (!IsVisible && (!_suppressOnFullscreen || !IsForegroundFullscreen()))
                    ShowOrRefresh(activateWindow: false, isAutoTrigger: true);
                else if (IsVisible)
                    ResetDismissTimer(_lastTriggerWasAuto);
            }
            _lastMediaKey = mediaKey;
            _wasPlaying = isPlaying;
            _hasInitialMediaSnapshot = true;
            _trackInfo.Text = string.IsNullOrWhiteSpace(properties.Artist) ? title : $"{title}\n{properties.Artist}";
            await RefreshArtworkAsync(properties, session);
            RefreshPlaybackOptions(session);
            UpdateTimeline(session);
        }
        catch
        {
            _trackInfo.Text = "Medya bilgisi alınamadı";
            _artwork.Source = null;
            _artworkKey = null;
            _shuffleButton.IsEnabled = false;
            _repeatButton.IsEnabled = false;
            UpdateTimeline(null);
        }
        finally
        {
            _refreshingMedia = false;
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

    private void ToggleApplicationMixer(object? sender, RoutedEventArgs args)
    {
        _applicationMixerExpanded = !_applicationMixerExpanded;
        _applicationMixerScroll.Visibility = _applicationMixerExpanded ? Visibility.Visible : Visibility.Collapsed;
        _applicationMixerToggle.Content = _applicationMixerExpanded ? "Uygulama seslerini gizle" : "Uygulama seslerini göster";
        if (_applicationMixerExpanded) RefreshApplicationMixer();
    }

    private void OpenSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch
        {
            _trackInfo.Text = "Ses Ayarları açılamadı; Windows Ayarları’ndan Sistem → Ses bölümünü açın.";
        }
    }

    private async void RefreshApplicationMixer()
    {
        if (_refreshingApplicationMixer) return;
        _refreshingApplicationMixer = true;
        try
        {
            var sessions = await Task.Run(AudioSessionService.GetActiveSessions);
            if (IsVisible && _applicationMixerExpanded) ApplyApplicationMixerSnapshot(sessions);
        }
        catch
        {
            if (IsVisible && _applicationMixerExpanded)
            {
                _applicationMixer.Children.Clear();
                _applicationMixer.Children.Add(new TextBlock { Text = "Uygulama sesleri okunamadı.", Foreground = Brush("#BCC6D6") });
            }
        }
        finally { _refreshingApplicationMixer = false; }
    }

    private void ApplyApplicationMixerSnapshot(IReadOnlyList<AudioSessionService.Session> sessions)
    {
        _updatingMixer = true;
        try
        {
            var previousValues = _applicationMixer.Children.OfType<FrameworkElement>()
                .Select(element => element.Tag as string)
                .Where(id => id is not null)
                .ToHashSet(StringComparer.Ordinal);
            var currentIds = sessions.Select(session => session.InstanceId).ToHashSet(StringComparer.Ordinal);
            var hasStatus = _applicationMixer.Children.OfType<TextBlock>().Any();
            if (sessions.Count == 0 && (!hasStatus || _applicationMixer.Children.OfType<TextBlock>().Any(text => text.Text != "Etkin uygulama ses oturumu yok.")))
            {
                _applicationMixer.Children.Clear();
                _applicationMixer.Children.Add(new TextBlock { Text = "Etkin uygulama ses oturumu yok.", Foreground = Brush("#BCC6D6") });
            }
            else if (sessions.Count > 0 && (!previousValues.SetEquals(currentIds) || hasStatus))
            {
                _applicationMixer.Children.Clear();
                foreach (var session in sessions) _applicationMixer.Children.Add(CreateApplicationSessionRow(session));
            }
            else
            {
                foreach (var row in _applicationMixer.Children.OfType<StackPanel>())
                {
                    if (row.Tag is not string id) continue;
                    var session = sessions.FirstOrDefault(item => item.InstanceId == id);
                    if (session is null) continue;
                    var slider = row.Children.OfType<Slider>().First();
                    slider.Value = session.Volume;
                    var mute = row.Children.OfType<Button>().First();
                    mute.Content = session.IsMuted ? "Sesi aç" : "Sessiz";
                }
            }
        }
        finally { _updatingMixer = false; }
    }

    private FrameworkElement CreateApplicationSessionRow(AudioSessionService.Session session)
    {
        var row = new StackPanel { Tag = session.InstanceId, Margin = new Thickness(0, 4, 0, 4) };
        var header = new DockPanel();
        var mute = CreateButton(session.IsMuted ? "Sesi aç" : "Sessiz", "Bu uygulamanın sesini aç/kapat", (_, _) =>
        {
            try
            {
                var current = AudioSessionService.GetActiveSessions().FirstOrDefault(item => item.InstanceId == session.InstanceId);
                if (current is not null) AudioSessionService.SetMuted(session.InstanceId, !current.IsMuted);
            }
            catch { }
            RefreshApplicationMixer();
        });
        mute.Padding = new Thickness(8, 3, 8, 3);
        mute.Margin = new Thickness(4, 0, 0, 0);
        DockPanel.SetDock(mute, Dock.Right);
        header.Children.Add(mute);
        header.Children.Add(new TextBlock
        {
            Text = session.Application,
            Foreground = Brush("#E3E9F2"),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(header);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = session.Volume, IsMoveToPointEnabled = true };
        slider.ValueChanged += (_, args) =>
        {
            if (_updatingMixer && !slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) return;
            if (!_updatingMixer) TrySetApplicationVolume(session.InstanceId, (int)Math.Round(args.NewValue));
        };
        row.Children.Add(slider);
        return row;
    }

    private void TrySetApplicationVolume(string instanceId, int percent)
    {
        try { AudioSessionService.SetVolume(instanceId, percent); }
        catch { }
    }

    private async Task RefreshArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties,
        GlobalSystemMediaTransportControlsSession session)
    {
        var key = $"{session.SourceAppUserModelId}|{properties.AlbumTitle}|{properties.Title}|{properties.Artist}";
        if (string.Equals(_artworkKey, key, StringComparison.Ordinal)) return;
        _artworkKey = key;
        _artwork.Source = null;
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
        }
        catch
        {
            _artwork.Source = null;
        }
    }

    private void RefreshPlaybackOptions(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var playback = session.GetPlaybackInfo();
            _shuffleEnabled = playback.IsShuffleActive ?? false;
            _repeatMode = playback.AutoRepeatMode;
            _shuffleButton.Content = _shuffleEnabled ? "Karıştır: açık" : "Karıştır: kapalı";
            _repeatButton.Content = $"Tekrar: {RepeatLabel(_repeatMode)}";
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
        var sessions = _sessionManager.GetSessions()
            .Where(session => IsMediaAppAllowed(session.SourceAppUserModelId, _mediaAppFilterMode, _mediaAppFilterList))
            .ToArray();
        var pinned = sessions.FirstOrDefault(session => string.Equals(
            session.SourceAppUserModelId, _pinnedSessionId, StringComparison.OrdinalIgnoreCase));
        var current = pinned ?? (_selectedSession is not null && IsMediaAppAllowed(_selectedSession.SourceAppUserModelId, _mediaAppFilterMode, _mediaAppFilterList)
            ? _selectedSession
            : sessions.FirstOrDefault(session => ReferenceEquals(session, _sessionManager.GetCurrentSession())));
        var choices = sessions.Select(session => new MediaSessionChoice(
            session,
            string.IsNullOrWhiteSpace(session.SourceAppUserModelId) ? "Medya uygulaması" : session.SourceAppUserModelId)).ToArray();

        _updatingSessionChoices = true;
        _sessionSelector.ItemsSource = choices;
        _sessionSelector.DisplayMemberPath = nameof(MediaSessionChoice.DisplayName);
        var selected = choices.FirstOrDefault(choice => ReferenceEquals(choice.Session, current)) ?? choices.FirstOrDefault();
        _sessionSelector.SelectedItem = selected;
        _selectedSession = selected?.Session;
        _updatingSessionChoices = false;
        _sessionSelector.Visibility = choices.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        _updatingPinButton = true;
        var isPinned = selected is not null && string.Equals(selected.Session.SourceAppUserModelId, _pinnedSessionId, StringComparison.OrdinalIgnoreCase);
        _pinSessionButton.Content = isPinned ? "Oturum sabitlemesini kaldır" : "Oturumu sabitle";
        _pinSessionButton.IsEnabled = selected is not null && !string.IsNullOrWhiteSpace(selected.Session.SourceAppUserModelId);
        _updatingPinButton = false;
        if (choices.Length == 0)
        {
            _selectedSession = null;
            _trackInfo.Text = _mediaAppFilterMode == "all"
                ? "Medya oturumu bekleniyor"
                : "Filtreye uyan medya uygulaması yok";
        }
    }

    private void TogglePinnedSession(object? sender, RoutedEventArgs args)
    {
        if (_updatingPinButton || _selectedSession is null) return;
        var appId = _selectedSession.SourceAppUserModelId;
        if (string.IsNullOrWhiteSpace(appId)) return;
        _pinnedSessionId = string.Equals(appId, _pinnedSessionId, StringComparison.OrdinalIgnoreCase) ? "" : appId;
        _savePinnedSession?.Invoke(_pinnedSessionId);
        RefreshSessionChoices();
        _ = RefreshAsync();
    }

    private void SessionSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updatingSessionChoices) return;
        if (_sessionSelector.SelectedItem is MediaSessionChoice choice)
        {
            _selectedSession = choice.Session;
            _ = RefreshAsync();
        }
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
                return;
            }

            var position = timeline.Position;
            if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                position += DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));
            _timelineSlider.Maximum = duration.TotalSeconds;
            if (!_seeking) _timelineSlider.Value = Math.Clamp(position.TotalSeconds, 0, duration.TotalSeconds);
            _timelineSlider.IsEnabled = timeline.MaxSeekTime > timeline.MinSeekTime;
            _timelineText.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
        }
        catch
        {
            _timelineSlider.IsEnabled = false;
            _timelineText.Text = "Konum bilgisi yok";
        }
        finally { _updatingTimeline = false; }
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

    private void SetVolume(int percent)
    {
        if (_updatingSlider || !IsVisible) return;
        try
        {
            var current = VolumeService.Current();
            var applied = VolumeService.StepFromFlyout(percent - current);
            _volumeText.Text = $"{applied}%";
            if (applied > 0 && _muted)
            {
                VolumeService.SetMutedFromFlyout(false);
                _muted = false;
                _muteButton.Text = "Sessiz";
            }
        }
        catch { _volumeText.Text = "Ses denetlenemedi"; }
    }

    private void ToggleMute()
    {
        try
        {
            _muted = !_muted;
            VolumeService.SetMutedFromFlyout(_muted);
            _muteButton.Text = _muted ? "Sesi aç" : "Sessiz";
        }
        catch { }
    }

    private static string StateText(int key) => (GetKeyState(key) & 1) != 0 ? "Açık" : "Kapalı";

    private static Button CreateButton(string label, string tooltip, RoutedEventHandler action)
    {
        var button = new Button
        {
            Content = label,
            ToolTip = tooltip,
            Height = 40,
            Margin = new Thickness(3),
            Background = Brush("#252D3A"),
            Foreground = Brush("#F1F4F9"),
            BorderBrush = Brush("#374253"),
            FontSize = 13,
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

    private static TextBlock ButtonText(string label) => new() { Text = label, Foreground = Brush("#B9C7E0"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
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

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
