using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
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
    private readonly IFeatureUiSurface _uiSurface;
    private int _selected;

    public POINT? Destination { get; private set; }

    public MouseJumpWindow(IReadOnlyList<MouseMonitor> monitors, IFeatureUiService? featureUi = null)
    {
        _monitors = monitors;
        _uiSurface = featureUi?.CreateSurface("mouse-jump")
            ?? throw new InvalidOperationException("The React mouse jump view is required.");
        _uiSurface.MessageReceived += OnUiMessage;
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
        Content = _uiSurface.Element;
        Loaded += (_, _) => PushUiState();
        Closed += (_, _) => _uiSurface.Dispose();
        PushUiState();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "mouse-jump.cancel": DialogResult = false; break;
            case "mouse-jump.move":
                if (payload.TryGetProperty("delta", out var deltaValue) && deltaValue.TryGetInt32(out var delta) && _monitors.Count > 0)
                    _selected = (_selected + delta + _monitors.Count) % _monitors.Count;
                PushUiState();
                break;
            case "mouse-jump.select":
                if (payload.TryGetProperty("index", out var selected) && selected.TryGetInt32(out var index) && index >= 0 && index < _monitors.Count)
                { _selected = index; PushUiState(); }
                break;
            case "mouse-jump.center":
                if (TryGetMonitor(payload, out var centerMonitor)) Destination = CenterOf(_monitors[centerMonitor].Bounds);
                if (Destination is not null) DialogResult = true;
                break;
            case "mouse-jump.choose":
                if (TryGetMonitor(payload, out var monitorIndex) &&
                    payload.TryGetProperty("x", out var xValue) && xValue.TryGetDouble(out var x) &&
                    payload.TryGetProperty("y", out var yValue) && yValue.TryGetDouble(out var y))
                {
                    var bounds = _monitors[monitorIndex].Bounds;
                    Destination = new POINT(bounds.Left + (int)Math.Round(Math.Clamp(x, 0, 1) * (bounds.Right - bounds.Left - 1)),
                        bounds.Top + (int)Math.Round(Math.Clamp(y, 0, 1) * (bounds.Bottom - bounds.Top - 1)));
                    DialogResult = true;
                }
                break;
        }
    }

    private bool TryGetMonitor(JsonElement payload, out int index)
    {
        index = -1;
        return payload.TryGetProperty("index", out var selected) && selected.TryGetInt32(out index) && index >= 0 && index < _monitors.Count;
    }

    private void PushUiState() => _uiSurface.SetState(new
    {
        selected = _selected,
        monitors = _monitors.Select(monitor => new
        {
            monitor.Index,
            monitor.Name,
            monitor.IsPrimary,
            width = monitor.Bounds.Right - monitor.Bounds.Left,
            height = monitor.Bounds.Bottom - monitor.Bounds.Top,
            preview = BitmapToDataUri(monitor.Preview)
        }).ToArray()
    });

    private static string? BitmapToDataUri(ImageSource? image)
    {
        if (image is not BitmapSource bitmap) return null;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
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
