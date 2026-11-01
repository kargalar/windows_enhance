using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinForge.Core;

namespace WinForge.Modules.DynamicIsland;

internal sealed class IslandWindow : Window
{
    private const double CompactWidth = 276;
    private const double CompactHeight = 72;
    private const double ExpandedWidth = 448;
    private const double ExpandedHeight = 354;
    private const int ClipboardUpdate = 0x031D;
    private const int DeviceChange = 0x0219;
    private readonly IModuleContext _context;
    private readonly IFeatureUiSurface _surface;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly GlobalKeyboardHook _keys = new();
    private readonly IslandFiles _files = new();
    private HwndSource? _source;
    private IDisposable? _volumeWatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _closed;
    private bool _polling;
    private bool _expanded;
    private bool _pinned;
    private bool _hidden;
    private bool _hovered;
    private bool _hasPower;
    private bool _charging;
    private int _battery = -1;
    private int _volume;
    private bool _muted;
    private string _title = "";
    private string _artist = "";
    private string _artwork = "";
    private bool _playing;
    private bool _canPlay;
    private bool _canPrevious;
    private bool _canNext;
    private double _position;
    private double _duration;
    private string _clipboard = "";
    private string _alert = "";
    private string _alertKind = "";
    private DateTimeOffset _alertUntil;
    private DateTimeOffset? _focusEnd;
    private int _focusRemaining;
    private int _ticks;
    private double _scale = 1;
    private DesktopDisplay? _display;
    private double _dpi = 1;

