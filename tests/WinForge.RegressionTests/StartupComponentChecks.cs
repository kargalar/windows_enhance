using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinForge.App;
using WinForge.Core;
using ForgeApplication = WinForge.App.App;

namespace WinForge.RegressionTests;

// Runs in a fresh test process. Does not activate modules or replace the user's running app.
internal static class StartupComponentChecks
{
    internal static void Run(ForgeApplication app, string directory, string outputPath, Action<bool, string> check)
    {
        using var process = Process.GetCurrentProcess();
        using var store = new SettingsStore(Path.Combine(directory, "startup-settings.json"));
        using var logger = new FileLogger(Path.Combine(directory, "startup.log"), asynchronous: true);
        var host = new ModuleHost(store, logger, new NoHotkeys(), Path.Combine(AppContext.BaseDirectory, "modules"), _ => { });
        var load = Stopwatch.StartNew();
        host.Load();
        var loadMilliseconds = load.Elapsed.TotalMilliseconds;
        check(host.Modules.Count > 0 && host.Modules.All(module => !module.IsActive || module.IsPersistent),
            "Startup component fixture leaves runtime modules inactive; persistent Windows configuration may report active");
        Set(nameof(ForgeApplication.Host), host); Set(nameof(ForgeApplication.Store), store); Set(nameof(ForgeApplication.Logger), logger);
        var profile = Path.GetFullPath(Path.Combine(directory, "startup-webview"));
        var environment = CoreWebView2Environment.CreateAsync(null, profile);
        var window = new MainWindow(environment);
        Process? browser = null;
        var show = Stopwatch.StartNew();
        try
        {
            window.Show();
            var web = (WebView2)window.FindName("UiWebView");
            Wait(() => web.CoreWebView2 is not null, "Startup WebView controller");
            var core = web.CoreWebView2;
            browser = Process.GetProcessById((int)core.BrowserProcessId);
            Wait(() =>
            {
                var script = core.ExecuteScriptAsync("document.querySelectorAll('.tool-card').length === " + host.Modules.Count + " && !!document.querySelector('.tool-card-open') && !!document.querySelector('#module-search')");
                Wait(() => script.IsCompleted, "Startup DOM query");
                return script.GetAwaiter().GetResult() == "true";
            }, "Usable module catalog");
            var showMilliseconds = show.Elapsed.TotalMilliseconds;
            var processMilliseconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
            check(true, "Native full snapshot renders the catalog, search and settings entry points");
            check(host.Modules.Where(module => module.Id is "default-apps" or "default-paths").All(module =>
                !(bool)module.GetType().BaseType!.GetField("_panelInitialized", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!),
                "The usable catalog leaves Windows association and folder data unread until details open");
            using var testAssembly = File.OpenRead(typeof(StartupComponentChecks).Assembly.Location);
            using var appAssembly = File.OpenRead(typeof(MainWindow).Assembly.Location);
            using var extrasAssembly = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
            var artifactFiles = Directory.EnumerateFiles(AppContext.BaseDirectory, "WinForge*.dll")
                .Concat(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "modules"), "WinForge*.dll"))
                .Concat(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "wwwroot"), "*", SearchOption.AllDirectories))
                .Order(StringComparer.Ordinal).Select(path =>
                {
                    using var stream = File.OpenRead(path);
                    return new { path = Path.GetRelativePath(AppContext.BaseDirectory, path), bytes = stream.Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(stream)) };
                }).ToArray();
            var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(artifactFiles))));
            var report = new
            {
                utc = DateTimeOffset.UtcNow, machine = BenchmarkMachineInfo.Read(),
                webView2 = core.Environment.BrowserVersionString,
                windowDpi = GetDpiForWindow(new WindowInteropHelper(window).Handle),
                modules = host.Modules.Count, activeModules = host.Modules.Where(module => module.IsActive).Select(module => module.Id).ToArray(),
                activatedModules = Array.Empty<string>(),
                hostLoadMilliseconds = loadMilliseconds, showToCatalogMilliseconds = showMilliseconds,
                processToCatalogMilliseconds = processMilliseconds,
                testSha256 = Convert.ToHexString(SHA256.HashData(testAssembly)),
                appSha256 = Convert.ToHexString(SHA256.HashData(appAssembly)),
                extrasSha256 = Convert.ToHexString(SHA256.HashData(extrasAssembly)),
                artifactFingerprint = fingerprint, artifactFiles,
                scope = "Fresh regression process, fresh WebView profile, warm operating-system file caches. Real module discovery and production dashboard; no activation, tray, mutex or App.OnStartup. DOM polling adds up to one 10 ms interval per query. This is a component benchmark, not full production-app cold startup."
            };
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Startup components: load {loadMilliseconds:0.0} ms; show/catalog {showMilliseconds:0.0} ms; process/catalog {processMilliseconds:0.0} ms");
        }
        finally
        {
            Set(nameof(ForgeApplication.IsExiting), true);
            window.Close();
            if (browser is not null) { Wait(() => browser.HasExited, "Startup browser teardown"); browser.Dispose(); }
            if (Path.GetDirectoryName(profile) != Path.GetFullPath(directory)) throw new InvalidOperationException("Unexpected startup profile path.");
            Wait(() =>
            {
                try { if (Directory.Exists(profile)) Directory.Delete(profile, true); return true; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }, "Startup profile cleanup");
            host.StopAll();
            PerformanceTrace.Flush();
        }

        void Set(string name, object value) => typeof(ForgeApplication).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(app, value);
    }

    private static void Wait(Func<bool> ready, string label)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (ready()) return;
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        throw new TimeoutException(label);
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    private sealed class NoHotkeys : IHotkeyService
    {
        public IDisposable Register(uint modifiers, uint key, Action callback) => throw new InvalidOperationException("Startup probe must not activate modules.");
    }
}
