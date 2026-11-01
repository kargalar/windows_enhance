using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class DynamicIslandChecks
{
    internal static void RunUi(Action<bool, string> check)
    {
        var serviceType = typeof(WinForge.App.App).Assembly.GetType("WinForge.App.ReactWindowHostService", true)!;
        using var service = (IDisposable)Activator.CreateInstance(serviceType)!;
        var context = new IslandContext { FeatureUiOverride = (IFeatureUiService)service };
        context.SettingsImpl.Values["hotkey"] = false;
        context.SettingsImpl.Values["hover"] = false;
        context.SettingsImpl.Values["motion"] = "spring";
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.DynamicIsland.dll"));
        var module = (ITweakModule)Activator.CreateInstance(assembly.GetType("WinForge.Modules.DynamicIsland.DynamicIslandModule", true)!)!;
        module.Initialize(context);
        try
        {
            module.Activate();
            var window = Application.Current.Windows.OfType<Window>().Single(item => item.Title == "WinForge · Dynamic Island");
            var web = (WebView2)window.Content;
            for (var attempt = 0; attempt < 100 && web.CoreWebView2 is null; attempt++) Pump(100);
            check(web.CoreWebView2 is not null, "Real WebView2 surface initializes");
            string Script(string source)
            {
                var task = web.ExecuteScriptAsync(source);
                for (var attempt = 0; attempt < 100 && !task.IsCompleted; attempt++) Pump(50);
                return task.GetAwaiter().GetResult();
            }
            for (var attempt = 0; attempt < 100 && Script("Boolean(document.querySelector('.dynamic-island'))") != "true"; attempt++) Pump(100);
            check(Script("Boolean(document.querySelector('.dynamic-island.collapsed'))") == "true", "Native island renders its initial collapsed React view");
            Script("document.querySelector('.island-summary-main').click()");
            Pump(650);
            check(window.Width > 400 && Script("Boolean(document.querySelector('.dynamic-island.expanded'))") == "true", "React expansion command animates the native window");
            Script("[...document.querySelectorAll('.island-tabs button')].find(x => x.textContent.includes('Odak')).click()");
            Pump(100);
            Script("document.querySelector('.island-focus button').click()");
            Pump(150);
            check(Script("document.querySelector('.island-focus-ring strong').textContent") == "\"1:00\"", "Focus button round-trips through the real native bridge");
            Script("document.querySelector('.island-focus button').click()");
            Pump(100);
            Script("[...document.querySelectorAll('.island-tabs button')].find(x => x.textContent.includes('Takvim')).click()");
            Pump(100);
            check(Script("document.querySelectorAll('.island-calendar-grid span[aria-current=date]').length") == "1", "Calendar marks exactly one current date in the native surface");
            Script("document.querySelector('.island-window-actions button').click()");
            Pump(100);
            check(Script("document.querySelector('.island-window-actions button').getAttribute('aria-pressed')") == "\"true\"", "Pin state returns to the React controls");
            var screenshotDirectory = Path.Combine(Directory.GetCurrentDirectory(), "output", "playwright");
            Directory.CreateDirectory(screenshotDirectory);
            using var screenshot = File.Create(Path.Combine(screenshotDirectory, "island-native.png"));
            var capture = web.CoreWebView2!.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            for (var attempt = 0; attempt < 100 && !capture.IsCompleted; attempt++) Pump(50);
            capture.GetAwaiter().GetResult();
        }
        finally { module.Deactivate(); }
    }

    internal static void Run(Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.DynamicIsland.dll"));
        var module = (ITweakModule)Activator.CreateInstance(assembly.GetType("WinForge.Modules.DynamicIsland.DynamicIslandModule", true)!)!;
        var context = new IslandContext();
        module.Initialize(context);
        try
        {
            check(!module.IsActive && module.Id == "dynamic-island", "Island is independently discoverable and initially disabled");
            check(module.Settings.Select(setting => setting.Key).Distinct().Count() == module.Settings.Count, "Island setting keys are unique");
            module.Apply();
            check(context.Ui.Surfaces.Count == 0, "Applying settings to a disabled island does not create a window");
            module.Activate();
            var surface = context.Ui.Surfaces.Single();
            var window = Application.Current.Windows.OfType<Window>().Single(item => item.Title == "WinForge · Dynamic Island");
            check(window.IsVisible && window.Topmost && !window.ShowInTaskbar && !window.ShowActivated, "Island is visible without activating or adding a taskbar button");
            check(context.HotkeysImpl.Registrations == 1, "Island registers its visibility shortcut exactly once");
            module.Activate();
            check(context.Ui.Surfaces.Count == 1, "Repeated activation does not duplicate the island");
            var compactWidth = window.Width;
            var compactCenter = window.Left + window.Width / 2;
            surface.Send("island.expand");
            check(surface.State.GetProperty("expanded").GetBoolean() && window.Width > compactWidth, "Expanding opens the dashboard");
            check(Math.Abs(window.Left + window.Width / 2 - compactCenter) < 1, "Expansion preserves the screen-center anchor");
            surface.Send("island.pin");
            surface.Send("island.hover", new { value = false });
            Pump(550);
            check(surface.State.GetProperty("expanded").GetBoolean() && surface.State.GetProperty("pinned").GetBoolean(), "Pinned island remains expanded after pointer leaves");
            surface.Send("island.pin");
            surface.Send("island.hover", new { value = false });
            Pump(550);
            check(!surface.State.GetProperty("expanded").GetBoolean() && window.Width == compactWidth, "Unpinned island collapses after the hover delay");
            surface.Send("island.focus");
            check(surface.State.GetProperty("focusRemaining").GetInt32() == 60, "Focus timer starts with the configured duration");
            surface.Send("island.hide");
            check(!window.IsVisible && surface.State.GetProperty("focusRemaining").GetInt32() > 0, "Hiding preserves the running focus timer");
            context.HotkeysImpl.Callback!();
            check(window.IsVisible, "Shortcut reveals a hidden island");
            surface.Send("island.focus");
            check(surface.State.GetProperty("focusRemaining").GetInt32() == 0, "Stopping focus clears the countdown");
            context.SettingsImpl.Values["position"] = "bottom";
            context.SettingsImpl.Values["scale"] = 140;
            module.Apply();
            var bottom = window.Top + window.Height;
            surface.Send("island.expand");
            check(Math.Abs(window.Top + window.Height - bottom) < 1, "Bottom placement stays anchored while expanding");
            check(surface.State.GetProperty("scale").GetDouble() == 1.4, "Size settings reach the shared surface");
            CheckDownloads(module, context, surface, check);
            context.SettingsImpl.Values["hotkey"] = false;
            module.Apply();
            check(context.HotkeysImpl.Registrations == 0, "Disabling the shortcut releases its registration");
            module.Deactivate();
            check(!module.IsActive && surface.Disposed && !window.IsVisible, "Deactivation closes the overlay and disposes its surface");
            var publications = surface.Publications;
            Pump(1100);
            check(surface.Publications == publications, "No timer publishes after island deactivation");
            module.Deactivate();
            module.Activate();
            check(context.Ui.Surfaces.Count == 2 && module.IsActive, "Island can be enabled again after cleanup");
        }
        finally { module.Deactivate(); }
    }

    private static void CheckDownloads(ITweakModule module, IslandContext context, IslandSurface surface, Action<bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("WinForge.Island.");
        var partial = Path.Combine(directory.FullName, "sample.crdownload");
        var final = Path.Combine(directory.FullName, "sample.txt");
        try
        {
            context.SettingsImpl.Values["downloads"] = true;
            context.SettingsImpl.Values["downloads-folder"] = directory.FullName;
            module.Apply();
            File.WriteAllText(partial, "island fixture");
            Pump(250);
            check(surface.State.GetProperty("downloads").GetArrayLength() == 0, "Temporary browser download files are excluded");
            File.Move(partial, final);
            Pump(300);
            check(surface.State.GetProperty("downloads").GetArrayLength() == 1 && surface.State.GetProperty("alertKind").GetString() == "download", "Renaming a completed download adds its reference and announces it");
            context.SettingsImpl.Values["downloads"] = false;
            module.Apply();
            check(surface.State.GetProperty("downloads").GetArrayLength() == 0, "Disabling download monitoring clears its session references");
        }
        finally
        {
            context.SettingsImpl.Values["downloads"] = false;
            module.Apply();
            File.Delete(partial);
            File.Delete(final);
            directory.Delete();
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class IslandContext : IModuleContext, IFeatureUiContext
    {
        internal IslandSettings SettingsImpl { get; } = new();
        internal IslandHotkeys HotkeysImpl { get; } = new();
        internal IslandUi Ui { get; } = new();
        internal IFeatureUiService? FeatureUiOverride { get; init; }
        public IModuleSettings Settings => SettingsImpl;
        public ILogger Log { get; } = new IslandLog();
        public IHotkeyService Hotkeys => HotkeysImpl;
        public IFeatureUiService FeatureUi => FeatureUiOverride ?? Ui;
        public void Notify(string message) { }
    }

    private sealed class IslandSettings : IModuleSettings
    {
        internal Dictionary<string, object> Values { get; } = new()
        {
            ["media"] = false,
            ["volume"] = false,
            ["power"] = false,
            ["keys"] = false,
            ["usb"] = false,
            ["clipboard"] = false,
            ["downloads"] = false,
            ["fullscreen"] = false,
            ["motion"] = "off",
            ["focus-minutes"] = 1
        };
        public bool GetBool(string key, bool fallback = false) => Values.TryGetValue(key, out var value) ? (bool)value : fallback;
        public int GetInt(string key, int fallback = 0) => Values.TryGetValue(key, out var value) ? (int)value : fallback;
        public string GetString(string key, string fallback = "") => Values.TryGetValue(key, out var value) ? (string)value : fallback;
        public void Set(string key, object? value) { if (value is not null) Values[key] = value; }
    }

    private sealed class IslandHotkeys : IHotkeyService
    {
        public int Registrations { get; private set; }
        public Action? Callback { get; private set; }
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback) { Registrations++; Callback = callback; return new Lease(() => { Registrations--; Callback = null; }); }
        private sealed class Lease(Action release) : IDisposable
        {
            private bool _disposed;
            public void Dispose() { if (!_disposed) { _disposed = true; release(); } }
        }
    }

    private sealed class IslandLog : ILogger
    {
        public string Path => "";
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) => throw new InvalidOperationException(message, exception);
    }

    private sealed class IslandUi : IFeatureUiService
    {
        internal List<IslandSurface> Surfaces { get; } = [];
        public IFeatureUiSurface CreateSurface(string view) { var surface = new IslandSurface(); Surfaces.Add(surface); return surface; }
        public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel") => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel = "Close") => throw new NotSupportedException();
    }

    private sealed class IslandSurface : IFeatureUiSurface
    {
        public FrameworkElement Element { get; } = new Grid();
        public event Action<string, JsonElement>? MessageReceived;
        internal JsonElement State { get; private set; }
        internal int Publications { get; private set; }
        internal bool Disposed { get; private set; }
        public void SetState(object state) { State = JsonSerializer.SerializeToElement(state); Publications++; }
        public void Dispose() => Disposed = true;
        internal void Send(string command, object? payload = null) => MessageReceived?.Invoke(command, JsonSerializer.SerializeToElement(payload ?? new { }));
    }
}
