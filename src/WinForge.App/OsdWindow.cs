using System.Windows;
using WinForge.Core;

namespace WinForge.App;

public static class OsdWindow
{
    private const string ToastKey = "osd-toast";
    private const string BrightnessKey = "osd-brightness";
    private const string VolumeKey = "osd-volume";
    private static IFeatureUiService? _featureUi;
    private static IFeatureUiWindow? _toastView;
    private static IFeatureUiWindow? _brightnessView;
    private static IFeatureUiWindow? _volumeView;
    private static System.Threading.Timer? _toastTimer;
    private static System.Threading.Timer? _brightnessTimer;
    private static System.Threading.Timer? _volumeTimer;
    private static Action? _volumeChanged;
    private static int _volumeDismissSeconds = 3;
    private static int _volumeHovered;
    private static int _volumePercent;
    private static bool _volumeMuted;

    public static void Configure(IFeatureUiService featureUi) => _featureUi = featureUi;

    public static void Dispose()
    {
        _toastTimer?.Dispose();
        _brightnessTimer?.Dispose();
        _volumeTimer?.Dispose();
        _toastTimer = null;
        _brightnessTimer = null;
        _volumeTimer = null;
        _toastView = null;
        _brightnessView = null;
        _volumeView = null;
        _volumeChanged = null;
        _featureUi = null;
    }

    public static void Toast(string message) => OnUiThread(() =>
    {
        var window = EnsureWindow(ref _toastView, ToastKey, "system-osd", "WinForge bildirimi",
            new FeatureUiWindowOptions(520, 64, Topmost: true, ActivateOnShow: false,
                Transparent: true, ClickThrough: true));
        if (window is null) return;
        PositionAtBottom(window, 520, 64, 72);
        window.SetState(new { kind = "toast", message });
        RestartTimer(ref _toastTimer, Math.Clamp(1400 + message.Length * 28, 1800, 6000), () => _toastView?.Hide());
    });

    public static void ShowBrightness(int percent, string deviceName) => OnUiThread(() =>
    {
        percent = Math.Clamp(percent, 0, 100);
        var window = EnsureWindow(ref _brightnessView, BrightnessKey, "system-osd", "Ekran parlaklığı",
            new FeatureUiWindowOptions(320, 80, Topmost: true, ActivateOnShow: false,
                Transparent: true, ClickThrough: true));
        if (window is null) return;
        PositionOnDisplay(window, deviceName, 56);
        window.SetState(new { kind = "brightness", percent, deviceName });
        RestartTimer(ref _brightnessTimer, 1500, () => _brightnessView?.Hide());
    });

    public static void ShowVolume(int percent, bool isMuted, Action? onVolumeChanged = null,
        int dismissAfterSeconds = 3) => OnUiThread(() =>
    {
        _volumeChanged = onVolumeChanged;
        _volumeDismissSeconds = Math.Clamp(dismissAfterSeconds, 1, 30);
        _volumePercent = Math.Clamp(percent, 0, 100);
        _volumeMuted = isMuted;
        Volatile.Write(ref _volumeHovered, 0);
        var window = EnsureWindow(ref _volumeView, VolumeKey, "system-osd", "Ses düzeyi",
            new FeatureUiWindowOptions(360, 76, Topmost: true, ActivateOnShow: false,
                Transparent: true, ClickThrough: false));
        if (window is null) return;
        window.MessageReceived -= OnVolumeMessage;
        window.MessageReceived += OnVolumeMessage;
        PositionAtBottom(window, 360, 76, 56);
        UpdateVolumeState(window);
        RestartVolumeTimer();
    });

    private static IFeatureUiWindow? EnsureWindow(ref IFeatureUiWindow? window, string key, string view,
        string title, FeatureUiWindowOptions options)
    {
        if (_featureUi is null) return null;
        window ??= _featureUi.Open(key, view, title, options);
        window.Show();
        return window;
    }

    private static void OnVolumeMessage(string command, System.Text.Json.JsonElement payload)
    {
        try
        {
            switch (command)
            {
                case "volume.set":
                    var target = Math.Clamp(payload.GetProperty("percent").GetInt32(), 0, 100);
                    var current = VolumeService.Current();
                    _volumePercent = VolumeService.StepFromFlyout(target - current);
                    _volumeMuted = VolumeService.IsMuted();
                    _volumeChanged?.Invoke();
                    break;
                case "volume.toggle-mute":
                    _volumeMuted = !_volumeMuted;
                    VolumeService.SetMutedFromFlyout(_volumeMuted);
                    _volumeChanged?.Invoke();
                    break;
                case "volume.hover":
                    Volatile.Write(ref _volumeHovered, payload.GetProperty("value").GetBoolean() ? 1 : 0);
                    break;
                case "volume.dismiss":
                    _volumeView?.Hide();
                    return;
            }

            if (command is "volume.set" or "volume.toggle-mute")
            {
                if (_volumeView is not null) UpdateVolumeState(_volumeView);
                RestartVolumeTimer();
            }
            else if (command == "volume.hover") RestartVolumeTimer();
        }
        catch
        {
            // A transient control must not break the system audio service.
        }
    }

    private static void UpdateVolumeState(IFeatureUiWindow window) => window.SetState(new
    {
        kind = "volume",
        percent = _volumePercent,
        isMuted = _volumeMuted,
        icon = _volumeMuted ? "muted" : _volumePercent <= 33 ? "low" : _volumePercent <= 66 ? "medium" : "high"
    });

    private static void PositionAtBottom(IFeatureUiWindow window, double width, double height, double bottomOffset)
    {
        var area = SystemParameters.WorkArea;
        window.SetBounds(area.Left + (area.Width - width) / 2,
            area.Bottom - bottomOffset - height, width, height);
    }

    private static void PositionOnDisplay(IFeatureUiWindow window, string deviceName, double bottomOffset)
    {
        var area = DesktopDisplays.Enumerate()
            .FirstOrDefault(display => display.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))?.WorkArea;
        var width = 320d;
        var height = 80d;
        var workArea = area is { } displayArea
            ? new Rect(displayArea.Left, displayArea.Top, displayArea.Right - displayArea.Left, displayArea.Bottom - displayArea.Top)
            : SystemParameters.WorkArea;
        window.SetBounds(workArea.Left + (workArea.Width - width) / 2,
            workArea.Bottom - bottomOffset - height, width, height);
    }

    private static void RestartTimer(ref System.Threading.Timer? timer, int milliseconds, Action callback)
    {
        timer ??= new System.Threading.Timer(_ => OnUiThread(callback), null, Timeout.Infinite, Timeout.Infinite);
        timer.Change(milliseconds, Timeout.Infinite);
    }

    private static void RestartVolumeTimer() => RestartTimer(ref _volumeTimer,
        Volatile.Read(ref _volumeHovered) != 0 ? 500 : _volumeDismissSeconds * 1000,
        () =>
        {
            if (Volatile.Read(ref _volumeHovered) != 0) RestartVolumeTimer();
            else _volumeView?.Hide();
        });

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
