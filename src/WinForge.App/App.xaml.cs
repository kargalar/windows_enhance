using System.Threading;
using System.Windows;
using WinForge.Core;

namespace WinForge.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;

    public static App Instance => (App)Current;

    public SettingsStore Store { get; private set; } = null!;
    public ModuleHost Host { get; private set; } = null!;
    public FileLogger Logger { get; private set; } = null!;
    public HotkeyService Hotkeys { get; private set; } = null!;
    public MainWindow? MainView { get; private set; }
    private TrayIcon? _tray;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\WinForge.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Logger?.Error("Beklenmeyen hata.", args.Exception);
            MessageBox.Show(
                "Beklenmeyen bir hata oluştu. Ayrıntılar log dosyasında.\n\n" + args.Exception.Message,
                "WinForge",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinForge");
        Directory.CreateDirectory(dataDirectory);

        Logger = new FileLogger(Path.Combine(dataDirectory, "winforge.log"));
        Logger.Info($"WinForge başlatılıyor · {Environment.OSVersion.VersionString}");

        Store = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
        Hotkeys = new HotkeyService();

        var modulesDirectory = Path.Combine(AppContext.BaseDirectory, "modules");
        Host = new ModuleHost(Store, Logger, Hotkeys, modulesDirectory, OsdWindow.Toast);
        Host.Load();
        Host.StartEnabled();

        MainView = new MainWindow();
        MainView.Show();

        _tray = new TrayIcon(ShowMain, ExitApp);
        _tray.Initialize();
    }

    public void ShowMain()
    {
        if (MainView is null) return;

        MainView.Show();
        if (MainView.WindowState == WindowState.Minimized) MainView.WindowState = WindowState.Normal;
        MainView.Activate();
    }

    public void ExitApp()
    {
        IsExiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Host?.StopAll(); } catch { }
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
