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
    internal static void Run(ForgeApplication app, string directory, Action<bool, string> check, bool contextOnly = false)
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
            Wait(() => Script("[...document.querySelectorAll('.tool-card h2, .module-card strong')].some(x => x.textContent === 'Sağ Tık Menüsü')") == "true", "Context menu card");
            Script("(() => { if (document.querySelector('.context-menu-panel')) return; const card = [...document.querySelectorAll('.tool-card, .module-card')].find(x => x.querySelector('h2, strong')?.textContent === 'Sağ Tık Menüsü'); (card.querySelector('.tool-settings-button') || card).click(); })()");
            Wait(() => Script("!!document.querySelector('.context-menu-panel')") == "true", "Context menu editor");
            check(Script("document.querySelectorAll('.context-entry-icon img').length > 0") == "true", "Real UI renders existing item icons");
            check(Script("!!document.querySelector('[data-entry-id=\"builtin:properties\"]') && !!document.querySelector('[data-entry-id=\"builtin:new-folder\"]')") == "true", "Real UI includes Properties and New Folder");
            check(Script("document.querySelector('[data-entry-id=\"builtin:properties\"]').querySelectorAll('.context-locations [role=switch]').length === 8") == "true", "Real menu editor includes shortcut, application and virtual item switches");
            check(Script("document.querySelectorAll('.context-options [role=switch]').length === 3") == "true", "Real menu editor exposes separator cleanup, separator visibility and unquoted path options");
            Script("document.querySelector('[aria-label=\"Yol olarak kopyala: Tırnak işaretlerini kaldır\"]').click()");
            Wait(() => host.SettingsOf(module).GetString("unquoted-copy-path").Length > 0 && !host.SettingsOf(module).GetBool("unquoted-copy-path", true), "Unquoted path preference persistence");
            check(true, "Real UI persists the path-copy preference without changing Explorer during the test");
            Script("[...document.querySelectorAll('.context-groups button')].find(b => b.textContent === 'Grup oluştur').click()");
            Wait(() => Script("!!document.querySelector('.context-group-editor')") == "true", "Group creation form");
            Script("(() => { const input = document.querySelector('[aria-label=\"Grup adı\"]'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'Diğer'); input.dispatchEvent(new Event('input', { bubbles: true })); })()");
            Script("document.querySelector('[data-group-entry-id=\"builtin:properties\"] input').click()");
            Wait(() => Script("document.querySelectorAll('.context-group-choices input:checked').length") == "1", "First group member selection");
            Script("document.querySelector('[data-group-entry-id=\"builtin:new-folder\"] input').click()");
            Wait(() => Script("document.querySelectorAll('.context-group-choices input:checked').length") == "2", "Second group member selection");
            Script("(() => { const input = document.querySelectorAll('.context-group-editor > label input')[1]; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'shell32.dll,3'); input.dispatchEvent(new Event('input', { bubbles: true })); })()");
            Script("document.querySelector('.context-group-editor').requestSubmit()");
            Wait(() => SavedGroup("builtin:properties") && SavedGroup("builtin:new-folder"), "Group member persistence");
            check(JsonDocument.Parse(host.SettingsOf(module).GetString("menu-rules")).RootElement.EnumerateArray().Count(r => r.GetProperty("group").GetString() == "Diğer") >= 2,
                "Real React/native bridge automatically saves both built-in items in one group");
            Wait(() => Script("!!document.querySelector('.context-group-title img')") == "true", "Saved group icon preview");
            using (var icons = JsonDocument.Parse(host.SettingsOf(module).GetString("menu-group-icons", "{}")))
                check(icons.RootElement.EnumerateObject().Single().Value.GetString() == "shell32.dll,3", "Group icon source persists through the React/native bridge");
            Script("document.querySelectorAll('[data-entry-id=\"builtin:properties\"] [role=switch]')[3].click()");
            Wait(() => JsonDocument.Parse(host.SettingsOf(module).GetString("menu-rules")).RootElement.EnumerateArray().Any(r => r.GetProperty("id").GetString() == "builtin:properties" && r.TryGetProperty("contexts", out var scopes) && scopes.ValueKind == JsonValueKind.Object && !scopes.GetProperty("desktop").GetBoolean()), "Desktop visibility switch persistence");
            check(Script("document.querySelectorAll('[data-entry-id=\"builtin:properties\"] [role=switch]')[1].getAttribute('aria-checked') === 'true'") == "true", "Folder visibility stays on independently");
            check(SavedGroup("builtin:properties") && SavedGroup("builtin:new-folder"), "Visibility changes preserve group membership");
            Script("[...document.querySelectorAll('.context-toolbar button')].find(b => b.textContent === 'Tümünü kapat').click()");
            Wait(() => Script("[...document.querySelectorAll('.context-entry [role=switch]')].every(b => b.getAttribute('aria-checked') === 'false')") == "true", "Batch hide acknowledgement");
            using (var hidden = JsonDocument.Parse(host.SettingsOf(module).GetString("menu-rules")))
                check(hidden.RootElement.EnumerateArray().All(r => !r.GetProperty("visible").GetBoolean() && r.GetProperty("contexts").EnumerateObject().All(scope => !scope.Value.GetBoolean())), "Batch hide persists every location through the native bridge");
            check(SavedGroup("builtin:properties") && SavedGroup("builtin:new-folder"), "Batch visibility preserves group membership");
            Script("[...document.querySelectorAll('.context-toolbar button')].find(b => b.textContent === 'Tümünü aç').click()");
            Wait(() => Script("[...document.querySelectorAll('.context-entry [role=switch]')].every(b => b.getAttribute('aria-checked') === 'true')") == "true", "Batch show acknowledgement");
            using (var shown = JsonDocument.Parse(host.SettingsOf(module).GetString("menu-rules")))
                check(shown.RootElement.EnumerateArray().All(r => r.GetProperty("visible").GetBoolean() && r.GetProperty("contexts").EnumerateObject().All(scope => scope.Value.GetBoolean())), "Batch show persists every location through the native bridge");
            Script("document.querySelector('.context-preview summary').click()");
            check(Script("document.querySelector('.context-preview').textContent.includes('Diğer')") == "true", "Menu preview reflects group");
            check(Script("![...document.querySelectorAll('.context-menu-panel button')].some(b => b.textContent === 'Kaydet')") == "true", "No Save button required");
            if (!contextOnly)
            {
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
            }
            Script("(() => { if (document.querySelector('.context-menu-panel')) return; const card = [...document.querySelectorAll('.tool-card, .module-card')].find(x => x.querySelector('h2, strong')?.textContent === 'Sağ Tık Menüsü'); (card.querySelector('.tool-settings-button') || card).click(); })()");
            Wait(() => Script("!!document.querySelector('.context-menu-panel')") == "true", "Return to context menu editor");
            check(Script("[...document.querySelectorAll('[data-entry-id]')].filter(e => e.dataset.entryId.startsWith('prepared:')).length === 3") == "true", "Prepared desktop shortcut, visibility and folder icon actions are available in real UI");
            var panel = (IModulePanel)module;
            panel.HandlePanelCommand("save-group", JsonSerializer.SerializeToElement(new { name = "Icon test", previousName = "", ids = Array.Empty<string>(), icon = "shell32.dll,3" }));
            panel.HandlePanelCommand("save-group", JsonSerializer.SerializeToElement(new { name = "Icon renamed", previousName = "Icon test", ids = Array.Empty<string>() }));
            using (var icons = JsonDocument.Parse(host.SettingsOf(module).GetString("menu-group-icons")))
                check(!icons.RootElement.TryGetProperty("Icon test", out _) && icons.RootElement.GetProperty("Icon renamed").GetString() == "shell32.dll,3", "Renaming moves the group icon without changing its source");
            panel.HandlePanelCommand("clear-group-icon", JsonSerializer.SerializeToElement(new { name = "Icon renamed" }));
            using (var icons = JsonDocument.Parse(host.SettingsOf(module).GetString("menu-group-icons")))
                check(!icons.RootElement.TryGetProperty("Icon renamed", out _), "Clearing a group icon removes its persisted assignment");
            panel.HandlePanelCommand("save-group", JsonSerializer.SerializeToElement(new { name = "Icon renamed", previousName = "Icon renamed", ids = Array.Empty<string>(), icon = "shell32.dll,3" }));
            panel.HandlePanelCommand("delete-group", JsonSerializer.SerializeToElement(new { name = "Icon renamed" }));
            using (var icons = JsonDocument.Parse(host.SettingsOf(module).GetString("menu-group-icons")))
                check(!icons.RootElement.TryGetProperty("Icon renamed", out _), "Deleting a group also removes its icon assignment");
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
