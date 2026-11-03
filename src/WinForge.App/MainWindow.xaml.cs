using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using WinForge.Core;

namespace WinForge.App;

public partial class MainWindow : Window
{
    private readonly Task<CoreWebView2Environment> _webViewEnvironment;
    private bool _backgroundNoticeShown;
    private IDisposable? _shortcutCapture;
    private ShortcutRecorder? _shortcutRecorder;
    private readonly System.Windows.Threading.DispatcherTimer _shortcutCaptureTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public MainWindow(Task<CoreWebView2Environment> webViewEnvironment)
    {
        _webViewEnvironment = webViewEnvironment;
        InitializeComponent();
        Appearance.Load();
        ApplyAppearance();
        ApplyLanguage();
        _shortcutCaptureTimer.Tick += (_, _) =>
        {
            try { PostMessage(new { type = "shortcut-recorded", shortcut = "" }); EndShortcutCapture(); }
            catch (Exception ex) { App.Instance.Logger.Error("Kısayollar geri açılamadı.", ex); }
        };
        Loaded += OnLoaded;
        SubscribeToModulePanels();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var darkMode = 1;
        DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        DwmSetWindowAttribute(handle, 19, ref darkMode, sizeof(int));
        var roundCorners = 2;
        DwmSetWindowAttribute(handle, 33, ref roundCorners, sizeof(int));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await UiWebView.EnsureCoreWebView2Async(await _webViewEnvironment);
            var core = UiWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.WebMessageReceived += OnWebMessageReceived;
            core.SetVirtualHostNameToFolderMapping("appassets.winforge",
                Path.Combine(AppContext.BaseDirectory, "wwwroot"),
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) =>
            {
                if (!args.Uri.StartsWith("https://appassets.winforge/", StringComparison.OrdinalIgnoreCase))
                    args.Cancel = true;
                else BeginPageNavigation();
            };
            core.ProcessFailed += (_, args) =>
            {
                App.Instance.Logger.Error($"Ana panel WebView2 işlemi durdu: {args.ProcessFailedKind}");
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                {
                    BeginPageNavigation();
                    try { core.Reload(); } catch (Exception error) { App.Instance.Logger.Error("Ana panel yeniden yüklenemedi.", error); }
                }
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;

            var uiPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            if (!File.Exists(uiPath))
                throw new FileNotFoundException("WinForge arayüz dosyaları bulunamadı.", uiPath);

            core.Navigate("https://appassets.winforge/index.html");
        }
        catch (Exception exception)
        {
            App.Instance.Logger.Error("Web arayüzü başlatılamadı.", exception);
            var isTr = WinForge.Core.Localization.IsTurkish(Appearance.Language);
            var runtimeMessage = exception is WebView2RuntimeNotFoundException
                ? (isTr ? "Microsoft Edge WebView2 Runtime bulunamadı. Runtime'ı yükleyip WinForge'u yeniden başlatın." : "Microsoft Edge WebView2 Runtime could not be found. Please install the runtime and restart WinForge.")
                : (isTr ? "WebView2 arayüzü başlatılamadı. WebView2 Runtime'ın yüklü olduğunu ve WinForge userdata klasörünün erişilebilir olduğunu kontrol edin." : "Could not initialize WebView2 interface. Ensure WebView2 Runtime is installed and WinForge userdata directory is accessible.");
            MessageBox.Show(this,
                runtimeMessage + "\n\n" + exception.Message,
                "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenData()
    {
        Directory.CreateDirectory(AppData.DirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { AppData.DirectoryPath }, UseShellExecute = true });
    }

    private void BeginShortcutCapture()
    {
        _shortcutCapture ??= App.Instance.Hotkeys.Suspend();
        _shortcutCaptureTimer.Stop();
        _shortcutCaptureTimer.Start();
    }

    private void BeginShortcutRecording()
    {
        BeginShortcutCapture();
        _shortcutRecorder?.Dispose();
        _shortcutRecorder = new ShortcutRecorder(chord => Dispatcher.BeginInvoke(new Action(() =>
            PostMessage(new { type = "shortcut-recorded", shortcut = chord }))));
    }

    private void EndShortcutCapture()
    {
        _shortcutRecorder?.Dispose();
        _shortcutRecorder = null;
        _shortcutCaptureTimer.Stop();
        var capture = _shortcutCapture;
        _shortcutCapture = null;
        capture?.Dispose();
    }

    private static object? ReadSetting(ITweakModule module, SettingDescriptor setting)
    {
        var values = App.Instance.Host.SettingsOf(module);
        return setting.Kind switch
        {
            SettingKind.Toggle => values.GetBool(setting.Key, setting.Default is true),
            SettingKind.Number => values.GetInt(setting.Key, Convert.ToInt32(setting.Default ?? 0)),
            SettingKind.Text or SettingKind.Choice or SettingKind.Palette =>
                values.GetString(setting.Key, setting.Default as string ?? string.Empty),
            _ => null
        };
    }

    private async Task ToggleModule(JsonElement payload)
    {
        var module = FindModule(payload.GetProperty("id").GetString());
        await App.Instance.Host.SetEnabledAsync(module, payload.GetProperty("enabled").GetBoolean());
        MarkModuleChanged(module.Id);
    }

    private async Task UpdateSetting(JsonElement payload)
    {
        var module = FindModule(payload.GetProperty("moduleId").GetString());
        var key = payload.GetProperty("key").GetString();
        var descriptor = module.Settings.FirstOrDefault(setting => setting.Key == key)
                         ?? throw new InvalidOperationException("Ayar bulunamadı.");
        var value = payload.GetProperty("value");
        object settingValue = descriptor.Kind switch
        {
            SettingKind.Toggle => value.GetBoolean(),
            SettingKind.Number => Math.Clamp(value.GetInt32(), descriptor.Min, descriptor.Max),
            SettingKind.Text or SettingKind.Choice or SettingKind.Palette => value.GetString() ?? string.Empty,
            _ => throw new InvalidOperationException("Bu ayar doğrudan düzenlenemez.")
        };

        await App.Instance.Host.UpdateSettingAsync(module, descriptor, settingValue);
        MarkModuleChanged(module.Id);
    }

    private void ExecuteSetting(JsonElement payload)
    {
        var module = FindModule(payload.GetProperty("moduleId").GetString());
        var key = payload.GetProperty("key").GetString();
        var descriptor = module.Settings.FirstOrDefault(setting => setting.Key == key)
                         ?? throw new InvalidOperationException("Eylem bulunamadı.");
        if (descriptor.Kind != SettingKind.Action)
            throw new InvalidOperationException("Bu ayar bir eylem değil.");
        descriptor.Execute?.Invoke();
        MarkModuleChanged(module.Id);
    }

    private void ExecutePanelCommand(JsonElement payload)
    {
        var module = FindModule(payload.GetProperty("moduleId").GetString());
        if (module is not IModulePanel panel) throw new InvalidOperationException("Modül paneli bulunamadı.");
        _inspectedModules.Add(module.Id);
        panel.HandlePanelCommand(payload.GetProperty("action").GetString() ?? "", payload.GetProperty("data"));
        MarkModuleChanged(module.Id);
    }

    private void UpdateAppearance(JsonElement payload)
    {
        if (payload.TryGetProperty("tone", out var tone)) Appearance.SetTone(tone.GetString() ?? "Varsayılan");
        if (payload.TryGetProperty("accent", out var accent)) Appearance.SetAccent(accent.GetString() ?? "Mavi");
        if (payload.TryGetProperty("glow", out var glow)) Appearance.SetGlow(glow.GetBoolean());
        if (payload.TryGetProperty("language", out var lang))
        {
            Appearance.SetLanguage(lang.GetString() ?? "en");
            ApplyLanguage();
            _fullRefreshNeeded = true;
        }
        ApplyAppearance();
        _appearanceChanged = true;
    }

    private void ApplyLanguage()
    {
        UpdateWindowControlLabels();
        App.Instance.UpdateTrayLanguage();
    }

    private static ITweakModule FindModule(string? id) =>
        App.Instance.Host.Modules.FirstOrDefault(module => module.Id == id)
        ?? throw new InvalidOperationException("Modül bulunamadı.");

    private async Task OpenLogs()
    {
        await App.Instance.Logger.FlushAsync();
        WriteDiagnosticReport();
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{App.Instance.Logger.Path}\"")
        {
            UseShellExecute = true
        });
    }

