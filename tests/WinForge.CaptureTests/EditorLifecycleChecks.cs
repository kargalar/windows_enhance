using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;
using WinForge.History;
using WinForge.Modules.Capture;

internal static class EditorLifecycleChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null, new byte[16], 8);
        image.Freeze();
        var store = new HistoryStore(Path.Combine(root, "editor"));
        var ui = new TestUi();
        var editor = new CaptureEditor(store, image, workflowCompletion: true, featureUi: ui);
        check(!editor.Completion.IsCompleted, "Editor workflow waits for the user");
        ui.Window.Close();
        check(editor.Completion.IsCompletedSuccessfully && editor.Completion.Result is null,
            "Host close completes the editor workflow without hanging the next capture");

        ui = new TestUi();
        editor = new CaptureEditor(store, image, workflowCompletion: true, featureUi: ui);
        editor.Close();
        editor.Close();
        check(editor.Completion.IsCompletedSuccessfully && editor.Completion.Result is null && !ui.Window.IsVisible,
            "Cancellation closes the editor idempotently");

        ui = new TestUi();
        editor = new CaptureEditor(store, image, workflowCompletion: true, featureUi: ui);
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        ui.Window.Send("capture-editor.complete", new { image = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray()) });
        check(editor.Completion.Result is { PixelWidth: 2, PixelHeight: 2 },
            "Host close after completion preserves the edited image");
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
        public bool IsVisible { get; private set; } = true;
        public event Action<string, JsonElement>? MessageReceived;
        public event Action? ClosedByHost;
        public void Send(string command, object payload) => MessageReceived?.Invoke(command, JsonSerializer.SerializeToElement(payload));
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close() { IsVisible = false; ClosedByHost?.Invoke(); }
        public void Dispose() => Close();
        public void SetBounds(double left, double top, double width, double height) { }
        public void SetState(object state) { }
    }
}
