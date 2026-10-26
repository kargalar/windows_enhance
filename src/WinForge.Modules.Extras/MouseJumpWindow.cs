using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using WpfImage = System.Windows.Controls.Image;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace WinForge.Modules.Extras;

internal sealed record MouseMonitor(int Index, string Name, RECT Bounds, bool IsPrimary, ImageSource? Preview);

internal sealed class MouseJumpWindow : Window
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;
    private static readonly MonitorEnumCallback EnumCallback = CollectMonitor;
    private readonly IReadOnlyList<MouseMonitor> _monitors;
    private readonly List<Border> _cards = new();
    private int _selected;

    public POINT? Destination { get; private set; }

    public MouseJumpWindow(IReadOnlyList<MouseMonitor> monitors)
    {
        _monitors = monitors;
        _selected = Math.Max(0, monitors.ToList().FindIndex(monitor => monitor.IsPrimary));
        Title = "Ekranlar Arası Fare Taşıma";
        Width = Math.Min(1100, Math.Max(620, monitors.Count * 350));
        Height = 520;
        MinWidth = 520;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(WpfColor.FromRgb(25, 29, 38));
        Foreground = WpfBrushes.White;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        AutomationProperties.SetName(this, Title);

        var root = new DockPanel { Margin = new Thickness(18) };
        var instructions = new TextBlock
        {
            Text = "Noktaya tıklayın · 1–9 veya P ekran merkezine taşır · Ok tuşlarıyla seçip Enter'a basın · Esc kapatır",
            Foreground = new SolidColorBrush(WpfColor.FromRgb(190, 198, 212)),
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(instructions, Dock.Top);
        root.Children.Add(instructions);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var monitor in monitors)
        {
            var card = CreateCard(monitor);
            _cards.Add(card);
            cards.Children.Add(card);
        }
        RefreshSelection();
        scroll.Content = cards;
        root.Children.Add(scroll);
        Content = root;

        PreviewKeyDown += HandleKeyDown;
        Loaded += (_, _) => Focus();
    }

    public static IReadOnlyList<MouseMonitor> CaptureMonitors()
    {
        var monitors = new List<(RECT Bounds, bool IsPrimary)>();
        var monitorHandle = GCHandle.Alloc(monitors);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumCallback, GCHandle.ToIntPtr(monitorHandle));
        }
        finally
        {
            monitorHandle.Free();
        }

        return monitors.Select((monitor, index) => new MouseMonitor(index,
            $"Ekran {index + 1}{(monitor.IsPrimary ? " · Birincil" : "")} · {monitor.Bounds.Right - monitor.Bounds.Left} × {monitor.Bounds.Bottom - monitor.Bounds.Top}",
            monitor.Bounds, monitor.IsPrimary, CapturePreview(monitor.Bounds))).ToArray();
    }

    private static bool CollectMonitor(IntPtr monitor, IntPtr deviceContext, ref RECT bounds, IntPtr data)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info))
            ((List<(RECT Bounds, bool IsPrimary)>)GCHandle.FromIntPtr(data).Target!)
                .Add((info.Monitor, (info.Flags & MonitorInfoPrimary) != 0));
        return true;
    }

    private static ImageSource? CapturePreview(RECT bounds)
    {
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return null;

        try
        {
            using var capture = new DrawingBitmap(width, height, DrawingPixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(capture))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new DrawingSize(width, height), CopyPixelOperation.SourceCopy);

            var scale = Math.Min(1.0, Math.Min(480.0 / width, 300.0 / height));
            var previewWidth = Math.Max(1, (int)Math.Round(width * scale));
            var previewHeight = Math.Max(1, (int)Math.Round(height * scale));
            using var preview = new DrawingBitmap(previewWidth, previewHeight, DrawingPixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(preview))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(capture, new DrawingRectangle(0, 0, previewWidth, previewHeight));
            }

            var handle = preview.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    private Border CreateCard(MouseMonitor monitor)
    {
        var image = new WpfImage
        {
            Source = monitor.Preview,
            Stretch = Stretch.Uniform,
            MaxWidth = 480,
            MaxHeight = 300,
            MinWidth = 240,
            MinHeight = 150,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };
        AutomationProperties.SetName(image, $"{monitor.Name} önizlemesi; hedef noktaya tıklayın");
        image.MouseLeftButtonDown += (_, args) =>
        {
            Destination = MapPoint(monitor.Bounds, image, args.GetPosition(image));
            DialogResult = true;
        };

        var previewContent = monitor.Preview is null
            ? new Border
            {
                Width = 360,
                Height = 220,
                Background = new SolidColorBrush(WpfColor.FromRgb(40, 46, 58)),
                Child = new TextBlock
                {
                    Text = "Önizleme yakalanamadı\nTıklayarak ekran merkezine taşıyın",
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WpfBrushes.White
                }
            }
            : (UIElement)image;

        if (monitor.Preview is null)
        {
            previewContent.MouseLeftButtonDown += (_, _) =>
            {
                Destination = CenterOf(monitor.Bounds);
                DialogResult = true;
            };
        }

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = monitor.Name,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        body.Children.Add(previewContent);

        var card = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(78, 88, 108)),
            Background = new SolidColorBrush(WpfColor.FromRgb(33, 38, 49)),
            Child = body
        };
        card.MouseEnter += (_, _) => card.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(76, 194, 255));
        card.MouseLeave += (_, _) => card.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(78, 88, 108));
        return card;
    }

    private static POINT MapPoint(RECT bounds, WpfImage image, System.Windows.Point point)
    {
        if (image.Source is not BitmapSource source) return CenterOf(bounds);
        var imageWidth = source.PixelWidth;
        var imageHeight = source.PixelHeight;
        if (imageWidth <= 0 || imageHeight <= 0 || image.ActualWidth <= 0 || image.ActualHeight <= 0)
            return CenterOf(bounds);

        var scale = Math.Min(image.ActualWidth / imageWidth, image.ActualHeight / imageHeight);
        var displayWidth = imageWidth * scale;
        var displayHeight = imageHeight * scale;
        var offsetX = (image.ActualWidth - displayWidth) / 2;
        var offsetY = (image.ActualHeight - displayHeight) / 2;
        var relativeX = Math.Clamp((point.X - offsetX) / displayWidth, 0, 1);
        var relativeY = Math.Clamp((point.Y - offsetY) / displayHeight, 0, 1);
        return new POINT(
            bounds.Left + (int)Math.Round(relativeX * (bounds.Right - bounds.Left - 1)),
            bounds.Top + (int)Math.Round(relativeY * (bounds.Bottom - bounds.Top - 1)));
    }

    private void HandleKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            DialogResult = false;
            args.Handled = true;
            return;
        }

        if (args.Key is Key.Left or Key.Right)
        {
            var offset = args.Key == Key.Right ? 1 : -1;
            _selected = (_selected + offset + _monitors.Count) % _monitors.Count;
            RefreshSelection();
            args.Handled = true;
            return;
        }

        if (args.Key is Key.Home or Key.End)
        {
            _selected = args.Key == Key.Home ? 0 : _monitors.Count - 1;
            RefreshSelection();
            args.Handled = true;
            return;
        }

        if (args.Key == Key.Enter)
        {
            Destination = CenterOf(_monitors[_selected].Bounds);
            DialogResult = true;
            args.Handled = true;
            return;
        }

        var number = args.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            _ => -1
        };

        if (number >= 0 && number < _monitors.Count)
        {
            Destination = CenterOf(_monitors[number].Bounds);
            DialogResult = true;
            args.Handled = true;
            return;
        }

        if (args.Key == Key.P)
        {
            var primary = _monitors.FirstOrDefault(monitor => monitor.IsPrimary);
            if (primary is not null) Destination = CenterOf(primary.Bounds);
            if (Destination is not null) DialogResult = true;
            args.Handled = true;
        }
    }

    private void RefreshSelection()
    {
        for (var index = 0; index < _cards.Count; index++)
            _cards[index].BorderBrush = new SolidColorBrush(index == _selected
                ? WpfColor.FromRgb(76, 194, 255)
                : WpfColor.FromRgb(78, 88, 108));
    }

    private static POINT CenterOf(RECT bounds) => new(
        bounds.Left + (bounds.Right - bounds.Left) / 2,
        bounds.Top + (bounds.Bottom - bounds.Top) / 2);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    private delegate bool MonitorEnumCallback(IntPtr monitor, IntPtr deviceContext, ref RECT bounds, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRect, MonitorEnumCallback callback, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}
