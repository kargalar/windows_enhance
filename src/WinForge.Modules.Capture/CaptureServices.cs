using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
    private static readonly ConcurrentDictionary<(string Store, string Executable), string> AppIcons = new();
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
    public static CaptureArea? WindowAtPoint(POINT point)
    {
        CaptureArea? result = null;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            Native.GetWindowThreadProcessId(window, out var process);
            if (process == Environment.ProcessId) return true;
            if (!Native.GetWindowRect(window, out var bounds) || point.X < bounds.Left || point.Y < bounds.Top || point.X >= bounds.Right || point.Y >= bounds.Bottom)
                return true;
            var area = WindowArea(window);
            if (!area.IsValid || point.X < area.X || point.Y < area.Y || point.X >= area.X + area.Width || point.Y >= area.Y + area.Height) return true;
            result = area;
            return false;
        }, 0);
        return result;
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
    public static string SaveProcessIcon(HistoryStore store, uint processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            var executable = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return "";
            return AppIcons.GetOrAdd((store.DirectoryPath, executable), key =>
            {
                var info = new ShellFileInfo();
                if (SHGetFileInfo(key.Executable, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100 | 0x1) == 0 || info.Icon == 0) return "";
                try
                {
                    var icon = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    icon.Freeze(); return Save(store, icon);
                }
                finally { DestroyIcon(info.Icon); }
            });
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return ""; }
    }
    public static double IdleSeconds
    {
        get { var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() }; return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) / 1000d : 0; }
    }
    public static bool Excluded(ForegroundInfo info, string patterns) => patterns.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Any(pattern => Regex.IsMatch(info.App + "|" + info.Title, Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", "."), RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)));
    public static bool HasSpace(string directory, long minimumBytes = MinimumFreeBytes) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace > minimumBytes;
    public static CaptureArea? SelectArea(bool quickCapture = true)
    {
        return SelectArea(quickCapture, out _);
    }
    internal static CaptureArea? SelectArea(bool quickCapture, out FrozenDesktop? snapshot)
    {
        var selection = new RegionPicker(quickCapture); selection.ShowDialog(); snapshot = selection.Snapshot; return selection.Result;
    }
    internal static (CaptureArea? Area, CaptureAnnotations Annotations, FrozenDesktop? Snapshot) SelectAnnotatedArea()
    {
        var selection = new RegionPicker(quickCapture: false, allowDrawing: true);
        selection.ShowDialog(); return (selection.Result, selection.Annotations!, selection.Snapshot);
    }
    public static IReadOnlyList<CaptureArea> SelectMultipleAreas()
    {
        return SelectMultipleAreas(out _);
    }
    internal static IReadOnlyList<CaptureArea> SelectMultipleAreas(out FrozenDesktop? snapshot)
    {
        var selection = new RegionPicker(quickCapture: false, multiple: true); selection.ShowDialog(); snapshot = selection.Snapshot; return selection.Results;
    }
    public static CaptureArea? SelectWindow() => SelectWindow(out _);
    internal static CaptureArea? SelectWindow(out FrozenDesktop? snapshot)
    {
        var selection = SelectArea(true, out snapshot); if (selection is not { } area) return null;
        var window = GetAncestor(WindowFromPoint(new POINT(area.X + area.Width / 2, area.Y + area.Height / 2)), 2);
        if (window == 0) return null;
        Native.GetWindowThreadProcessId(window, out var process);
        if (process == Environment.ProcessId) throw new InvalidOperationException("Yakalanacak başka bir uygulama penceresi seçin.");
        return WindowArea(window);
    }
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out RECT value, int size);
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder value, int length, out int needed);
}

