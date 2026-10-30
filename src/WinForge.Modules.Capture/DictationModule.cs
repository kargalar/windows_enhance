using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
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
    private IDisposable? hotkey;
    private GlobalKeyboardHook? hook;
    private CancellationTokenSource? operation;
    private TextTarget? target;
    private HistoryStore? activeStore;
    private StatusOverlay? overlay;
    private bool recording, busy;
    private uint shortcutKey;
    private DispatcherTimer? monitor;
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
                new() { Key = "hotkey", Label = "Dikte kısayolu", Kind = SettingKind.Text, Default = DictationDefaults.Hotkey },
                new() { Key = "hold", Label = "Basılı tutarak konuş", Default = false },
                new() { Key = "language", Label = "Dil", Kind = SettingKind.Choice, Default = "auto", Options = DictationLanguages.All.Keys.ToArray(), OptionLabels = DictationLanguages.All },
                new() { Key = "model", Label = "Model", Kind = SettingKind.Choice, Default = "0", Options = ["0", "1"], OptionLabels = new Dictionary<string, string> { ["0"] = "Turbo", ["1"] = "Large" } },
                new() { Key = "output", Label = "İngilizceye çevir (Large kullanır)", Default = false },
                new() { Key = "cpu", Label = "Yalnız CPU kullan", Default = false },
                new() { Key = "microphone", Label = "Mikrofon", Kind = SettingKind.Choice, Default = "", Options = devices.Select(d => d.Id).ToArray(), OptionLabels = devices.ToDictionary(d => d.Id, d => d.Name) },
                new() { Key = "models", Label = "Modelleri yönet", Kind = SettingKind.Action, Execute = () => new ModelWindow(Store).Show() },
                new() { Key = "cancel", Label = "Kaydı / çözümlemeyi iptal et", Kind = SettingKind.Action, Execute = Cancel }
            ];
        }
    }
    public override void Activate()
    {
        if (IsActive) return;
        activeStore = Store;
        try { Register(); base.Activate(); }
        catch { Deactivate(); throw; }
    }
    private void Register()
    {
        hotkey?.Dispose(); hook?.Dispose(); hotkey = null; hook = null;
        var (modifiers, key) = ModuleShortcut.Parse(Context.Settings.GetString("hotkey", DictationDefaults.Hotkey)); shortcutKey = key;
        hotkey = Context.Hotkeys.Register(modifiers, shortcutKey, () =>
        {
            if (!IsActive) return;
            var action = shortcut.Press(Context.Settings.GetBool("hold"), recording);
            if (action == DictationKeyAction.Start) _ = StartAsync(); else if (action == DictationKeyAction.Stop) _ = StopAsync();
        });
        hook = new(); hook.KeyStateChanged += (vk, down) =>
        {
            if (vk == 0x1B && down && (recording || busy)) Cancel();
            if (vk == shortcutKey && !down && shortcut.Release() == DictationKeyAction.Stop) _ = StopAsync();
        }; hook.Install();
    }
    public override void Apply() { if (IsActive) { Cancel(); Register(); } }
    public override void Deactivate()
    {
        IsActive = false; Cancel(); hotkey?.Dispose(); hotkey = null; hook?.Dispose(); hook = null;
        client.Dispose(); overlay?.Close(); overlay = null; activeStore = null; base.Deactivate();
    }
    private async Task StartAsync()
    {
        if (busy || recording || !IsActive) return;
        busy = true; operation?.Dispose(); operation = new(); var token = operation.Token;
        try
        {
            target = await Task.Run(FocusedTextOutput.Capture).WaitAsync(TimeSpan.FromMilliseconds(800), token);
            if (token.IsCancellationRequested || !IsActive || (Context.Settings.GetBool("hold") && !shortcut.Held)) return;
            var translate = Context.Settings.GetBool("output") || Context.Settings.GetString("output") == "1";
            var model = translate ? 1 : Math.Clamp(Context.Settings.GetInt("model"), 0, 1);
            if (!File.Exists(Path.Combine(activeStore!.DirectoryPath, "models", DictationDefaults.ModelFiles[model])))
                throw new FileNotFoundException("Model bulunamadı. Modelleri yönet bölümünden ekleyin.");
            audio.Clear();
            microphone = new(true, Context.Settings.GetString("microphone")); microphone.SamplesAvailable += Samples;
            try { microphone.Start(); }
            catch when (Context.Settings.GetString("microphone").Length > 0)
            {
                microphone.Dispose(); microphone = new(true); microphone.SamplesAvailable += Samples; microphone.Start(); Context.Notify("Seçilen mikrofon bulunamadı; varsayılan kullanılıyor.");
            }
            recording = true; ShowStatus("Dinleniyor… · Esc: iptal");
            monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            monitor.Tick += (_, _) =>
            {
                if (microphone?.LastError is { } error) { Cancel(); Report(new InvalidOperationException("Mikrofon bağlantısı kesildi.", error)); }
                else if (audio.Full) _ = StopAsync();
            }; monitor.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Cancel(); Report(error); }
        finally { busy = false; }
    }
    private void Samples(ReadOnlySpan<float> samples, int rate)
    {
        audio.Append(samples, rate);
    }
    private async Task StopAsync()
    {
        if (!recording || busy) return;
        recording = false; busy = true; monitor?.Stop(); monitor = null; microphone?.Dispose(); microphone = null;
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
            ShowStatus(result == TextInsertion.Inserted ? "Metin yazıldı · " + client.Engine : "Hedef değişti veya yazma engellendi; metin geçmişte.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Report(error); ShowStatus("Dikte başarısız; yeniden deneyebilirsiniz."); }
        finally { Array.Clear(samples); busy = false; }
    }
    private void Cancel()
    {
        shortcut.Reset(); recording = false; operation?.Cancel(); monitor?.Stop(); monitor = null;
        microphone?.Dispose(); microphone = null; client.Dispose(); audio.Clear(); overlay?.Hide();
    }
    private void ShowStatus(string text)
    {
        if (!IsActive) return; overlay ??= new StatusOverlay(); overlay.Message.Text = text; overlay.Show();
    }
    public override IEnumerable<QuickAction> GetQuickActions(string query) => base.GetQuickActions(query).Append(new(Id + ".cancel", "Dikteyi iptal et", "whisper ses", Cancel));
}

internal sealed class StatusOverlay : Window
{
    public TextBlock Message { get; } = new() { Margin = new(18), Foreground = Brushes.WhiteSmoke };
    public StatusOverlay()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; Background = new SolidColorBrush(Color.FromRgb(25, 30, 38)); Content = Message;
        Left = SystemParameters.WorkArea.Left + 24; Top = SystemParameters.WorkArea.Bottom - 90;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle; SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x80);
        };
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
