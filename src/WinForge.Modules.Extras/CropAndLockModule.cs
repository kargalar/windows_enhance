using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class CropAndLockModule : ITweakModule
{
    private const int VkT = 0x54;
    private const uint CropModifiers = HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.Shift;
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private CropThumbnailWindow? _thumbnail;

    public string Id => "crop-and-lock";
    public string Name => "Crop And Lock · Pencere Kırpma";
    public string Category => "Pencereler";
    public string Description => "Bir uygulama penceresinin alanını kırpar ve canlı küçük pencerede izler.";
    public string? Warning => "Bu ilk sürüm canlı Thumbnail modudur; kırpılmış pencereden kaynak uygulamaya tıklayarak komut gönderemezsiniz.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public CropAndLockModule()
    {
        Settings =
        [
            new SettingDescriptor
            {
                Key = "start-crop",
                Label = "Etkin pencereyi kırp",
                Kind = SettingKind.Action,
                Description = "Win+Ctrl+Shift+T kısayolunu da kullanabilirsiniz.",
                Execute = StartCrop
            },
            new SettingDescriptor
            {
                Key = "hotkey-enabled",
                Label = "Win+Ctrl+Shift+T kısayolu etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Etkin pencerenin alanını seçerek canlı küçük pencere oluşturur."
            }
        ];
    }

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        try
        {
            RegisterHotkey();
            IsActive = true;
        }
        catch
        {
            _hotkey?.Dispose();
            _hotkey = null;
            throw;
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
        _thumbnail?.Close();
        _thumbnail = null;
    }

    public void Apply()
    {
        if (!IsActive) return;
        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        if (_context?.Settings.GetBool("hotkey-enabled", true) == true)
            _hotkey = _context.Hotkeys.Register(CropModifiers, VkT, StartCrop);
    }

    private void StartCrop()
    {
        if (!IsActive) return;
        var source = Native.GetForegroundWindow();
        if (source == IntPtr.Zero || !Native.IsWindowVisible(source) || !Native.GetWindowRect(source, out var bounds)) return;

        try
        {
            var selector = new CropSelectionOverlay(bounds);
            if (selector.ShowDialog() != true || selector.Selection is not { } selection) return;
            var sourceRect = CropSelectionMath.ToWindowClientRect(selection, bounds);
            if (sourceRect is null)
            {
                _context?.Notify("Seçim etkin pencerenin dışındaydı veya çok küçüktü.");
                return;
            }

            _thumbnail?.Close();
            _thumbnail = new CropThumbnailWindow(source, sourceRect.Value, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            _thumbnail.Closed += (_, _) => _thumbnail = null;
            _thumbnail.Show();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Pencere kırpma başarısız.", exception);
            _context?.Notify("Pencere kırpılamadı: " + exception.Message);
        }
    }
}

internal readonly record struct CropScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

internal static class CropSelectionMath
{
    public static RECT? ToWindowClientRect(CropScreenRect selection, RECT windowBounds)
    {
        var left = Math.Max(selection.Left, windowBounds.Left) - windowBounds.Left;
        var top = Math.Max(selection.Top, windowBounds.Top) - windowBounds.Top;
        var right = Math.Min(selection.Right, windowBounds.Right) - windowBounds.Left;
        var bottom = Math.Min(selection.Bottom, windowBounds.Bottom) - windowBounds.Top;
        if (right - left < 8 || bottom - top < 8) return null;
        if (left != selection.Left - windowBounds.Left || top != selection.Top - windowBounds.Top ||
            right != selection.Right - windowBounds.Left || bottom != selection.Bottom - windowBounds.Top) return null;
        return new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
    }
}

internal sealed class CropSelectionOverlay : Window
{
    private readonly RECT _sourceBounds;
    private readonly Canvas _canvas = new();
    private readonly Border _selection;
    private readonly Border _hint;
    private Point? _origin;
    public CropScreenRect? Selection { get; private set; }

    public CropSelectionOverlay(RECT sourceBounds)
    {
        _sourceBounds = sourceBounds;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(88, 4, 8, 16));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _selection = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(76, 194, 255)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(38, 76, 194, 255)),
            Visibility = Visibility.Collapsed
        };
        _hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 18, 21, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(76, 194, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 9, 16, 9),
            Child = new TextBlock
            {
                Text = "Kaynak pencerenin içinde alanı sürükleyerek seçin · Esc iptal",
                Foreground = Brushes.White,
                FontSize = 13
            }
        };
        _canvas.Children.Add(_selection);
        _canvas.Children.Add(_hint);
        Content = _canvas;
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
            _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_hint, Math.Max(0, (Width - _hint.DesiredSize.Width) / 2));
            Canvas.SetTop(_hint, 36);
        };
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs args)
    {
        _origin = args.GetPosition(_canvas);
        _hint.Visibility = Visibility.Collapsed;
        _selection.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs args)
    {
        if (_origin is not { } origin) return;
        var current = args.GetPosition(_canvas);
        Canvas.SetLeft(_selection, Math.Min(origin.X, current.X));
        Canvas.SetTop(_selection, Math.Min(origin.Y, current.Y));
        _selection.Width = Math.Abs(current.X - origin.X);
        _selection.Height = Math.Abs(current.Y - origin.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs args)
    {
        if (_origin is null) return;
        ReleaseMouseCapture();
        var local = new Rect(Canvas.GetLeft(_selection), Canvas.GetTop(_selection), _selection.Width, _selection.Height);
        var handle = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(handle, out var overlayBounds);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var start = transform.Transform(local.TopLeft);
        var end = transform.Transform(local.BottomRight);
        Selection = new CropScreenRect(
            overlayBounds.Left + (int)Math.Round(start.X),
            overlayBounds.Top + (int)Math.Round(start.Y),
            overlayBounds.Left + (int)Math.Round(end.X),
            overlayBounds.Top + (int)Math.Round(end.Y));
        DialogResult = CropSelectionMath.ToWindowClientRect(Selection.Value, _sourceBounds) is not null;
        Close();
    }
}

