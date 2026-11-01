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
            foreach (var id in new[] { "default-paths", "default-apps" })
            {
                Script("[...document.querySelectorAll('.module-card')].find(x => x.querySelector('strong')?.textContent === " + JsonSerializer.Serialize(host.Modules.Single(m => m.Id == id).Name) + ").click()");
                Wait(() => Script("!!document.querySelector('.defaults-panel')") == "true", "Inline default panel");
                check(Script("document.querySelectorAll('.defaults-list article').length > 0") == "true", id + " renders populated settings inside main window");
                check(Script("![...document.querySelectorAll('.defaults-panel button')].some(b => b.textContent === 'Kapat')") == "true", id + " has no separate-window close action");
                var artifact = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", id + "-inline.png");
                Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
                using var capture = File.Create(artifact);
                Complete(web.CoreWebView2!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture));
            }
            var start = host.Modules.Single(m => m.Id == "start-menu");
            foreach (var key in new[] { "hide-recent", "hide-categories", "hide-all-apps-heading", "hide-places", "hide-user", "hide-power" })
                check(start.Settings.Any(setting => setting.Key == key && setting.Kind == SettingKind.Toggle), "Start section has independent toggle: " + key);
            var appearance = start.GetType().GetField("_appearance", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(start)!;
            var startType = appearance.GetType();
            host.SettingsOf(start).Set("hide-recent", true);
            host.SettingsOf(start).Set("hide-categories", true);
            var startSettings = startType.GetMethod("ReadSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(appearance, null);
            var rules = (IReadOnlyList<ShellStyleRule>)startType.GetMethod("BuildRules", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [startSettings, true])!;
            check(rules.Any(rule => rule.Selector.Contains("CategoryControl")) && rules.Any(rule => rule.Selector.Contains("TopLevelSuggestionsRoot")), "Recent and Categories toggles generate independent native shell rules");
            Script("[...document.querySelectorAll('.module-card')].find(x => x.querySelector('strong')?.textContent === 'Başlat Menüsü').click()");
            Wait(() => Script("document.body.textContent.includes('Başlat temizliği etkin') && document.body.textContent.includes('Arama temizliği etkin')") == "true", "Combined Start Menu settings");
            check(Script("document.body.textContent.includes('Menü genişliği') && document.body.textContent.includes('Web (Bing) sonuçlarını kapat') && document.body.textContent.includes('Yeni uygulama bildirimlerini gizle')") == "true",
                "Real Start Menu panel combines appearance, cleanup and search settings");
            Script("[...document.querySelectorAll('.module-card')].find(x => x.querySelector('strong')?.textContent === 'Fare').click()");
            Wait(() => Script("document.body.textContent.includes('Fare ayarları etkin') && document.body.textContent.includes('Fare işaretçisi etkin')") == "true", "Combined Mouse settings");
            check(Script("document.body.textContent.includes('Tekerlek kaydırma satırı') && document.body.textContent.includes('Çift Ctrl ile fareyi vurgula')") == "true",
                "Real Mouse panel combines behavior and pointer settings");
            check(Script("![...document.querySelectorAll('.module-card strong')].some(x => ['Ekranlar Arası Fare Taşıma', 'Başlat Menüsü Tasarımı', 'Başlat Menüsü Temizliği', 'Arama Temizliği', 'Fare Ayarları', 'Fare İşaretçisi'].includes(x.textContent))") == "true",
                "Real UI contains no retired module cards");
            check(Script("document.querySelectorAll('.module-family').length > 1") == "true", "Related modules appear in nested families");
            Script("[...document.querySelectorAll('.module-card')].find(x => x.querySelector('strong')?.textContent === 'Sağ Tık Menüsü').click()");
            Wait(() => Script("!!document.querySelector('.context-menu-panel')") == "true", "Return to context menu editor");
            check(Script("[...document.querySelectorAll('[data-entry-id]')].filter(e => e.dataset.entryId.startsWith('prepared:')).length === 2") == "true", "Prepared desktop shortcut and visibility actions are available in real UI");
            check(!module.IsActive, "UI test does not change real Explorer menu");
            Script("document.querySelector('.context-preview').open = false; document.querySelector('.context-menu-panel').scrollIntoView()");
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
