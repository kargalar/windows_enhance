using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

public readonly record struct CaptureArea(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0 && (long)Width * Height <= 30_000_000;
}
public sealed record ForegroundInfo(nint Window, uint Process, string App, string Title);
public static class CaptureServices
{
    public const long MinimumFreeBytes = 256L * 1024 * 1024;
    public static bool IsDefaultInputDesktop
    {
        get
        {
            var desktop = OpenInputDesktop(0, false, 1);
            if (desktop == 0) return false;
            try { var name = new StringBuilder(256); return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _) && name.ToString() == "Default"; }
            finally { CloseDesktop(desktop); }
        }
    }
    public static ForegroundInfo Foreground()
    {
        var window = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(window, out var pid);
        var title = new StringBuilder(2048); Native.GetWindowText(window, title, title.Capacity);
        string app;
        try { using var process = System.Diagnostics.Process.GetProcessById((int)pid); app = process.ProcessName + ".exe"; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { app = "Bilinmeyen"; }
        return new(window, pid, app, title.ToString());
    }
    public static CaptureArea Monitor(string device = "cursor")
    {
        DesktopDisplay display;
        if (device == "cursor") { Native.GetCursorPos(out var point); display = DesktopDisplays.ForPoint(point); }
        else display = DesktopDisplays.Enumerate().FirstOrDefault(d => d.DeviceName == device) ?? DesktopDisplays.Enumerate().First(d => d.IsPrimary);
        return Area(display.Bounds);
    }
    public static CaptureArea Area(RECT rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    public static CaptureArea WindowArea(nint window)
    {
        if (DwmGetWindowAttribute(window, 9, out RECT rect, Marshal.SizeOf<RECT>()) != 0 && !Native.GetWindowRect(window, out rect))
            throw new InvalidOperationException("Pencere sınırları okunamadı.");
        return Area(rect);
    }
    public static BitmapSource Capture(CaptureArea area)
    {
        if (!area.IsValid) throw new ArgumentException("Yakalama alanı geçersiz veya 30 megapikselden büyük.");
        var (pixels, width, height) = DesktopCapture.Capture(area.X, area.Y, area.Width, area.Height);
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4); image.Freeze(); return image;
    }
    public static BitmapSource Load(string path)
    {
        return WinForge.FileTools.UI.ImageEngine.Load(path);
    }
    public static string Save(HistoryStore store, BitmapSource image, bool webp = false, int quality = 80)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var memory = new MemoryStream(); encoder.Save(memory); var bytes = memory.ToArray();
        quality = Math.Clamp(quality, 1, 100);
        var name = Convert.ToHexString(SHA256.HashData(bytes)) + (webp ? $"-q{quality}.webp" : ".png"); var path = store.ResolveMedia(name);
        store.ReserveMedia(name);
        if (!File.Exists(path))
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (webp) WinForge.FileTools.UI.ImageEngine.Save(image, temporary, "webp", quality);
                else File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, true);
            }
            catch { store.ReleasePendingMedia(name); throw; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return name;
    }
    public static double IdleSeconds
    {
        get { var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() }; return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) / 1000d : 0; }
    }
    public static bool Excluded(ForegroundInfo info, string patterns) => patterns.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Any(pattern => Regex.IsMatch(info.App + "|" + info.Title, Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", "."), RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)));
    public static bool HasSpace(string directory, long minimumBytes = MinimumFreeBytes) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace > minimumBytes;
    public static CaptureArea? SelectArea()
    {
        var selection = new RegionPicker(); selection.ShowDialog(); return selection.Result;
    }
    public static CaptureArea? SelectWindow()
    {
        var selection = SelectArea(); if (selection is not { } area) return null;
        var window = GetAncestor(WindowFromPoint(new POINT(area.X + area.Width / 2, area.Y + area.Height / 2)), 2);
        if (window == 0) return null;
        Native.GetWindowThreadProcessId(window, out var process);
        if (process == Environment.ProcessId) throw new InvalidOperationException("Yakalanacak başka bir uygulama penceresi seçin.");
        return WindowArea(window);
    }
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out RECT value, int size);
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder value, int length, out int needed);
}

/// <summary>Physical coordinates on each monitor, including monitors with different DPI.</summary>
internal sealed class RegionPicker : Window
{
    private readonly List<RegionSurface> surfaces = [];
    public CaptureArea? Result { get; private set; }
    public RegionPicker()
    {
        Width = Height = 0; WindowStyle = WindowStyle.None; ShowInTaskbar = false; Opacity = 0;
        Loaded += (_, _) =>
        {
            foreach (var monitor in DesktopDisplays.Enumerate())
            {
                var surface = new RegionSurface(monitor, Finish); surfaces.Add(surface); surface.Show();
            }
        };
        Closed += (_, _) => { foreach (var surface in surfaces) surface.Close(); };
    }
    private void Finish(CaptureArea? area) { Result = area; Close(); }
    private sealed class RegionSurface : Window
    {
        private readonly System.Windows.Controls.Canvas canvas = new();
        private readonly System.Windows.Shapes.Rectangle border = new() { Stroke = Brushes.DodgerBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 80, 160, 255)) };
        private POINT? origin;
        public RegionSurface(DesktopDisplay display, Action<CaptureArea?> finish)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0));
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Cursor = System.Windows.Input.Cursors.Cross;
            Content = canvas; canvas.Children.Add(border);
            SourceInitialized += (_, _) => Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST,
                display.Bounds.Left, display.Bounds.Top, display.Bounds.Right - display.Bounds.Left, display.Bounds.Bottom - display.Bounds.Top, 0x0040);
            KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) finish(null); };
            MouseLeftButtonDown += (_, _) => { Native.GetCursorPos(out var point); origin = point; CaptureMouse(); };
            MouseMove += (_, _) =>
            {
                if (origin is not { } start) return;
                Native.GetCursorPos(out var end); var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                var top = scale.Transform(new Point(Math.Min(start.X, end.X) - display.Bounds.Left, Math.Min(start.Y, end.Y) - display.Bounds.Top));
                var size = scale.Transform(new Vector(Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y)));
                System.Windows.Controls.Canvas.SetLeft(border, top.X); System.Windows.Controls.Canvas.SetTop(border, top.Y); border.Width = size.X; border.Height = size.Y;
            };
            MouseLeftButtonUp += (_, _) =>
            {
                if (origin is not { } start) return; ReleaseMouseCapture(); Native.GetCursorPos(out var end);
                finish(new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y)));
            };
        }
    }
}
