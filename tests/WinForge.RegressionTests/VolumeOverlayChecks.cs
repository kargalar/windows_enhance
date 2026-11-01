using System.Text.Json;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WinForge.App;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class VolumeOverlayChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var ui = new OverlayUi();
        var mediaVisible = false;
        var mediaHovered = false;
        void ShowMedia() => mediaVisible = true;
        void HideMedia() => mediaVisible = false;
        bool KeepOpen() => mediaHovered;
        AudioVolumeOverlayLifecycle.Shown += ShowMedia;
        AudioVolumeOverlayLifecycle.Hidden += HideMedia;
        AudioVolumeOverlayLifecycle.KeepOpenRequested += KeepOpen;
        OsdWindow.Configure(ui);
        try
        {
            OsdWindow.ShowVolume(40, false, dismissAfterSeconds: 1);
            check(ui.Window.IsVisible && mediaVisible, "Volume and media companions open together");
            mediaHovered = true;
            PumpFor(TimeSpan.FromMilliseconds(1250));
            check(ui.Window.IsVisible && mediaVisible, "Media hover keeps both panels open beyond the dismissal deadline");
            mediaHovered = false;
            AudioVolumeOverlayLifecycle.NotifyInteraction();
            PumpFor(TimeSpan.FromMilliseconds(1250));
            check(!ui.Window.IsVisible && !mediaVisible, "Both panels close on the shared dismissal deadline");

            OsdWindow.ShowVolume(42, false, dismissAfterSeconds: 1);
            ui.Window.Send("volume.hover", new { value = true });
            PumpFor(TimeSpan.FromMilliseconds(1250));
            check(ui.Window.IsVisible && mediaVisible, "Volume hover keeps both panels open");
            ui.Window.Send("volume.dismiss", new { });
            check(!ui.Window.IsVisible && !mediaVisible, "Closing the volume panel closes its media companion");

            OsdWindow.ShowVolume(42, false, dismissAfterSeconds: 1);
            ui.Window.Send("volume.drag", new { value = true });
            ui.Window.Send("volume.hover", new { value = false });
            PumpFor(TimeSpan.FromMilliseconds(1250));
            check(ui.Window.IsVisible, "Dragging outside the volume panel keeps the slider available");
            ui.Window.Send("volume.drag", new { value = false });
            PumpFor(TimeSpan.FromMilliseconds(1250));
            check(!ui.Window.IsVisible, "Releasing the volume slider resumes automatic dismissal");

            var brightness = -1;
            OsdWindow.ShowBrightness(40, "test-display", percent => brightness = percent);
            ui.Window.Send("brightness.drag", new { value = true });
            ui.Window.Send("brightness.set", new { percent = 73 });
            check(brightness == 73, "Brightness slider forwards the requested percentage to the display control");
            PumpFor(TimeSpan.FromMilliseconds(1750));
            check(ui.Window.IsVisible, "Brightness remains visible throughout a drag outside the panel");
            ui.Window.Send("brightness.drag", new { value = false });
            PumpFor(TimeSpan.FromMilliseconds(1750));
            check(!ui.Window.IsVisible, "Releasing the brightness slider resumes automatic dismissal");

            OsdWindow.ShowVolume(44, false);
            AudioVolumeOverlayLifecycle.RequestDismiss();
            check(!ui.Window.IsVisible && !mediaVisible, "Closing the media companion closes the volume panel");

            AudioVolumeOverlayLifecycle.Shown -= ShowMedia;
            OsdWindow.ShowVolume(46, false);
            check(ui.Window.IsVisible && !mediaVisible, "Volume remains available without a media companion");
            AudioVolumeOverlayLifecycle.RequestDismiss();
            CheckMediaAvailability(ui, check);
        }
        finally
        {
            OsdWindow.Dispose();
            AudioVolumeOverlayLifecycle.Shown -= ShowMedia;
            AudioVolumeOverlayLifecycle.Hidden -= HideMedia;
            AudioVolumeOverlayLifecycle.KeepOpenRequested -= KeepOpen;
        }
    }

    private static void CheckMediaAvailability(OverlayUi ui, Action<bool, string> check)
    {
        var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var type = extras.GetType("WinForge.Modules.Extras.MediaFlyoutWindow", throwOnError: true)!;
        var window = (Window)Activator.CreateInstance(type, [ui])!;
        try
        {
            // An empty allowlist excludes even a system-current session from the media card.
            type.GetMethod("Configure")!.Invoke(window,
                ["bottom-right", "primary", "none", 8, "allowlist", "", "", false, null, 1, false]);
            OsdWindow.ShowVolume(48, false, dismissAfterSeconds: 10);
            var pending = type.GetField("_preparingPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (var attempt = 0; attempt < 20 && (bool)pending.GetValue(window)!; attempt++)
                PumpFor(TimeSpan.FromMilliseconds(100));
            check(!(bool)pending.GetValue(window)! && ui.Window.IsVisible && !window.IsVisible,
                "Media panel stays hidden when no allowed media session exists");
        }
        finally
        {
            AudioVolumeOverlayLifecycle.RequestDismiss();
            window.Close();
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class OverlayUi : IFeatureUiService
    {
        public OverlayWindow Window { get; } = new();
        public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options) => Window;
        public IFeatureUiSurface CreateSurface(string view) => new OverlaySurface();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel") => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel = "Close") => throw new NotSupportedException();
    }

    private sealed class OverlaySurface : IFeatureUiSurface
    {
        public FrameworkElement Element { get; } = new System.Windows.Controls.Grid();
        public event Action<string, JsonElement>? MessageReceived { add { } remove { } }
        public void SetState(object state) { }
        public void Dispose() { }
    }

    private sealed class OverlayWindow : IFeatureUiWindow
    {
        public bool IsVisible { get; private set; }
        public event Action<string, JsonElement>? MessageReceived;
        public event Action? ClosedByHost { add { } remove { } }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close() => Hide();
        public void Dispose() => Close();
        public void SetBounds(double left, double top, double width, double height) { }
        public void SetState(object state) { }
        public void Send(string command, object payload) => MessageReceived?.Invoke(command, JsonSerializer.SerializeToElement(payload));
    }
}
