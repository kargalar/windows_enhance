using System.Text.Json;
using System.Windows;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed class RulerOverlay : IDisposable
{
    private readonly IFeatureUiWindow _window;
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _unit;
    private bool _disposed;

    public RulerOverlay(IFeatureUiService featureUi, string unit)
    {
        _unit = unit;
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        _window = featureUi.Open(
            "screen-ruler-" + Guid.NewGuid().ToString("N"),
            "screen-ruler",
            "Ekran cetveli",
            new FeatureUiWindowOptions(
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight,
                Topmost: true,
                Transparent: true,
                Left: left,
                Top: top));
        _window.MessageReceived += OnMessageReceived;
        _window.ClosedByHost += OnWindowClosed;
        _window.SetState(new { unit });
    }

    public Task<string?> Completion => _completion.Task;

    private void OnMessageReceived(string command, JsonElement payload)
    {
        if (command == "screen-ruler.cancel")
        {
            _completion.TrySetResult(null);
            _window.Close();
            return;
        }

        if (command != "screen-ruler.complete" ||
            !payload.TryGetProperty("widthPixels", out var widthValue) || !widthValue.TryGetDouble(out var width) ||
            !payload.TryGetProperty("heightPixels", out var heightValue) || !heightValue.TryGetDouble(out var height) ||
            !payload.TryGetProperty("dpiX", out var dpiXValue) || !dpiXValue.TryGetDouble(out var dpiX) ||
            !payload.TryGetProperty("dpiY", out var dpiYValue) || !dpiYValue.TryGetDouble(out var dpiY) ||
            !double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(dpiX) || !double.IsFinite(dpiY) ||
            width < 1 || height < 1 || dpiX <= 0 || dpiY <= 0)
            return;

        var result = Describe(width, height, dpiX, dpiY);
        _completion.TrySetResult(result);
        _window.Close();
    }

    private string Describe(double widthPixels, double heightPixels, double dpiX, double dpiY)
    {
        if (string.Equals(_unit, "Santimetre", StringComparison.CurrentCultureIgnoreCase))
            return $"{widthPixels / dpiX * 2.54:0.0} x {heightPixels / dpiY * 2.54:0.0} cm";

        if (string.Equals(_unit, "İnç", StringComparison.CurrentCultureIgnoreCase))
            return $"{widthPixels / dpiX:0.0} x {heightPixels / dpiY:0.0} in";

        return $"{Math.Round(widthPixels):0} x {Math.Round(heightPixels):0} px";
    }

    private void OnWindowClosed() => _completion.TrySetResult(null);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        _window.ClosedByHost -= OnWindowClosed;
        _window.Close();
        _window.Dispose();
        _completion.TrySetResult(null);
    }
}
