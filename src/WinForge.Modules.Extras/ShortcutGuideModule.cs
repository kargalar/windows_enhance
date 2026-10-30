using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Text.Json;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ShortcutGuideModule : ITweakModule
{
    private const uint VirtualKeyOem2 = 0xBF;
    private const uint VirtualKeyLeftWindows = 0x5B;
    private const uint VirtualKeyRightWindows = 0x5C;
    private readonly SettingDescriptor[] _settings;
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private ShortcutGuideWindow? _window;
    private ShortcutGuideWindow? _heldWindow;
    private GlobalKeyboardHook? _keyboardHook;
    private DispatcherTimer? _holdTimer;
    private readonly HashSet<uint> _pressedKeys = new();

    public ShortcutGuideModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open-guide",
                Label = "Kısayol rehberini aç",
                Kind = SettingKind.Action,
                Description = "Windows ve WinForge kısayollarını arayın.",
                Execute = OpenGuide
            },
            new SettingDescriptor
            {
                Key = "hotkey-enabled",
                Label = "Win+Shift+/ kısayolu etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Kısayol rehberini hızlıca açar."
            },
            new SettingDescriptor
            {
                Key = "hold-win-enabled",
                Label = "Win tuşunu basılı tutunca aç",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Win tuşunu tek başına basılı tutunca rehber katmanını gösterir; başka bir tuşa basılırsa iptal olur. Tuş girdileri engellenmez."
            },
            new SettingDescriptor
            {
                Key = "hold-win-delay-ms",
                Label = "Win basılı tutma gecikmesi (ms)",
                Kind = SettingKind.Number,
                Default = 500,
                Min = 250,
                Max = 1500,
                Description = "Basılı tutma katmanını göstermek için Win tuşunun ne kadar süre tek başına tutulması gerektiği."
            }
        ];
    }

    public string Id => "shortcut-guide";
    public string Name => "Kısayol Rehberi";
    public string Category => "Verimlilik";
    public string Description => "Windows ve WinForge klavye kısayollarını arayıp filtreleyin.";
    public string? Warning => null;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        try
        {
            RegisterHotkey();
            RegisterWinHoldHook();
        }
        catch
        {
            IsActive = false;
            throw;
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
        ReleaseWinHoldHook();
        _window?.Close();
        _window = null;
        _heldWindow?.Close();
        _heldWindow = null;
    }

    public void Apply()
    {
        if (!IsActive) return;
        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
        RegisterWinHoldHook();
    }

    private void RegisterHotkey()
    {
        if (_context?.Settings.GetBool("hotkey-enabled", true) == true)
            _hotkey = _context.Hotkeys.Register(HotkeyModifiers.Win | HotkeyModifiers.Shift, VirtualKeyOem2, OpenGuide);
    }

    private void RegisterWinHoldHook()
    {
        ReleaseWinHoldHook();
        if (!IsActive || _context?.Settings.GetBool("hold-win-enabled", true) != true) return;

        try
        {
            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.KeyStateChanged += OnKeyboardKeyStateChanged;
            _keyboardHook.Install();
            _holdTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Clamp(_context.Settings.GetInt("hold-win-delay-ms", 500), 250, 1500))
            };
            _holdTimer.Tick += (_, _) => ShowHeldGuide();
        }
        catch (Exception exception)
        {
            ReleaseWinHoldHook();
            _context.Log.Error("Win tuşu basılı tutma kancası kurulamadı.", exception);
            _context.Notify("Win tuşu basılı tutma özelliği kullanılamıyor: " + exception.Message);
        }
    }

    private void ReleaseWinHoldHook()
    {
        _holdTimer?.Stop();
        _holdTimer = null;
        _keyboardHook?.Dispose();
        _keyboardHook = null;
        _pressedKeys.Clear();
        HideHeldGuide();
    }

    private void OnKeyboardKeyStateChanged(uint virtualKey, bool isDown)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(DispatcherPriority.Input,
            new Action(() => ProcessKeyboardKeyStateChanged(virtualKey, isDown)));
    }

    private void ProcessKeyboardKeyStateChanged(uint virtualKey, bool isDown)
    {
        if (IsWindowsKey(virtualKey))
        {
            var wasDown = IsWindowsKeyDown();
            if (isDown) _pressedKeys.Add(virtualKey);
            else _pressedKeys.Remove(virtualKey);
            var isWindowsDown = IsWindowsKeyDown();
            if (!wasDown && isWindowsDown)
            {
                if (_pressedKeys.Any(key => !IsWindowsKey(key))) return;
                _holdTimer?.Stop();
                _holdTimer?.Start();
            }
            else if (wasDown && !isWindowsDown)
            {
                _holdTimer?.Stop();
                HideHeldGuide();
            }
            return;
        }

        if (isDown)
        {
            _pressedKeys.Add(virtualKey);
            if (!IsWindowsKeyDown()) return;
            _holdTimer?.Stop();
            HideHeldGuide();
        }
        else
        {
            _pressedKeys.Remove(virtualKey);
        }
    }

    private bool IsWindowsKeyDown() =>
        _pressedKeys.Contains(VirtualKeyLeftWindows) || _pressedKeys.Contains(VirtualKeyRightWindows);

    private static bool IsWindowsKey(uint virtualKey) =>
        virtualKey is VirtualKeyLeftWindows or VirtualKeyRightWindows;

    private void ShowHeldGuide()
    {
        _holdTimer?.Stop();
        if (!IsActive || !IsWindowsKeyDown() || _pressedKeys.Any(key => !IsWindowsKey(key))) return;
        try
        {
            _heldWindow ??= new ShortcutGuideWindow(nonActivating: true, featureUi: (_context as IFeatureUiContext)?.FeatureUi);
            _heldWindow.Closed += (_, _) => _heldWindow = null;
            if (!_heldWindow.IsVisible) _heldWindow.Show();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Kısayol rehberi basılı tutma katmanı açılamadı.", exception);
            _context?.Notify("Kısayol rehberi açılamadı: " + exception.Message);
        }
    }

    private void HideHeldGuide()
    {
        if (_heldWindow?.IsVisible == true) _heldWindow.Hide();
    }

    private void OpenGuide()
    {
        if (!IsActive) return;
        HideHeldGuide();
        try
        {
            _window ??= new ShortcutGuideWindow(featureUi: (_context as IFeatureUiContext)?.FeatureUi);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            _window.Activate();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Kısayol rehberi açılamadı.", exception);
            _context?.Notify("Kısayol rehberi açılamadı: " + exception.Message);
        }
    }
}

