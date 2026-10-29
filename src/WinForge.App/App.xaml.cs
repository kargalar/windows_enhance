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
    public MainWindow? MainView { get; private set; }
    private TrayIcon? _tray;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
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
        Host = new ModuleHost(Store, Logger, Hotkeys, modulesDirectory, OsdWindow.Toast, OsdWindow.ShowBrightness,
            OsdWindow.ShowVolume);
        Host.Load();
        Host.StartEnabled();

        var initialCommand = IsModuleCommand(e.Args);
        MainView = new MainWindow();
        if (!e.Args.Any(arg => arg.Equals("--startup", StringComparison.OrdinalIgnoreCase)) && !initialCommand)
            MainView.Show();

        _tray = new TrayIcon(ShowMain, ExitApp);
        _tray.Initialize();

        _commandServerCancellation = new CancellationTokenSource();
        _ = Task.Run(() => RunCommandServerAsync(_commandServerCancellation.Token));
        if (initialCommand)
            Dispatcher.BeginInvoke(new Action(() => DispatchCommand(e.Args)));
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
                MessageBox.Show(exception.Message, "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
    }

    public void ExitApp()
    {
        IsExiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _commandServerCancellation?.Cancel(); } catch { }
        try { _commandServerCancellation?.Dispose(); } catch { }
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
