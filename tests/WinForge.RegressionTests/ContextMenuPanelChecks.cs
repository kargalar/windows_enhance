using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinForge.App;
using ForgeApplication = WinForge.App.App;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class ContextMenuPanelChecks
{
    internal static void Run(ForgeApplication app, string directory, Action<bool, string> check)
    {
        var store = new SettingsStore(Path.Combine(directory, "context-menu-panel.json"));
        var logger = new FileLogger(Path.Combine(directory, "context-menu-panel.log"));
        var host = new ModuleHost(store, logger, new Hotkeys(), Path.Combine(AppContext.BaseDirectory, "modules"), _ => { }); host.Load();
        var module = host.Modules.Single(m => m.Id == "context-menu");
        Set(app, nameof(ForgeApplication.Host), host); Set(app, nameof(ForgeApplication.Logger), logger);
        var environment = CoreWebView2Environment.CreateAsync(null, Path.Combine(AppData.DirectoryPath, "WebView2"));
        var window = new MainWindow(environment);
        try
        {
            window.Show(); var web = (WebView2)window.FindName("UiWebView");
            Wait(() => web.CoreWebView2 is not null, "WebView initialization");
            Wait(() => Script("[...document.querySelectorAll('.module-card strong')].some(x => x.textContent === 'Sağ Tık Menüsü')") == "true", "Context menu card");
            Script("[...document.querySelectorAll('.module-card')].find(x => x.querySelector('strong')?.textContent === 'Sağ Tık Menüsü').click()");
            Wait(() => Script("!!document.querySelector('.context-menu-panel')") == "true", "Context menu editor");
            check(Script("document.querySelectorAll('.context-entry-icon img').length > 0") == "true", "Real UI renders existing item icons");
            check(Script("!!document.querySelector('[data-entry-id=\"builtin:properties\"]') && !!document.querySelector('[data-entry-id=\"builtin:new-folder\"]')") == "true", "Real UI includes Properties and New Folder");
            foreach (var id in new[] { "builtin:properties", "builtin:new-folder" })
            {
                Script("(() => { const input = document.querySelector('[data-entry-id=\"" + id + "\"] input[list]'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'Diğer'); input.dispatchEvent(new Event('input', { bubbles: true })); })()");
                Wait(() => SavedGroup(id), "Automatic group persistence: " + id);
            }
            check(JsonDocument.Parse(host.SettingsOf(module).GetString("menu-rules")).RootElement.EnumerateArray().Count(r => r.GetProperty("group").GetString() == "Diğer") == 2,
                "Real React/native bridge automatically saves both built-in items in one group");
            Script("document.querySelector('.context-preview summary').click()");
            check(Script("document.querySelector('.context-preview').textContent.includes('Diğer')") == "true", "Menu preview reflects group");
            check(Script("![...document.querySelectorAll('.context-menu-panel button')].some(b => b.textContent === 'Kaydet')") == "true", "No Save button required");
            check(!module.IsActive, "UI test does not change real Explorer menu");
            Script("document.querySelector('.context-preview summary').click(); document.querySelector('.context-menu-panel').scrollIntoView()");
            var screenshot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "context-menu-panel.png"); Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            using var output = File.Create(screenshot); Complete(web.CoreWebView2!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output));
            bool SavedGroup(string id)
            {
                using var data = JsonDocument.Parse(host.SettingsOf(module).GetString("menu-rules", "[]"));
                return data.RootElement.EnumerateArray().Any(r => r.GetProperty("id").GetString() == id && r.GetProperty("group").GetString() == "Diğer");
            }
            string Script(string code) { var task = web.ExecuteScriptAsync(code); Complete(task); return task.GetAwaiter().GetResult(); }
        }
        finally { var exiting = app.IsExiting; Set(app, nameof(ForgeApplication.IsExiting), true); window.Close(); Set(app, nameof(ForgeApplication.IsExiting), exiting); }
    }
    private static void Set(ForgeApplication app, string name, object value) => typeof(ForgeApplication).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.SetValue(app, value);
    private static void Complete(Task task) { Wait(() => task.IsCompleted, "WebView operation"); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> predicate, string label)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (predicate()) return;
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        throw new TimeoutException(label);
    }
    private sealed class Hotkeys : IHotkeyService { public IDisposable Register(uint modifiers, uint key, Action action) => new Lease(); private sealed class Lease : IDisposable { public void Dispose() { } } }
}
