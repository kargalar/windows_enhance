using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
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

    private async void BeginPick()
    {
        if (_picking || _context is null) return;

        _picking = true;
        try
        {
            var featureUi = (_context as IFeatureUiContext)?.FeatureUi
                ?? throw new InvalidOperationException("React arayüz altyapısı kullanılamıyor.");
            using var overlay = new PickerOverlay(featureUi);
            if (await overlay.Completion is not { } point) return;

            await Task.Delay(150);
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

    private sealed class PickerOverlay : IDisposable
    {
        private readonly IFeatureUiWindow _window;
        private readonly TaskCompletionSource<POINT?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private POINT? _clickPoint;
        private bool _disposed;

        public PickerOverlay(IFeatureUiService featureUi)
        {
            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            _window = featureUi.Open(
                "color-picker-" + Guid.NewGuid().ToString("N"),
                "screen-color-picker",
                "Renk seçici",
                new FeatureUiWindowOptions(
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight,
                    Topmost: true,
                    Transparent: true,
                    Left: left,
                    Top: top));
            _window.MessageReceived += OnMessageReceived;
            _window.ClosedByHost += OnWindowClosed;
            _window.SetState(new { });
        }

        public Task<POINT?> Completion => _completion.Task;

        private void OnMessageReceived(string command, JsonElement _)
        {
            if (command == "screen-color-picker.cancel")
            {
                _completion.TrySetResult(null);
                _window.Close();
                return;
            }

            if (command == "screen-color-picker.begin")
            {
                _clickPoint = Native.GetCursorPos(out var point) ? point : null;
                return;
            }

            if (command != "screen-color-picker.commit") return;
            _completion.TrySetResult(_clickPoint);
            _window.Close();
        }

        private void OnWindowClosed() => _completion.TrySetResult(null);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.MessageReceived -= OnMessageReceived;
            _window.ClosedByHost -= OnWindowClosed;
            _window.Close();
            _window.Dispose();
            _completion.TrySetResult(null);
        }
    }
}
