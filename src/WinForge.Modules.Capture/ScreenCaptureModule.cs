using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

public sealed class ScreenCaptureModule : CaptureModuleBase
{
    private static readonly string[] WorkflowActions = ["save", "copy", "edit", "upload"];
    private static readonly string[] WorkflowOptions = BuildWorkflowOptions();
    private static readonly Dictionary<string, string> WorkflowLabels = WorkflowOptions.ToDictionary(
        value => value,
        value => string.Join(" → ", value.Split('-').Select(WorkflowActionLabel)));
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
        new() { Key = "upload", Label = "Yakalama sonrası özel adrese yükle", Default = false,
            Description = "Yükleme varsayılan olarak kapalıdır; yalnızca aşağıdaki http/https adresi ve işlem sırasında yükle adımı seçilirse çalışır." },
        new() { Key = "upload-url", Label = "Özel yükleme HTTP adresi", Kind = SettingKind.Text, Default = "",
            Description = "PNG dosyasını multipart form alanı 'file' ile gönderir. Yanıt URL metni, Location başlığı veya url/link/direct_url JSON alanı olmalıdır." },
        new() { Key = "workflow", Label = "Yakalama sonrası işlem sırası", Kind = SettingKind.Choice, Default = "save-copy-edit-upload",
            Options = WorkflowOptions, OptionLabels = WorkflowLabels },
        new() { Key = "region-confirm", Label = "Bölge seçimini taşıyıp boyutlandır, Enter ile yakala", Default = false,
            Description = "Açıkken bölgeyi sürükleyerek yeniden konumlandırın, sağ alt köşeden boyutlandırın ve Enter ile onaylayın." },
        new() { Key = "delay", Label = "Yakalama gecikmesi (saniye)", Kind = SettingKind.Number, Min = 0, Max = 30, Default = 0 },
        new() { Key = "microphone", Label = "Video kaydında mikrofon", Default = false },
        new() { Key = "system-audio", Label = "Video kaydında sistem sesi", Default = false },
        new() { Key = "cursor", Label = "Video kaydında imleci göster", Default = true },
        new() { Key = "fps", Label = "Video kare hızı (5–120 FPS)", Kind = SettingKind.Number, Min = 5, Max = 120, Default = 15 },
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
        foreach (var (mode, label) in new[]
        {
            ("region", "Bölge yakala"), ("window", "Pencere yakala"), ("monitor", "Monitör yakala"), ("desktop", "Tüm ekranlar"),
            ("last", "Son bölge"), ("scroll", "Kayan ekran"), ("video", "Video kaydı"), ("gif", "GIF kaydı"),
            ("annotate-text", "Ekrana yazı yaz"), ("annotate-shape", "Şekil çiz"), ("annotate-freehand", "Serbest çiz")
        })
            panel.Children.Add(CaptureUi.Button(label, () => { window.Close(); _ = CaptureAsync(mode); }));
        panel.Children.Add(CaptureUi.Button("Çoklu bölge yakala", () => { window.Close(); _ = CaptureAsync("multi-region"); }));
        panel.Children.Add(CaptureUi.Button("Yerel geçmiş", Open)); window.Show();
    }
    internal async Task CaptureAsync(string mode)
    {
        if (busy || !IsActive) return; busy = true; operation?.Dispose(); operation = new(); var token = operation.Token;
        try
        {
            await Task.Delay(200 + Math.Clamp(Context.Settings.GetInt("delay"), 0, 30) * 1000, token);
            var foreground = CaptureServices.Foreground();
            if (mode == "multi-region")
            {
                await CaptureMultipleAreasAsync(CaptureServices.SelectMultipleAreas(), foreground, token);
                return;
            }
            var annotationTool = mode switch
            {
                "annotate-text" => DrawingTool.Text,
                "annotate-shape" => DrawingTool.Rectangle,
                "annotate-freehand" => DrawingTool.Freehand,
                _ => (DrawingTool?)null
            };
            CaptureArea? area = annotationTool is not null ? DesktopArea() : mode switch
            {
                "last" => lastArea,
                "monitor" => CaptureServices.Monitor(),
                "window" => CaptureServices.SelectWindow(),
                "active-window" => CaptureServices.WindowArea(foreground.Window),
                "desktop" => DesktopArea(),
                _ => CaptureServices.SelectArea(!Context.Settings.GetBool("region-confirm"))
            };
            if (area is not { IsValid: true } selected) return;
            token.ThrowIfCancellationRequested();
            if (annotationTool is null) lastArea = selected;
            await Task.Delay(120, token); var store = Store;
            if (!CaptureServices.HasSpace(store.DirectoryPath)) throw new IOException("Kayıt için yeterli disk alanı yok.");
            if (mode is "video" or "gif")
            {
                recording = new(store.DirectoryPath, selected, Context.Settings.GetBool("microphone"), Context.Settings.GetBool("system-audio"), Context.Settings.GetBool("cursor", true), Math.Clamp(Context.Settings.GetInt("fps", 15), 5, 120));
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
            if (annotationTool is null) await ProcessAfterCaptureAsync(image!, store, foreground.App, title, token);
            else new CaptureEditor(store, image!, annotationTool).Show();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Report(error); }
        finally { if (recording is null) busy = false; }
    }
    private async Task CaptureMultipleAreasAsync(IReadOnlyList<CaptureArea> areas, ForegroundInfo foreground, CancellationToken token)
    {
        if (areas.Count == 0) return;
        var store = Store;
        if (!CaptureServices.HasSpace(store.DirectoryPath)) throw new IOException("Kayıt için yeterli disk alanı yok.");
        for (var index = 0; index < areas.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var image = CaptureServices.Capture(areas[index]); lastArea = areas[index];
            await ProcessAfterCaptureAsync(image, store, foreground.App, $"Çoklu bölge {index + 1}/{areas.Count}", token);
        }
        Context.Notify($"{areas.Count} bölge yakalandı.");
    }

    private async Task ProcessAfterCaptureAsync(BitmapSource image, HistoryStore store, string app, string title, CancellationToken token)
    {
        var sequence = Context.Settings.GetString("workflow", "save-copy-edit-upload").Split('-');
        foreach (var step in sequence)
        {
            token.ThrowIfCancellationRequested();
            switch (step)
            {
                case "save" when Context.Settings.GetBool("save-history", true):
                    var media = await Task.Run(() => CaptureServices.Save(store, image), token);
                    token.ThrowIfCancellationRequested(); store.Add("capture", DateTimeOffset.UtcNow, app, title, media: media);
                    break;
                case "copy" when Context.Settings.GetBool("copy"):
                    Clipboard.SetImage(image);
                    break;
                case "edit" when Context.Settings.GetBool("edit", true):
                    var editor = new CaptureEditor(store, image, workflowCompletion: true);
                    if (editor.ShowDialog() != true || editor.CompletedImage is null) return;
                    image = editor.CompletedImage;
                    break;
                case "upload" when Context.Settings.GetBool("upload"):
                    var link = await ImageUploadService.UploadAsync(image, Context.Settings.GetString("upload-url"), token);
                    Clipboard.SetText(link.AbsoluteUri);
                    Context.Notify("Görsel yüklendi; bağlantı panoya kopyalandı.");
                    break;
            }
        }
    }

    private static string[] BuildWorkflowOptions()
    {
        var result = new List<string>();
        AddPermutations([], WorkflowActions.ToList(), result);
        return result.ToArray();
    }

    private static void AddPermutations(List<string> prefix, List<string> remaining, List<string> result)
    {
        if (remaining.Count == 0)
        {
            result.Add(string.Join('-', prefix));
            return;
        }

        foreach (var action in remaining)
        {
            var nextPrefix = new List<string>(prefix) { action };
            var nextRemaining = remaining.Where(candidate => candidate != action).ToList();
            AddPermutations(nextPrefix, nextRemaining, result);
        }
    }

    private static string WorkflowActionLabel(string action) => action switch
    {
        "save" => "Kaydet",
        "copy" => "Kopyala",
        "edit" => "Düzenle",
        "upload" => "Yükle",
        _ => action
    };

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
        new QuickAction(Id + ".multi-region", "Birden fazla bölge yakala", "sharex multiple screenshot", () => _ = CaptureAsync("multi-region")),
        new QuickAction(Id + ".tools", "Ekran yakalama araçları", "video gif kayan ekran", ShowTools)
    });
}
