using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using WinForge.App;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class NetSpeedVisibilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckPlacementCadence(check);
        var serviceType = typeof(OsdWindow).Assembly.GetType("WinForge.App.ReactWindowHostService")!;
        using var lifetime = (IDisposable)Activator.CreateInstance(serviceType, nonPublic: true)!;
        var service = (IFeatureUiService)lifetime;
        var panel = service.Open("net-speed-visibility-check", "net-speed-overlay", "Network speed check",
            new(220, 38, Topmost: true, ActivateOnShow: false, Transparent: true,
                ClickThrough: true, ShowOnOpen: false));
        try
        {
            var window = (Window)panel;
            var area = CursorPanelPlacement.WorkArea;
            var left = area.Left + 16;
            var top = area.Bottom - 50;
            check(!panel.IsVisible, "Network overlay stays hidden until its position and state are ready");
            panel.SetState(new { download = "1.0 MB/s", upload = "12 KB/s", fontSize = 11,
                showDownload = true, showUpload = true, layout = "Yan yana" });
            panel.SetBounds(left, top, 220, 38);
            panel.Show();
            CheckPosition();

            var webView = (WebView2)window.Content;
            var initialized = false;
            PumpUntil(() => webView.CoreWebView2 is not null);
            var core = webView.CoreWebView2;
            core.NavigationCompleted += (_, args) => initialized = args.IsSuccess;
            // Navigation can have completed during controller initialization.
            PumpUntil(() => initialized || core.DocumentTitle.Length > 0);
            var navigations = 0;
            core.NavigationStarting += (_, _) => navigations++;

            for (var i = 0; i < 3; i++)
            {
                panel.Hide();
                check(!panel.IsVisible, "Network overlay can hide with the taskbar");
                panel.SetBounds(left, top, 220, 38);
                panel.Show();
                CheckPosition();
                // Allow Loaded and navigation events to run after each restore.
                PumpFor(TimeSpan.FromMilliseconds(200));
            }
            check(navigations == 0, "Restoring the overlay preserves the loaded page without blanking or reloading");

            void CheckPosition() => check(panel.IsVisible && Math.Abs(window.Left - left) < 1 &&
                Math.Abs(window.Top - top) < 1, "Explicit network overlay bounds survive Show without cursor centering");
        }
        finally { panel.Close(); }
    }

    private static void CheckPlacementCadence(Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.NetSpeed.dll"));
        var type = assembly.GetType("WinForge.Modules.NetSpeed.NetSpeedWindow")!;
        var ui = new TrackingUi();
        var indicator = Activator.CreateInstance(type, ui)!;
        type.GetMethod("ApplyOptions")!.Invoke(indicator, [11d, true, true, 10, "Otomatik", 3000, "Görev çubuğunun içinde", "Yan yana"]);
        type.GetMethod("Start")!.Invoke(indicator, null);
        try
        {
            var placements = ui.Window.Placements;
            var samples = ui.Window.Samples;
            PumpFor(TimeSpan.FromMilliseconds(400));
            check(ui.Window.Placements >= placements + 3, "Taskbar placement follows visibility and stacking changes without waiting for the 3-second network interval");
            check(ui.Window.Samples == samples, "Fast taskbar tracking does not increase network sampling or reset speed values");
            type.GetMethod("ApplyOptions")!.Invoke(indicator, [11d, false, false, 10, "Otomatik", 3000, "Görev çubuğunun içinde", "Yan yana"]);
            PumpFor(TimeSpan.FromMilliseconds(100));
            check(!ui.Window.IsVisible, "Fast placement keeps the overlay hidden when both speed rows are disabled");
        }
        finally { type.GetMethod("Stop")!.Invoke(indicator, null); }
        var stopped = ui.Window.Placements;
        PumpFor(TimeSpan.FromMilliseconds(150));
        check(ui.Window.Placements == stopped, "Stopping the indicator releases its fast placement timer");
    }

    private sealed class TrackingUi : IFeatureUiService
    {
        internal TrackingWindow Window { get; } = new();
        public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options) => Window;
        public IFeatureUiSurface CreateSurface(string view) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel") => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel = "Close") => throw new NotSupportedException();
    }

    private sealed class TrackingWindow : IFeatureUiWindow
    {
        internal int Placements;
        internal int Samples;
        public bool IsVisible { get; private set; }
        public event Action<string, JsonElement>? MessageReceived { add { } remove { } }
        public event Action? ClosedByHost { add { } remove { } }
        public void Show() { Placements++; IsVisible = true; }
        public void Hide() { Placements++; IsVisible = false; }
        public void Close() => Hide();
        public void Dispose() => Close();
        public void SetBounds(double left, double top, double width, double height) { }
        public void SetState(object state) => Samples++;
    }

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < deadline) PumpFor(TimeSpan.FromMilliseconds(20));
        if (!ready()) throw new TimeoutException("Network overlay WebView did not initialize.");
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
