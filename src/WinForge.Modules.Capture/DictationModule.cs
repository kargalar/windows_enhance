using System.Runtime.InteropServices;
using System.Windows;
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
                new() { Key = "models", Label = "Modelleri yönet", Kind = SettingKind.Action, Execute = () => new ModelWindow(Store, (Context as IFeatureUiContext)?.FeatureUi).Show() },
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
    private void ToggleDictation()
    {
        if (recording) { RequestStop(); return; }
        if (starting) { stopWhenStarted = true; return; }
        if (busy) { Context.Notify("Dikte çözümlemesi sürüyor."); return; }
        _ = StartAsync();
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
            var copied = TryCopyResult(text);
            var result = await FocusedTextOutput.InsertAsync(target, text, token);
            var copyStatus = copied ? "Panoya kopyalandı" : "Panoya kopyalanamadı";
            var insertionStatus = result == TextInsertion.Inserted ? "metin yazıldı" : "metin alana yazılamadı";
            ShowResult(text, $"{copyStatus} · {insertionStatus} · {client.Engine}");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Report(error); ShowStatus("Dikte başarısız; yeniden deneyebilirsiniz."); }
        finally { Array.Clear(samples); busy = false; }
    }
    private static bool TryCopyResult(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (Exception error) when (error is ExternalException or InvalidOperationException) { return false; }
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
            overlay ??= new StatusOverlay((Context as IFeatureUiContext)?.FeatureUi);
            overlay.ShowStatus(text);
            overlay.Place(Context.Settings.GetString("overlay-position", "bottom-right"));
    }
    private void ShowResult(string text, string status)
    {
        if (!IsActive) return;
            overlay ??= new StatusOverlay((Context as IFeatureUiContext)?.FeatureUi);
            overlay.ShowResult(text, status, Context.Settings.GetInt("result-duration", 5));
        overlay.Place(Context.Settings.GetString("overlay-position", "bottom-right"));
    }
    public override IEnumerable<QuickAction> GetQuickActions(string query) => base.GetQuickActions(query).Concat(new[]
    {
        new QuickAction(Id + ".toggle", "Dikteyi başlat / durdur", "whisper ses konuşma yazıya çevir", ToggleDictation),
        new QuickAction(Id + ".cancel", "Dikteyi iptal et", "whisper ses", Cancel)
    });
}

internal sealed class StatusOverlay : IDisposable
{
    private const double StatusWidth = 420;
    private const double StatusHeight = 104;
    private const double ResultWidth = 480;
    private const double ResultHeight = 210;
    private readonly IFeatureUiService? featureUi;
    private readonly DispatcherTimer resultTimer = new();
    private IFeatureUiWindow? statusWindow;
    private IFeatureUiWindow? resultWindow;
    private string message = "";
    private string result = "";
    private double audioLevel;
    private bool recording;

    public StatusOverlay(IFeatureUiService? featureUi)
    {
        this.featureUi = featureUi;
        resultTimer.Tick += (_, _) =>
        {
            resultTimer.Stop();
            resultWindow?.Hide();
        };
    }

    public void BeginRecording()
    {
        recording = true;
        audioLevel = 0;
        PushStatus();
    }

    public void SetAudioLevel(double value)
    {
        audioLevel = Math.Clamp(value, 0, 100);
        PushStatus();
    }

    public void ShowStatus(string text)
    {
        message = text;
        result = "";
        recording = text.Contains("Dinleniyor", StringComparison.OrdinalIgnoreCase);
        resultTimer.Stop();
        statusWindow ??= OpenStatusWindow();
        statusWindow?.SetState(new { message, audioLevel, recording });
        statusWindow?.Show();
        resultWindow?.Hide();
    }

    public void ShowResult(string text, string status, int durationSeconds)
    {
        message = status;
        result = text;
        recording = false;
        statusWindow?.Hide();
        resultWindow ??= OpenResultWindow();
        resultWindow?.SetState(new { message, result, recording = false, audioLevel = 0 });
        resultWindow?.Show();
        resultTimer.Stop();
        if (durationSeconds > 0)
        {
            resultTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 1, 120));
            resultTimer.Start();
        }
    }

    public void Place(string position)
    {
        var area = CursorPanelPlacement.WorkArea;
        PlaceWindow(statusWindow, position, StatusWidth, StatusHeight, area);
        PlaceWindow(resultWindow, position, ResultWidth, ResultHeight, area);
    }

    public void Hide()
    {
        resultTimer.Stop();
        statusWindow?.Hide();
        resultWindow?.Hide();
    }

    public void Close()
    {
        resultTimer.Stop();
        statusWindow?.Close();
        resultWindow?.Close();
        statusWindow = null;
        resultWindow = null;
    }

    public void Dispose() => Close();

    private IFeatureUiWindow? OpenStatusWindow()
    {
        if (featureUi is null) return null;
        return featureUi.Open("dictation-status", "dictation-status", "Dictation status",
            new FeatureUiWindowOptions(StatusWidth, StatusHeight, Topmost: true, ActivateOnShow: false,
                Transparent: true, ClickThrough: true));
    }

    private IFeatureUiWindow? OpenResultWindow()
    {
        if (featureUi is null) return null;
        var window = featureUi.Open("dictation-result", "dictation-status", "Dictation result",
            new FeatureUiWindowOptions(ResultWidth, ResultHeight, Topmost: true, ActivateOnShow: false,
                Transparent: true));
        window.MessageReceived += OnResultMessage;
        return window;
    }

    private void OnResultMessage(string command, System.Text.Json.JsonElement payload)
    {
        if (command != "dictation.copy") return;
        try
        {
            if (!string.IsNullOrEmpty(result)) Clipboard.SetText(result);
        }
        catch (InvalidOperationException) { }
        Hide();
    }

    private void PushStatus()
    {
        statusWindow ??= OpenStatusWindow();
        statusWindow?.SetState(new { message, audioLevel, recording });
    }

    private static void PlaceWindow(IFeatureUiWindow? window, string position, double width, double height, Rect area)
    {
        if (window is null) return;
        const double margin = 20;
        var left = position switch
        {
            "top-left" or "bottom-left" => area.Left + margin,
            "center" => area.Left + (area.Width - width) / 2,
            _ => area.Right - width - margin
        };
        var top = position switch
        {
            "top-left" or "top-right" => area.Top + margin,
            "center" => area.Top + (area.Height - height) / 2,
            _ => area.Bottom - height - margin
        };
        window.SetBounds(left, top, width, height);
    }
}
