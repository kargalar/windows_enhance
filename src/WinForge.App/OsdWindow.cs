using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Brushes = System.Windows.Media.Brushes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.App;

public sealed class OsdWindow : Window
{
    private static OsdWindow? _instance;

    private readonly TextBlock _text;
    private readonly Border _container;
    private readonly DispatcherTimer _timer;
    private readonly Grid _volumeRow;
    private readonly Button _muteButton;
    private readonly Grid _volumeTrackContainer;
    private readonly Border _volumeTrack;
    private readonly Border _volumeTrackFill;
    private readonly Border _volumeThumb;
    private readonly TextBlock _volumeText;
    private bool _nativeWindowInitialized;
    private Action? _onVolumeChanged;
    private int _volumePercent;
    private bool _volumeMuted;
    private bool _draggingVolumeSlider;
    private int _volumeDismissSeconds = 3;
    private const double DefaultFontSize = 12.5;

    private OsdWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = DefaultFontSize,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560
        };

        _muteButton = new Button
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Sessize al / sesi aç"
        };
        _muteButton.Click += (_, _) => ToggleMute();

        _volumeTrackContainer = new Grid
        {
            Height = 24,
            Margin = new Thickness(9, 0, 9, 0),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = true,
            ToolTip = "Ses düzeyini değiştir"
        };
        _volumeTrack = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(72, 72, 72)),
            VerticalAlignment = VerticalAlignment.Center
        };
        _volumeTrackFill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 0
        };
        _volumeThumb = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };
        _volumeTrackContainer.Children.Add(_volumeTrack);
        _volumeTrackContainer.Children.Add(_volumeTrackFill);
        _volumeTrackContainer.Children.Add(_volumeThumb);
        _volumeTrackContainer.PreviewMouseLeftButtonDown += (_, args) =>
        {
            _draggingVolumeSlider = true;
            _volumeTrackContainer.CaptureMouse();
            _volumeTrackContainer.Focus();
            SetVolumeFromPosition(args.GetPosition(_volumeTrackContainer).X);
        };
        _volumeTrackContainer.PreviewMouseMove += (_, args) =>
        {
            if (_draggingVolumeSlider && args.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                SetVolumeFromPosition(args.GetPosition(_volumeTrackContainer).X);
        };
        _volumeTrackContainer.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _draggingVolumeSlider = false;
            _volumeTrackContainer.ReleaseMouseCapture();
        };
        _volumeTrackContainer.PreviewKeyDown += (_, args) =>
        {
            var nextVolume = args.Key switch
            {
                System.Windows.Input.Key.Left => _volumePercent - 2,
                System.Windows.Input.Key.Right => _volumePercent + 2,
                System.Windows.Input.Key.Home => 0,
                System.Windows.Input.Key.End => 100,
                _ => _volumePercent
            };
            if (nextVolume == _volumePercent) return;
            args.Handled = true;
            SetVolume(nextVolume);
        };

        _volumeText = new TextBlock
        {
            Width = 42,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.Bold
        };
        _volumeRow = new Grid { Width = 286, Visibility = Visibility.Collapsed };
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        Grid.SetColumn(_muteButton, 0);
        Grid.SetColumn(_volumeTrackContainer, 1);
        Grid.SetColumn(_volumeText, 2);
        _volumeRow.Children.Add(_muteButton);
        _volumeRow.Children.Add(_volumeTrackContainer);
        _volumeRow.Children.Add(_volumeText);

        var content = new StackPanel { Children = { _text, _volumeRow } };

        _container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(244, 10, 10, 10)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            Child = content,
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 2,
                Opacity = 0.45,
                Color = Colors.Black
            }
        };
        Content = _container;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _timer.Tick += (_, _) =>
        {
            if (_volumeRow.Visibility == Visibility.Visible && IsMouseOver)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(500);
                _timer.Start();
                return;
            }
            _timer.Stop();
            Hide();
            SetInteractive(false);
        };
        MouseEnter += (_, _) =>
        {
            if (_volumeRow.Visibility == Visibility.Visible) _timer.Stop();
        };
        MouseLeave += (_, _) =>
        {
            if (_volumeRow.Visibility != Visibility.Visible) return;
            _timer.Interval = TimeSpan.FromSeconds(_volumeDismissSeconds);
            _timer.Stop();
            _timer.Start();
        };

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            if (_volumeRow.Visibility != Visibility.Visible) style |= Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
            _nativeWindowInitialized = true;
        };
    }

    public static void Toast(string message)
    {
        var app = Application.Current;
        if (app is null) return;

        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _instance ??= new OsdWindow();
                _instance.ShowMessage(message);
            }
            catch
            {
                // A notification must never break the caller.
            }
        }));
    }

    public static void ShowBrightness(int percent, string deviceName)
    {
        var app = Application.Current;
        if (app is null) return;

        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _instance ??= new OsdWindow();
                _instance.ShowBrightnessMessage(Math.Clamp(percent, 0, 100), deviceName);
            }
            catch
            {
                // A notification must never break the caller.
            }
        }));
    }

    public static void ShowVolume(int percent, bool isMuted, Action? onVolumeChanged = null, int dismissAfterSeconds = 3)
    {
        var app = Application.Current;
        if (app is null) return;

        void Show()
        {
            try
            {
                _instance ??= new OsdWindow();
                _instance.ShowVolumeMessage(Math.Clamp(percent, 0, 100), isMuted, onVolumeChanged, dismissAfterSeconds);
            }
            catch
            {
                // A notification must never break the caller.
            }
        }

        if (app.Dispatcher.CheckAccess()) Show();
        else app.Dispatcher.BeginInvoke(new Action(Show));
    }

    private void ShowMessage(string message)
    {
        _volumeRow.Visibility = Visibility.Collapsed;
        _text.Visibility = Visibility.Visible;
        SetInteractive(false);
        _container.Padding = new Thickness(18, 11, 18, 11);
        _container.CornerRadius = new CornerRadius(10);
        _text.FontSize = DefaultFontSize;
        _text.FontWeight = FontWeights.Bold;
        _text.Text = message;

        if (!IsVisible) Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 72;

        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(1400 + message.Length * 28, 1800, 6000));
        _timer.Stop();
        _timer.Start();
    }

    private void ShowBrightnessMessage(int percent, string deviceName)
    {
        _volumeRow.Visibility = Visibility.Collapsed;
        _text.Visibility = Visibility.Visible;
        SetInteractive(false);
        _container.Padding = new Thickness(14, 8, 14, 8);
        _container.CornerRadius = new CornerRadius(8);
        _text.FontSize = 14;
        _text.FontWeight = FontWeights.Bold;
        _text.Text = $"☼  {percent}%";

        if (!IsVisible) Show();
        UpdateLayout();

        var area = DesktopDisplays.Enumerate().FirstOrDefault(display =>
            display.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))?.WorkArea;
        if (area is null)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
            Top = workArea.Bottom - ActualHeight - 56;
        }
        else
        {
            var workArea = area.Value;
            Left = workArea.Left + ((workArea.Right - workArea.Left) - ActualWidth) / 2;
            Top = area.Value.Bottom - ActualHeight - 56;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(1500);
        _timer.Stop();
        _timer.Start();
    }

    private void ShowVolumeMessage(int percent, bool isMuted, Action? onVolumeChanged, int dismissAfterSeconds)
    {
        _onVolumeChanged = onVolumeChanged;
        _volumeDismissSeconds = Math.Clamp(dismissAfterSeconds, 1, 30);
        _volumeRow.Visibility = Visibility.Visible;
        _text.Visibility = Visibility.Collapsed;
        SetInteractive(true);
        _container.Padding = new Thickness(10, 7, 10, 7);
        _container.CornerRadius = new CornerRadius(12);
        _volumePercent = Math.Clamp(percent, 0, 100);
        _volumeMuted = isMuted;
        UpdateVolumeIndicator();

        if (!IsVisible) Show();
        UpdateLayout();
        UpdateVolumeTrack();

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 56;

        _timer.Interval = TimeSpan.FromSeconds(_volumeDismissSeconds);
        _timer.Stop();
        _timer.Start();
    }

    private void SetVolumeFromPosition(double x)
    {
        var width = _volumeTrackContainer.ActualWidth;
        if (width <= 0) return;
        var travel = Math.Max(1, width - _volumeThumb.Width);
        var value = Math.Clamp((x - _volumeThumb.Width / 2) / travel, 0, 1);
        SetVolume((int)Math.Round(value * 100));
    }

    private void SetVolume(int target)
    {
        if (_volumeRow.Visibility != Visibility.Visible) return;
        try
        {
            var current = VolumeService.Current();
            var applied = VolumeService.StepFromFlyout(target - current);
            _volumePercent = applied;
            _volumeMuted = VolumeService.IsMuted();
            UpdateVolumeIndicator();
            _onVolumeChanged?.Invoke();
            RestartVolumeTimer();
        }
        catch
        {
            // A transient control must not break the audio service.
        }
    }

    private void ToggleMute()
    {
        try
        {
            _volumeMuted = !_volumeMuted;
            VolumeService.SetMutedFromFlyout(_volumeMuted);
            UpdateVolumeIndicator();
            _onVolumeChanged?.Invoke();
            RestartVolumeTimer();
        }
        catch
        {
            // A transient control must not break the audio service.
        }
    }

    private void UpdateVolumeIndicator()
    {
        _muteButton.Content = _volumeMuted ? "🔇" : _volumePercent switch
        {
            <= 33 => "🔈",
            <= 66 => "🔉",
            _ => "🔊"
        };
        _muteButton.ToolTip = _volumeMuted ? "Sesi aç" : "Sessize al";
        _volumeText.Text = $"{_volumePercent}%";
        UpdateVolumeTrack();
    }

    private void UpdateVolumeTrack()
    {
        var width = _volumeTrackContainer.ActualWidth;
        if (width <= 0) return;
        var thumbTravel = Math.Max(0, width - _volumeThumb.Width);
        var thumbPosition = thumbTravel * _volumePercent / 100;
        _volumeTrackFill.Width = thumbPosition + _volumeThumb.Width / 2;
        _volumeThumb.Margin = new Thickness(thumbPosition, 0, 0, 0);
    }

    private void RestartVolumeTimer()
    {
        _timer.Interval = TimeSpan.FromSeconds(_volumeDismissSeconds);
        _timer.Stop();
        _timer.Start();
    }

    private void SetInteractive(bool interactive)
    {
        IsHitTestVisible = interactive;
        if (!_nativeWindowInitialized) return;

        var handle = new WindowInteropHelper(this).Handle;
        var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
        style = interactive ? style & ~Native.WS_EX_TRANSPARENT : style | Native.WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
    }
}
