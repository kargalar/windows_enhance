using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinForge.Core;

namespace WinForge.App;

public partial class App : Application
{
    private const string CommandPipeName = "WinForge.Commands";
    private const string SingleInstanceMutexName = @"Local\WinForge.SingleInstance";
    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _commandServerCancellation;

    public static App Instance => (App)Current;

    public SettingsStore Store { get; private set; } = null!;
    public ModuleHost Host { get; private set; } = null!;
    public FileLogger Logger { get; private set; } = null!;
    public HotkeyService Hotkeys { get; private set; } = null!;
    public IFeatureUiService FeatureUi { get; private set; } = null!;
    public MainWindow? MainView { get; private set; }
    private TrayIcon? _tray;

    public bool IsExiting { get; private set; }

    private static SettingsStore LoadSettings(string dataDirectory)
    {
        AppData.MigrateLegacyData();
        var store = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
        var legacyDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForge", "History");
        PortableHistoryMigration.Migrate(store, dataDirectory, legacyDefault);
        return store;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, SingleInstanceMutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            if (IsModuleCommand(e.Args) && !ForwardCommand(e.Args))
                MessageBox.Show("WinForge zaten açık, ancak Gezgin komutu çalışan örneğe iletilemedi. İki örneğin de aynı yetki düzeyinde çalıştığını kontrol edin.",
                    "WinForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Logger?.Error("Beklenmeyen hata.", args.Exception);
            var message = "Beklenmeyen bir hata oluştu. Ayrıntılar log dosyasında.\n\n" + args.Exception.Message;
            if (FeatureUi is { } featureUi)
                _ = featureUi.ShowMessageAsync("WinForge", message, "Kapat");
            else
                MessageBox.Show(message, "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window { WindowStartupLocation: WindowStartupLocation.CenterScreen or WindowStartupLocation.CenterOwner } window)
            {
                CursorPanelPlacement.Center(window);
                window.IsVisibleChanged += (_, _) => { if (window.IsVisible) CursorPanelPlacement.Center(window); };
            }
        }));

        var initialCommand = IsModuleCommand(e.Args);
        var showMain = !e.Args.Any(arg => arg.Equals("--startup", StringComparison.OrdinalIgnoreCase)) && !initialCommand;
        var startupWindow = showMain ? new StartupWindow() : null;
        startupWindow?.Show();
        var dataDirectory = AppData.DirectoryPath;
        try
        {
            Store = await Task.Run(() => LoadSettings(dataDirectory));
            if (IsExiting) return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show("WinForge verileri taşınamadı. Ayrıntılar:\n\n" + exception.Message,
                "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        finally { startupWindow?.Complete(); }

        Logger = new FileLogger(Path.Combine(dataDirectory, "winforge.log"));
        Logger.Info($"WinForge başlatılıyor · {Environment.OSVersion.VersionString}");

        Hotkeys = new HotkeyService();
        var featureUi = new ReactWindowHostService();
        FeatureUi = featureUi;
        OsdWindow.Configure(FeatureUi);

        var modulesDirectory = Path.Combine(AppContext.BaseDirectory, "modules");
        Host = new ModuleHost(Store, Logger, Hotkeys, modulesDirectory, OsdWindow.Toast, OsdWindow.ShowBrightness,
            OsdWindow.ShowVolume, FeatureUi);
        Host.Load();

        MainView = new MainWindow(featureUi.WebViewEnvironment);
        MainWindow = MainView;
        if (showMain)
            MainView.Show();

        _tray = new TrayIcon(ShowMain, ExitApp);
        _tray.Initialize();

        _commandServerCancellation = new CancellationTokenSource();
        _ = Task.Run(() => RunCommandServerAsync(_commandServerCancellation.Token));
        _ = StartModulesAsync(initialCommand ? e.Args : null);
    }

    private async Task StartModulesAsync(string[]? initialCommand)
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Host.StartEnabledAsync();
        if (IsExiting) return;
        MainView?.RefreshState();
        if (initialCommand is not null) DispatchCommand(initialCommand);
    }

    public void ShowMain()
    {
        if (MainView is null) return;

        MainView.Show();
        if (MainView.WindowState == WindowState.Minimized) MainView.WindowState = WindowState.Normal;
        MainView.Activate();
    }

    private static bool IsCommandRequest(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0] is "--peek" or "--power-rename" or "--file-locksmith";

    private static bool IsModuleCommand(string[] args) => IsCommandRequest(args) && args.Length > 1;

    private static bool ForwardCommand(string[] args)
    {
        var payload = JsonSerializer.Serialize(args);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", CommandPipeName, PipeDirection.Out,
                    PipeOptions.CurrentUserOnly);
                client.Connect(400);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };
                writer.WriteLine(payload);
                return true;
            }
            catch (IOException)
            {
                if (attempt == 4) return false;
                Thread.Sleep(100);
            }
            catch (TimeoutException)
            {
                if (attempt == 4) return false;
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        return false;
    }

    private async Task RunCommandServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(CommandPipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024, leaveOpen: true);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null || line.Length > 32_768) continue;

                var args = JsonSerializer.Deserialize<string[]>(line);
                if (args is not null && IsModuleCommand(args))
                    _ = Dispatcher.BeginInvoke(new Action(() => DispatchCommand(args)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                    Logger?.Error("Gezgin komutu alınamadı.", exception);
            }
        }
    }

    private void DispatchCommand(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !IsCommandRequest(args)) return;
        if (args.Count > 102 || args.Sum(argument => argument.Length) > 30_000) return;

        var command = args[0];
        var commandArguments = args.Skip(1).ToArray();
        foreach (var module in Host.Modules.OfType<IAppCommandModule>())
        {
            try
            {
                if (module.HandleCommand(command, commandArguments)) return;
            }
            catch (Exception exception)
            {
                Logger.Error($"Gezgin komutu işlenemedi: {command}", exception);
                _ = FeatureUi.ShowMessageAsync("WinForge", exception.Message, "Kapat");
                return;
            }
        }
    }

    public async void ExitApp()
    {
        if (IsExiting) return;
        IsExiting = true;
        MainView?.Hide();
        if (Host is not null) await Host.StopAllAsync();
        await SettingsChangeBroadcaster.BroadcastAsync();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _commandServerCancellation?.Cancel(); } catch { }
        try { _commandServerCancellation?.Dispose(); } catch { }
        try { Host?.StopAll(); } catch { }
        try { OsdWindow.Dispose(); } catch { }
        try { (FeatureUi as IDisposable)?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { Hotkeys?.Dispose(); } catch { }
        try { Store?.Save(); } catch { }

        if (_mutex is not null && _ownsMutex)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }

        base.OnExit(e);
    }
}