internal sealed class CropThumbnailWindow : Window
{
    private const uint ThumbnailSourceClientAreaOnlyFlag = 0x00000010;
    private const uint ThumbnailOpacity = 0x00000004;
    private const uint DestinationRect = 0x00000001;
    private const uint SourceRect = 0x00000002;
    private const uint ThumbnailVisibleFlag = 0x00000008;
    private IntPtr _thumbnail;
    private readonly IntPtr _source;
    private readonly RECT _sourceRect;

    public CropThumbnailWindow(IntPtr source, RECT sourceRect, int sourceWidth, int sourceHeight)
    {
        _source = source;
        _sourceRect = sourceRect;
        Title = "WinForge · Kırpılmış Canlı Görünüm";
        Width = Math.Clamp(sourceWidth, 220, 800);
        Height = Math.Clamp(sourceHeight + 34, 150, 620);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(24, 29, 38));
        var root = new DockPanel();
        var header = new DockPanel { Height = 32, Margin = new Thickness(9, 4, 5, 0) };
        var close = new Button { Content = "×", Width = 32, Height = 27, FontSize = 18, Padding = new Thickness(0), ToolTip = "Kırpılmış görünümü kapat" };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock
        {
            Text = "CANLI KIRPMA · Kaynak uygulama denetlenemez",
            Foreground = Brushes.White,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        Content = root;
        SourceInitialized += (_, _) => RegisterThumbnail();
        Loaded += (_, _) => UpdateThumbnail();
        SizeChanged += (_, _) => UpdateThumbnail();
        Closed += (_, _) => UnregisterThumbnail();
    }

    private void RegisterThumbnail()
    {
        var destination = new WindowInteropHelper(this).Handle;
        var result = DwmRegisterThumbnail(destination, _source, out _thumbnail);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        UpdateThumbnail();
    }

    private void UpdateThumbnail()
    {
        if (_thumbnail == IntPtr.Zero || !IsVisible) return;
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var client = new RECT();
            GetClientRect(handle, out client);
            var headerHeight = (int)Math.Ceiling(36 * (Native.GetDpiForWindow(handle) / 96d));
            var properties = new DwmThumbnailProperties
            {
                Flags = DestinationRect | SourceRect | ThumbnailOpacity | ThumbnailVisibleFlag | ThumbnailSourceClientAreaOnlyFlag,
                Destination = new RECT { Left = 0, Top = headerHeight, Right = client.Right, Bottom = client.Bottom },
                Source = _sourceRect,
                Opacity = 255,
                Visible = true,
                SourceClientAreaOnly = false
            };
            var result = DwmUpdateThumbnailProperties(_thumbnail, ref properties);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }
        catch { }
    }

    private void UnregisterThumbnail()
    {
        if (_thumbnail == IntPtr.Zero) return;
        DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public uint Flags;
        public RECT Destination;
        public RECT Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DwmThumbnailProperties properties);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RECT rect);
}
