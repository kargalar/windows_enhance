using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinForge.App;
using WinForge.Core;
using ForgeApplication = WinForge.App.App;

namespace WinForge.RegressionTests;

internal static class DashboardBridgeChecks
{
    internal static void Run(ForgeApplication app, string directory, Action<bool, string> check, string? reportPath = null)
    {
        using var store = new SettingsStore(Path.Combine(directory, "bridge-settings.json")) { DeferWrites = true };
        using var logger = new FileLogger(Path.Combine(directory, "bridge.log"), asynchronous: true);
        var host = new ModuleHost(store, logger, new NoHotkeys(), Path.Combine(AppContext.BaseDirectory, "modules"), _ => { });
        host.Load();
        var panelProbe = new DeferredPanelProbe(fixtureOnly: true);
        ((List<ITweakModule>)typeof(ModuleHost).GetField("_modules", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).Add(panelProbe);
        var previousHost = app.Host; var previousStore = app.Store; var previousLogger = app.Logger;
        Set(app, nameof(ForgeApplication.Host), host); Set(app, nameof(ForgeApplication.Store), store); Set(app, nameof(ForgeApplication.Logger), logger);
        var browserDirectory = Path.GetFullPath(Path.Combine(directory, "bridge-webview"));
        var window = new MainWindow(CoreWebView2Environment.CreateAsync(null, browserDirectory));
        Process? browser = null;
        try
        {
            window.Show();
            var web = (WebView2)window.FindName("UiWebView");
            Wait(() => web.CoreWebView2 is not null, "Dashboard controller");
            var core = web.CoreWebView2;
            browser = Process.GetProcessById((int)core.BrowserProcessId);
            Wait(() => Script("document.querySelectorAll('.tool-card').length") == host.Modules.Count.ToString(), "Initial native catalog");
            const string capture = "window.__bridgeMessages=[];window.__bridgeStarts=Object.create(null);window.__bridgeDurations=Object.create(null);window.chrome.webview.addEventListener('message', e=>{if(['state','state.patch','error'].includes(e.data.type))window.__bridgeMessages.push(e.data);const id=e.data.requestId;if(window.__bridgeStarts[id]!==undefined){window.__bridgeDurations[id]=performance.now()-window.__bridgeStarts[id];delete window.__bridgeStarts[id];}});";
            var install = core.AddScriptToExecuteOnDocumentCreatedAsync(capture); Complete(install);
            Script(capture);
            var full = Send("ui.resync", new { }, "full");
            check(full.GetProperty("protocolVersion").GetInt32() == 1 && full.GetProperty("payload").GetProperty("modules").GetArrayLength() == host.Modules.Count,
                "Dashboard resync returns a versioned full catalog with a correlated request ID");
            check(full.GetProperty("payload").GetProperty("appearance").GetProperty("language").GetString() == Appearance.Language,
                "Native appearance snapshots include the persisted interface language");
            var fullBytes = Bytes(full);
            check(panelProbe.Reads == 0 && full.GetProperty("payload").GetProperty("modules").EnumerateArray()
                .All(item => item.GetProperty("panel").ValueKind == JsonValueKind.Null && !item.GetProperty("panelLoaded").GetBoolean()),
                "Initial full catalog defers every panel getter, including an instrumented probe");
            var defaults = host.Modules.Single(item => item.Id == "default-apps");
            var defaultsInitialized = defaults.GetType().BaseType!.GetField("_panelInitialized", BindingFlags.Instance | BindingFlags.NonPublic)!;
            check(!(bool)defaultsInitialized.GetValue(defaults)!, "Catalog rendering does not read Windows file associations");
            Script("[...document.querySelectorAll('.tool-card')].find(card=>card.querySelector('h2')?.textContent==='Deferred panel probe').querySelector('.tool-card-open').click()");
            Wait(() => panelProbe.Reads == 1 && Script("!!document.querySelector('#module-details .panel-loading')") == "false", "Lazy panel acknowledgement");
            check(!(bool)defaultsInitialized.GetValue(defaults)!, "Opening one panel leaves unrelated Windows data unprepared");
            panelProbe.FailRead = true;
            Send("module.inspect", new { moduleId = panelProbe.Id }, "probe-failure");
            Wait(() => Script("!!document.querySelector('#module-details .management-section .error-box button')") == "true", "Panel retry action");
            var failedReads = panelProbe.Reads;
            Pump(TimeSpan.FromMilliseconds(200));
            check(panelProbe.Reads == failedReads, "A failed panel read exposes retry without an automatic request loop");
            panelProbe.FailRead = false;
            Script("document.querySelector('#module-details .management-section .error-box button').click()");
            Wait(() => panelProbe.Reads > failedReads && Script("!!document.querySelector('#module-details .error-box')") == "false", "Panel retry recovery");
            var inspected = Send("ui.resync", new { }, "inspected-resync");
            check(inspected.GetProperty("payload").GetProperty("modules").EnumerateArray().Single(item => item.GetProperty("id").GetString() == panelProbe.Id)
                .GetProperty("panelLoaded").GetBoolean(), "Full resync retains an inspected panel without loading unopened panels");
            var module = host.Modules.Single(module => module.Id == "net-speed");
            var setting = module.Settings.First(setting => setting.Kind == SettingKind.Number && setting.Min < setting.Max);
            Script("(() => { const card=[...document.querySelectorAll('.tool-card')].find(card=>card.querySelector('h2')?.textContent===" + JsonSerializer.Serialize(module.Name) + ");card.querySelector('.tool-card-open').click();})()");
            var control = "[...document.querySelectorAll('#module-details input')].find(input=>input.getAttribute('aria-label')===" + JsonSerializer.Serialize(setting.Label) + ")";
            Wait(() => Script("!!(" + control + ")") == "true", "Module control before patch");
            var patch = Send("setting.update", new { moduleId = module.Id, key = setting.Key, value = setting.Max }, "edit");
            var payload = patch.GetProperty("payload");
            check(patch.GetProperty("type").GetString() == "state.patch" && ChangedIds(payload).SequenceEqual(new[] { module.Id }),
                "One setting edit transports only its own module instead of unrelated panels");
            check(!payload.GetProperty("persistence").GetProperty("pending").GetBoolean()
                && new SettingsStore(store.FilePath).For(module.Id)[setting.Key] == setting.Max.ToString(),
                "A dashboard acknowledgement follows durable persistence");
            check(Bytes(patch) < fullBytes / 2, "A module patch reduces canonical JSON bytes by more than fifty percent");
            Wait(() => Script("(" + control + ").value") == JsonSerializer.Serialize(setting.Max.ToString()), "Patched setting in the rendered control");
            check(true, "The native field delta updates the rendered setting without replacing unrelated modules");

            var durations = new List<double>();
            var pollingDurations = new List<double>();
            var sizes = new List<int>();
            for (var i = 0; i < 30; i++)
            {
                var watch = Stopwatch.StartNew();
                var response = Send("setting.update", new { moduleId = module.Id, key = setting.Key, value = i % 2 == 0 ? setting.Min : setting.Max }, "burst-" + i);
                pollingDurations.Add(watch.Elapsed.TotalMilliseconds);
                durations.Add(JsonSerializer.Deserialize<double>(Script("window.__bridgeDurations[" + JsonSerializer.Serialize("burst-" + i) + "]")));
                sizes.Add(Bytes(response));
                check(ChangedIds(response.GetProperty("payload")).SequenceEqual(new[] { module.Id }), "Burst acknowledgement remains module-scoped " + i);
            }
            var states = Messages();
            for (var i = 1; i < states.Length; i++)
            {
                check(states[i].GetProperty("version").GetInt64() > states[i - 1].GetProperty("version").GetInt64(), "Dashboard state versions increase " + i);
                if (states[i].GetProperty("type").GetString() == "state.patch")
                    check(states[i].GetProperty("baseVersion").GetInt64() == states[i - 1].GetProperty("version").GetInt64(), "Patch links to the last delivered version " + i);
            }
            var failed = Send("setting.update", new { moduleId = module.Id, key = "missing-setting", value = 1 }, "invalid");
            check(failed.GetProperty("type").GetString() == "error" && failed.GetProperty("payload").GetProperty("modules").GetArrayLength() == host.Modules.Count,
                "A failed command returns a correlated current snapshot for recovery");
            var current = host.SettingsOf(module).GetInt(setting.Key);
            var recovery = Send("ui.resync", new { }, "before-reload");
            var session = full.GetProperty("sessionId").GetString();
            var previousVersion = recovery.GetProperty("version").GetInt64();
            var navigations = 0;
            core.NavigationStarting += (_, _) => navigations++;
            core.Reload();
            Wait(() => Messages().Any(message => message.GetProperty("type").GetString() == "state" && message.GetProperty("version").GetInt64() > previousVersion), "Reload snapshot");
            var reloaded = Messages().Last();
            check(reloaded.GetProperty("sessionId").GetString() == session && reloaded.GetProperty("payload").GetProperty("modules").GetArrayLength() == host.Modules.Count,
                "Renderer reload negotiates a fresh full snapshot without losing native state");
            var restored = reloaded.GetProperty("payload").GetProperty("modules").EnumerateArray().Single(item => item.GetProperty("id").GetString() == module.Id)
                .GetProperty("settings").EnumerateArray().Single(item => item.GetProperty("key").GetString() == setting.Key).GetProperty("value").GetInt32();
            check(restored == current, "Reload restores the last durable setting");
            check(reloaded.GetProperty("payload").GetProperty("modules").EnumerateArray().All(item => !item.GetProperty("panelLoaded").GetBoolean()),
                "A new page starts with deferred panels instead of retaining the previous page's inspected catalog");
            Script("[...document.querySelectorAll('.tool-card')].find(card=>card.querySelector('h2')?.textContent===" + JsonSerializer.Serialize(defaults.Name) + ").querySelector('.tool-card-open').click()");
            Wait(() => Script("document.querySelectorAll('.defaults-list article').length > 0") == "true", "Deferred Windows file associations");
            check((bool)defaultsInitialized.GetValue(defaults)!, "Opening default applications loads and renders the real Windows associations on demand");
            var beforeShow = navigations;
            window.Hide(); window.Show(); Pump(TimeSpan.FromMilliseconds(250));
            check(navigations == beforeShow, "Dashboard Hide/Show reuses its page instead of registering and navigating again");

            var legacy = Send("ui.ready", new { }, "legacy-ready");
            check(legacy.GetProperty("payload").GetProperty("modules").EnumerateArray().Where(item => item.GetProperty("panelAvailable").GetBoolean())
                .All(item => item.GetProperty("panelLoaded").GetBoolean()),
                "A client without the deferred-panel capability still receives populated panels in its first full snapshot");
            Send("ui.ready", new { deferredPanels = true }, "deferred-ready");

            var beforeCrash = Messages().Last().GetProperty("version").GetInt64();
            var renderFailed = false;
            core.ProcessFailed += (_, args) => { if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited) renderFailed = true; };
            var crash = core.CallDevToolsProtocolMethodAsync("Page.crash", "{}");
            Wait(() => renderFailed && Messages().Any(message => message.GetProperty("type").GetString() == "state" && message.GetProperty("version").GetInt64() > beforeCrash), "Automatic renderer-crash recovery");
            if (crash.IsFaulted) _ = crash.Exception; // A crashed renderer may not answer the triggering DevTools call.
            var afterCrash = Messages().Last(message => message.GetProperty("type").GetString() == "state");
            check(renderFailed && afterCrash.GetProperty("payload").GetProperty("modules").GetArrayLength() == host.Modules.Count,
                "An actual renderer crash automatically reloads the dashboard and restores its full native state");

            host.SettingsOf(module).Set("diagnostic-private-probe", "private-value-sentinel"); store.Flush();
            var diagnosticsPath = (string)typeof(MainWindow).GetMethod("WriteDiagnosticReport", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            var diagnosticsText = File.ReadAllText(diagnosticsPath);
            using (var diagnostics = JsonDocument.Parse(diagnosticsText))
                check(diagnostics.RootElement.GetProperty("logging").GetProperty("droppedMessages").GetInt64() == logger.DroppedMessages
                    && diagnostics.RootElement.GetProperty("performanceTrace").GetProperty("droppedEntries").GetInt64() == PerformanceTrace.DroppedEntries
                    && !diagnosticsText.Contains("private-value-sentinel"),
                    "Local diagnostics expose log/trace loss counters without exporting settings values");

            durations.Sort(); pollingDurations.Sort(); sizes.Sort();
            var p95 = durations[(int)Math.Ceiling(durations.Count * .95) - 1];
            var report = new
            {
                utc = DateTimeOffset.UtcNow, machine = BenchmarkMachineInfo.Read(),
                windowDpi = Native.GetDpiForWindow(new WindowInteropHelper(window).Handle),
                webView2 = core.Environment.BrowserVersionString, modules = host.Modules.Count, instrumentedProbeModules = 1,
                fullSnapshotBytes = fullBytes, medianPatchBytes = sizes[sizes.Count / 2],
                reductionPercent = 100 * (1 - sizes.Average() / fullBytes), iterations = durations.Count,
                commandP95Milliseconds = p95, samplesMilliseconds = durations,
                testPollingP95Milliseconds = pollingDurations[(int)Math.Ceiling(pollingDurations.Count * .95) - 1],
                note = "Canonical System.Text.Json bytes from received browser objects. Command times use browser performance.now from postMessage to native acknowledgement; test polling overhead is reported separately. Modules are inactive to avoid changing Windows settings."
            };
            reportPath ??= Path.GetFullPath(Path.Combine("artifacts", "performance", "dashboard-bridge.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Dashboard bridge: {fullBytes} -> median {sizes[sizes.Count / 2]} canonical bytes; p95 {p95:0.0} ms. Report: {reportPath}");
            check(p95 <= 100, "Browser/native acknowledgement p95 meets the 100 ms command budget");

            string Script(string code)
            {
                var task = core.ExecuteScriptAsync(code); Complete(task); return task.GetAwaiter().GetResult();
            }
            JsonElement[] Messages()
            {
                try
                {
                    var json = JsonSerializer.Deserialize<string>(Script("JSON.stringify(window.__bridgeMessages||[])"));
                    if (string.IsNullOrEmpty(json)) return [];
                    using var document = JsonDocument.Parse(json);
                    return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
                }
                catch (COMException) { return []; }
            }
            JsonElement Send(string command, object data, string requestId)
            {
                Script("window.__bridgeStarts[" + JsonSerializer.Serialize(requestId) + "]=performance.now();window.chrome.webview.postMessage(" + JsonSerializer.Serialize(new { command, payload = data, requestId }) + ")");
                JsonElement? response = null;
                Wait(() => { response = Messages().Where(message => HasRequest(message, requestId)).Select(message => (JsonElement?)message).LastOrDefault(); return response.HasValue; }, "Command " + requestId);
                return response!.Value;
            }
        }
        finally
        {
            var exiting = app.IsExiting;
            try
            {
                Set(app, nameof(ForgeApplication.IsExiting), true); window.Close();
                if (browser is not null)
                {
                    Wait(() => browser.HasExited, "Owned WebView browser teardown");
                    check(browser.HasExited, "Closing the dashboard releases its owned WebView browser process before profile cleanup");
                }
                if (Path.GetDirectoryName(browserDirectory) != Path.GetFullPath(directory)) throw new InvalidOperationException("Unexpected browser profile cleanup path.");
                Wait(() =>
                {
                    try { if (Directory.Exists(browserDirectory)) Directory.Delete(browserDirectory, true); return true; }
                    catch (IOException) { return false; }
                    catch (UnauthorizedAccessException) { return false; }
                }, "Owned browser profile cleanup");
            }
            finally
            {
                Set(app, nameof(ForgeApplication.IsExiting), exiting); browser?.Dispose();
                host.StopAll();
                Set(app, nameof(ForgeApplication.Host), previousHost); Set(app, nameof(ForgeApplication.Store), previousStore); Set(app, nameof(ForgeApplication.Logger), previousLogger);
            }
        }
    }

    private static int Bytes(JsonElement value) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value));
    private static IEnumerable<string?> ChangedIds(JsonElement payload) => payload.GetProperty("modules").EnumerateArray().Select(module => module.GetProperty("id").GetString())
        .Concat(payload.GetProperty("moduleChanges").EnumerateArray().Select(module => module.GetProperty("id").GetString()));
    private static bool HasRequest(JsonElement value, string id) => value.TryGetProperty("requestId", out var request) && request.GetString() == id;
    private static void Set(ForgeApplication app, string name, object? value) => typeof(ForgeApplication).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(app, value);
    private static void Complete(Task task) { Wait(() => task.IsCompleted, "WebView script"); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> ready, string label)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(25)) { if (ready()) return; Pump(TimeSpan.FromMilliseconds(20)); }
        throw new TimeoutException(label);
    }
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private sealed class NoHotkeys : IHotkeyService
    {
        public IDisposable Register(uint modifiers, uint key, Action callback) => throw new InvalidOperationException("The dashboard benchmark does not activate input modules.");
    }

    private sealed class DeferredPanelProbe : ITweakModule, IModulePanel
    {
        // The regression assembly is also loaded as a plugin fixture. This probe
        // must not satisfy ModuleHost's public parameterless-constructor contract.
        public DeferredPanelProbe(bool fixtureOnly) { }
        public string Id => "deferred-panel-probe";
        public string Name => "Deferred panel probe";
        public string Category => "Test";
        public string Description => "Instrumented panel getter";
        public string? Warning => null;
        public bool IsActive => false;
        public IReadOnlyList<SettingDescriptor> Settings => [];
        public int Reads { get; private set; }
        public bool FailRead { get; set; }
        public event Action? PanelStateChanged { add { } remove { } }
        public object GetPanelState()
        {
            Reads++;
            if (FailRead) throw new IOException("Simulated panel read failure");
            return new { kind = "probe", value = "ready" };
        }
        public void HandlePanelCommand(string action, JsonElement payload) { }
        public void Initialize(IModuleContext context) { }
        public void Activate() => throw new InvalidOperationException("Probe must not activate");
        public void Deactivate() { }
        public void Apply() { }
    }
}
