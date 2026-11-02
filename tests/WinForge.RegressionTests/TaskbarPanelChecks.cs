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

internal static class TaskbarPanelChecks
{
    internal static void Run(ForgeApplication app, string directory, Action<bool, string> check)
    {
        using var store = new SettingsStore(Path.Combine(directory, "taskbar-panel.json"));
        using var logger = new FileLogger(Path.Combine(directory, "taskbar-panel.log"));
        var host = new ModuleHost(store, logger, new FakeHotkeys(), Path.Combine(AppContext.BaseDirectory, "modules"), _ => { });
        host.Load();
        var module = host.Modules.Single(module => module.Id == "taskbar");
        check(host.Modules.All(module => module.Id is not "shell-customization" and not "tray-icons"),
            "The real host exposes taskbar customization and tray settings as one module");
        SetAppProperty(app, nameof(ForgeApplication.Host), host);
        SetAppProperty(app, nameof(ForgeApplication.Logger), logger);
        SetAppProperty(app, nameof(ForgeApplication.Store), store);
        var profile = Path.GetFullPath(Path.Combine(directory, "taskbar-webview"));
        var window = new MainWindow(CoreWebView2Environment.CreateAsync(null, profile));
        Process? browser = null;
        try
        {
            window.Show();
            var web = (WebView2)window.FindName("UiWebView");
            WaitUntil(() => web.CoreWebView2 is not null, "WebView2 initialization");
            browser = Process.GetProcessById((int)web.CoreWebView2.BrowserProcessId);
            WaitUntil(() => Script("[...document.querySelectorAll('.tool-card h2, .module-card strong')].some(x => x.textContent === 'Görev Çubuğu')") == "true", "Taskbar module card");
            Script("(() => { const card = [...document.querySelectorAll('.tool-card, .module-card')].find(x => x.querySelector('h2, strong')?.textContent === 'Görev Çubuğu'); (card.querySelector('.tool-card-open') || card).click(); })()");
            WaitUntil(() => Script("document.querySelector('.details-title h2')?.textContent") == "\"Görev Çubuğu\"", "Taskbar panel selection");
            check(Script("['Bileşenler','Sistem simgeleri','Dock animasyonu','Saat','Bildirim alanı'].every(s => [...document.querySelectorAll('.settings-section h3')].some(x => x.textContent === s))") == "true",
                "The rendered React panel groups component, system icon, dock, clock and application-tray controls");
            check(Script("document.querySelectorAll('[role=switch][aria-label=\"Başlat düğmesini gizle\"]').length") == "1",
                "The real panel renders exactly one Start visibility switch");
            Script("document.querySelector('[role=switch][aria-label=\"Başlat düğmesini gizle\"]').click()");
            WaitUntil(() => host.SettingsOf(module).GetBool("hide-start"), "Start switch persistence");
            check(!module.IsActive && new SettingsStore(store.FilePath).For("taskbar")["hide-start"] == "true",
                "Start visibility saves through the native UI bridge while a disabled module leaves the actual taskbar unchanged");
            Script("const bell = document.querySelector('select[aria-label=\"Bildirim zili\"]'); bell.value = 'when-empty'; bell.dispatchEvent(new Event('change', {bubbles:true}));");
            WaitUntil(() => host.SettingsOf(module).GetString("hide-bell") == "when-empty" &&
                JsonSerializer.Deserialize<string>(Script("document.querySelector('select[aria-label=\"Bildirim zili\"]').value")) == "when-empty",
                "Bell persistence and rendered acknowledgement");
            var bellLabel = JsonSerializer.Deserialize<string>(Script("document.querySelector('select[aria-label=\"Bildirim zili\"] option:checked').textContent"));
            check(bellLabel == "Yeni bildirim yoksa gizle",
                "The notification bell offers localized labels and persists its conditional-hide mode");
            check(Script("document.querySelector('input[aria-label=\"Masaüstünü göster genişliği (0: Windows)\"]')?.max") == "\"100\"" &&
                  Script("document.querySelector('input[aria-label=\"Büyütme (%)\"]')?.max") == "\"160\"",
                "The rendered desktop-width and dock-scale inputs expose their validated limits");
            check(Script("document.querySelector('input[type=color][aria-label=\"Görev çubuğu arka plan rengi seçici\"]') !== null && document.querySelector('input[aria-label=\"Görev çubuğu arka plan opaklığı (%)\"]')?.max === '100'") == "true",
                "The taskbar panel renders a color picker and a zero-to-100 background opacity slider");
            Script("document.querySelector('[role=switch][aria-label=\"Özel görev çubuğu arka planı\"]').click()");
            WaitUntil(() => host.SettingsOf(module).GetBool("custom-background"), "Custom background switch persistence");
            Script("(() => { const input = document.querySelector('input[type=color][aria-label=\"Görev çubuğu arka plan rengi seçici\"]'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '#336699'); input.dispatchEvent(new Event('input', {bubbles:true})); })()");
            WaitUntil(() => host.SettingsOf(module).GetString("background-color") == "#336699", "Background color persistence");
            Script("(() => { const input = document.querySelector('input[aria-label=\"Görev çubuğu arka plan opaklığı (%)\"]'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '50'); input.dispatchEvent(new Event('input', {bubbles:true})); })()");
            WaitUntil(() => host.SettingsOf(module).GetInt("background-opacity") == 50, "Background opacity persistence");
            var savedBackground = new SettingsStore(store.FilePath).For("taskbar");
            check(savedBackground["custom-background"] == "true" && savedBackground["background-color"] == "#336699" && savedBackground["background-opacity"] == "50",
                "Background toggle, color and opacity persist through the rendered controls and native settings bridge");
            Script("[...document.querySelectorAll('.settings-section')].find(x => x.querySelector('h3')?.textContent === 'Tasarım').scrollIntoView()");
            var screenshotPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "taskbar-panel.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            using var screenshot = File.Create(screenshotPath);
            Complete(web.CoreWebView2!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot));

            string Script(string script)
            {
                var task = web.ExecuteScriptAsync(script);
                Complete(task);
                return task.GetAwaiter().GetResult();
            }
        }
        finally
        {
            var wasExiting = app.IsExiting;
            SetAppProperty(app, nameof(ForgeApplication.IsExiting), true);
            window.Close();
            if (browser is not null) { WaitUntil(() => browser.HasExited, "Owned taskbar-panel browser teardown"); browser.Dispose(); }
            if (Path.GetDirectoryName(profile) != Path.GetFullPath(directory)) throw new InvalidOperationException("Unexpected taskbar profile path.");
            WaitUntil(() =>
            {
                try { if (Directory.Exists(profile)) Directory.Delete(profile, true); return true; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }, "Owned taskbar-panel profile cleanup");
            SetAppProperty(app, nameof(ForgeApplication.IsExiting), wasExiting);
        }
    }

    private static void SetAppProperty(ForgeApplication app, string name, object value) =>
        typeof(ForgeApplication).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.SetValue(app, value);

    private static void Complete(Task task)
    {
        WaitUntil(() => task.IsCompleted, "WebView2 operation");
        task.GetAwaiter().GetResult();
    }

    private static void WaitUntil(Func<bool> predicate, string operation)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (predicate()) return;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Thread.Sleep(25);
        }
        throw new TimeoutException(operation);
    }
}
