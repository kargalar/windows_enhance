using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinForge.App;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class PanelPlacementChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Native.GetCursorPos(out var original);
        var ui = new PanelUi();
        OsdWindow.Configure(ui);
        var serviceType = typeof(OsdWindow).Assembly.GetType("WinForge.App.ReactWindowHostService")!;
        using var serviceLifetime = (IDisposable)Activator.CreateInstance(serviceType, nonPublic: true)!;
        var service = (IFeatureUiService)serviceLifetime;
        IFeatureUiWindow? panel = null;
        try
        {
            foreach (var display in DesktopDisplays.Enumerate())
            {
                check(Native.SetCursorPos((display.WorkArea.Left + display.WorkArea.Right) / 2,
                    (display.WorkArea.Top + display.WorkArea.Bottom) / 2), "Cursor positioned on " + display.DeviceName);
                var area = CursorPanelPlacement.WorkArea;
                OsdWindow.ShowVolume(50, false);
                CheckBounds(ui.Windows["osd-volume"].Bounds, area, check, "Volume on " + display.DeviceName);
                // Deliberately supply a different target: the panel must follow the cursor.
                OsdWindow.ShowBrightness(50, "different-display");
                CheckBounds(ui.Windows["osd-brightness"].Bounds, area, check, "Brightness on " + display.DeviceName);
                OsdWindow.Toast("Position check");
                CheckBounds(ui.Windows["osd-toast"].Bounds, area, check, "Toast on " + display.DeviceName);
                panel = service.Open("placement-check", "app-dialog", "Panel placement check", new(420, 200, ActivateOnShow: false));
                var native = (Window)panel;
                check(DesktopDisplays.ForWindow(new WindowInteropHelper(native).Handle).Handle == display.Handle,
                    "Real React panel opens on cursor display " + display.DeviceName);
                panel.Hide();
            }
            var calls = 0;
            using (var save = new AutomaticSettingsSave(() => calls++))
            {
                save.Schedule(); save.Schedule();
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
                check(calls == 1, "Automatic settings saves coalesce text edits");
                save.Schedule();
            }
            check(calls == 2, "Closing editor flushes a pending automatic save");
        }
        finally { panel?.Close(); OsdWindow.Dispose(); Native.SetCursorPos(original.X, original.Y); }
    }

    private static void CheckBounds(Rect bounds, Rect area, Action<bool, string> check, string label) =>
        check(area.Contains(bounds) && Math.Abs(bounds.Left + bounds.Width / 2 - (area.Left + area.Width / 2)) < 1, label);

    private sealed class PanelUi : IFeatureUiService
    {
        internal Dictionary<string, PanelWindow> Windows { get; } = new();
        public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options)
        {
            var window = Windows[key] = new();
            if (options.ShowOnOpen) window.Show();
            return window;
        }
        public IFeatureUiSurface CreateSurface(string view) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel") => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel = "Close") => throw new NotSupportedException();
    }
    private sealed class PanelWindow : IFeatureUiWindow
    {
        internal Rect Bounds;
        public bool IsVisible { get; private set; }
        public event Action<string, JsonElement>? MessageReceived { add { } remove { } }
        public event Action? ClosedByHost { add { } remove { } }
        public void Show()
        {
            if (Bounds.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0 || !_hasState)
                throw new InvalidOperationException("A panel must receive its bounds and content before its first visible frame.");
            IsVisible = true;
        }
        public void Hide() => IsVisible = false;
        public void Close() => Hide();
        public void Dispose() => Close();
        public void SetBounds(double left, double top, double width, double height) => Bounds = new(left, top, width, height);
        private bool _hasState;
        public void SetState(object state) => _hasState = true;
    }
}
