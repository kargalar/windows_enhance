using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForge.Core;
using WinForge.Dictation;
using WinForge.History;

namespace WinForge.Modules.Capture;

public sealed class DictationModule : CaptureModuleBase
{
    private readonly DictationClient client = new();
    private readonly SpeechBuffer audio = new();
    private readonly DictationShortcutState shortcut = new();
    private WasapiLoopbackCapture? microphone;
    private PlaybackMuteScope? playbackMute;
    private IDisposable? hotkey;
    private GlobalKeyboardHook? hook;
    private CancellationTokenSource? operation;
    private TextTarget? target;
    private HistoryStore? activeStore;
    private StatusOverlay? overlay;
    private bool recording, busy, starting, stopWhenStarted;
    private uint shortcutKey;
    private uint shortcutModifiers;
    private DispatcherTimer? monitor;
    private DateTime lastWaveformUpdate;
    public override string Id => "voice-dictation";
    public override string Name => "Sesle Yazma";
    public override string Description => "Çevrimdışı Whisper diktesi; hedef alana yazma, aranabilir geçmiş ve Turbo/Large modelleri.";
    protected override string Kind => "dictation";
    protected override IEnumerable<SettingDescriptor> ModuleSettings
    {
        get
        {
            var devices = AudioInputDevices.Enumerate();
            return
            [
                new() { Key = "hotkey", Label = "Dikte kısayolu", Kind = SettingKind.Text, Default = DictationDefaults.Hotkey,
                    Description = "Kısa basıp bırakın: başlat/durdur. Basılı tutup bırakın: bırakınca durdurur." },
                new() { Key = "language", Label = "Dil", Kind = SettingKind.Choice, Default = "auto", Options = DictationLanguages.All.Keys.ToArray(), OptionLabels = DictationLanguages.All },
                new() { Key = "model", Label = "Model", Kind = SettingKind.Choice, Default = "0", Options = ["0", "1"], OptionLabels = new Dictionary<string, string> { ["0"] = "Turbo", ["1"] = "Large" } },
                new() { Key = "output", Label = "İngilizceye çevir (Large kullanır)", Default = false },
                new() { Key = "cpu", Label = "Yalnız CPU kullan", Default = false },
                new() { Key = "microphone", Label = "Mikrofon", Kind = SettingKind.Choice, Default = "", Options = devices.Select(d => d.Id).ToArray(), OptionLabels = devices.ToDictionary(d => d.Id, d => d.Name) },
                new() { Key = "start-with-windows", Label = "Windows açıldığında WinForge'u başlat", Default = false },
                new() { Key = "mute-playback", Label = "Dikte sırasında etkin ses çıkışlarını sessize al", Default = true },
                new() { Key = "result-duration", Label = "Sonuç bildirimi süresi (saniye; 0 = sürekli)", Kind = SettingKind.Number, Min = 0, Max = 120, Default = 5 },
                new() { Key = "overlay-position", Label = "Dikte bildiriminin konumu", Kind = SettingKind.Choice, Default = "bottom-right",
                    Options = ["top-left", "top-right", "bottom-left", "bottom-right", "center"],
                    OptionLabels = new Dictionary<string, string> { ["top-left"] = "Sol üst", ["top-right"] = "Sağ üst", ["bottom-left"] = "Sol alt", ["bottom-right"] = "Sağ alt", ["center"] = "Ekran ortası" } },
                new() { Key = "models", Label = "Modelleri yönet", Kind = SettingKind.Action, Execute = () => new ModelWindow(Store).Show() },
                new() { Key = "cancel", Label = "Kaydı / çözümlemeyi iptal et", Kind = SettingKind.Action, Execute = Cancel }
            ];
        }
    }
    public override void Activate()
    {
        if (IsActive) return;
        activeStore = Store;
        try { ApplyStartupSetting(); Register(); base.Activate(); }
        catch { Deactivate(); throw; }
    }
    private void Register()
    {
        hotkey?.Dispose(); hook?.Dispose(); hotkey = null; hook = null;
        var (modifiers, key) = ModuleShortcut.Parse(Context.Settings.GetString("hotkey", DictationDefaults.Hotkey)); shortcutKey = key; shortcutModifiers = modifiers;
        hotkey = Context.Hotkeys.Register(modifiers, shortcutKey, () =>
        {
            if (!IsActive) return;
            var action = shortcut.Press(recording || starting);
            if (action == DictationKeyAction.Start) _ = StartAsync(); else if (action == DictationKeyAction.Stop) RequestStop();
        });
        hook = new(); hook.KeyStateChanged += (vk, down) =>
        {
            if (vk == 0x1B && down && (recording || busy)) Cancel();
            if (!down && (vk == shortcutKey || IsConfiguredModifier(vk)) && shortcut.Release() == DictationKeyAction.Stop) RequestStop();
        }; hook.Install();
    }
    private bool IsConfiguredModifier(uint key) =>
        ((shortcutModifiers & HotkeyModifiers.Control) != 0 && key is 0x11 or 0xA2 or 0xA3) ||
        ((shortcutModifiers & HotkeyModifiers.Shift) != 0 && key is 0x10 or 0xA0 or 0xA1) ||
        ((shortcutModifiers & HotkeyModifiers.Alt) != 0 && key is 0x12 or 0xA4 or 0xA5) ||
        ((shortcutModifiers & HotkeyModifiers.Win) != 0 && key is 0x5B or 0x5C);
    private void RequestStop()
    {
        if (starting) stopWhenStarted = true;
        else _ = StopAsync();
    }
    public override void Apply()
    {
        ApplyStartupSetting();
        if (IsActive) { Cancel(); Register(); }
    }
    private void ApplyStartupSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (Context.Settings.GetBool("start-with-windows"))
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("WinForge çalıştırılabilir dosya yolu bulunamadı.");
                key?.SetValue("WinForgeVoiceDictation", $"\"{executable}\" --startup");
            }
            else key?.DeleteValue("WinForgeVoiceDictation", false);
        }
        catch (Exception error) { Report(new InvalidOperationException("Windows başlangıç ayarı uygulanamadı.", error)); }
    }
    public override void Deactivate()
    {
        IsActive = false; Cancel(); hotkey?.Dispose(); hotkey = null; hook?.Dispose(); hook = null;
        client.Dispose(); overlay?.Close(); overlay = null; activeStore = null; base.Deactivate();
    }
    private async Task StartAsync()
    {
        if (busy || recording || !IsActive) return;
        busy = true; starting = true; operation?.Dispose(); operation = new(); var token = operation.Token;
        try
        {
            target = await Task.Run(FocusedTextOutput.Capture).WaitAsync(TimeSpan.FromMilliseconds(800), token);
            if (token.IsCancellationRequested || !IsActive) return;
            var translate = Context.Settings.GetBool("output") || Context.Settings.GetString("output") == "1";
            var model = translate ? 1 : Math.Clamp(Context.Settings.GetInt("model"), 0, 1);
            if (!File.Exists(Path.Combine(activeStore!.DirectoryPath, "models", DictationDefaults.ModelFiles[model])))
                throw new FileNotFoundException("Model bulunamadı. Modelleri yönet bölümünden ekleyin.");
            audio.Clear();
            if (Context.Settings.GetBool("mute-playback", true)) playbackMute = PlaybackMuteScope.Begin();
            microphone = new(true, Context.Settings.GetString("microphone")); microphone.SamplesAvailable += Samples;
            try { microphone.Start(); }
            catch when (Context.Settings.GetString("microphone").Length > 0)
            {
                microphone.Dispose(); microphone = new(true); microphone.SamplesAvailable += Samples; microphone.Start(); Context.Notify("Seçilen mikrofon bulunamadı; varsayılan kullanılıyor.");
            }
            recording = true; lastWaveformUpdate = DateTime.MinValue; ShowStatus("Dinleniyor… · Esc: iptal"); overlay?.BeginRecording();
            monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            monitor.Tick += (_, _) =>
            {
                if (microphone?.LastError is { } error) { Cancel(); Report(new InvalidOperationException("Mikrofon bağlantısı kesildi.", error)); }
                else if (audio.Full) _ = StopAsync();
            }; monitor.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Cancel(); Report(error); }
        finally
        {
            starting = false;
            busy = false;
            if (stopWhenStarted)
            {
                stopWhenStarted = false;
                if (recording) _ = StopAsync();
            }
        }
    }
    private void Samples(ReadOnlySpan<float> samples, int rate)
    {
        audio.Append(samples, rate);
        var now = DateTime.UtcNow;
        if ((now - lastWaveformUpdate).TotalMilliseconds < 90) return;
        lastWaveformUpdate = now;
        var peak = 0f;
        foreach (var sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        var level = Math.Clamp(peak * 100, 0, 100);
        Application.Current?.Dispatcher.BeginInvoke(() => overlay?.SetAudioLevel(level));
    }
    private async Task StopAsync()
    {
        if (!recording || busy) return;
        recording = false; busy = true; monitor?.Stop(); monitor = null; microphone?.Dispose(); microphone = null;
        RestorePlayback();
        var samples = audio.Take();
        var token = operation!.Token; var currentStore = activeStore!;
        try
        {
            if (!SpeechBuffer.ContainsSpeech(samples)) { ShowStatus("Konuşma algılanmadı."); return; }
            var translate = Context.Settings.GetBool("output") || Context.Settings.GetString("output") == "1";
            var model = translate ? 1 : Math.Clamp(Context.Settings.GetInt("model"), 0, 1);
            var path = Path.Combine(currentStore.DirectoryPath, "models", DictationDefaults.ModelFiles[model]);
            if (!File.Exists(path)) throw new FileNotFoundException("Model bulunamadı. Modelleri yönet bölümünden ekleyin.", path);
            ShowStatus("Konuşma çözümleniyor… · Esc: iptal");
            var config = new WorkerConfiguration(path, Context.Settings.GetString("language", "auto"), translate, !Context.Settings.GetBool("cpu"));
            var text = await client.TranscribeAsync(samples, config, token); token.ThrowIfCancellationRequested();
            if (text.Length == 0) { ShowStatus("Metin bulunamadı."); return; }
            await Task.Run(() => { token.ThrowIfCancellationRequested(); currentStore.Add("dictation", DateTimeOffset.UtcNow, title: DictationDefaults.ModelFiles[model], text: text); }, token);
            token.ThrowIfCancellationRequested();
            var result = await FocusedTextOutput.InsertAsync(target, text, token);
            ShowResult(text, result == TextInsertion.Inserted ? "Metin yazıldı · " + client.Engine : "Hedef değişti veya yazma engellendi; metin geçmişte.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Report(error); ShowStatus("Dikte başarısız; yeniden deneyebilirsiniz."); }
        finally { Array.Clear(samples); busy = false; }
    }
    private void Cancel()
    {
        shortcut.Reset(); stopWhenStarted = false; starting = false; recording = false; operation?.Cancel(); monitor?.Stop(); monitor = null;
        microphone?.Dispose(); microphone = null; client.Dispose(); audio.Clear(); overlay?.Hide();
        RestorePlayback();
    }
    private void RestorePlayback() { playbackMute?.Dispose(); playbackMute = null; }
    private void ShowStatus(string text)
    {
        if (!IsActive) return;
        overlay ??= new StatusOverlay();
        overlay.ClearResult();
        overlay.Message.Text = text;
        overlay.Show();
        overlay.Place(Context.Settings.GetString("overlay-position", "bottom-right"));
    }
    private void ShowResult(string text, string status)
    {
        if (!IsActive) return;
        overlay ??= new StatusOverlay();
        overlay.Message.Text = status;
        overlay.ShowResult(text, Context.Settings.GetInt("result-duration", 5));
        overlay.Show();
        overlay.Place(Context.Settings.GetString("overlay-position", "bottom-right"));
    }
    public override IEnumerable<QuickAction> GetQuickActions(string query) => base.GetQuickActions(query).Append(new(Id + ".cancel", "Dikteyi iptal et", "whisper ses", Cancel));
}

internal sealed class StatusOverlay : Window
{
    private readonly DispatcherTimer resultTimer = new();
    public TextBlock Message { get; } = new() { Margin = new(18), Foreground = Brushes.WhiteSmoke };
    private readonly ProgressBar audioLevel = new() { Height = 5, Minimum = 0, Maximum = 100, Margin = new Thickness(18, 0, 18, 10), Visibility = Visibility.Collapsed };
    private readonly TextBox result = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxWidth = 430, MaxHeight = 90, Visibility = Visibility.Collapsed, Margin = new Thickness(18, 0, 18, 10) };
    private readonly Button copy = new() { Content = "Metni kopyala", Margin = new Thickness(18, 0, 18, 12), Visibility = Visibility.Collapsed };
    public StatusOverlay()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Width = 480;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; Background = new SolidColorBrush(Color.FromRgb(25, 30, 38));
        var panel = new StackPanel(); panel.Children.Add(Message); panel.Children.Add(audioLevel); panel.Children.Add(result); panel.Children.Add(copy); Content = panel;
        copy.Click += (_, _) => { if (!string.IsNullOrEmpty(result.Text)) Clipboard.SetText(result.Text); };
        resultTimer.Tick += (_, _) => { resultTimer.Stop(); Hide(); };
        Closed += (_, _) => resultTimer.Stop();
        Left = SystemParameters.WorkArea.Left + 24; Top = SystemParameters.WorkArea.Bottom - 90;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle; SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x80);
        };
    }
    public void BeginRecording() { audioLevel.Value = 0; audioLevel.Visibility = Visibility.Visible; }
    public void SetAudioLevel(double value) { if (IsVisible) audioLevel.Value = value; }
    public void ShowResult(string text, int durationSeconds)
    {
        resultTimer.Stop();
        audioLevel.Visibility = Visibility.Collapsed; result.Text = text; result.Visibility = Visibility.Visible; copy.Visibility = Visibility.Visible;
        if (durationSeconds > 0) { resultTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 1, 120)); resultTimer.Start(); }
    }
    public void ClearResult() { resultTimer.Stop(); result.Clear(); result.Visibility = Visibility.Collapsed; copy.Visibility = Visibility.Collapsed; audioLevel.Visibility = Visibility.Collapsed; }
    public void Place(string position)
    {
        if (!IsVisible) return;
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        const double margin = 20;
        Left = position switch
        {
            "top-left" or "bottom-left" => area.Left + margin,
            "center" => area.Left + (area.Width - width) / 2,
            _ => area.Right - width - margin
        };
        Top = position switch
        {
            "top-left" or "top-right" => area.Top + margin,
            "center" => area.Top + (area.Height - height) / 2,
            _ => area.Bottom - height - margin
        };
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