    private string WriteDiagnosticReport()
    {
        var logger = App.Instance.Logger;
        var report = new
        {
            createdUtc = DateTimeOffset.UtcNow,
            appVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
            windows = Environment.OSVersion.VersionString, runtime = Environment.Version.ToString(),
            webView2 = UiWebView.CoreWebView2?.Environment.BrowserVersionString,
            modules = App.Instance.Host.Modules.Select(module => new { id = module.Id, active = module.IsActive, error = App.Instance.Host.ErrorOf(module) }).ToArray(),
            persistence = App.Instance.Store.Persistence,
            logging = new { droppedMessages = logger.DroppedMessages, failedMessages = logger.FailedMessages, lastError = logger.LastError },
            performanceTrace = new { enabled = PerformanceTrace.Enabled, droppedEntries = PerformanceTrace.DroppedEntries }
        };
        var path = Path.Combine(AppData.DirectoryPath, "diagnostics.json");
        Directory.CreateDirectory(AppData.DirectoryPath);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }

    private static void ExitApp()
    {
        App.Instance.ExitApp();
    }

    private void ApplyAppearance() => Appearance.Apply();

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateWindowControlLabels();
    }

    private void UpdateWindowControlLabels()
    {
        if (MinimizeButton is null || MaximizeButton is null || CloseButton is null) return;

        var language = Appearance.Language;
        var maximized = WindowState == WindowState.Maximized;
        TitleBarSubtitle.Text = language == "tr" ? "KONTROL MERKEZ\u0130" : "CONTROL CENTER";
        var minText = WinForge.Core.Localization.Get("Simge durumuna k\u00fc\u00e7\u00fclt", language);
        var maxText = maximized
            ? (language == "tr" ? "\u00d6nceki boyuta getir" : "Restore")
            : WinForge.Core.Localization.Get("Ekran\u0131 kapla", language);
        var closeText = WinForge.Core.Localization.Get("Kapat", language);

        MinimizeButton.ToolTip = minText;
        System.Windows.Automation.AutomationProperties.SetName(MinimizeButton, minText);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maxText;
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maxText);
        CloseButton.ToolTip = closeText;
        System.Windows.Automation.AutomationProperties.SetName(CloseButton, closeText);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_shortcutCapture is not null) return;
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UiWebView.CoreWebView2?.ExecuteScriptAsync("window.dispatchEvent(new Event('winforge-search'))");
            e.Handled = true;
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        if (_shortcutCapture is not null)
        {
            PostMessage(new { type = "shortcut-recorded", shortcut = "" });
            try { EndShortcutCapture(); }
            catch (Exception error) { App.Instance.Logger.Error("Kısayollar geri açılamadı.", error); }
        }
        base.OnDeactivated(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        EndShortcutCapture();
        if (!App.Instance.IsExiting)
        {
            e.Cancel = true;
            Hide();

            if (!_backgroundNoticeShown)
            {
                _backgroundNoticeShown = true;
                OsdWindow.Toast("WinForge arka planda çalışıyor · Kısayollar etkin");
            }
        }
        else
        {
            DisposeStateBridge();
            UiWebView.Dispose();
        }
        base.OnClosing(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
