using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media.Control;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>A lightweight, optional mini-player anchored just above the primary taskbar.</summary>
internal sealed class TaskbarMediaWidgetWindow : Window
{
    private readonly Action _openFlyout;
    private readonly IFeatureUiSurface _uiSurface;
    private readonly DispatcherTimer _refreshTimer;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _filterMode = "all";
    private HashSet<string> _filterList = new(StringComparer.OrdinalIgnoreCase);
    private string _pinnedSessionId = "";
    private Action<string?>? _savePinnedSession;
    private bool _refreshing;
    private readonly MediaSessionTracker _tracker = new();
    private string _trackInfoText = "Medya oturumu aranıyor";
    private string _trackTooltip = "";
    private GlobalSystemMediaTransportControlsSession[] _availableSessions = [];
    private string _manualSessionId = "";
    private string _scrollMode = "master-volume";
    private const int ScrollVolumeStep = 2;
    private const uint AbmGetTaskbarPos = 0x00000005;
    private const uint AbeLeft = 0;
    private const uint AbeTop = 1;
    private const uint AbeRight = 2;
    private const uint AbeBottom = 3;
    private const double TaskbarGap = 10;
    private const double ScreenEdgeGap = 10;

    public TaskbarMediaWidgetWindow(Action openFlyout, IFeatureUiService? featureUi = null)
    {
        _openFlyout = openFlyout;
        _uiSurface = featureUi?.CreateSurface("taskbar-media-widget")
            ?? throw new InvalidOperationException("Görev çubuğu medya bileşeni için React arayüzü gerekli.");
        _uiSurface.MessageReceived += OnUiMessage;
        Width = 340;
        Height = 54;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Content = _uiSurface.Element;
        PreviewMouseWheel += (_, args) =>
        {
            if (_scrollMode == "off") return;
            try
            {
                var delta = Math.Sign(args.Delta) * ScrollVolumeStep;
                if (_scrollMode == "player-volume")
                {
                    var playerKey = MediaFlyoutWindow.NormalizeMediaAppId(_session?.SourceAppUserModelId ?? "");
                    var audioSessions = AudioSessionService.GetActiveSessions()
                        .Where(session => IsAudioSessionForMediaApp(
                            _session?.SourceAppUserModelId ?? "", session.Application) ||
                            IsAudioSessionForMediaApp(
                                _session?.SourceAppUserModelId ?? "", GetAudioSessionProcessName(session.ProcessId)))
                        .ToArray();
                    if (playerKey.Length == 0 || audioSessions.Length == 0)
                    {
                        _trackTooltip = $"{GetPlayerName()} · etkin ses oturumu bulunamadı\nTekerlek: oynatıcı sesi\nSağ tık: seçenekler";
                        args.Handled = true;
                        return;
                    }

                    var updated = 0;
                    foreach (var session in audioSessions)
                        if (AudioSessionService.SetVolume(session.InstanceId,
                                Math.Clamp(session.Volume + delta, 0, 100))) updated++;
                    _trackTooltip = updated > 0
                        ? $"{GetPlayerName()} · {Math.Clamp(audioSessions[0].Volume + delta, 0, 100)}% uygulama sesi\nTekerlek: oynatıcı sesini değiştirir\nSağ tık: seçenekler"
                        : $"{GetPlayerName()} · uygulama sesi değiştirilemedi\nSağ tık: seçenekler";
                }
                else
                {
                    var volume = VolumeService.Step(delta);
                    _trackTooltip = $"{GetPlayerName()} · {volume}% ana ses\nTekerlek: ana ses düzeyini değiştirir\nSağ tık: seçenekler";
                }
            }
            catch
            {
                _trackTooltip = _scrollMode == "player-volume"
                    ? "Oynatıcı uygulamasının ses düzeyi değiştirilemedi."
                    : "Ana ses düzeyi değiştirilemedi.";
            }
            args.Handled = true;
        };
        Loaded += async (_, _) => await RefreshAsync();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { _refreshTimer.Stop(); _uiSurface?.Dispose(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SetNonActivatingStyle();
    }

    private void SetNonActivatingStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExNoActivate | WsExToolWindow;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PositionAboveTaskbar();
        _refreshTimer.Start();
    }

    public void ConfigureMediaFilter(string mode, string list, string scrollMode = "master-volume",
        string pinnedSessionId = "", Action<string?>? savePinnedSession = null)
    {
        _filterMode = mode;
        _filterList = MediaFlyoutWindow.ParseMediaAppFilterList(list);
        _scrollMode = scrollMode is "player-volume" or "off" ? scrollMode : "master-volume";
        _pinnedSessionId = pinnedSessionId;
        _savePinnedSession = savePinnedSession;
        _ = RefreshAsync();
    }

    private void SetPinnedSession(string? appId)
    {
        _pinnedSessionId = appId ?? "";
        _savePinnedSession?.Invoke(appId);
        _ = RefreshAsync();
    }

    private void PositionAboveTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        var edge = AbeBottom;
        var taskbar = default(NativeRect);
        var appBar = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref appBar) != UIntPtr.Zero)
        {
            edge = appBar.Edge;
            taskbar = appBar.Rect;
        }

        var taskbarBounds = ToDipRect(taskbar);
        var placement = CalculatePlacement(workArea, taskbarBounds, edge, Width, Height);
        Left = placement.Left;
        Top = placement.Top;
    }

    internal static Rect CalculatePlacement(Rect workArea, Rect taskbarBounds, uint edge,
        double widgetWidth, double widgetHeight)
    {
        double left;
        double top;
        var hasTaskbarBounds = !taskbarBounds.IsEmpty;
        switch (edge)
        {
            case AbeTop:
                left = workArea.Right - widgetWidth - 14;
                top = hasTaskbarBounds ? taskbarBounds.Bottom + TaskbarGap : workArea.Top + TaskbarGap;
                break;
            case AbeLeft:
                left = hasTaskbarBounds ? taskbarBounds.Right + TaskbarGap : workArea.Left + TaskbarGap;
                top = workArea.Bottom - widgetHeight - ScreenEdgeGap;
                break;
            case AbeRight:
                left = hasTaskbarBounds ? taskbarBounds.Left - widgetWidth - TaskbarGap : workArea.Right - widgetWidth - TaskbarGap;
                top = workArea.Bottom - widgetHeight - ScreenEdgeGap;
                break;
            default:
                left = workArea.Right - widgetWidth - 14;
                top = hasTaskbarBounds ? taskbarBounds.Top - widgetHeight - TaskbarGap : workArea.Bottom - widgetHeight - ScreenEdgeGap;
                break;
        }

        var maxLeft = Math.Max(workArea.Left, workArea.Right - widgetWidth);
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - widgetHeight);
        return new Rect(Math.Clamp(left, workArea.Left, maxLeft), Math.Clamp(top, workArea.Top, maxTop),
            Math.Min(widgetWidth, workArea.Width), Math.Min(widgetHeight, workArea.Height));
    }

    private Rect ToDipRect(NativeRect rect)
    {
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return Rect.Empty;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = transform.Transform(new Point(rect.Right, rect.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || !IsLoaded) return;
        _refreshing = true;
        try
        {
            if (MediaFlyoutWindow.IsForegroundFullscreen())
            {
                Hide();
                return;
            }

            _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = _sessionManager.GetSessions()
                .Where(session => MediaFlyoutWindow.IsMediaAppAllowed(
                    session.SourceAppUserModelId, _filterMode, _filterList))
                .ToArray();
            if (_tracker.Update(sessions)) _manualSessionId = "";
            sessions = _tracker.Order(sessions);
            _availableSessions = sessions;
            _session = sessions.FirstOrDefault(session => string.Equals(session.SourceAppUserModelId,
                _pinnedSessionId, StringComparison.OrdinalIgnoreCase))
                ?? sessions.FirstOrDefault(session => string.Equals(session.SourceAppUserModelId,
                    _manualSessionId, StringComparison.OrdinalIgnoreCase))
                ?? sessions.FirstOrDefault();
            if (_session is null)
            {
                _trackInfoText = "Medya oturumu yok veya filtrelendi";
                Hide();
                return;
            }

            var properties = await _session.TryGetMediaPropertiesAsync();
            var title = string.IsNullOrWhiteSpace(properties.Title) ? "Bilinmeyen parça" : properties.Title;
            _trackInfoText = string.IsNullOrWhiteSpace(properties.Artist)
                ? title
                : $"{title} · {properties.Artist}";
            _trackTooltip = $"{GetPlayerName()} · {GetCurrentVolumeText()} ana ses\n" +
                (_scrollMode switch
                {
                    "off" => "Tekerlek: kapalı",
                    "player-volume" => "Tekerlek: oynatıcı uygulaması ses düzeyi",
                    _ => "Tekerlek: ana ses düzeyi"
                }) +
                "\nSağ tık: seçenekler";
            if (!IsVisible)
            {
                PositionAboveTaskbar();
                Show();
            }
        }
        catch
        {
            _session = null;
            Hide();
        }
        finally
        {
            _refreshing = false;
            PushUiState();
        }
    }

    private async void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "widget.open-flyout": _openFlyout(); break;
            case "widget.previous": await SkipPreviousAsync(); break;
            case "widget.next": await SkipNextAsync(); break;
            case "widget.toggle": await TogglePlaybackAsync(); break;
            case "widget.switch-session": await SwitchSessionAsync(); break;
            case "widget.select-session":
                if (payload.TryGetProperty("id", out var id)) SetPinnedSession(id.GetString());
                break;
            case "widget.open-player": OpenPlayer(); break;
            case "widget.open-sound-settings": OpenSoundSettings(); break;
        }
    }

    private void PushUiState()
    {
        var sessions = _availableSessions.Select(session => new
        {
            id = session.SourceAppUserModelId,
            label = MediaFlyoutWindow.NormalizeMediaAppId(session.SourceAppUserModelId)
        }).ToArray();
        var isPlaying = false;
        var canPrevious = false;
        var canNext = false;
        if (_session is not null)
        {
            try
            {
                var playback = _session.GetPlaybackInfo();
                isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                canPrevious = playback.Controls.IsPreviousEnabled;
                canNext = playback.Controls.IsNextEnabled;
            }
            catch { }
        }
        _uiSurface.SetState(new
        {
            title = _trackInfoText,
            tooltip = _trackTooltip,
            player = GetPlayerName(),
            isPlaying,
            canPrevious,
            canNext,
            sessions,
            selectedSessionId = _session?.SourceAppUserModelId ?? "",
            showSessionSelector = sessions.Length > 1,
            pinnedSessionId = _pinnedSessionId,
            scrollMode = _scrollMode
        });
    }

    private Task SwitchSessionAsync() => SwitchSessionCoreAsync();

    private async Task SwitchSessionCoreAsync()
    {
        if (_availableSessions.Length < 2) return;
        var index = Array.FindIndex(_availableSessions, session => ReferenceEquals(session, _session));
        var next = _availableSessions[(index + 1) % _availableSessions.Length];
        _manualSessionId = next.SourceAppUserModelId;
        _pinnedSessionId = "";
        await RefreshAsync();
    }

    private async Task SkipPreviousAsync()
    {
        if (_session is not null) await _session.TrySkipPreviousAsync();
        await RefreshAsync();
    }

    private async Task SkipNextAsync()
    {
        if (_session is not null) await _session.TrySkipNextAsync();
        await RefreshAsync();
    }

    private async Task TogglePlaybackAsync()
    {
        if (_session is null) return;
        var playing = _session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (playing) await _session.TryPauseAsync();
        else await _session.TryPlayAsync();
        await RefreshAsync();
    }

    private string GetPlayerName() => _session is null
        ? "Medya oynatıcı"
        : MediaFlyoutWindow.NormalizeMediaAppId(_session.SourceAppUserModelId);

    internal static bool IsAudioSessionForMediaApp(string mediaAppId, string audioApplication)
    {
        var mediaName = MediaFlyoutWindow.NormalizeMediaAppId(mediaAppId);
        var audioName = MediaFlyoutWindow.NormalizeMediaAppId(audioApplication);
        return mediaName.Length > 0 && audioName.Length > 0 &&
               string.Equals(mediaName, audioName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAudioSessionProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }

    private static string GetCurrentVolumeText()
    {
        try { return $"{VolumeService.Current()}%"; }
        catch { return "—"; }
    }

    private void OpenPlayer()
    {
        if (_session is null) return;
        var appId = _session.SourceAppUserModelId;
        var processName = Path.GetFileNameWithoutExtension(MediaFlyoutWindow.NormalizeMediaAppId(appId));
        try
        {
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var process = Process.GetProcessesByName(processName)
                    .FirstOrDefault(candidate => candidate.MainWindowHandle != IntPtr.Zero);
                if (process is not null)
                {
                    using (process)
                    {
                        ShowWindow(process.MainWindowHandle, SwRestore);
                        SetForegroundWindow(process.MainWindowHandle);
                        return;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(appId))
                Process.Start(new ProcessStartInfo($"shell:AppsFolder\\{appId}") { UseShellExecute = true });
        }
        catch
        {
            _trackTooltip = "Oynatıcı açılamadı; uygulamayı Başlat menüsünden açın.";
        }
    }

    private static void OpenSoundSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch { }
    }

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const int SwRestore = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public IntPtr Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
