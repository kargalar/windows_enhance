using System.Windows;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Captures output audio and streams spectrum state to the shared React overlay.</summary>
internal sealed class TaskbarAudioVisualizer : IDisposable
{
    private const int BarCount = AudioSpectrumAnalyzer.BandCount;
    private const double Width = 178;
    private const double Height = 28;
    private const int RenderIntervalMilliseconds = 33;
    private readonly IFeatureUiWindow? _view;
    private AudioSpectrumCapture? _capture;
    private readonly Timer _timer;
    private readonly string _position;
    private readonly bool _widgetEnabled;
    private readonly bool _hideWhenSilent;
    private readonly bool _showBaseline;
    private int _frame;
    private int _rendering;
    private int _disposed;

    public TaskbarAudioVisualizer(IFeatureUiService? featureUi, string position, bool widgetEnabled,
        bool showBaseline, bool hideWhenSilent)
    {
        _position = position == "near-widget" ? "near-widget" : "center";
        _widgetEnabled = widgetEnabled;
        _hideWhenSilent = hideWhenSilent;
        _showBaseline = showBaseline;
        _view = featureUi?.Open("taskbar-audio-visualizer", "taskbar-audio-visualizer",
            "Ses görselleştiricisi", new FeatureUiWindowOptions(Width, Height, Topmost: true,
                ActivateOnShow: false, Transparent: true, ClickThrough: true));

        try
        {
            _capture = new AudioSpectrumCapture();
            _capture.Start();
        }
        catch
        {
            _capture?.Dispose();
            _capture = null;
            _view?.Close();
        }

        PositionAboveTaskbar();
        var dueTime = _capture is null ? Timeout.Infinite : RenderIntervalMilliseconds;
        _timer = new Timer(_ => Render(), null, dueTime, RenderIntervalMilliseconds);
    }

    private void Render()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _rendering, 1) != 0) return;
        try
        {
            var levels = _capture?.GetLevels() ?? new float[BarCount];
            var isFullscreen = ++_frame % 30 == 0 && MediaFlyoutWindow.IsForegroundFullscreen();
            var hasAudio = levels.Any(level => level >= 0.035f);
            var visible = !isFullscreen && (hasAudio || (_showBaseline && !_hideWhenSilent));
            _view?.SetState(new { levels, visible, showBaseline = _showBaseline });
            if (_frame % 30 == 0) PositionAboveTaskbar();
        }
        catch
        {
            _view?.Close();
            Dispose();
        }
        finally
        {
            Volatile.Write(ref _rendering, 0);
        }
    }

    private void PositionAboveTaskbar()
    {
        if (_view is null) return;
        var area = SystemParameters.WorkArea;
        var left = _position == "center"
            ? area.Left + (area.Width - Width) / 2
            : area.Right - Width - (_widgetEnabled ? 350 : 14);
        var top = Math.Max(area.Top + 4, area.Bottom - Height - 6);
        _view.SetBounds(left, top, Width, Height);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
        var capture = Interlocked.Exchange(ref _capture, null);
        if (capture is not null) _ = Task.Run(capture.Dispose);
        _view?.Close();
    }
}
