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
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Task<CoreWebView2Environment> _webViewEnvironment;
    private bool _backgroundNoticeShown;
    private IDisposable? _shortcutCapture;
    private readonly System.Windows.Threading.DispatcherTimer _shortcutCaptureTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public MainWindow(Task<CoreWebView2Environment> webViewEnvironment)
    {
        _webViewEnvironment = webViewEnvironment;
        InitializeComponent();
        Appearance.Load();
        ApplyAppearance();
        _shortcutCaptureTimer.Tick += (_, _) =>
        {
            try { EndShortcutCapture(); }
            catch (Exception ex) { App.Instance.Logger.Error("Kısayollar geri açılamadı.", ex); }
        };
        Loaded += OnLoaded;
        foreach (var panel in App.Instance.Host.Modules.OfType<IModulePanel>())
            panel.PanelStateChanged += () => Dispatcher.BeginInvoke(new Action(RefreshState));
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
            var runtimeMessage = exception is WebView2RuntimeNotFoundException
                ? "Microsoft Edge WebView2 Runtime bulunamadı. Runtime'ı yükleyip WinForge'u yeniden başlatın."
                : "WebView2 arayüzü başlatılamadı. WebView2 Runtime'ın yüklü olduğunu ve WinForge userdata klasörünün erişilebilir olduğunu kontrol edin.";
            MessageBox.Show(this,
                runtimeMessage + "\n\n" + exception.Message,
                "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            var command = root.GetProperty("command").GetString();
            var payload = root.TryGetProperty("payload", out var value) ? value : default;
            object? result = command switch
            {
                "ui.ready" => BuildState(),
                "module.toggle" => await ToggleModule(payload),
                "setting.update" => await UpdateSetting(payload),
                "setting.execute" => ExecuteSetting(payload),
                "module.command" => ExecutePanelCommand(payload),
                "shortcut.capture-begin" => BeginShortcutCapture(),
                "shortcut.capture-end" => EndShortcutCapture(),
                "appearance.update" => UpdateAppearance(payload),
                "system.openLogs" => OpenLogs(),
                "app.exit" => ExitApp(),
                _ => throw new InvalidOperationException("Bilinmeyen arayüz komutu.")
            };

            if (command != "app.exit") PostMessage(new { type = "state", payload = result ?? BuildState() });
        }
        catch (Exception exception)
        {
            App.Instance.Logger.Error("Arayüz komutu işlenemedi.", exception);
            PostMessage(new { type = "error", message = exception.Message, payload = BuildState() });
        }
    }

    private object BeginShortcutCapture()
    {
        _shortcutCapture ??= App.Instance.Hotkeys.Suspend();
        _shortcutCaptureTimer.Stop();
        _shortcutCaptureTimer.Start();
        return BuildState();
    }

    private object EndShortcutCapture()
    {
        _shortcutCaptureTimer.Stop();
        var capture = _shortcutCapture;
        _shortcutCapture = null;
        capture?.Dispose();
        return BuildState();
    }

    private object BuildState()
    {
        var modules = App.Instance.Host.Modules.Select(module => new
        {
            id = module.Id,
            name = module.Name,
            category = module.Category,
            description = module.Description,
            warning = module.Warning,
            isActive = module.IsActive,
            error = App.Instance.Host.ErrorOf(module),
            panel = (module as IModulePanel)?.GetPanelState(),
            settings = module.Settings.Select(setting => new
            {
                key = setting.Key,
                label = setting.Label,
                description = setting.Description,
                section = setting.Section,
                kind = setting.Kind.ToString().ToLowerInvariant(),
                defaultValue = setting.Default,
                value = ReadSetting(module, setting),
                min = setting.Min,
                max = setting.Max,
                options = setting.Options,
                optionLabels = setting.OptionLabels
            })
        }).ToArray();

        return new
        {
            modules,
            categories = modules.Select(module => module.category).Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            activeCount = modules.Count(module => module.isActive),
            totalCount = modules.Length,
            version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            appearance = new
            {
                tone = Appearance.ToneName,
                accent = Appearance.AccentName,
                glow = Appearance.Glow,
                tones = Appearance.Tones.Select(tone => tone.Name),
                accents = Appearance.Accents
            }
        };
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

    private async Task<object> ToggleModule(JsonElement payload)
    {
        var module = FindModule(payload.GetProperty("id").GetString());
        await App.Instance.Host.SetEnabledAsync(module, payload.GetProperty("enabled").GetBoolean());
        return BuildState();
    }

    private async Task<object> UpdateSetting(JsonElement payload)
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

        var settings = App.Instance.Host.SettingsOf(module);
        var previous = ReadSetting(module, descriptor);
        settings.Set(descriptor.Key, settingValue);
        await App.Instance.Host.ApplyAsync(module);
        if (App.Instance.Host.ErrorOf(module) is { } error)
        {
            settings.Set(descriptor.Key, previous);
            await App.Instance.Host.ApplyAsync(module);
            throw new InvalidOperationException("Ayar uygulanamadı; önceki kayıt korundu. " + error);
        }
        return BuildState();
    }

    private object ExecuteSetting(JsonElement payload)
    {
        var module = FindModule(payload.GetProperty("moduleId").GetString());
        var key = payload.GetProperty("key").GetString();
        var descriptor = module.Settings.FirstOrDefault(setting => setting.Key == key)
                         ?? throw new InvalidOperationException("Eylem bulunamadı.");
        if (descriptor.Kind != SettingKind.Action)
            throw new InvalidOperationException("Bu ayar bir eylem değil.");
        descriptor.Execute?.Invoke();
        return BuildState();
    }

    private object ExecutePanelCommand(JsonElement payload)
    {
        var module = FindModule(payload.GetProperty("moduleId").GetString());
        if (module is not IModulePanel panel) throw new InvalidOperationException("Modül paneli bulunamadı.");
        panel.HandlePanelCommand(payload.GetProperty("action").GetString() ?? "", payload.GetProperty("data"));
        return BuildState();
    }

    private object UpdateAppearance(JsonElement payload)
    {
        if (payload.TryGetProperty("tone", out var tone)) Appearance.SetTone(tone.GetString() ?? "Varsayılan");
        if (payload.TryGetProperty("accent", out var accent)) Appearance.SetAccent(accent.GetString() ?? "Mavi");
        if (payload.TryGetProperty("glow", out var glow)) Appearance.SetGlow(glow.GetBoolean());
        ApplyAppearance();
        return BuildState();
    }

    private static ITweakModule FindModule(string? id) =>
        App.Instance.Host.Modules.FirstOrDefault(module => module.Id == id)
        ?? throw new InvalidOperationException("Modül bulunamadı.");

    private object OpenLogs()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{App.Instance.Logger.Path}\"")
        {
            UseShellExecute = true
        });
        return BuildState();
    }

    private static object? ExitApp()
    {
        App.Instance.ExitApp();
        return null;
    }

    private void PostMessage(object message)
    {
        if (App.Instance.IsExiting) return;
        if (UiWebView.CoreWebView2 is null) return;
        UiWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, _jsonOptions));
    }

    internal void RefreshState() => PostMessage(new { type = "state", payload = BuildState() });

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
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        var label = maximized ? "?nceki boyuta getir" : "Ekran? kapla";
        MaximizeButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, label);
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
            UiWebView.Dispose();
        }
        base.OnClosing(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
