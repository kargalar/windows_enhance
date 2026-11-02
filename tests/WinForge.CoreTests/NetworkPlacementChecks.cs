using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using WinForge.Core;
using WinForge.Modules.NetSpeed;

internal static class NetworkPlacementChecks
{
    public static void Run(Action<bool, string> check)
    {
        var ui = new TestUi();
        NetSpeedWindow.Placement? placement = new(-300, 120, 220, 38);
        var reads = 0;
        var indicator = new NetSpeedWindow(ui, () => { reads++; return placement; });
        indicator.ApplyOptions(11, true, true, 10, "Otomatik", 3000, NetSpeedWindow.InsideTaskbar, NetSpeedWindow.SideBySide);
        indicator.Start();
        try
        {
            check(ui.Window.Bounds == placement && ui.Window.IsVisible, "Network indicator places and shows a valid negative-coordinate overlay");
            for (var i = 0; i < 20; i++) indicator.Reposition();
            check(ui.Window.BoundsWrites == 1 && ui.Window.Shows == 1, "Stable geometry sends neither repeated bounds nor repeated show requests");
            var initialReads = reads;
            var initialSamples = ui.Window.Samples;
            Pump(TimeSpan.FromMilliseconds(1100));
            check(reads - initialReads is >= 3 and <= 5, "Fallback taskbar checks run at most four times per second instead of twenty");
            check(ui.Window.Samples == initialSamples, "Placement fallback does not increase network sampling");
            placement = null;
            indicator.Reposition(); indicator.Reposition();
            check(!ui.Window.IsVisible && ui.Window.Hides == 1, "Unavailable or hidden taskbar hides the overlay once");
            placement = new(-200, 130, 132, 44);
            indicator.Reposition();
            check(ui.Window.IsVisible && ui.Window.BoundsWrites == 2 && ui.Window.Bounds == placement, "Changed placement restores the overlay with the latest geometry");
            var shows = ui.Window.Shows;
            indicator.Reposition(restack: true);
            check(ui.Window.Shows == shows + 1 && ui.Window.BoundsWrites == 2, "Foreground changes can restore stacking without resending geometry");
            indicator.ApplyOptions(11, false, false, 10, "Otomatik", 3000, NetSpeedWindow.InsideTaskbar, NetSpeedWindow.SideBySide);
            check(!ui.Window.IsVisible, "Disabling both rows hides the overlay immediately");
        }
        finally { indicator.Stop(); indicator.Stop(); }
        var stopped = reads;
        Pump(TimeSpan.FromMilliseconds(300));
        check(reads == stopped && ui.Window.Closed, "Repeated stop releases placement work and closes the overlay");
    }
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private sealed class TestUi : IFeatureUiService
    {
        public TestWindow Window { get; } = new();
        public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options) => Window;
        public IFeatureUiSurface CreateSurface(string view) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel) => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel) => throw new NotSupportedException();
    }
    private sealed class TestWindow : IFeatureUiWindow
    {
        public bool IsVisible { get; private set; }
        public NetSpeedWindow.Placement? Bounds { get; private set; }
        public int BoundsWrites, Shows, Hides, Samples;
        public bool Closed;
        public event Action<string, JsonElement>? MessageReceived { add { } remove { } }
        public event Action? ClosedByHost { add { } remove { } }
        public void Show() { Shows++; IsVisible = true; }
        public void Hide() { Hides++; IsVisible = false; }
        public void Close() { Closed = true; Hide(); }
        public void Dispose() => Close();
        public void SetBounds(double left, double top, double width, double height) { BoundsWrites++; Bounds = new(left, top, width, height); }
        public void SetState(object state) => Samples++;
    }
}
