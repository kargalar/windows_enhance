using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Shows a low-cost live spectrum of the default Windows output above the taskbar.</summary>
internal sealed class TaskbarAudioVisualizerWindow : Window
{
    private const int BarCount = AudioSpectrumAnalyzer.BandCount;
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(33);
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly Border _baseline;
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly string _position;
    private readonly bool _widgetEnabled;
    private readonly bool _hideWhenSilent;
    private readonly bool _showBaseline;
    private AudioSpectrumCapture? _capture = new();
    private bool _captureStarted;

    public TaskbarAudioVisualizerWindow(string position, bool widgetEnabled, bool showBaseline, bool hideWhenSilent)
    {
        _position = position == "near-widget" ? "near-widget" : "center";
        _widgetEnabled = widgetEnabled;
        _hideWhenSilent = hideWhenSilent;
        _showBaseline = showBaseline;

        Width = 178;
        Height = 28;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var bars = new UniformGrid { Columns = BarCount, VerticalAlignment = VerticalAlignment.Bottom, Height = 22 };
        for (var index = 0; index < _bars.Length; index++)
        {
            _bars[index] = new Rectangle
            {
                Width = 3,
                Height = 2,
                RadiusX = 1.5,
                RadiusY = 1.5,
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = new SolidColorBrush(Color.FromRgb(99, 184, 255)),
                Margin = new Thickness(1, 0, 1, 0)
            };
            bars.Children.Add(_bars[index]);
        }

        _baseline = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(showBaseline ? (byte)150 : (byte)0, 160, 190, 220)),
            Margin = new Thickness(3, 0, 3, 2)
        };
        var canvas = new Grid { ClipToBounds = true };
        canvas.Children.Add(bars);
        canvas.Children.Add(_baseline);
        Content = canvas;

        SourceInitialized += (_, _) => SetNonActivatingStyle();
        Loaded += (_, _) => StartCapture();
        Closed += (_, _) => StopCapture();
        _renderTimer = new DispatcherTimer { Interval = RenderInterval };
        _renderTimer.Tick += (_, _) => RenderSpectrum();
        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fullscreenTimer.Tick += (_, _) => RefreshVisibility();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PositionAboveTaskbar();
        _renderTimer.Start();
        _fullscreenTimer.Start();
        RefreshVisibility();
    }

    private void StartCapture()
    {
        if (_captureStarted) return;
        try
        {
            _capture?.Start();
            _captureStarted = true;
        }
        catch
        {
            ToolTip = "Ses görselleştiricisi başlatılamadı. Varsayılan ses çıkışını kontrol edin.";
            Hide();
        }
    }

    private void StopCapture()
    {
        _renderTimer.Stop();
        _fullscreenTimer.Stop();
        var capture = Interlocked.Exchange(ref _capture, null);
        if (capture is not null) _ = Task.Run(capture.Dispose);
        _captureStarted = false;
    }

    private void RenderSpectrum()
    {
        if (!IsVisible) return;
        var levels = _capture?.GetLevels() ?? new float[BarCount];
        for (var index = 0; index < _bars.Length; index++)
            _bars[index].Height = Math.Max(2, levels[index] * 20);
        RefreshVisibility(levels);
    }

    private void RefreshVisibility(float[]? levels = null)
    {
        if (MediaFlyoutWindow.IsForegroundFullscreen())
        {
            Hide();
            return;
        }

        levels ??= _capture?.GetLevels() ?? new float[BarCount];
        var hasAudio = levels.Any(level => level >= 0.035f);
        var showBaselineOnly = _showBaseline && !_hideWhenSilent;
        if (hasAudio || showBaselineOnly)
        {
            if (!IsVisible)
            {
                PositionAboveTaskbar();
                Show();
            }
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    private void PositionAboveTaskbar()
    {
        var area = SystemParameters.WorkArea;
        Left = _position == "center"
            ? area.Left + (area.Width - Width) / 2
            : area.Right - Width - (_widgetEnabled ? 350 : 14);
        Top = Math.Max(area.Top + 4, area.Bottom - Height - 6);
    }

    private void SetNonActivatingStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExNoActivate | WsExToolWindow;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}

