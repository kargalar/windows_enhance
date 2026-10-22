using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ColorPickerModule : ITweakModule
{
    private const uint ClrInvalid = 0xFFFFFFFF;

    private static readonly string[] Formats = { "HEX", "RGB", "HSL" };

    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private bool _picking;

    public string Id => "color-picker";
    public string Name => "Renk Seçici";
    public string Category => "Verimlilik";
    public string Description => "Win+Alt+C ile renk seçici modunu açar; ekranda tıkladığınız noktanın rengini alır ve panoya kopyalar.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "hotkey-enabled",
            Label = "Win+Alt+C kısayolu etkin",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "format",
            Label = "Kopyalama biçimi",
            Kind = SettingKind.Choice,
            Options = Formats,
            Default = "HEX"
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        RegisterHotkey();
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
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
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");
        if (!context.Settings.GetBool("hotkey-enabled", true)) return;

        _hotkey = context.Hotkeys.Register(HotkeyModifiers.Win | HotkeyModifiers.Alt, 0x43 /* C */, BeginPick);
        context.Log.Info("Renk seçici kısayolu kaydedildi (Win+Alt+C).");
    }

    private void BeginPick()
    {
        if (_picking || _context is null) return;

        _picking = true;
        try
        {
            var overlay = new PickerOverlay();
            if (overlay.ShowDialog() != true || overlay.ClickPoint is not { } point) return;

            Thread.Sleep(150);
            Capture(point);
        }
        finally
        {
            _picking = false;
        }
    }

    private void Capture(POINT point)
    {
        var context = _context;
        if (context is null) return;

        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            context.Notify("Ekran okunamadı");
            return;
        }

        try
        {
            var color = GetPixel(dc, point.X, point.Y);
            if (color == ClrInvalid)
            {
                context.Notify("Renk okunamadı");
                return;
            }

            var red = (int)(color & 0xFF);
            var green = (int)((color >> 8) & 0xFF);
            var blue = (int)((color >> 16) & 0xFF);

            var format = context.Settings.GetString("format", Formats[0]);
            var text = string.Equals(format, "RGB", StringComparison.CurrentCultureIgnoreCase)
                ? $"rgb({red}, {green}, {blue})"
                : string.Equals(format, "HSL", StringComparison.CurrentCultureIgnoreCase)
                    ? ToHsl(red, green, blue)
                    : $"#{red:X2}{green:X2}{blue:X2}";

            if (!TryCopy(text))
            {
                context.Notify("Panoya kopyalanamadı");
                return;
            }

            context.Notify($"{text} · panoya kopyalandı");
        }
        catch (Exception ex)
        {
            context.Log.Error("Renk seçilemedi.", ex);
            context.Notify("Renk seçilemedi");
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private static string ToHsl(int red, int green, int blue)
    {
        var r = red / 255d;
        var g = green / 255d;
        var b = blue / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        var delta = max - min;

        double hue = 0;
        double saturation = 0;

        if (delta > 0)
        {
            saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);

            if (max == r) hue = (g - b) / delta + (g < b ? 6 : 0);
            else if (max == g) hue = (b - r) / delta + 2;
            else hue = (r - g) / delta + 4;

            hue *= 60;
        }

        return $"hsl({Math.Round(hue)}, {Math.Round(saturation * 100)}%, {Math.Round(lightness * 100)}%)";
    }

    private static bool TryCopy(string text)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);

    private sealed class PickerOverlay : Window
    {
        private static readonly Brush ClickCatcher = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        private bool _finished;
        private bool _leftDown;
        private bool _rightDown;

        public POINT? ClickPoint { get; private set; }

        public PickerOverlay()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = ClickCatcher;
            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Cursor = Cursors.Cross;
            Focusable = true;

            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            var hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 24, 0, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "Renk seçmek için tıklayın · Esc veya sağ tık: iptal",
                    Foreground = Brushes.White,
                    FontSize = 14
                }
            };

            Content = new Grid
            {
                Background = ClickCatcher,
                Children = { hint }
            };

            MouseLeftButtonDown += OnLeftDown;
            MouseLeftButtonUp += OnLeftUp;
            MouseRightButtonDown += (_, e) =>
            {
                _rightDown = true;
                e.Handled = true;
            };
            MouseRightButtonUp += (_, e) =>
            {
                if (!_rightDown) return;
                _rightDown = false;
                e.Handled = true;
                Cancel();
            };
            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Cancel();
            };

            Loaded += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0003);
                SetForegroundWindow(handle);
            };
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            Focus();
        }

        private void OnLeftDown(object sender, MouseButtonEventArgs e)
        {
            if (_finished || _leftDown) return;
            e.Handled = true;

            if (!Native.GetCursorPos(out var point))
            {
                Cancel();
                return;
            }

            ClickPoint = point;
            _leftDown = true;
        }

        private void OnLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (!_leftDown) return;
            _leftDown = false;
            if (_finished) return;
            _finished = true;
            e.Handled = true;
            DialogResult = true;
        }

        private void Cancel()
        {
            if (_finished) return;
            _finished = true;
            DialogResult = false;
        }
    }
}