    public IslandWindow(IModuleContext context, IFeatureUiService ui)
    {
        _context = context;
        Title = "WinForge · Dynamic Island";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = CompactWidth;
        Height = CompactHeight;
        _surface = ui.CreateSurface("dynamic-island");
        Content = _surface.Element;
        _surface.MessageReceived += OnMessage;
        _keys.KeyStateChanged += OnKey;
        _files.DownloadAdded += name => Alert($"Yeni dosya · {name}", "download");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); if (!_hovered && !_pinned) Expand(false); };
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => Position();
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (_source is null) return;
        var style = Native.GetWindowLongPtr(_source.Handle, -20).ToInt64();
        Native.SetWindowLongPtr(_source.Handle, -20, new IntPtr(style | 0x08000080));
        _source.AddHook(WindowProc);
        if (!AddClipboardFormatListener(_source.Handle)) _context.Log.Info("Dynamic Island: pano dinleyicisi başlatılamadı.");
        ApplySettings();
        _timer.Start();
        _ = PollMediaAsync();
    }

    public void ApplySettings()
    {
        _scale = Math.Clamp(_context.Settings.GetInt("scale", 100), 80, 140) / 100d;
        _display = _context.Settings.GetString("monitor", "primary") == "cursor" && Native.GetCursorPos(out var point)
            ? DesktopDisplays.ForPoint(point) : DesktopDisplays.Enumerate().First(display => display.IsPrimary);
        _dpi = GetDpiForWindow(_source?.Handle ?? IntPtr.Zero) / 96d;
        if (_dpi <= 0) _dpi = 1;
        if (!_context.Settings.GetBool("clipboard")) _clipboard = "";
        if (!_context.Settings.GetBool("media", true)) ClearMedia();
        if (_context.Settings.GetBool("keys", true))
        {
            try { _keys.Install(); }
            catch (Exception exception) { _context.Log.Error("Dynamic Island: kilit tuşları izlenemiyor.", exception); }
        }
        else _keys.Dispose();
        ConfigureVolumeWatcher();
        if (_context.Settings.GetBool("downloads", true))
        {
            try { _files.WatchDownloads(_context.Settings.GetString("downloads-folder"), action => Dispatcher.BeginInvoke(action)); }
            catch (Exception exception) { _context.Log.Error("Dynamic Island: İndirilenler klasörü izlenemiyor.", exception); }
        }
        else _files.StopDownloads();
        Resize(animate: false);
        Publish();
    }

    private void ConfigureVolumeWatcher()
    {
        _volumeWatcher?.Dispose();
        _volumeWatcher = null;
        if (!_context.Settings.GetBool("volume", true)) return;
        try
        {
            _volume = VolumeService.Current();
            _muted = VolumeService.IsMuted();
            _volumeWatcher = VolumeService.WatchDefaultEndpoint(change => Dispatcher.BeginInvoke(() =>
            {
                if (_closed || !_context.Settings.GetBool("volume", true)) return;
                _volume = change.VolumePercent;
                _muted = change.IsMuted;
                Alert(_muted ? "Ses kapalı" : $"Ses · %{_volume}", "volume");
            }));
        }
        catch (Exception exception) { _context.Log.Error("Dynamic Island: ses aygıtına erişilemiyor.", exception); }
    }

    private async void OnTick(object? sender, EventArgs args)
    {
        if (_closed) return;
        UpdateVisibility();
        UpdatePower();
        if (_focusEnd is { } end)
        {
            _focusRemaining = Math.Max(0, (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalSeconds));
            if (_focusRemaining == 0) { _focusEnd = null; Alert("Odak süren tamamlandı. Bir mola ver.", "focus"); }
        }
        if (_alert.Length > 0 && DateTimeOffset.UtcNow >= _alertUntil) { _alert = ""; _alertKind = ""; }
        Publish();
        if (++_ticks % 2 == 0 && !_hidden) await PollMediaAsync();
        // Rebind after output-device changes; the old endpoint's callback remains valid until disposed.
        if (_ticks % 15 == 0) ConfigureVolumeWatcher();
    }

    private void UpdatePower()
    {
        if (!GetSystemPowerStatus(out var power)) return;
        var battery = power.BatteryLifePercent <= 100 && (power.BatteryFlag & 128) == 0 ? power.BatteryLifePercent : -1;
        var charging = power.AcLineStatus == 1;
        if (_hasPower && _context.Settings.GetBool("power", true) && battery >= 0)
        {
            if (charging != _charging) Alert(charging ? $"Şarj oluyor · %{battery}" : $"Pil kullanılıyor · %{battery}", "power");
            else if (battery <= 20 && _battery > 20 && !charging) Alert($"Pil azalıyor · %{battery}", "power");
        }
        _hasPower = true;
        _battery = battery;
        _charging = charging;
    }

    private async Task PollMediaAsync()
    {
        if (_polling || _closed || !_context.Settings.GetBool("media", true)) return;
        _polling = true;
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_closed) return;
            var session = _manager.GetCurrentSession();
            if (session is null) { ClearMedia(); Publish(); return; }
            var properties = await session.TryGetMediaPropertiesAsync();
            if (_closed || !_context.Settings.GetBool("media", true)) return;
            var changed = session != _session || properties.Title != _title || properties.Artist != _artist;
            _session = session;
            _title = properties.Title ?? "";
            _artist = properties.Artist ?? "";
            var playback = session.GetPlaybackInfo();
            _playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _canPlay = playback.Controls.IsPlayPauseToggleEnabled;
            _canPrevious = playback.Controls.IsPreviousEnabled;
            _canNext = playback.Controls.IsNextEnabled;
            var timeline = session.GetTimelineProperties();
            _duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
            _position = Math.Clamp((timeline.Position - timeline.StartTime).TotalSeconds, 0, _duration);
            if (changed)
            {
                _artwork = "";
                if (properties.Thumbnail is { } thumbnail)
                {
                    using var stream = await thumbnail.OpenReadAsync();
                    if (stream.Size <= 4 * 1024 * 1024)
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.DecodePixelWidth = 160;
                        image.StreamSource = stream.AsStreamForRead();
                        image.EndInit();
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        using var output = new MemoryStream();
                        encoder.Save(output);
                        _artwork = "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
                    }
                }
                if (!_closed && _title.Length > 0) Alert(_title, "media");
            }
            if (!_closed) Publish();
        }
        catch (Exception exception) { if (!_closed) _context.Log.Error("Dynamic Island: medya bilgisi alınamadı.", exception); }
        finally { _polling = false; }
    }

    private void ClearMedia()
    {
        _session = null;
        _title = _artist = _artwork = "";
        _playing = false;
        _canPlay = _canPrevious = _canNext = false;
        _position = _duration = 0;
    }

    private void OnKey(uint key, bool down)
    {
        if (!down || !_context.Settings.GetBool("keys", true) || key is not (0x14 or 0x90)) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_closed) Alert($"{(key == 0x14 ? "Caps Lock" : "Num Lock")} · {((GetKeyState((int)key) & 1) != 0 ? "Açık" : "Kapalı")}", "key");
        });
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == ClipboardUpdate && _context.Settings.GetBool("clipboard"))
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    _clipboard = Clipboard.GetText();
                    _clipboard = _clipboard[..Math.Min(500, _clipboard.Length)];
                    Alert("Metin panoya kopyalandı", "clipboard");
                }
                else { _clipboard = ""; Publish(); }
            }
            catch (COMException) { /* Another application may own the clipboard momentarily. */ }
        }
        if (message == DeviceChange && _context.Settings.GetBool("usb", true))
        {
            if (wParam.ToInt64() == 0x8000) Alert("Aygıt bağlandı", "device");
            if (wParam.ToInt64() == 0x8004) Alert("Aygıt çıkarıldı", "device");
        }
        if (message is 0x007E or 0x02E0) Dispatcher.BeginInvoke(ApplySettings);
        return IntPtr.Zero;
    }

    private async void OnMessage(string command, JsonElement payload)
    {
        if (_closed) return;
        try
        {
            switch (command)
            {
                case "island.hover":
                    _hovered = payload.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True;
                    _collapseTimer.Stop();
                    if (_hovered && _context.Settings.GetBool("hover", true)) Expand(true);
                    else if (!_hovered) _collapseTimer.Start();
                    break;
                case "island.expand": if (_expanded) _pinned = false; Expand(!_expanded); break;
                case "island.pin": _pinned = !_pinned; if (_pinned) Expand(true); Publish(); break;
                case "island.hide": ToggleVisibility(); break;
                case "island.focus":
                    if (_focusEnd is not null) { _focusEnd = null; _focusRemaining = 0; }
                    else { _focusRemaining = Math.Clamp(_context.Settings.GetInt("focus-minutes", 25), 1, 120) * 60; _focusEnd = DateTimeOffset.UtcNow.AddSeconds(_focusRemaining); }
                    Publish(); break;
                case "island.clipboard.clear": _clipboard = ""; Publish(); break;
                case "island.files.add": _files.AddFiles(this); Publish(); break;
                case "island.files.clear": _files.ClearShelf(); Publish(); break;
                case "island.files.reveal":
                    if (payload.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String) _files.Reveal(path.GetString()!);
                    break;
                case "island.play" when _session is not null: await _session.TryTogglePlayPauseAsync(); await PollMediaAsync(); break;
                case "island.previous" when _session is not null: await _session.TrySkipPreviousAsync(); await PollMediaAsync(); break;
                case "island.next" when _session is not null: await _session.TrySkipNextAsync(); await PollMediaAsync(); break;
                case "island.seek" when _session is not null:
                    if (payload.TryGetProperty("seconds", out var seconds) && seconds.TryGetDouble(out var seek) && double.IsFinite(seek))
                    {
                        var start = _session.GetTimelineProperties().StartTime;
                        await _session.TryChangePlaybackPositionAsync((start + TimeSpan.FromSeconds(Math.Clamp(seek, 0, _duration))).Ticks);
                        await PollMediaAsync();
                    }
                    break;
                case "island.volume":
                    if (payload.TryGetProperty("step", out var step) && step.TryGetInt32(out var amount))
                        _volume = VolumeService.StepFromFlyout(Math.Clamp(amount, -5, 5));
                    break;
                case "island.mute": VolumeService.SetMutedFromFlyout(!VolumeService.IsMuted()); break;
            }
        }
        catch (Exception exception) { _context.Log.Error("Dynamic Island: işlem tamamlanamadı.", exception); Alert("İşlem tamamlanamadı", "error"); }
    }

    private void Alert(string text, string kind)
    {
        if (_closed) return;
        _alert = text;
        _alertKind = kind;
        _alertUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(_context.Settings.GetInt("duration", 4), 2, 15));
        Publish();
    }

    public void Reveal() { _hidden = false; Show(); Expand(true); }
    public void ToggleVisibility() { _hidden = !_hidden; UpdateVisibility(); }

    private void UpdateVisibility()
    {
        var fullscreen = false;
        if (_context.Settings.GetBool("fullscreen", true) && _display is { } display)
        {
            var foreground = Native.GetForegroundWindow();
            if (foreground != _source?.Handle && Native.GetWindowRect(foreground, out var bounds))
            {
                var className = new System.Text.StringBuilder(256);
                Native.GetClassName(foreground, className, className.Capacity);
                fullscreen = className.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd") &&
                    bounds.Left <= display.Bounds.Left && bounds.Top <= display.Bounds.Top &&
                    bounds.Right >= display.Bounds.Right && bounds.Bottom >= display.Bounds.Bottom;
            }
        }
        if (_hidden || fullscreen) { if (IsVisible) Hide(); _hovered = false; if (!_pinned) Expand(false); }
        else if (!IsVisible) Show();
    }

    private void Expand(bool expanded)
    {
        _expanded = expanded;
        Resize(animate: true);
        Publish();
    }

    private void Resize(bool animate)
    {
        var width = (_expanded ? ExpandedWidth : CompactWidth) * _scale;
        var height = (_expanded ? ExpandedHeight : CompactHeight) * _scale;
        var motion = _context.Settings.GetString("motion", "spring");
        if (!animate || motion == "off" || !SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(WidthProperty, null); BeginAnimation(HeightProperty, null);
            Width = width; Height = height; Position(); return;
        }
        IEasingFunction easing = motion == "gentle" ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new BackEase { Amplitude = .16, EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(motion == "gentle" ? 280 : 420);
        BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = easing });
        BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = easing });
    }

    private void Position()
    {
        if (_display is not { } display) return;
        var area = display.WorkArea;
        var offset = Math.Clamp(_context.Settings.GetInt("offset", 12), 0, 100);
        Left = (area.Left + (area.Right - area.Left) / 2d) / _dpi - Width / 2;
        Top = _context.Settings.GetString("position", "top") == "bottom"
            ? area.Bottom / _dpi - Height - offset : area.Top / _dpi + offset;
    }

    private void Publish()
    {
        if (_closed) return;
        var now = DateTime.Now;
        _surface.SetState(new
        {
            expanded = _expanded,
            pinned = _pinned,
            theme = _context.Settings.GetString("theme", "obsidian"),
            reducedMotion = !SystemParameters.ClientAreaAnimation || _context.Settings.GetString("motion", "spring") == "off",
            scale = _scale,
            time = now.ToString("HH:mm"),
            date = now.ToString("dddd, d MMMM", CultureInfo.GetCultureInfo("tr-TR")),
            year = now.Year,
            month = now.Month,
            day = now.Day,
            title = _title,
            artist = _artist,
            artwork = _artwork,
            playing = _playing,
            position = _position,
            duration = _duration,
            canPlay = _canPlay,
            canPrevious = _canPrevious,
            canNext = _canNext,
            battery = _battery,
            charging = _charging,
            volume = _volume,
            muted = _muted,
            volumeEnabled = _context.Settings.GetBool("volume", true),
            clipboard = _clipboard,
            clipboardEnabled = _context.Settings.GetBool("clipboard"),
            alert = _alert,
            alertKind = _alertKind,
            focusRemaining = _focusRemaining,
            focusMinutes = Math.Clamp(_context.Settings.GetInt("focus-minutes", 25), 1, 120),
            files = _files.Shelf.Select(path => new { path, name = Path.GetFileName(path) }),
            downloads = _files.Downloads.Select(path => new { path, name = Path.GetFileName(path) }),
            downloadsEnabled = _context.Settings.GetBool("downloads", true)
        });
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _closed = true;
        _timer.Stop();
        _collapseTimer.Stop();
        _keys.Dispose();
        _volumeWatcher?.Dispose();
        _files.Dispose();
        if (_source is not null) { RemoveClipboardFormatListener(_source.Handle); _source.RemoveHook(WindowProc); }
        _surface.MessageReceived -= OnMessage;
        _surface.Dispose();
        _clipboard = "";
        ClearMedia();
        _manager = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
