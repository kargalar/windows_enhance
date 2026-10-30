using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.TextGrabber;

public sealed class SelectionOverlay : Window
{
    private readonly IFeatureUiSurface uiSurface;
    private bool finished;

    public Rect? Result { get; private set; }

    public SelectionOverlay(IFeatureUiService? featureUi)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        Focusable = true;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        uiSurface = featureUi?.CreateSurface("screen-text-selection")
            ?? throw new InvalidOperationException("Ekran metni seçimi için React arayüzü gerekli.");
        Content = uiSurface.Element;
        uiSurface.MessageReceived += OnUiMessage;
        uiSurface.SetState(new { hint = "Alanı seçmek için sürükle · Esc ile iptal" });
        Closed += (_, _) =>
        {
            uiSurface.MessageReceived -= OnUiMessage;
            uiSurface.Dispose();
        };
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
        };
    }

    private void OnUiMessage(string command, System.Text.Json.JsonElement payload)
    {
        if (command == "screen-selection.cancel")
        {
            finished = true;
            Result = null;
            Close();
            return;
        }
        if (command != "screen-selection.complete" ||
            !payload.TryGetProperty("x", out var x) ||
            !payload.TryGetProperty("y", out var y) ||
            !payload.TryGetProperty("width", out var width) ||
            !payload.TryGetProperty("height", out var height) ||
            width.GetDouble() < 6 || height.GetDouble() < 6) return;

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var handle = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(handle, out var windowBounds);
        var left = transform.Transform(new Point(x.GetDouble(), y.GetDouble()));
        var right = transform.Transform(new Point(x.GetDouble() + width.GetDouble(), y.GetDouble() + height.GetDouble()));
        Result = new Rect(
            windowBounds.Left + left.X,
            windowBounds.Top + left.Y,
            Math.Max(1, right.X - left.X),
            Math.Max(1, right.Y - left.Y));
        finished = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!finished) Result = null;
        base.OnClosed(e);
    }
}
