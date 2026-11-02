using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinForge.App;
using WinForge.Core;
using ForgeApplication = WinForge.App.App;

namespace WinForge.RegressionTests;

internal static class DashboardInterfaceChecks
{
    internal static void Run(ForgeApplication app, string directory, Action<bool, string> check)
    {
        using var store = new SettingsStore(Path.Combine(directory, "dashboard-interface.json")) { DeferWrites = true };
        using var logger = new FileLogger(Path.Combine(directory, "dashboard-interface.log"));
        var plugins = Path.Combine(directory, "dashboard-plugins");
        Directory.CreateDirectory(plugins);
        File.Copy(typeof(FixtureModule).Assembly.Location, Path.Combine(plugins, "WinForge.Modules.Fixture.dll"), true);
        var host = new ModuleHost(store, logger, new NoHotkeys(), Path.Combine(AppContext.BaseDirectory, "modules"), _ => { });
        host.Load();
        var fixtureHost = new ModuleHost(store, logger, new NoHotkeys(), plugins, _ => { });
        fixtureHost.Load();
        var fixture = fixtureHost.Modules.Single();
        ((List<ITweakModule>)typeof(ModuleHost).GetField("_modules", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).Add(fixture);
        ((Dictionary<string, IModuleSettings>)typeof(ModuleHost).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).Add(fixture.Id, fixtureHost.SettingsOf(fixture));
        var previousHost = app.Host; var previousStore = app.Store; var previousLogger = app.Logger;
        var originalTone = Appearance.ToneName; var originalAccent = Appearance.AccentName;
        Set(nameof(ForgeApplication.Host), host); Set(nameof(ForgeApplication.Store), store); Set(nameof(ForgeApplication.Logger), logger);
        var profile = Path.GetFullPath(Path.Combine(directory, "dashboard-interface-webview"));
        var window = new MainWindow(CoreWebView2Environment.CreateAsync(null, profile));
        Process? browser = null;
        try
        {
            window.Show();
            var web = (WebView2)window.FindName("UiWebView");
            Wait(() => web.CoreWebView2 is not null, "Dashboard interface controller");
            var core = web.CoreWebView2;
            browser = Process.GetProcessById((int)core.BrowserProcessId);
            Wait(() => Script("document.querySelectorAll('.tool-card').length") == host.Modules.Count.ToString(), "Full rendered catalog");
            check(Script("document.querySelector('#module-details') === null") == "true", "Catalog initially keeps details closed without a stale selection");
            check(Script("document.querySelectorAll('.dashboard-navigation button').length") == "8", "All current navigation routes render");
            Input("#module-search", "   TASKBAR   ");
            Wait(() => Script("document.querySelectorAll('.tool-card').length > 0") == "true", "Trimmed category search");
            var taskbarNames = host.Modules.Where(module => module.Category == "Taskbar").Select(module => module.Name).ToArray();
            check(Script("[...document.querySelectorAll('.tool-card h2')].every(item=>" + JsonSerializer.Serialize(taskbarNames) + ".includes(item.textContent))") == "true",
                "Search trims whitespace and matches module categories case-insensitively");
            Input("#module-search", "no-such-module-123456");
            Wait(() => Script("document.querySelectorAll('.tool-card').length") == "0", "Empty search");
            check(Script("!!document.querySelector('.dashboard-empty') && !document.querySelector('#module-details')") == "true", "Empty results clear stale details and show recovery");
            Script("document.querySelector('.dashboard-empty button').click()");
            Wait(() => Script("document.querySelectorAll('.tool-card').length") == host.Modules.Count.ToString(), "Filter reset");
            check(Script("document.querySelector('#module-search').value") == "\"\"", "Reset restores the complete catalog and clears search");
            foreach (var module in host.Modules)
            {
                Open(module);
                Wait(() => Text("document.querySelector('#module-details h2')?.textContent") == module.Name, "Module details " + module.Id);
                check(Script("!!document.querySelector('.details-description') && !!document.querySelector('.details-header [role=switch]')") == "true", "Settings render: " + module.Name);
                foreach (var setting in module.Settings)
                    check(Script("[...document.querySelectorAll('#module-details .setting-copy strong')].some(item=>item.textContent===" + JsonSerializer.Serialize(setting.Label) + ")") == "true", "Setting label renders: " + module.Id + "/" + setting.Key);
            }
            var windowControl = host.Modules.Single(module => module.Id == "window-control");
            Open(windowControl);
            var leftAction = windowControl.Settings.Single(setting => setting.Key == "LMB");
            Select(leftAction.Label, "Ortala", leftAction.Section);
            Wait(() => host.SettingsOf(windowControl).GetString("LMB") == "Ortala", "Window action persistence");
            check(!windowControl.IsActive, "Editing the native window action persists without activating the real engine");
            Open(host.Modules.Single(module => module.Id == "net-speed"));
            Script("document.querySelector('.details-scroll').scrollTop=100000");
            check(Script("document.querySelector('.details-scroll').scrollTop > 0") == "true", "Long settings scroll to their final options");
            Open(fixture);
            check(Script("document.querySelector('.details-scroll').scrollTop") == "0", "Changing the selected module resets detail scrolling");
            var settings = host.SettingsOf(fixture);
            Script("document.querySelector('.details-header [role=switch]').click()");
            Wait(() => fixture.IsActive && Script(Card(fixture) + ".classList.contains('is-enabled')") == "true", "Fixture activation acknowledgement");
            check(true, "Detail switch updates the module and its catalog card");
            Script("document.querySelector('[aria-label=\"Test toggle\"]').click()");
            Wait(() => settings.GetBool("toggle"), "Toggle setting");
            check(true, "Toggle setting persists through the rendered control");
            Input("[aria-label=\"Test number\"]", "7");
            Wait(() => settings.GetInt("number") == 7, "Number setting");
            check(true, "Numeric setting persists through automatic save");
            Input("[aria-label=\"Test text\"]", "  saved text  ");
            Script("document.querySelector('[aria-label=\"Test text\"]').dispatchEvent(new FocusEvent('focusout',{bubbles:true}))");
            Wait(() => settings.GetString("text") == "saved text", "Text setting");
            check(true, "Text setting trims and persists on focus loss");
            Select("Test choice", "Two"); Select("Test labeled choice", "21996");
            Wait(() => settings.GetString("choice") == "Two" && settings.GetString("labeled-choice") == "21996" &&
                Script("document.querySelector('[aria-label=\"Test labeled choice\"] option:checked').value") == "\"21996\"", "Choice acknowledgement");
            check(JsonSerializer.Deserialize<string>(Script("document.querySelector('[aria-label=\"Test labeled choice\"] option:checked').textContent")) == "SunValley (Legacy)", "Choices display friendly names while persisting underlying values");
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            Pump();
            check(Script("document.documentElement.scrollWidth <= window.innerWidth && document.querySelector('.dashboard-settings').getBoundingClientRect().width > 300") == "true", "Minimum window size keeps settings usable without page overflow");
            check(Script("[...document.querySelectorAll('.tool-card .toggle')].every(button=>button.getBoundingClientRect().right <= window.innerWidth)") == "true", "Catalog switches remain within the minimum-width viewport");
            Script("document.querySelector('[aria-label=\"Uygulama ayarları\"]').click()");
            Wait(() => Script("!!document.querySelector('[role=dialog]')") == "true", "Appearance dialog");
            check(true, "Appearance opens from the toolbar as a modal dialog");
            foreach (var tone in Appearance.Tones)
            foreach (var accent in Appearance.Accents)
            {
                Script("window.chrome.webview.postMessage(" + JsonSerializer.Serialize(new { command = "appearance.update", payload = new { tone = tone.Name, accent = accent.Name } }) + ")");
                Wait(() => Appearance.ToneName == tone.Name && Appearance.AccentName == accent.Name &&
                    Text("document.querySelector('.tone-option.selected').textContent") == (tone.Name == "Siyah" ? "Karanlık" : tone.Name), "Theme acknowledgement");
                var colorTask = core.ExecuteScriptAsync("(() => {const root=document.querySelector('.dashboard'),panel=document.querySelector('.details-scroll'),category=panel.querySelector('.details-category'),toggle=panel.querySelector('.details-header .toggle');const rgb=color=>{const e=document.createElement('i');e.style.color=color;document.body.append(e);const a=getComputedStyle(e).color.match(/[0-9.]+/g).slice(0,3).map(Number);e.remove();return a.map(v=>{v/=255;return v<=.04045?v/12.92:Math.pow((v+.055)/1.055,2.4)}).reduce((s,v,i)=>s+v*[.2126,.7152,.0722][i],0)};const contrast=(a,b)=>{a=rgb(a);b=rgb(b);return(Math.max(a,b)+.05)/(Math.min(a,b)+.05)};return{muted:contrast(getComputedStyle(category).color,getComputedStyle(panel).backgroundColor),accent:contrast(getComputedStyle(root).getPropertyValue('--accent'),getComputedStyle(panel).backgroundColor),switchRatio:contrast(getComputedStyle(toggle.firstElementChild).backgroundColor,getComputedStyle(toggle).backgroundColor)}})()");
                Complete(colorTask);
                using var colors = JsonDocument.Parse(colorTask.Result);
                check(colors.RootElement.GetProperty("muted").GetDouble() >= 4.5 && colors.RootElement.GetProperty("accent").GetDouble() >= 4.5,
                    "Rendered text contrast: " + tone.Name + "/" + accent.Name);
                check(colors.RootElement.GetProperty("switchRatio").GetDouble() >= 3, "Rendered switch contrast: " + tone.Name + "/" + accent.Name);
            }
            Script("document.querySelector('.appearance-dialog [aria-label=\"Kapat\"]').click()");
            Script(Card(fixture) + ".querySelector('.toggle').click()");
            Wait(() => !fixture.IsActive && Script(Card(fixture) + ".classList.contains('is-disabled')") == "true", "Catalog deactivation acknowledgement");
            check(!((FixtureModule)fixture).ResourceAllocated, "Catalog switch releases the fixture module resources");

            string Script(string code) { var task = core.ExecuteScriptAsync(code); Complete(task); return task.Result; }
            string? Text(string code) => JsonSerializer.Deserialize<string>(Script(code));
            void Input(string selector, string value) => Script("(()=>{const input=document.querySelector(" + JsonSerializer.Serialize(selector) + ");Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input," + JsonSerializer.Serialize(value) + ");input.dispatchEvent(new Event('input',{bubbles:true}));})()");
            void Select(string label, string value, string? section = null) => Script("(()=>{const input=[...document.querySelectorAll('select[aria-label='+JSON.stringify(" + JsonSerializer.Serialize(label) + ")+']')].find(input=>!" + JsonSerializer.Serialize(section) + "||input.closest('.settings-section').querySelector('h3').textContent===" + JsonSerializer.Serialize(section) + ");input.value=" + JsonSerializer.Serialize(value) + ";input.dispatchEvent(new Event('change',{bubbles:true}));})()");
            string Card(ITweakModule module) => "[...document.querySelectorAll('.tool-card')].find(card=>card.querySelector('h2')?.textContent===" + JsonSerializer.Serialize(module.Name) + ")";
            void Open(ITweakModule module) { Script(Card(module) + ".querySelector('.tool-card-open').click()"); Wait(() => Text("document.querySelector('#module-details h2')?.textContent") == module.Name, "Open " + module.Id); }
        }
        finally
        {
            var wasExiting = app.IsExiting;
            Set(nameof(ForgeApplication.IsExiting), true); window.Close();
            if (browser is not null) { Wait(() => browser.HasExited, "Interface browser teardown"); browser.Dispose(); }
            if (Path.GetDirectoryName(profile) != Path.GetFullPath(directory)) throw new InvalidOperationException("Unexpected interface profile path");
            Wait(() => { try { if (Directory.Exists(profile)) Directory.Delete(profile, true); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }, "Interface profile cleanup");
            host.StopAll(); Appearance.SetTone(originalTone); Appearance.SetAccent(originalAccent);
            Set(nameof(ForgeApplication.Host), previousHost); Set(nameof(ForgeApplication.Store), previousStore); Set(nameof(ForgeApplication.Logger), previousLogger); Set(nameof(ForgeApplication.IsExiting), wasExiting);
        }
        void Set(string name, object? value) => typeof(ForgeApplication).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(app, value);
    }
    private static void Complete(Task task) { Wait(() => task.IsCompleted, "Interface script"); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> predicate, string label) { var watch = Stopwatch.StartNew(); while (watch.Elapsed < TimeSpan.FromSeconds(20)) { if (predicate()) return; Pump(); } throw new TimeoutException(label); }
    private static void Pump() { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    private sealed class NoHotkeys : IHotkeyService { public IDisposable Register(uint modifiers, uint key, Action callback) => throw new InvalidOperationException("Real input modules must stay inactive in the interface fixture."); }
}
