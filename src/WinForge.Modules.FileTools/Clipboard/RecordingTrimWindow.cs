using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using WinForge.Core;

namespace WinForge.ClipboardLine;

internal sealed class RecordingTrimWindow : Window
{
    private readonly string _source;
    private readonly string _directory;
    private readonly MediaElement _player;
    private readonly TextBox _start = new() { Text = "0", Width = 100, Margin = new Thickness(6) };
    private readonly TextBox _end = new() { Width = 100, Margin = new Thickness(6) };
    private readonly TextBlock _status = new() { Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(6) };
    private readonly Button _save;
    private readonly Slider _timeline = new() { Minimum = 0, Maximum = 1, Margin = new Thickness(6), IsEnabled = false };
    private readonly System.Windows.Threading.DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly IFeatureUiAssetSurface _uiSurface;
    private bool _updatingTimeline;
    private CancellationTokenSource? _operation;

    internal RecordingTrimWindow(Window owner, string source, string directory, IFeatureUiService? featureUi=null)
    {
        Owner = owner; _source = source; _directory = directory;
        Title = "Ekran kaydını kırp"; Width = 780; Height = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _player = new MediaElement { Source = new Uri(source), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform };
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(_timeline);
        _timeline.ValueChanged += (_, _) =>
        {
            if (!_updatingTimeline && _timeline.IsEnabled)
                _player.Position = TimeSpan.FromSeconds(_timeline.Value);
        };
        _playbackTimer.Tick += (_, _) =>
        {
            if (!_timeline.IsEnabled || _timeline.IsMouseCaptureWithin) return;
            _updatingTimeline = true;
            try { _timeline.Value = Math.Clamp(_player.Position.TotalSeconds, _timeline.Minimum, _timeline.Maximum); }
            finally { _updatingTimeline = false; }
        };
        var range = new WrapPanel(); bottom.Children.Add(range);
        range.Children.Add(new TextBlock { Text = "Başlangıç (sn)", VerticalAlignment = VerticalAlignment.Center }); range.Children.Add(_start);
        range.Children.Add(new TextBlock { Text = "Bitiş (sn)", VerticalAlignment = VerticalAlignment.Center }); range.Children.Add(_end);
        range.Children.Add(ClipboardShelfWindow.ActionButton("Burayı başlangıç yap", () => _start.Text = _player.Position.TotalSeconds.ToString("0.###", CultureInfo.CurrentCulture)));
        range.Children.Add(ClipboardShelfWindow.ActionButton("Burayı bitiş yap", () => _end.Text = _player.Position.TotalSeconds.ToString("0.###", CultureInfo.CurrentCulture)));
        var buttons = new WrapPanel(); bottom.Children.Add(buttons);
        buttons.Children.Add(ClipboardShelfWindow.ActionButton("Oynat", () => _player.Play())); buttons.Children.Add(ClipboardShelfWindow.ActionButton("Duraklat", () => _player.Pause()));
        _save = ClipboardShelfWindow.ActionButton("Kırp ve kaydet", () => _ = SaveAsync()); buttons.Children.Add(_save);
        buttons.Children.Add(ClipboardShelfWindow.ActionButton("İptal", () => { if (_operation is null) Close(); else _operation.Cancel(); }));
        bottom.Children.Add(_progress); bottom.Children.Add(_status);
        root.Children.Add(_player);
        _player.MediaOpened += (_, _) =>
        {
            if (!_player.NaturalDuration.HasTimeSpan) return;
            var seconds = _player.NaturalDuration.TimeSpan.TotalSeconds;
            _end.Text = seconds.ToString("0.###", CultureInfo.CurrentCulture);
            _timeline.Maximum = seconds; _timeline.IsEnabled = seconds > 0; _playbackTimer.Start();
            PushUiState();
        };
        Loaded += async (_, _) => { try { var info = await RecordingTools.InspectAsync(_source); var duration=info.Duration.TotalSeconds; _end.Text = duration.ToString("0.###", CultureInfo.CurrentCulture); _timeline.Maximum=duration;_timeline.IsEnabled=duration>0;PushUiState(); } catch (Exception error) { _status.Text = error.Message; _save.IsEnabled = false;PushUiState(); } };
        _uiSurface = featureUi?.CreateSurface("clipboard-recording-trim") as IFeatureUiAssetSurface
            ?? throw new InvalidOperationException("Kayıt kırpma için React varlık yüzeyi gerekli.");
        _uiSurface.MessageReceived += OnUiMessage;
        Content = _uiSurface.Element;
        Closed += (_, _) => _uiSurface.Dispose();
        PushUiState();
        Closing += (_, args) => { if (_operation is not null) { _operation.Cancel(); args.Cancel = true; } };
        Closed += (_, _) => { _playbackTimer.Stop(); _player.Close(); };
        PreviewKeyDown += (_, args) => { if (Keyboard.FocusedElement is TextBox) return; if (args.Key == Key.Escape) Close(); else if (args.Key == Key.Enter) _ = SaveAsync(); else return; args.Handled = true; };
    }

    private async Task SaveAsync()
    {
        if (_operation is not null) return;
        if (!double.TryParse(_start.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var start) || !double.TryParse(_end.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var end) || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start)
        { _status.Text = "Geçerli başlangıç ve bitiş saniyeleri girin."; PushUiState(); return; }
        _operation = new CancellationTokenSource(); _save.IsEnabled = false; PushUiState();
        try
        {
            await RecordingTools.TrimAsync(_source, _directory, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), new Progress<double>(value => { _progress.Value = value; PushUiState(); }), _operation.Token);
            _operation.Dispose(); _operation = null; DialogResult = true;
        }
        catch (OperationCanceledException) { _status.Text = "Kırpma iptal edildi."; }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _operation?.Dispose(); _operation = null; _save.IsEnabled = true; PushUiState(); }
    }

    private void OnUiMessage(string command,JsonElement payload)
    {
        switch(command)
        {
            case "recording-trim.range":
                _start.Text=payload.GetProperty("start").GetString()??"0";_end.Text=payload.GetProperty("end").GetString()??"";PushUiState();break;
            case "recording-trim.set-point":
                var position=payload.GetProperty("position").GetDouble();var field=payload.GetProperty("field").GetString();
                if(field=="start")_start.Text=position.ToString("0.###",CultureInfo.CurrentCulture);else if(field=="end")_end.Text=position.ToString("0.###",CultureInfo.CurrentCulture);PushUiState();break;
            case "recording-trim.save":
                _start.Text=payload.GetProperty("start").GetString()??"0";_end.Text=payload.GetProperty("end").GetString()??"";_ = SaveAsync();break;
            case "recording-trim.cancel": if(_operation is null)Close();else _operation.Cancel();break;
        }
    }

    private void PushUiState()
    {
        string mediaUrl="";
        try{if(File.Exists(_source))mediaUrl=_uiSurface.MapLocalAsset(_source).AbsoluteUri;}catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException){}
        var duration=_timeline.Maximum;
        _uiSurface.SetState(new{file=Path.GetFileName(_source),mediaUrl,duration,start=_start.Text,end=_end.Text,progress=_progress.Value/100,status=_status.Text,working=_operation is not null,ready=_timeline.IsEnabled&&duration>0});
    }
}
