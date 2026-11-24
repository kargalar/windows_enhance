using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
    private IDisposable? hotkey, translationHotkey;
    private GlobalKeyboardHook? hook;
    private CancellationTokenSource? operation;
    private TextTarget? target;
    private nint recordingWindow, recordingFocus;
    private DictationConfiguration? configuration;
    private readonly StringBuilder transcript = new();
    private Task liveTask = Task.CompletedTask;
    private int liveOffset, pastedLength;
    private bool livePaused, liveTargetChanged;
    private HistoryStore? activeStore;
    private StatusOverlay? overlay;
    private bool recording, busy, starting, stopWhenStarted;
    private (uint Modifiers, uint Key) pressedShortcut;
    private bool preferShiftInsert, shiftInsertIsBound;
    private DispatcherTimer? shortcutMonitor;
    private DispatcherTimer? monitor;
    private DateTime lastWaveformUpdate;
    public override string Id => "voice-dictation";
    public override string Name => "Sesle Yazma";
    public override string Description => "Çevrimdışı Whisper diktesi; hedef alana yazma, aranabilir geçmiş ve Small/Turbo/Large modelleri.";
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
                new() { Key = "translation-hotkey", Label = "İngilizce dikte kısayolu", Kind = SettingKind.Text, Default = DictationDefaults.TranslationHotkey,
                    Description = "Konuşmayı çevrimdışı İngilizceye çevirip yazar. Large modeli gerekir. Kısayolu kapatmak için boş bırakın." },
                new() { Key = "language", Label = "Dil", Kind = SettingKind.Choice, Default = "auto", Options = DictationLanguages.All.Keys.ToArray(), OptionLabels = DictationLanguages.All },
                new() { Key = "model", Label = "Model", Kind = SettingKind.Choice, Default = "0", Options = ["0", "1", "2"], OptionLabels = new Dictionary<string, string> { ["0"] = "Turbo", ["1"] = "Large", ["2"] = "Small (hızlı, daha az doğru olabilir)" },
                    Description = "Small CPU'da daha hızlıdır; özellikle özel isim ve teknik kelimelerde daha fazla hata yapabilir." },
                new() { Key = "output", Label = "Normal dikteyi de İngilizceye çevir (Large kullanır)", Default = false },
                new() { Key = "cpu", Label = "Yalnız CPU kullan", Default = false },
                new() { Key = "microphone", Label = "Mikrofon", Kind = SettingKind.Choice, Default = "", Options = devices.Select(d => d.Id).ToArray(), OptionLabels = devices.ToDictionary(d => d.Id, d => d.Name) },
                new() { Key = "mute-playback", Label = "Dikte sırasında etkin ses çıkışlarını sessize al", Default = true },
                new() { Key = "overlay-position", Label = "Dikte bildiriminin konumu", Kind = SettingKind.Choice, Default = "bottom-center",
                    Options = ["bottom-center", "top-left", "top-right", "bottom-left", "bottom-right", "center"],
                    OptionLabels = new Dictionary<string, string> { ["bottom-center"] = "Alt orta", ["top-left"] = "Sol üst", ["top-right"] = "Sağ üst", ["bottom-left"] = "Sol alt", ["bottom-right"] = "Sağ alt", ["center"] = "Ekran ortası" } },
                new() { Key = "models", Label = "Modelleri yönet", Kind = SettingKind.Action, Execute = () => new ModelWindow(Store, (Context as IFeatureUiContext)?.FeatureUi).Show() },
                new() { Key = "cancel", Label = "Kaydı / çözümlemeyi iptal et", Kind = SettingKind.Action, Execute = Cancel }
            ];
        }
    }
    public override void Activate()
    {
        if (IsActive) return;
        activeStore = Store;
        try { Register(); base.Activate(); PrepareSelectedModel(); }
        catch { Deactivate(); throw; }
    }
    private void Register()
    {
        var normal = ModuleShortcut.Parse(Context.Settings.GetString("hotkey", DictationDefaults.Hotkey));
        var translationSetting = Context.Settings.GetString("translation-hotkey", DictationDefaults.TranslationHotkey);
        var implicitDefault = translationSetting == DictationDefaults.TranslationHotkey && Context.Settings.GetString("translation-hotkey").Length == 0;
        (uint Modifiers, uint Key)? translation = string.IsNullOrWhiteSpace(translationSetting) ? null : ModuleShortcut.Parse(translationSetting);
        if (translation == normal && !implicitDefault)
            throw new InvalidOperationException("Dikte ve İngilizce dikte kısayolları farklı olmalı.");
        var controlV = (HotkeyModifiers.Control, 0x56u);
        var shiftInsert = (HotkeyModifiers.Shift, 0x2Du);
        if ((normal == controlV && translation == shiftInsert) || (normal == shiftInsert && translation == controlV))
            throw new InvalidOperationException("Dikte, metni yapıştırmak için Ctrl+V veya Shift+Insert tuşlarından birine ihtiyaç duyar.");

        Unregister();
        hotkey = Bind(normal, translateToEnglish: false);
        if (translation is { } chord)
        {
            try
            {
                if (chord == normal) throw new InvalidOperationException("Dikte zaten bu kısayolu kullanıyor.");
                translationHotkey = Bind(chord, translateToEnglish: true);
            }
            catch (InvalidOperationException error) when (implicitDefault)
            {
                Context.Log.Error("İngilizce dikte kısayolu kaydedilemedi.", error);
                Context.Notify("Ctrl+Shift+Space kaydedilemedi. İngilizce dikte için ayarlardan başka bir kısayol seçin.");
                translation = null;
            }
        }
        preferShiftInsert = normal == controlV || translation == controlV;
        shiftInsertIsBound = normal == shiftInsert || translation == shiftInsert;
        shortcutMonitor = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
        shortcutMonitor.Tick += (_, _) => CheckShortcutRelease();
        hook = new(); hook.KeyStateChanged += (vk, down) =>
        {
            if (vk == 0x1B && down && (recording || busy)) Cancel();
            if (!down && shortcut.Held && (vk == pressedShortcut.Key || IsModifier(pressedShortcut.Modifiers, vk)))
                FinishShortcutCheck(shortcut.Release());
        }; hook.Install();
    }
    private IDisposable Bind((uint Modifiers, uint Key) chord, bool translateToEnglish) =>
        Context.Hotkeys.Register(chord.Modifiers, chord.Key, () =>
        {
            if (!IsActive) return;
            if (busy && !recording && !starting)
            {
                Context.Notify("Dikte çözümlemesi sürüyor. İptal etmek için Esc'ye basın.");
                return;
            }
            pressedShortcut = chord;
            var action = shortcut.PressRegistered(recording || starting, IsKeyDown(chord.Key));
            shortcutMonitor?.Start();
            if (action == DictationKeyAction.Start) _ = StartAsync(translateToEnglish);
            else RequestStop();
        });

    private void Unregister()
    {
        shortcutMonitor?.Stop(); shortcutMonitor = null;
        translationHotkey?.Dispose(); translationHotkey = null;
        hotkey?.Dispose(); hotkey = null;
        hook?.Dispose(); hook = null;
    }

    private void CheckShortcutRelease()
    {
        var (modifiers, key) = pressedShortcut;
        var modifiersDown = ((modifiers & HotkeyModifiers.Control) == 0 || IsKeyDown(0x11)) &&
            ((modifiers & HotkeyModifiers.Shift) == 0 || IsKeyDown(0x10)) &&
            ((modifiers & HotkeyModifiers.Alt) == 0 || IsKeyDown(0x12)) &&
            ((modifiers & HotkeyModifiers.Win) == 0 || IsKeyDown(0x5B) || IsKeyDown(0x5C));
        FinishShortcutCheck(shortcut.ObserveKeyState(IsKeyDown(key), modifiersDown));
    }

    private void FinishShortcutCheck(DictationKeyAction action)
    {
        if (!shortcut.Held) shortcutMonitor?.Stop();
        if (action == DictationKeyAction.Stop) RequestStop();
    }

    private static bool IsKeyDown(uint key) => (Native.GetAsyncKeyState((int)key) & 0x8000) != 0;

    private static bool IsModifier(uint modifiers, uint key) =>
        ((modifiers & HotkeyModifiers.Control) != 0 && key is 0x11 or 0xA2 or 0xA3) ||
        ((modifiers & HotkeyModifiers.Shift) != 0 && key is 0x10 or 0xA0 or 0xA1) ||
        ((modifiers & HotkeyModifiers.Alt) != 0 && key is 0x12 or 0xA4 or 0xA5) ||
        ((modifiers & HotkeyModifiers.Win) != 0 && key is 0x5B or 0x5C);
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
        if (IsActive) { Cancel(); Register(); activeStore = Store; PrepareSelectedModel(); }
    }
    private void PrepareSelectedModel()
    {
        var config = GetConfiguration(translateToEnglish: false);
        // Missing models are reported when recording starts, not during application startup.
        if (File.Exists(config.Model)) _ = PrepareModelAsync(config, default);
    }

    private DictationConfiguration GetConfiguration(bool translateToEnglish)
    {
        var settings = Context.Settings;
        var translate = translateToEnglish || settings.GetBool("output") || settings.GetString("output") == "1";
        var model = translate ? DictationDefaults.TranslationModel :
            DictationDefaults.ModelFiles[Math.Clamp(settings.GetInt("model"), 0, DictationDefaults.ModelFiles.Length - 1)];
        return new(Path.Combine(activeStore!.DirectoryPath, "models", model), settings.GetString("language", "auto"), translate, !settings.GetBool("cpu"));
    }

    public override void Deactivate()
    {
        IsActive = false; Cancel(); Unregister();
        client.Dispose(); overlay?.Close(); overlay = null; activeStore = null; base.Deactivate();
    }
    private async Task StartAsync(bool translateToEnglish = false)
    {
        if (busy || recording || !IsActive) return;
        busy = true; starting = true; operation?.Dispose(); operation = new(); var token = operation.Token;
        try
        {
            var config = GetConfiguration(translateToEnglish);
            if (!File.Exists(config.Model))
                throw new FileNotFoundException(config.Translate
                    ? "İngilizce dikte için Large modeli gerekir. Modelleri yönet bölümünden ekleyin."
                    : "Model bulunamadı. Modelleri yönet bölümünden ekleyin.", config.Model);
            configuration = config;
            // Remember the native focus while the accessibility provider resolves the editable field.
            target = null;
            recordingWindow = Native.GetForegroundWindow();
            recordingFocus = FocusedTextOutput.FocusedWindow(recordingWindow);
            var captureTarget = CaptureTargetAsync(recordingWindow, token, focusedWindow: recordingFocus);
            audio.Clear();
            transcript.Clear();
            liveOffset = pastedLength = 0;
            livePaused = liveTargetChanged = false;
            liveTask = Task.CompletedTask;
            ShowStatus("preparing");
            if (Context.Settings.GetBool("mute-playback", true)) playbackMute = PlaybackMuteScope.Begin();
            var selectedDevice = Context.Settings.GetString("microphone");
            var capture = new WasapiLoopbackCapture(true, selectedDevice);
            microphone = capture; capture.SamplesAvailable += Samples;
            try { await Task.Run(capture.Start); }
            catch when (selectedDevice.Length > 0 && !token.IsCancellationRequested)
            {
                capture.Dispose();
                capture = new WasapiLoopbackCapture(true);
                microphone = capture; capture.SamplesAvailable += Samples;
                await Task.Run(capture.Start);
                token.ThrowIfCancellationRequested();
                Context.Notify("Seçilen mikrofon açılamadı; varsayılan kullanılıyor.");
            }
            token.ThrowIfCancellationRequested();
            var recordingStarted = Stopwatch.GetTimestamp();
            recording = true; lastWaveformUpdate = DateTime.MinValue; ShowStatus("listening");
            _ = PrepareModelAsync(config, token);
            monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            monitor.Tick += (_, _) =>
            {
                if (microphone?.LastError is { } error) OnMicrophoneFailure(error);
                else if (audio.Full || Stopwatch.GetElapsedTime(recordingStarted) >= TimeSpan.FromSeconds(DictationAudio.MaximumSeconds))
                {
                    Context.Notify("Dikte beş dakikalık kayıt sınırına ulaştı; kayıt çözümleniyor.");
                    RequestStop();
                }
                else StartLiveChunk(config, token);
            }; monitor.Start();
            var capturedTarget = await captureTarget;
            token.ThrowIfCancellationRequested();
            target = capturedTarget;
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (token.IsCancellationRequested) { }
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
    internal void OnMicrophoneFailure(Exception error)
    {
        monitor?.Stop();
        Context.Log.Error("Dikte mikrofon bağlantısı kesildi.", error);
        Context.Notify("Mikrofon bağlantısı kesildi; kaydedilen bölüm çözümleniyor.");
        RequestStop();
    }
    internal static async Task<TextTarget?> CaptureTargetAsync(nint foreground, CancellationToken token,
        Func<TextTarget?>? capture = null, nint focusedWindow = 0, Func<nint, nint>? readFocus = null)
    {
        try
        {
            var result = await Task.Run(capture ?? FocusedTextOutput.Capture, token).WaitAsync(TimeSpan.FromMilliseconds(800), token);
            // The accessibility query runs asynchronously. Never accept a field in a
            // different window that happened to gain focus after the shortcut.
            if (foreground == 0 || result?.Window != foreground) return null;
            if (focusedWindow != 0 && (readFocus ?? FocusedTextOutput.FocusedWindow)(foreground) != focusedWindow) return null;
            return result;
        }
        catch (TimeoutException) { return null; }
    }

    internal static Task<TextTarget?> RecoverTargetAsync(nint recordingWindow, nint recordingFocus, CancellationToken token,
        Func<TextTarget?>? capture = null, Func<nint>? readForeground = null, Func<nint, nint>? readFocus = null)
    {
        readFocus ??= FocusedTextOutput.FocusedWindow;
        if (recordingFocus == 0 || (readForeground ?? Native.GetForegroundWindow)() != recordingWindow ||
            readFocus(recordingWindow) != recordingFocus) return Task.FromResult<TextTarget?>(null);
        return CaptureTargetAsync(recordingWindow, token, capture, recordingFocus, readFocus);
    }
    private async Task PrepareModelAsync(DictationConfiguration configuration, CancellationToken token)
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            await client.PrepareAsync(configuration, token);
            Context.Log.Info($"Dictation model ready: {client.Engine}; preparation={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
        }
        catch (OperationCanceledException) { }
        // The stop path reports loading errors; preparation must not interrupt microphone capture.
        catch (Exception error) { Context.Log.Error("Dictation model preparation", error); }
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

    private bool CanPasteWithHeldShift => shortcut.Held && pressedShortcut.Modifiers == HotkeyModifiers.Shift && !shiftInsertIsBound;

    private void StartLiveChunk(DictationConfiguration config, CancellationToken token)
    {
        if (starting || !recording || target is null || livePaused || !liveTask.IsCompleted ||
            !FocusedTextOutput.CanPasteImmediately(CanPasteWithHeldShift)) return;
        var samples = audio.ReadLiveChunk(liveOffset, out var end);
        if (samples is null) liveOffset = end;
        else liveTask = TranscribeLiveChunkAsync(samples, end, config, token);
    }

    private async Task TranscribeLiveChunkAsync(float[] samples, int end, DictationConfiguration config, CancellationToken token)
    {
        try
        {
            var text = await client.TranscribeAsync(samples, config, token);
            token.ThrowIfCancellationRequested();
            if (text.Length == 0)
            {
                // Try this audio again with the rest of the recording for context.
                livePaused = true;
                return;
            }
            AppendTranscript(text);
            liveOffset = end;
            if (!recording) return;
            var heldShift = CanPasteWithHeldShift;
            if (!FocusedTextOutput.CanPasteImmediately(heldShift)) return;
            var pending = transcript.ToString(pastedLength, transcript.Length - pastedLength);
            var insertion = await FocusedTextOutput.PasteAsync(target, () => CopyResult(pending), token,
                useShiftInsert: preferShiftInsert || (heldShift && !FocusedTextOutput.CanPasteImmediately()), waitForModifiers: false);
            if (insertion == TextInsertion.Inserted)
            {
                pastedLength = transcript.Length;
                // SendInput queues the shortcut. Give the target time to read the paste before reusing the clipboard.
                await Task.Delay(250, token);
            }
            else
            {
                livePaused = true;
                liveTargetChanged = insertion == TextInsertion.TargetChanged;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            livePaused = true;
            Context.Log.Error("Canlı dikte parçası çözümlenemedi.", error);
        }
        finally { Array.Clear(samples); }
    }

    private void AppendTranscript(string text)
    {
        if (text.Length == 0) return;
        if (transcript.Length > 0) transcript.Append(' ');
        transcript.Append(text);
    }

    private async Task StopAsync()
    {
        if (!recording || busy) return;
        recording = false; busy = true; monitor?.Stop(); monitor = null;
        var token = operation!.Token; var currentStore = activeStore!;
        var config = configuration!;
        float[] samples = [];
        try
        {
            var capture = microphone;
            microphone = null;
            try { capture?.Dispose(); }
            finally { RestorePlayback(); }
            ShowStatus("processing");
            // A slow provider can miss the initial lookup. Retry at stop only in the original native focus.
            var recoveredTarget = target is null ? RecoverTargetAsync(recordingWindow, recordingFocus, token) : null;
            await liveTask;
            token.ThrowIfCancellationRequested();
            samples = audio.Take(out var peak, trimSilence: true, startOffset: liveOffset);
            var hasSpeech = SpeechBuffer.ContainsSpeech(samples);
            if (!hasSpeech && transcript.Length == 0)
            {
                overlay?.Hide();
                Context.Notify(peak > 0 && peak < 0.01f
                    ? "Ses çok düşük algılandı. Mikrofon düzeyini ve seçilen mikrofonu kontrol edin."
                    : "Konuşma algılanmadı. Mikrofonu ve kayıt süresini kontrol edin.");
                return;
            }
            if (hasSpeech)
            {
                var started = Stopwatch.GetTimestamp();
                var final = await client.TranscribeAsync(samples, config, token);
                token.ThrowIfCancellationRequested();
                AppendTranscript(final);
                Context.Log.Info($"Dictation transcription: {client.Engine}; audio={(double)samples.Length / DictationAudio.SampleRate:F2} s; elapsed={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
            }
            var text = transcript.ToString();
            if (text.Length == 0) { overlay?.Hide(); Context.Notify("Metin bulunamadı."); return; }
            var pending = text[pastedLength..];
            uint clipboardSequence = 0;
            var insertion = liveTargetChanged ? TextInsertion.TargetChanged : TextInsertion.Inserted;
            if (pending.Length > 0 && !liveTargetChanged)
            {
                if (recoveredTarget is not null) target = await recoveredTarget;
                insertion = await FocusedTextOutput.PasteAsync(target, () => clipboardSequence = CopyResult(pending), token,
                    useShiftInsert: preferShiftInsert);
            }
            // A queued paste of the suffix needs to keep its clipboard payload. The complete text goes in history.
            if (pending.Length == 0 || (insertion != TextInsertion.Inserted && (clipboardSequence == 0 || pastedLength > 0)))
                clipboardSequence = CopyResult(text);
            var saved = false;
            try
            {
                await Task.Run(() => { token.ThrowIfCancellationRequested(); currentStore.Add("dictation", DateTimeOffset.UtcNow, title: Path.GetFileName(config.Model), text: text); }, token);
                saved = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                Context.Log.Error("Dikte geçmişe kaydedilemedi.", error);
                Context.Notify("Dikte geçmişe kaydedilemedi; metin yine de sunulacak.");
            }
            token.ThrowIfCancellationRequested();
            var copied = clipboardSequence != 0 && FocusedTextOutput.GetClipboardSequenceNumber() == clipboardSequence;
            overlay?.Hide();
            if (insertion != TextInsertion.Inserted)
            {
                var reason = insertion switch
                {
                    TextInsertion.TargetChanged => "Odak değiştiği için metnin tamamı yazılamadı.",
                    _ => "Metin hedef alana yazılamadı."
                };
                Context.Notify(reason + (copied ? " Tam metin panoda." : saved ? " Metni dikte geçmişinden alabilirsiniz." : " Metin kaydedilemedi veya panoya kopyalanamadı."));
            }
            else if (!copied) Context.Notify("Dikte metni panoya kopyalanamadı.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { overlay?.Hide(); Report(error); }
        finally { Array.Clear(samples); busy = false; }
    }
    private static uint CopyResult(string text)
    {
        try
        {
            Clipboard.SetText(text);
            var current = FocusedTextOutput.GetClipboardSequenceNumber();
            return current != 0 && Clipboard.GetText() == text && FocusedTextOutput.GetClipboardSequenceNumber() == current ? current : 0;
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException) { return 0; }
    }
    private void Cancel()
    {
        shortcutMonitor?.Stop();
        shortcut.Reset(); stopWhenStarted = false; starting = false; recording = false; operation?.Cancel(); monitor?.Stop(); monitor = null;
        microphone?.Dispose(); microphone = null; client.Dispose(); audio.Clear(); overlay?.Hide();
        RestorePlayback();
    }
    private void RestorePlayback() { playbackMute?.Dispose(); playbackMute = null; }
    private void ShowStatus(string phase)
    {
        if (!IsActive) return;
        overlay ??= new StatusOverlay((Context as IFeatureUiContext)?.FeatureUi);
        overlay.ShowStatus(phase, Context.Settings.GetString("overlay-position", "bottom-center"));
    }
    public override IEnumerable<QuickAction> GetQuickActions(string query) => base.GetQuickActions(query).Concat(new[]
    {
        new QuickAction(Id + ".toggle", "Dikteyi başlat / durdur", "whisper ses konuşma yazıya çevir", ToggleDictation),
        new QuickAction(Id + ".cancel", "Dikteyi iptal et", "whisper ses", Cancel)
    });
}

internal sealed class StatusOverlay : IDisposable
{
    internal const double StatusWidth = 76;
    internal const double StatusHeight = 48;
    private readonly IFeatureUiService? featureUi;
    private IFeatureUiWindow? statusWindow;
    private string phase = "preparing";

    public StatusOverlay(IFeatureUiService? featureUi)
    {
        this.featureUi = featureUi;
    }

    public void SetAudioLevel(double value)
    {
        if (phase == "listening") statusWindow?.SetState(new { phase, audioLevel = Math.Clamp(value, 0, 100) });
    }

    public void ShowStatus(string phase, string position)
    {
        this.phase = phase;
        statusWindow ??= OpenStatusWindow();
        if (statusWindow is null) return;
        statusWindow.SetState(new { phase, audioLevel = 0d });
        PlaceWindow(statusWindow, position, StatusWidth, StatusHeight, CursorPanelPlacement.WorkArea);
        statusWindow.Show();
    }

    public void Hide()
    {
        statusWindow?.Hide();
    }

    public void Close()
    {
        statusWindow?.Close();
        statusWindow = null;
    }

    public void Dispose() => Close();

    private IFeatureUiWindow? OpenStatusWindow()
    {
        if (featureUi is null) return null;
        return featureUi.Open("dictation-status", "dictation-status", "Dictation status",
            new FeatureUiWindowOptions(StatusWidth, StatusHeight, Topmost: true, ActivateOnShow: false,
                Transparent: true, ClickThrough: true));
    }

    private static void PlaceWindow(IFeatureUiWindow window, string position, double width, double height, Rect area)
    {
        const double margin = 20;
        var left = position switch
        {
            "top-left" or "bottom-left" => area.Left + margin,
            "center" or "bottom-center" => area.Left + (area.Width - width) / 2,
            _ => area.Right - width - margin
        };
        var top = position switch
        {
            "top-left" or "top-right" => area.Top + margin,
            "center" => area.Top + (area.Height - height) / 2,
            "bottom-center" => area.Bottom - height - 56,
            _ => area.Bottom - height - margin
        };
        window.SetBounds(left, top, width, height);
    }
}
