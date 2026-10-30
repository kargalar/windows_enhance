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
            var selector = new CropSelectionOverlay(bounds, (_context as IFeatureUiContext)?.FeatureUi);
            if (selector.ShowDialog() != true || selector.Selection is not { } selection) return;
            var sourceRect = CropSelectionMath.ToWindowClientRect(selection, bounds);
            if (sourceRect is null)
            {
                _context?.Notify("Seçim etkin pencerenin dışındaydı veya çok küçüktü.");
                return;
            }

            _thumbnail?.Close();
            _thumbnail = new CropThumbnailWindow(source, sourceRect.Value, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                (_context as IFeatureUiContext)?.FeatureUi);
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
    private readonly RECT sourceBounds;
    private readonly IFeatureUiSurface uiSurface;
    public CropScreenRect? Selection { get; private set; }

    public CropSelectionOverlay(RECT sourceBounds, IFeatureUiService? featureUi)
    {
        this.sourceBounds = sourceBounds;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        uiSurface = featureUi?.CreateSurface("crop-selection")
            ?? throw new InvalidOperationException("Kırpma seçimi için React arayüzü gerekli.");
        Content = uiSurface.Element;
        uiSurface.MessageReceived += OnUiMessage;
        uiSurface.SetState(new { hint = "Kaynak pencerenin içinde alanı sürükleyerek seçin · Esc iptal" });
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
        if (command == "crop-selection.cancel")
        {
            DialogResult = false;
            Close();
            return;
        }
        if (command != "crop-selection.complete" ||
            !payload.TryGetProperty("x", out var xValue) ||
            !payload.TryGetProperty("y", out var yValue) ||
            !payload.TryGetProperty("width", out var widthValue) ||
            !payload.TryGetProperty("height", out var heightValue)) return;

        var matrix = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var handle = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(handle, out var overlayBounds);
        var left = (int)Math.Round(xValue.GetDouble() * matrix.M11);
        var top = (int)Math.Round(yValue.GetDouble() * matrix.M22);
        var right = (int)Math.Round((xValue.GetDouble() + widthValue.GetDouble()) * matrix.M11);
        var bottom = (int)Math.Round((yValue.GetDouble() + heightValue.GetDouble()) * matrix.M22);
        Selection = new CropScreenRect(
            overlayBounds.Left + left,
            overlayBounds.Top + top,
            overlayBounds.Left + right,
            overlayBounds.Top + bottom);
        DialogResult = CropSelectionMath.ToWindowClientRect(Selection.Value, sourceBounds) is not null;
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
    private readonly IFeatureUiSurface _uiSurface;

    public CropThumbnailWindow(IntPtr source, RECT sourceRect, int sourceWidth, int sourceHeight, IFeatureUiService? featureUi)
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
        Background = Brushes.Transparent;
        _uiSurface = featureUi?.CreateSurface("crop-thumbnail")
            ?? throw new InvalidOperationException("Kırpma önizlemesi için React arayüzü gerekli.");
        Content = _uiSurface.Element;
        _uiSurface.MessageReceived += OnUiMessage;
        _uiSurface.SetState(new { title = "Canlı kırpma", subtitle = "Kaynak uygulama denetlenemez" });
        Closed += (_, _) =>
        {
            _uiSurface.MessageReceived -= OnUiMessage;
            _uiSurface.Dispose();
        };
        SourceInitialized += (_, _) => RegisterThumbnail();
        Loaded += (_, _) => UpdateThumbnail();
        SizeChanged += (_, _) => UpdateThumbnail();
        Closed += (_, _) => UnregisterThumbnail();
    }

    private void OnUiMessage(string command, System.Text.Json.JsonElement payload)
    {
        if (command == "crop-thumbnail.close") Close();
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
