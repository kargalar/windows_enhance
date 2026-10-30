using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
    private readonly IFeatureUiSurface? _uiSurface;
    private readonly TextBlock _trackInfo;
    private readonly Button _playPauseButton;
    private readonly DispatcherTimer _refreshTimer;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _filterMode = "all";
    private HashSet<string> _filterList = new(StringComparer.OrdinalIgnoreCase);
    private string _pinnedSessionId = "";
    private Action<string?>? _savePinnedSession;
    private bool _refreshing;
    private readonly MediaSessionTracker _tracker = new();
    private readonly Button _switchButton;
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
        _uiSurface = featureUi?.CreateSurface("taskbar-media-widget");
        if (_uiSurface is not null) _uiSurface.MessageReceived += OnUiMessage;
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

        _trackInfo = new TextBlock
        {
            Text = "Medya oturumu aranıyor",
            Foreground = Brushes.White,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(12, 0, 8, 0)
        };
        _trackInfo.MouseLeftButtonUp += (_, _) => _openFlyout();

        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _switchButton = CreateButton("⇄", "Başka medya oturumuna geç", SwitchSession);
        _switchButton.Visibility = Visibility.Collapsed;
        controls.Children.Add(_switchButton);
        controls.Children.Add(CreateButton("⏮", "Önceki parça", SkipPrevious));
        _playPauseButton = CreateButton("▶", "Oynat veya duraklat", TogglePlayback);
        controls.Children.Add(_playPauseButton);
        controls.Children.Add(CreateButton("⏭", "Sonraki parça", SkipNext));

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(controls, Dock.Right);
        row.Children.Add(controls);
        row.Children.Add(_trackInfo);
        var legacyContent = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(248, 27, 27, 29)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 66, 66, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(5, 4, 6, 4),
            Child = row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.3, Color = Colors.Black }
        };
        Content = _uiSurface?.Element ?? legacyContent;
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
                        _trackInfo.ToolTip = $"{GetPlayerName()} · etkin ses oturumu bulunamadı\nTekerlek: oynatıcı sesi\nSağ tık: seçenekler";
                        args.Handled = true;
                        return;
                    }

                    var updated = 0;
                    foreach (var session in audioSessions)
                        if (AudioSessionService.SetVolume(session.InstanceId,
                                Math.Clamp(session.Volume + delta, 0, 100))) updated++;
                    _trackInfo.ToolTip = updated > 0
                        ? $"{GetPlayerName()} · {Math.Clamp(audioSessions[0].Volume + delta, 0, 100)}% uygulama sesi\nTekerlek: oynatıcı sesini değiştirir\nSağ tık: seçenekler"
                        : $"{GetPlayerName()} · uygulama sesi değiştirilemedi\nSağ tık: seçenekler";
                }
                else
                {
                    var volume = VolumeService.Step(delta);
                    _trackInfo.ToolTip = $"{GetPlayerName()} · {volume}% ana ses\nTekerlek: ana ses düzeyini değiştirir\nSağ tık: seçenekler";
                }
            }
            catch
            {
                _trackInfo.ToolTip = _scrollMode == "player-volume"
                    ? "Oynatıcı uygulamasının ses düzeyi değiştirilemedi."
                    : "Ana ses düzeyi değiştirilemedi.";
            }
            args.Handled = true;
        };
        ContextMenu = new ContextMenu();
        ContextMenu.Opened += (_, _) => RefreshSessionMenu();
        ContextMenu.Items.Add(CreateMenuItem("Medya denetimlerini aç", (_, _) => _openFlyout()));
        ContextMenu.Items.Add(CreateMenuItem("Windows Ses Ayarları", (_, _) => OpenSoundSettings()));
        ContextMenu.Items.Add(CreateMenuItem("Oynatıcıya geç", (_, _) => OpenPlayer()));
        if (_uiSurface is not null) ContextMenu = null;

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

    private void RefreshSessionMenu()
    {
        if (ContextMenu is null || _sessionManager is null) return;

        while (ContextMenu.Items.Count > 3) ContextMenu.Items.RemoveAt(0);
        ContextMenu.Items.Insert(0, new Separator());
        var sessions = _sessionManager.GetSessions()
            .Where(session => MediaFlyoutWindow.IsMediaAppAllowed(
                session.SourceAppUserModelId, _filterMode, _filterList))
            .ToArray();
        var currentSession = sessions.FirstOrDefault(session =>
            string.Equals(session.SourceAppUserModelId, _pinnedSessionId, StringComparison.OrdinalIgnoreCase))
            ?? _sessionManager.GetCurrentSession();
        var automaticItem = CreateSessionMenuItem("Otomatik seç", currentSession is null ||
            string.IsNullOrWhiteSpace(_pinnedSessionId), () => SetPinnedSession(null));
        ContextMenu.Items.Insert(0, automaticItem);
        foreach (var session in sessions.Reverse())
        {
            var appId = session.SourceAppUserModelId;
            var name = MediaFlyoutWindow.NormalizeMediaAppId(appId);
            if (string.IsNullOrWhiteSpace(name)) name = "Medya oynatıcı";
            var item = CreateSessionMenuItem(name,
                string.Equals(appId, _pinnedSessionId, StringComparison.OrdinalIgnoreCase),
                () => SetPinnedSession(appId));
            ContextMenu.Items.Insert(0, item);
        }
        ContextMenu.Items.Insert(0, new MenuItem { Header = "Medya oturumu", IsEnabled = false });
    }

    private MenuItem CreateSessionMenuItem(string label, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
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
            _switchButton.Visibility = sessions.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            _session = sessions.FirstOrDefault(session => string.Equals(session.SourceAppUserModelId,
                _pinnedSessionId, StringComparison.OrdinalIgnoreCase))
                ?? sessions.FirstOrDefault(session => string.Equals(session.SourceAppUserModelId,
                    _manualSessionId, StringComparison.OrdinalIgnoreCase))
                ?? sessions.FirstOrDefault();
            if (_session is null)
            {
                _trackInfo.Text = "Medya oturumu yok veya filtrelendi";
                Hide();
                return;
            }

            var properties = await _session.TryGetMediaPropertiesAsync();
            var title = string.IsNullOrWhiteSpace(properties.Title) ? "Bilinmeyen parça" : properties.Title;
            _trackInfo.Text = string.IsNullOrWhiteSpace(properties.Artist)
                ? title
                : $"{title} · {properties.Artist}";
            _trackInfo.ToolTip = $"{GetPlayerName()} · {GetCurrentVolumeText()} ana ses\n" +
                (_scrollMode switch
                {
                    "off" => "Tekerlek: kapalı",
                    "player-volume" => "Tekerlek: oynatıcı uygulaması ses düzeyi",
                    _ => "Tekerlek: ana ses düzeyi"
                }) +
                "\nSağ tık: seçenekler";
            _playPauseButton.Content = _session.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "❚❚" : "▶";
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
        if (_uiSurface is null) return;
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
            title = _session is null ? "Medya oturumu yok veya filtrelendi" : _trackInfo.Text,
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

    private async void SwitchSession(object? sender, RoutedEventArgs e)
    {
        if (_availableSessions.Length < 2) return;
        var index = Array.FindIndex(_availableSessions, session => ReferenceEquals(session, _session));
        var next = _availableSessions[(index + 1) % _availableSessions.Length];
        _manualSessionId = next.SourceAppUserModelId;
        _pinnedSessionId = "";
        await RefreshAsync();
    }

    private async void SkipPrevious(object? sender, RoutedEventArgs e)
    {
        if (_session is not null) await _session.TrySkipPreviousAsync();
        await RefreshAsync();
    }

    private async void SkipNext(object? sender, RoutedEventArgs e)
    {
        if (_session is not null) await _session.TrySkipNextAsync();
        await RefreshAsync();
    }

    private async void TogglePlayback(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var isPlaying = _session.GetPlaybackInfo().PlaybackStatus ==
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (isPlaying) await _session.TryPauseAsync();
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
            _trackInfo.ToolTip = "Oynatıcı açılamadı; uygulamayı Başlat menüsünden açın.";
        }
    }

    private static void OpenSoundSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch { }
    }

    private static MenuItem CreateMenuItem(string label, RoutedEventHandler action)
    {
        var item = new MenuItem { Header = label };
        item.Click += action;
        return item;
    }

    private static Button CreateButton(string label, string tooltip, RoutedEventHandler action)
    {
        var button = new Button
        {
            Content = label,
            ToolTip = tooltip,
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(1),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "PopupButton");
        button.Click += action;
        return button;
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