internal sealed class ShortcutGuideWindow : Window
{
    private static readonly ShortcutGuideEntry[] Entries =
    [
        new("Windows", "Win", "Başlat menüsünü aç"),
        new("Windows", "Win + A", "Hızlı Ayarlar’ı aç"),
        new("Windows", "Win + D", "Masaüstünü göster veya geri yükle"),
        new("Windows", "Win + E", "Dosya Gezgini’ni aç"),
        new("Windows", "Win + I", "Ayarlar’ı aç"),
        new("Windows", "Win + L", "Bilgisayarı kilitle"),
        new("Windows", "Win + M", "Tüm pencereleri küçült"),
        new("Windows", "Win + Shift + M", "Küçültülen pencereleri geri yükle"),
        new("Windows", "Win + P", "Ekran yansıtma seçeneklerini aç"),
        new("Windows", "Win + R", "Çalıştır iletişim kutusunu aç"),
        new("Windows", "Win + S", "Windows Arama’yı aç"),
        new("Windows", "Win + V", "Pano geçmişini aç"),
        new("Windows", "Win + .", "Emoji ve simgeler panelini aç"),
        new("Windows", "Win + Tab", "Görev görünümünü aç"),
        new("Windows", "Win + Ctrl + D", "Sanal masaüstü oluştur"),
        new("Windows", "Win + Ctrl + ← / →", "Sanal masaüstleri arasında geç"),
        new("Windows", "Win + Ctrl + F4", "Geçerli sanal masaüstünü kapat"),
        new("Pencereler", "Win + ← / →", "Pencereyi ekranın soluna veya sağına yasla"),
        new("Pencereler", "Win + ↑ / ↓", "Pencereyi büyüt, küçült veya simge durumuna al"),
        new("Pencereler", "Alt + Tab", "Açık pencereler arasında geç"),
        new("Pencereler", "Alt + F4", "Etkin pencereyi kapat"),
        new("Pencereler", "Win + Z", "Pencere yerleşimlerini aç"),
        new("Erişilebilirlik", "Win + U", "Erişilebilirlik ayarlarını aç"),
        new("Erişilebilirlik", "Win + Ctrl + Enter", "Ekran Okuyucusu’nu başlat"),
        new("WinForge", "Win + Alt + Space", "Hızlı Başlatıcı’yı aç"),
        new("WinForge", "Win + Alt + M", "Medya ve Ses Flyout’unu aç"),
        new("WinForge", "Win + Alt + C", "Renk seçiciyi aç"),
        new("WinForge", "Win + Alt + P", "Dosya önizlemesini aç"),
        new("WinForge", "Win + Alt + J", "Ekranlar arası fare taşıma panelini aç"),
        new("WinForge", "Win + Shift + /", "Bu kısayol rehberini aç")
    ];

    private readonly IFeatureUiSurface _uiSurface;
    private string _query = "";

    public ShortcutGuideWindow() : this(nonActivating: false) { }

    public ShortcutGuideWindow(bool nonActivating = false, IFeatureUiService? featureUi = null)
    {
        _uiSurface = featureUi?.CreateSurface("shortcut-guide")
            ?? throw new InvalidOperationException("The React shortcut guide view is required.");
        _uiSurface.MessageReceived += OnUiMessage;
        Title = "Kısayol Rehberi";
        Width = 760;
        Height = 600;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = !nonActivating;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 34, 44));

        Content = _uiSurface.Element;

        Closed += (_, _) => _uiSurface.Dispose();
        Filter();
    }

    private void Filter()
    {
        var query = _query.Trim();
        var entries = string.IsNullOrWhiteSpace(query)
            ? Entries
            : Entries.Where(entry => entry.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                     entry.Keys.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                     entry.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _uiSurface.SetState(new { query, entries = entries.Select(entry => new { category = entry.Category, keys = entry.Keys, description = entry.Description }).ToArray() });
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        if (command == "shortcut-guide.search")
        {
            _query = payload.TryGetProperty("query", out var query) ? query.GetString() ?? "" : "";
            Filter();
        }
        else if (command == "shortcut-guide.close") Close();
    }

    internal sealed record ShortcutGuideEntry(string Category, string Keys, string Description);
}