/// <summary>Physical coordinates on each monitor, including monitors with different DPI.</summary>
internal sealed class RegionPicker : Window
{
    private readonly List<RegionSurface> surfaces = [];
    private readonly List<CaptureArea> regions = [];
    private readonly bool quickCapture;
    private readonly bool multiple;
    private CaptureArea? selection;
    internal FrozenDesktop? Snapshot { get; private set; }
    internal CaptureAnnotations? Annotations { get; }
    public CaptureArea? Result { get; private set; }
    public IReadOnlyList<CaptureArea> Results { get; private set; } = [];
    public RegionPicker(bool quickCapture, bool multiple = false, bool allowDrawing = false)
    {
        this.quickCapture = quickCapture; this.multiple = multiple;
        Annotations = allowDrawing ? new() : null;
        Width = Height = 0; WindowStyle = WindowStyle.None; ShowInTaskbar = false; Opacity = 0;
        Loaded += (_, _) =>
        {
            var displays = DesktopDisplays.Enumerate().ToArray();
            // Capture every monitor before showing any overlay, including on mixed-DPI desktops.
            Snapshot = FrozenDesktop.Capture(displays);
            foreach (var monitor in displays)
            {
                var surface = new RegionSurface(monitor, Snapshot.ForDisplay(monitor), quickCapture, Select, UpdateSelection, FinishSelected, Finish, UpdateHover, () => selection, () => regions, Annotations, RefreshAnnotations); surfaces.Add(surface); surface.Show();
            }
        };
        Closed += (_, _) => { foreach (var surface in surfaces) surface.Close(); };
    }
    private void RefreshAnnotations() { foreach (var surface in surfaces) surface.RefreshAnnotations(); }
    private void UpdateHover(CaptureArea? area)
    {
        foreach (var surface in surfaces) surface.SetHover(area);
    }
    private void Select(CaptureArea area)
    {
        if (!area.IsValid) return;
        if (!multiple) regions.Clear();
        if (regions.Count >= 20) return;
        regions.Add(area);
        selection = area;
        foreach (var surface in surfaces) surface.SetSelections(regions);
        if (quickCapture) FinishSelected();
    }
    private void FinishSelected()
    {
        if (multiple && regions.Count > 0) { Results = regions.ToArray(); Finish(regions[0]); }
        else if (selection is { IsValid: true } area) Finish(area);
    }
    private void UpdateSelection(CaptureArea original, CaptureArea area)
    {
        var index = regions.LastIndexOf(original);
        if (index < 0) { Select(area); return; }
        regions[index] = area; selection = area;
        foreach (var surface in surfaces) surface.SetSelections(regions);
    }
    private void Finish(CaptureArea? area) { Result = area; if (area is null) Results = []; Close(); }
    private sealed class RegionSurface : Window
    {
        private readonly System.Windows.Controls.Canvas canvas = new();
        private readonly System.Windows.Shapes.Rectangle border = new() { Stroke = Brushes.DodgerBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 80, 160, 255)) };
        private readonly List<System.Windows.Shapes.Rectangle> selectionBorders = [];
        private readonly System.Windows.Shapes.Rectangle windowBorder = new() { Stroke = Brushes.Gold, StrokeThickness = 3, StrokeDashArray = [5, 3], Fill = Brushes.Transparent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly DesktopDisplay display;
        private readonly bool quickCapture;
        private readonly Action<CaptureArea> select;
        private readonly Action<CaptureArea, CaptureArea> updateSelection;
        private readonly Action finishSelected;
        private readonly Func<CaptureArea?> getSelection;
        private readonly Func<IReadOnlyList<CaptureArea>> getSelections;
        private readonly CaptureAnnotations? annotations;
        private readonly Action refreshAnnotations;
        private readonly Image annotationImage = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
        private FrameworkElement? toolbar;
        private readonly Dictionary<DrawingTool, Button> toolButtons = [];
        private ComboBox? colorPicker, lineWidthPicker;
        private bool updatingOptions;
        private FrameworkElement? drawingOptions, geometryOptions, textOptions;
        private ComboBox? geometryPicker;
        private TextBox? annotationText;
        private POINT? origin;
        private CaptureArea? initialArea;
        private bool moving, resizing, resizeLeft, resizeTop;
        public RegionSurface(DesktopDisplay display, BitmapSource frozenImage, bool quickCapture, Action<CaptureArea> select, Action<CaptureArea, CaptureArea> updateSelection, Action finishSelected, Action<CaptureArea?> finish, Action<CaptureArea?> updateHover, Func<CaptureArea?> getSelection, Func<IReadOnlyList<CaptureArea>> getSelections, CaptureAnnotations? annotations, Action refreshAnnotations)
        {
            this.display = display;
            this.quickCapture = quickCapture; this.select = select; this.finishSelected = finishSelected;
            this.updateSelection = updateSelection;
            this.getSelection = getSelection;
            this.getSelections = getSelections;
            this.annotations = annotations; this.refreshAnnotations = refreshAnnotations;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)); Focusable = true;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Cursor = System.Windows.Input.Cursors.Cross;
            var backdrop = new DrawingGroup();
            using (var drawing = backdrop.Open())
            {
                var bounds = new Rect(0, 0, frozenImage.PixelWidth, frozenImage.PixelHeight);
                drawing.DrawImage(frozenImage, bounds);
                drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), null, bounds);
            }
            backdrop.Freeze(); canvas.Background = new DrawingBrush(backdrop) { Stretch = Stretch.Fill };
            Content = canvas; canvas.Children.Add(annotationImage); canvas.Children.Add(windowBorder); canvas.Children.Add(border); border.Visibility = Visibility.Collapsed;
            if (annotations is not null)
            {
                toolbar = CreateAnnotationToolbar(finish);
                System.Windows.Controls.Canvas.SetLeft(toolbar, 16); System.Windows.Controls.Canvas.SetTop(toolbar, 16);
                System.Windows.Controls.Panel.SetZIndex(toolbar, 100); canvas.Children.Add(toolbar);
                Loaded += (_, _) => RefreshAnnotations();
            }
            else if (!quickCapture)
            {
                var instructions = new TextBlock { Text = "Alan çiz · içinden sürükleyerek taşı · kenarından boyutlandır · Enter yakala · Esc iptal", Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(220, 20, 24, 32)), Padding = new Thickness(12), IsHitTestVisible = false };
                System.Windows.Controls.Canvas.SetLeft(instructions, 16); System.Windows.Controls.Canvas.SetTop(instructions, 16); canvas.Children.Add(instructions);
            }
            SourceInitialized += (_, _) => Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST,
                display.Bounds.Left, display.Bounds.Top, display.Bounds.Right - display.Bounds.Left, display.Bounds.Bottom - display.Bounds.Top, 0x0040);
            KeyDown += (_, e) =>
            {
                if (e.OriginalSource is TextBox && e.Key != System.Windows.Input.Key.Escape)
                {
                    if (e.Key == System.Windows.Input.Key.Enter) { Focus(); e.Handled = true; }
                    return;
                }
                if (e.Key == System.Windows.Input.Key.Escape) { finish(null); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.Enter) { if (annotations?.IsDrawing != true) CaptureSelection(); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.Z && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) && annotations is not null)
                { annotations.Undo(); refreshAnnotations(); e.Handled = true; }
            };
            MouseDoubleClick += (_, e) => { if (!IsToolbarInput(e.OriginalSource) && (annotations is null || annotations.Tool == DrawingTool.Select) && e.ChangedButton == System.Windows.Input.MouseButton.Left) finishSelected(); };
            MouseLeftButtonDown += (_, e) =>
            {
                if (IsToolbarInput(e.OriginalSource)) return;
                Focus();
                if (annotations is not null && annotations.Tool != DrawingTool.Select)
                {
                    Native.GetCursorPos(out var mark);
                    if (annotations.Tool == DrawingTool.Text)
                    {
                        annotations.AddText(new Point(mark.X, mark.Y)); refreshAnnotations(); e.Handled = true; return;
                    }
                    annotations.Begin(new Point(mark.X, mark.Y));
                    CaptureMouse(); refreshAnnotations(); e.Handled = true; return;
                }
                Native.GetCursorPos(out var point); var selected = getSelections().Where(area => Contains(area, point)).Select(area => (CaptureArea?)area).LastOrDefault() ?? getSelection();
                if (!quickCapture && selected is { IsValid: true } area && Contains(area, point))
                {
                    initialArea = area; origin = point;
                    resizeLeft = Math.Abs(point.X - area.X) <= 10;
                    resizeTop = Math.Abs(point.Y - area.Y) <= 10;
                    resizing = resizeLeft || Math.Abs(point.X - (area.X + area.Width)) <= 10 || resizeTop || Math.Abs(point.Y - (area.Y + area.Height)) <= 10;
                    moving = !resizing; CaptureMouse(); return;
                }
                origin = point; initialArea = null; moving = resizing = false;
                border.Visibility = Visibility.Visible; windowBorder.Visibility = Visibility.Collapsed; CaptureMouse();
            };
            MouseMove += (_, _) =>
            {
                Native.GetCursorPos(out var end);
                if (annotations?.IsDrawing == true) { annotations.Move(new Point(end.X, end.Y)); refreshAnnotations(); return; }
                if (annotations is not null && annotations.Tool != DrawingTool.Select) { Cursor = System.Windows.Input.Cursors.Pen; windowBorder.Visibility = Visibility.Collapsed; return; }
                if (origin is not { } start)
                {
                    var selected = getSelections().Where(area => Contains(area, end)).Select(area => (CaptureArea?)area).LastOrDefault() ?? getSelection();
                    if (!quickCapture && selected is { IsValid: true } region && Contains(region, end))
                    {
                        var nearHorizontal = Math.Abs(end.X - region.X) <= 10 || Math.Abs(end.X - (region.X + region.Width)) <= 10;
                        var nearVertical = Math.Abs(end.Y - region.Y) <= 10 || Math.Abs(end.Y - (region.Y + region.Height)) <= 10;
                        Cursor = nearHorizontal && nearVertical ? System.Windows.Input.Cursors.SizeNWSE : nearHorizontal ? System.Windows.Input.Cursors.SizeWE : nearVertical ? System.Windows.Input.Cursors.SizeNS : System.Windows.Input.Cursors.SizeAll;
                        return;
                    }
                    Cursor = System.Windows.Input.Cursors.Cross;
                    updateHover(CaptureServices.WindowAtPoint(end));
                    return;
                }
                if (initialArea is { } original && moving) { SetSelection(new(original.X + end.X - start.X, original.Y + end.Y - start.Y, original.Width, original.Height)); return; }
                if (initialArea is { } resized && resizing)
                {
                    var left = resizeLeft ? resized.X + end.X - start.X : resized.X;
                    var topEdge = resizeTop ? resized.Y + end.Y - start.Y : resized.Y;
                    var right = resizeLeft ? resized.X + resized.Width : resized.X + resized.Width + end.X - start.X;
                    var bottom = resizeTop ? resized.Y + resized.Height : resized.Y + resized.Height + end.Y - start.Y;
                    if (right - left < 24) { if (resizeLeft) left = right - 24; else right = left + 24; }
                    if (bottom - topEdge < 24) { if (resizeTop) topEdge = bottom - 24; else bottom = topEdge + 24; }
                    SetSelection(new(left, topEdge, right - left, bottom - topEdge)); return;
                }
                var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                var top = scale.Transform(new Point(Math.Min(start.X, end.X) - display.Bounds.Left, Math.Min(start.Y, end.Y) - display.Bounds.Top));
                var size = scale.Transform(new Vector(Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y)));
                System.Windows.Controls.Canvas.SetLeft(border, top.X); System.Windows.Controls.Canvas.SetTop(border, top.Y); border.Width = size.X; border.Height = size.Y;
            };
            MouseLeftButtonUp += (_, _) =>
            {
                if (annotations?.IsDrawing == true)
                {
                    Native.GetCursorPos(out var mark); annotations.Move(new Point(mark.X, mark.Y)); annotations.Complete();
                    ReleaseMouseCapture(); refreshAnnotations(); return;
                }
                if (origin is not { } start) return; ReleaseMouseCapture(); Native.GetCursorPos(out var end);
                origin = null;
                if (initialArea is { } edited)
                {
                    var original = initialArea.Value;
                    initialArea = null; moving = resizing = resizeLeft = resizeTop = false; updateSelection(original, edited); return;
                }
                if (Math.Abs(start.X - end.X) <= 6 && Math.Abs(start.Y - end.Y) <= 6)
                {
                    var window = CaptureServices.WindowAtPoint(start);
                    if (window is { IsValid: true }) select(window.Value);
                    else if (quickCapture) finish(null);
                    else SetSelection(null);
                }
                else select(new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y)));
            };
            LostMouseCapture += (_, _) => { if (annotations?.IsDrawing == true) { annotations.Cancel(); refreshAnnotations(); } };
        }
        private bool IsToolbarInput(object source)
        {
            for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, toolbar)) return true;
            return false;
        }
        private FrameworkElement CreateAnnotationToolbar(Action<CaptureArea?> finish)
        {
            var panel = new WrapPanel { MaxWidth = 850 };
            var tools = new[] {
                (DrawingTool.Select, "Alan seç", "M3,8 L3,3 8,3 M16,3 L21,3 21,8 M21,16 L21,21 16,21 M8,21 L3,21 3,16 M7,7 L17,7 17,17 7,17 Z"),
                (DrawingTool.Freehand, "Kalem", "M4,16 L15,5 19,9 8,20 3,21 Z M13,7 L17,11"),
                (DrawingTool.Rectangle, "Geometri", "M3,4 L14,4 14,15 3,15 Z M17,10 A6,6 0 1 1 10,17"),
                (DrawingTool.Text, "Metin", "M4,5 L20,5 M12,5 L12,21 M8,21 L16,21") };
            foreach (var (tool, label, path) in tools)
            {
                var button = CaptureUi.Button("", () => {
                    annotations!.Tool = tool == DrawingTool.Rectangle ? (DrawingTool)(geometryPicker?.SelectedValue ?? DrawingTool.Rectangle) : tool;
                    refreshAnnotations();
                    if (tool == DrawingTool.Text) annotationText?.Focus();
                });
                button.Content = new System.Windows.Shapes.Path {
                    Data = Geometry.Parse(path), Stroke = Brushes.White, StrokeThickness = 1.7,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Width = 24, Height = 24, Stretch = Stretch.Uniform };
                button.Width = 44; button.Height = 44; button.Padding = new Thickness(10);
                button.ToolTip = label;
                System.Windows.Automation.AutomationProperties.SetName(button, label);
                toolButtons[tool] = button; panel.Children.Add(button);
            }
            var options = new WrapPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(4, 0, 4, 0) };
            drawingOptions = options;
            var shapes = new ComboBox { Width = 120, Margin = new Thickness(4), SelectedValuePath = "Tag", ToolTip = "Geometrik şekil" };
            foreach (var (tool, name) in new[] { (DrawingTool.Rectangle, "Dikdörtgen"), (DrawingTool.Ellipse, "Elips"), (DrawingTool.Arrow, "Ok"), (DrawingTool.Line, "Çizgi") })
                shapes.Items.Add(new ComboBoxItem { Content = name, Tag = tool });
            shapes.SelectedIndex = 0;
            shapes.SelectionChanged += (_, _) => { if (!updatingOptions) { annotations!.Tool = (DrawingTool)shapes.SelectedValue; refreshAnnotations(); } };
            geometryPicker = shapes; geometryOptions = shapes; options.Children.Add(shapes);
            var text = new TextBox { Width = 220, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4), Text = annotations!.Text, ToolTip = "Metni yazın, ardından ekranda yerleştirilecek noktaya tıklayın" };
            System.Windows.Automation.AutomationProperties.SetName(text, "Eklenecek metin");
            text.TextChanged += (_, _) => annotations!.Text = text.Text;
            annotationText = text; textOptions = text; options.Children.Add(text);
            var colors = new ComboBox { Width = 90, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Çizim rengi" };
            foreach (var (color, name) in new[] { (Colors.Crimson, "Kırmızı"), (Colors.Gold, "Sarı"), (Colors.DodgerBlue, "Mavi"), (Colors.LimeGreen, "Yeşil"), (Colors.White, "Beyaz"), (Colors.Black, "Siyah") })
                colors.Items.Add(new ComboBoxItem { Content = name, Tag = color });
            colors.SelectedIndex = 0;
            colors.SelectionChanged += (_, _) => { if (!updatingOptions) { annotations!.Color = (Color)((ComboBoxItem)colors.SelectedItem).Tag; refreshAnnotations(); } };
            colorPicker = colors; options.Children.Add(colors);
            var width = new ComboBox { Width = 60, Margin = new Thickness(4), ItemsSource = new[] { 2, 4, 8, 12 }, SelectedItem = 4, ToolTip = "Çizgi kalınlığı (px)" };
            width.SelectionChanged += (_, _) => { if (!updatingOptions) { annotations!.Width = (int)width.SelectedItem; refreshAnnotations(); } };
            lineWidthPicker = width; options.Children.Add(width);
            panel.Children.Add(CaptureUi.Button("Geri al", () => { annotations!.Undo(); refreshAnnotations(); }));
            panel.Children.Add(CaptureUi.Button("Temizle", () => { annotations!.Clear(); refreshAnnotations(); }));
            var capture = CaptureUi.Button("Yakala ↵", CaptureSelection);
            capture.Background = new SolidColorBrush(Color.FromRgb(35, 93, 149)); panel.Children.Add(capture);
            panel.Children.Add(CaptureUi.Button("İptal", () => finish(null)));
            foreach (var button in panel.Children.OfType<Button>()) button.Foreground = Brushes.White;
            var stack = new StackPanel(); stack.Children.Add(panel); stack.Children.Add(options);
            stack.Children.Add(new TextBlock { Text = "Alan seç · Kalem / Geometri / Metin ile ekrana ekle · Enter yakala · Esc iptal", Foreground = Brushes.White, Margin = new Thickness(8, 2, 8, 6) });
            return new Border { Background = new SolidColorBrush(Color.FromArgb(245, 20, 24, 32)), CornerRadius = new CornerRadius(8), Child = stack };
        }
        private void CaptureSelection()
        {
            if (annotations is not null && getSelection() is not { IsValid: true })
                select(new(display.Bounds.Left, display.Bounds.Top, display.Bounds.Right - display.Bounds.Left, display.Bounds.Bottom - display.Bounds.Top));
            finishSelected();
        }
        public void RefreshAnnotations()
        {
            if (annotations is null) return;
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var size = transform.Transform(new Vector(display.Bounds.Right - display.Bounds.Left, display.Bounds.Bottom - display.Bounds.Top));
            if (toolbar is not null) toolbar.MaxWidth = Math.Max(1, size.X - 32);
            Cursor = annotations.Tool == DrawingTool.Select ? System.Windows.Input.Cursors.Cross : System.Windows.Input.Cursors.Pen;
            var geometry = annotations.Tool is DrawingTool.Rectangle or DrawingTool.Ellipse or DrawingTool.Arrow or DrawingTool.Line;
            if (drawingOptions is not null) drawingOptions.Visibility = annotations.Tool == DrawingTool.Select ? Visibility.Collapsed : Visibility.Visible;
            if (geometryOptions is not null) geometryOptions.Visibility = geometry ? Visibility.Visible : Visibility.Collapsed;
            if (textOptions is not null) textOptions.Visibility = annotations.Tool == DrawingTool.Text ? Visibility.Visible : Visibility.Collapsed;
            windowBorder.Visibility = Visibility.Collapsed;
            foreach (var (tool, button) in toolButtons)
            {
                var selected = tool == annotations.Tool || tool == DrawingTool.Rectangle && geometry;
                button.Background = selected ? new SolidColorBrush(Color.FromRgb(52, 73, 100)) : Brushes.Transparent;
                button.Foreground = Brushes.White;
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "Seçili" : "");
            }
            updatingOptions = true;
            try
            {
                if (geometry && geometryPicker is not null) geometryPicker.SelectedValue = annotations.Tool;
                if (colorPicker is not null) colorPicker.SelectedItem = colorPicker.Items.Cast<ComboBoxItem>().First(item => Equals(item.Tag, annotations.Color));
                if (lineWidthPicker is not null) lineWidthPicker.SelectedItem = (int)annotations.Width;
            }
            finally { updatingOptions = false; }
            annotationImage.Width = size.X; annotationImage.Height = size.Y;
            annotationImage.Source = annotations.Preview(new(display.Bounds.Left, display.Bounds.Top, display.Bounds.Right - display.Bounds.Left, display.Bounds.Bottom - display.Bounds.Top));
        }
        private static bool Contains(CaptureArea area, POINT point) => point.X >= area.X && point.Y >= area.Y && point.X < area.X + area.Width && point.Y < area.Y + area.Height;
        public void SetSelections(IEnumerable<CaptureArea> areas)
        {
            foreach (var old in selectionBorders) canvas.Children.Remove(old);
            selectionBorders.Clear();
            foreach (var area in areas)
            {
                var outline = new System.Windows.Shapes.Rectangle { Stroke = Brushes.DodgerBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 80, 160, 255)), IsHitTestVisible = false };
                canvas.Children.Add(outline); selectionBorders.Add(outline); SetHover(area, outline, Brushes.DodgerBlue, Visibility.Visible);
            }
        }
        public void SetSelection(CaptureArea? area)
        {
            var items = getSelections().ToList();
            var index = initialArea is { } original ? items.LastIndexOf(original) : items.Count - 1;
            if (index >= 0)
            {
                if (area is { IsValid: true } updated) items[index] = updated;
                else items.RemoveAt(index);
            }
            SetSelections(items);
        }
        public void SetHover(CaptureArea? area)
            => SetHover(area, windowBorder, Brushes.Gold, area is null ? Visibility.Collapsed : Visibility.Visible);
        private void SetHover(CaptureArea? area, System.Windows.Shapes.Rectangle shape, Brush color, Visibility visibility)
        {
            if (origin is not null && shape == windowBorder) return;
            if (area is not { IsValid: true } selected || selected.X >= display.Bounds.Right || selected.Y >= display.Bounds.Bottom || selected.X + selected.Width <= display.Bounds.Left || selected.Y + selected.Height <= display.Bounds.Top)
            {
                shape.Visibility = Visibility.Collapsed;
                return;
            }
            var left = Math.Max(selected.X, display.Bounds.Left); var top = Math.Max(selected.Y, display.Bounds.Top);
            var right = Math.Min(selected.X + selected.Width, display.Bounds.Right); var bottom = Math.Min(selected.Y + selected.Height, display.Bounds.Bottom);
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var position = transform.Transform(new Point(left - display.Bounds.Left, top - display.Bounds.Top));
            var size = transform.Transform(new Vector(right - left, bottom - top));
            System.Windows.Controls.Canvas.SetLeft(shape, position.X); System.Windows.Controls.Canvas.SetTop(shape, position.Y);
            shape.Width = size.X; shape.Height = size.Y; shape.Visibility = visibility;
        }
    }
}
