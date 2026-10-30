using System.Windows;
using System.Windows.Controls;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

public sealed class ScreenCaptureModule : CaptureModuleBase
{
    private IDisposable? shortcut;
    private GlobalKeyboardHook? cancellationHook;
    private CaptureArea? lastArea;
    private CancellationTokenSource? operation;
    private bool busy;
    private ScreenRecording? recording;
    private Window? controls;
    private Window? toolsWindow;
    public override string Id => "screen-capture";
    public override string Name => "Ekran Yakalama";
    public override string Description => "Bölge/pencere/ekran, kayan yakalama, işaretleme, OCR, video ve GIF kaydı.";
    protected override string Kind => "capture";
    protected override IEnumerable<SettingDescriptor> ModuleSettings =>
    [
        new() { Key = "capture", Label = "Yakalama araçlarını aç", Kind = SettingKind.Action, Execute = ShowTools },
        new() { Key = "hotkey", Label = "Bölge yakalama kısayolu", Kind = SettingKind.Text, Default = "Win+Alt+S" },
        new() { Key = "edit", Label = "Yakalama sonrası düzenle", Default = true },
        new() { Key = "copy", Label = "Yakalama sonrası panoya kopyala", Default = false },
        new() { Key = "save-history", Label = "Yakalama sonrası geçmişe kaydet", Default = true },
        new() { Key = "delay", Label = "Yakalama gecikmesi (saniye)", Kind = SettingKind.Number, Min = 0, Max = 30, Default = 0 },
        new() { Key = "microphone", Label = "Video kaydında mikrofon", Default = false },
        new() { Key = "system-audio", Label = "Video kaydında sistem sesi", Default = false },
        new() { Key = "cursor", Label = "Video kaydında imleci göster", Default = true },
        new() { Key = "setup-media", Label = "Video araçlarını hazırla (FFmpeg/FFprobe)", Kind = SettingKind.Action, Execute = async () =>
        {
            try { Context.Notify("Medya araçları hazırlanıyor…"); await SharedMediaTools.Create().InstallAsync(); Context.Notify("Video araçları hazır."); }
            catch (Exception error) { Report(error); }
        } },
        new() { Key = "cancel", Label = "Yakalamayı / kaydı iptal et", Kind = SettingKind.Action, Execute = Cancel }
    ];
    public override void Activate()
    {
        if (IsActive) return;
        RegisterShortcut();
        cancellationHook = new(); cancellationHook.KeyStateChanged += (key, down) => { if (key == 0x1B && down && busy) Cancel(); }; cancellationHook.Install();
        base.Activate();
    }
    private void RegisterShortcut()
    {
        var (modifiers, key) = ModuleShortcut.Parse(Context.Settings.GetString("hotkey", "Win+Alt+S"));
        shortcut?.Dispose(); shortcut = Context.Hotkeys.Register(modifiers, key, () => _ = CaptureAsync("region"));
    }
    public override void Apply() { if (IsActive) RegisterShortcut(); }
    public override void Deactivate() { IsActive = false; shortcut?.Dispose(); shortcut = null; cancellationHook?.Dispose(); cancellationHook = null; Cancel(); controls?.Close(); controls = null; toolsWindow?.Close(); toolsWindow = null; base.Deactivate(); }
    private void ShowTools()
    {
        if (!IsActive) { Context.Notify("Önce Ekran Yakalama modülünü etkinleştirin."); return; }
        if (toolsWindow is { } existing) { existing.Activate(); return; }
        var window = new Window { Title = Name, Width = 520, Height = 440 }; toolsWindow = window; window.Closed += (_, _) => toolsWindow = null; CaptureUi.Theme(window);
        var panel = new WrapPanel { Margin = new(20) }; window.Content = panel;
        foreach (var (mode, label) in new[] { ("region", "Bölge"), ("window", "Pencere"), ("monitor", "Monitör"), ("desktop", "Tüm ekranlar"), ("last", "Son bölge"), ("scroll", "Kayan ekran"), ("video", "Video kaydı"), ("gif", "GIF kaydı") })
            panel.Children.Add(CaptureUi.Button(label, () => { window.Close(); _ = CaptureAsync(mode); }));
        panel.Children.Add(CaptureUi.Button("Yerel geçmiş", Open)); window.Show();
    }
    internal async Task CaptureAsync(string mode)
    {
        if (busy || !IsActive) return; busy = true; operation?.Dispose(); operation = new(); var token = operation.Token;
        try
        {
            await Task.Delay(200 + Math.Clamp(Context.Settings.GetInt("delay"), 0, 30) * 1000, token);
            var foreground = CaptureServices.Foreground();
            CaptureArea? area = mode switch
            {
                "last" => lastArea,
                "monitor" => CaptureServices.Monitor(),
                "window" => CaptureServices.SelectWindow(),
                "active-window" => CaptureServices.WindowArea(foreground.Window),
                "desktop" => DesktopArea(),
                _ => CaptureServices.SelectArea()
            };
            if (area is not { IsValid: true } selected) return;
            token.ThrowIfCancellationRequested(); lastArea = selected;
            await Task.Delay(120, token); var store = Store;
            if (!CaptureServices.HasSpace(store.DirectoryPath)) throw new IOException("Kayıt için yeterli disk alanı yok.");
            if (mode is "video" or "gif")
            {
                recording = new(store.DirectoryPath, selected, Context.Settings.GetBool("microphone"), Context.Settings.GetBool("system-audio"), Context.Settings.GetBool("cursor", true));
                ShowRecordingControls(mode == "gif", store); return;
            }
            var image = mode == "scroll" ? null : CaptureServices.Capture(selected);
            var title = "Ekran görüntüsü";
            if (mode == "scroll")
            {
                var result = await ScrollingCapture.RunAsync(selected, token); image = result.Image;
                title = $"Kayan yakalama · %{result.Confidence * 100:0} güven{(result.Partial ? " · KISMİ SONUÇ" : "")}"; Context.Notify(title);
            }
            token.ThrowIfCancellationRequested();
            if (Context.Settings.GetBool("save-history", true))
            {
                var media = await Task.Run(() => CaptureServices.Save(store, image!), token);
                token.ThrowIfCancellationRequested(); store.Add("capture", DateTimeOffset.UtcNow, foreground.App, title, media: media);
            }
            if (Context.Settings.GetBool("copy")) Clipboard.SetImage(image!);
            if (Context.Settings.GetBool("edit", true)) new CaptureEditor(store, image!).Show();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Report(error); }
        finally { if (recording is null) busy = false; }
    }
    private static CaptureArea DesktopArea()
    {
        var displays = DesktopDisplays.Enumerate(); var x = displays.Min(d => d.Bounds.Left); var y = displays.Min(d => d.Bounds.Top);
        return new(x, y, displays.Max(d => d.Bounds.Right) - x, displays.Max(d => d.Bounds.Bottom) - y);
    }
    private void ShowRecordingControls(bool gif, HistoryStore store)
    {
        var window = new Window { Title = "Kayıt sürüyor", Width = 440, Height = 150, Topmost = true }; controls = window; CaptureUi.Theme(window);
        var panel = new WrapPanel { Margin = new(14) }; window.Content = panel;
        var pause = CaptureUi.Button("Duraklat", () => { recording?.TogglePause(); window.Title = recording?.Paused == true ? "Kayıt duraklatıldı" : "Kayıt sürüyor"; }); panel.Children.Add(pause);
        panel.Children.Add(CaptureUi.Button("Bitir ve kaydet", async () =>
        {
            if (recording is not { } session) return; recording = null; window.IsEnabled = false;
            var token = operation!.Token;
            try
            {
                var file = await session.FinishAsync(gif, token); var media = await store.ImportMediaAsync(file, token); token.ThrowIfCancellationRequested();
                store.Add("capture", DateTimeOffset.UtcNow, title: gif ? "GIF kaydı" : "Video kaydı", media: media);
                Context.Notify(session.Error is null ? "Kayıt geçmişe kaydedildi." : "Kısmi kayıt kaydedildi: " + session.Error);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Report(error); }
            finally { await session.DisposeAsync(); busy = false; window.Close(); }
        }));
        panel.Children.Add(CaptureUi.Button("İptal", Cancel));
        window.Closed += (_, _) => { if (recording is not null) Cancel(); }; window.Show();
    }
    private async void Cancel()
    {
        operation?.Cancel(); var session = recording; recording = null;
        try { if (session is not null) { session.RequestCancel(); await session.DisposeAsync(); } }
        catch (Exception error) { Report(error); }
        finally { if (session is not null) busy = false; controls?.Close(); controls = null; }
    }
    public override IEnumerable<QuickAction> GetQuickActions(string query) => base.GetQuickActions(query).Concat(new[]
    {
        new QuickAction(Id + ".region", "Ekrandan bölge yakala", "sharex screenshot", () => _ = CaptureAsync("region")),
        new QuickAction(Id + ".tools", "Ekran yakalama araçları", "video gif kayan ekran", ShowTools)
    });
}
