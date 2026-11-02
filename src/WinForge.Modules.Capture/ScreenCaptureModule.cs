using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Text.Json;
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
    private IFeatureUiWindow? controls;
    private IFeatureUiWindow? toolsWindow;
    private CapturePreviewWindow? preview;
    public override string Id => "screen-capture";
    public override string Name => "Ekran Yakalama";
    public override string Description => "Bölge/pencere/ekran, kayan yakalama, işaretleme, OCR, video ve GIF kaydı.";
    protected override string Kind => "capture";
    protected override IEnumerable<SettingDescriptor> ModuleSettings =>
    [
        new() { Key = "image-directory", Label = "Yakalanan görsellerin klasörü", Kind = SettingKind.Text, Default = "",
            Description = "Boş bırakılırsa geçmişin medya klasörü kullanılır." },
        new() { Key = "choose-image-directory", Label = "Yakalanan görseller için klasör seç", Kind = SettingKind.Action, Execute = () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Yakalama klasörünü seç" };
            if (picker.ShowDialog() == true) Context.Settings.Set("image-directory", picker.FolderName);
        } },
        new() { Key = "capture", Label = "Yakalama araçlarını aç", Kind = SettingKind.Action, Execute = ShowTools },
        new() { Key = "hotkey", Label = "Bölge yakalama kısayolu", Kind = SettingKind.Text, Default = "Win+Alt+S" },
        new() { Key = "edit", Label = "Yakalama sonrası düzenle", Default = true },
        new() { Key = "copy", Label = "Yakalama sonrası panoya kopyala", Default = true },
        new() { Key = "save-history", Label = "Yakalama sonrası geçmişe kaydet", Default = true },
        new() { Key = "upload", Label = "Yakalama sonrası özel adrese yükle", Default = false,
            Description = "Yükleme varsayılan olarak kapalıdır; yalnızca aşağıdaki http/https adresi ve işlem sırasında yükle adımı seçilirse çalışır." },
        new() { Key = "upload-url", Label = "Özel yükleme HTTP adresi", Kind = SettingKind.Text, Default = "",
            Description = "PNG dosyasını multipart form alanı 'file' ile gönderir. Yanıt URL metni, Location başlığı veya url/link/direct_url JSON alanı olmalıdır." },
        new() { Key = "workflow", Label = "Yakalama sonrası işlem sırası", Kind = SettingKind.Choice, Default = "save-copy-edit-upload",
            Options = WorkflowOptions, OptionLabels = WorkflowLabels },
        new() { Key = "region-confirm", Label = "Bölge seçimini taşıyıp boyutlandır, Enter ile yakala", Default = false,
            Description = "Açıkken bölgeyi sürükleyerek yeniden konumlandırın, sağ alt köşeden boyutlandırın ve Enter ile onaylayın." },
        new() { Key = "annotate-before-capture", Label = "Bölge yakalamada çekim öncesi çizim ve işaretleme", Default = true,
            Description = "Alan seçiminden önce veya sonra kalem, ok, şekil ve vurgulama kullanın; Yakala veya Enter ile işaretlerle birlikte alın. Kapalıyken hızlı bölge seçimi kullanılır." },
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
        var configured = CaptureShortcut.Parse(Context.Settings.GetString("hotkey", "Win+Alt+S"));
        shortcut?.Dispose(); shortcut = configured.Register(Context.Hotkeys, () => _ = CaptureAsync("region"));
    }
    public override void Apply() { if (IsActive) RegisterShortcut(); }
    public override void Deactivate() { IsActive = false; shortcut?.Dispose(); shortcut = null; cancellationHook?.Dispose(); cancellationHook = null; Cancel(); preview?.Close(); controls?.Close(); controls = null; toolsWindow?.Close(); toolsWindow = null; base.Deactivate(); }
    private void ShowTools()
    {
        if (!IsActive) { Context.Notify("Önce Ekran Yakalama modülünü etkinleştirin."); return; }
        toolsWindow = (Context as IFeatureUiContext)?.FeatureUi?.Open("capture-tools", "capture-tools", Name,
            new FeatureUiWindowOptions(520, 440, Topmost: true));
        if (toolsWindow is null)
        {
            Context.Notify("React arayüz altyapısı kullanılamıyor.");
            return;
        }
        toolsWindow.MessageReceived -= OnToolsMessage;
        toolsWindow.MessageReceived += OnToolsMessage;
        toolsWindow.SetState(new { title = Name });
    }

    private void OnToolsMessage(string command, JsonElement payload)
    {
        if (command == "capture-tools.close")
        {
            toolsWindow?.Close();
            toolsWindow = null;
            return;
        }
        if (command != "capture-tools.run" || !payload.TryGetProperty("mode", out var modeElement)) return;
        var mode = modeElement.GetString();
        if (string.IsNullOrWhiteSpace(mode)) return;
        toolsWindow?.Close();
        toolsWindow = null;
        _ = CaptureAsync(mode);
    }

    internal async Task CaptureAsync(string mode)
    {
        if (busy || !IsActive) return; busy = true; operation?.Dispose(); operation = new(); var token = operation.Token;
        try
        {
            preview?.Close();
            await Task.Delay(200 + Math.Clamp(Context.Settings.GetInt("delay"), 0, 30) * 1000, token);
            var foreground = CaptureServices.Foreground();
            if (mode == "multi-region")
            {
                var areas = CaptureServices.SelectMultipleAreas(out var multiSnapshot);
                await CaptureMultipleAreasAsync(areas, multiSnapshot, foreground, token);
                return;
            }
            var annotationTool = mode switch
            {
                "annotate-text" => DrawingTool.Text,
                "annotate-shape" => DrawingTool.Rectangle,
                "annotate-freehand" => DrawingTool.Freehand,
                _ => (DrawingTool?)null
            };
            CaptureAnnotations? annotations = null;
            FrozenDesktop? snapshot = null;
            CaptureArea? SelectRegion()
            {
                if (!Context.Settings.GetBool("annotate-before-capture", true)) return CaptureServices.SelectArea(!Context.Settings.GetBool("region-confirm"), out snapshot);
                var selection = CaptureServices.SelectAnnotatedArea(); annotations = selection.Annotations; snapshot = selection.Snapshot; return selection.Area;
            }
            CaptureArea? area = annotationTool is not null ? DesktopArea() : mode switch
            {
                "region" => SelectRegion(),
                "last" => lastArea,
                "monitor" => CaptureServices.Monitor(),
                "window" => CaptureServices.SelectWindow(out snapshot),
                "active-window" => CaptureServices.WindowArea(foreground.Window),
                "desktop" => DesktopArea(),
                _ => CaptureServices.SelectArea(!Context.Settings.GetBool("region-confirm"), out snapshot)
            };
            if (area is not { IsValid: true } selected)
            {
                if (mode == "last") Context.Notify("Önce bir bölge yakalayın; henüz kayıtlı bir son bölge yok.");
                return;
            }
            token.ThrowIfCancellationRequested();
            if (annotationTool is null) lastArea = selected;
            await Task.Delay(120, token); var store = Store;
            if (!CaptureServices.HasSpace(store.DirectoryPath)) throw new IOException("Kayıt için yeterli disk alanı yok.");
            if (mode is "video" or "gif")
            {
                recording = new(store.DirectoryPath, selected, Context.Settings.GetBool("microphone"), Context.Settings.GetBool("system-audio"), Context.Settings.GetBool("cursor", true), Math.Clamp(Context.Settings.GetInt("fps", 15), 5, 120));
                ShowRecordingControls(mode == "gif", store); return;
            }
            var image = mode == "scroll" ? null : snapshot?.Crop(selected) ?? CaptureServices.Capture(selected);
            if (image is not null && annotations is not null) image = annotations.Apply(image, selected);
            var title = "Ekran görüntüsü";
            if (mode == "scroll")
            {
                var result = await ScrollingCapture.RunAsync(selected, token); image = result.Image;
                title = $"Kayan yakalama · %{result.Confidence * 100:0} güven{(result.Partial ? " · KISMİ SONUÇ" : "")}"; Context.Notify(title);
            }
            token.ThrowIfCancellationRequested();
            ShowPreview(image!);
            if (annotationTool is null) await ProcessAfterCaptureAsync(image!, store, foreground.App, title, token);
            else new CaptureEditor(store, image!, annotationTool, featureUi: (Context as IFeatureUiContext)?.FeatureUi).Show();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Report(error); }
        finally { if (recording is null) busy = false; }
    }
    private async Task CaptureMultipleAreasAsync(IReadOnlyList<CaptureArea> areas, FrozenDesktop? snapshot, ForegroundInfo foreground, CancellationToken token)
    {
        if (areas.Count == 0) return;
        var store = Store;
        if (!CaptureServices.HasSpace(store.DirectoryPath)) throw new IOException("Kayıt için yeterli disk alanı yok.");
        for (var index = 0; index < areas.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            preview?.Close();
            var image = snapshot?.Crop(areas[index]) ?? CaptureServices.Capture(areas[index]); lastArea = areas[index];
            ShowPreview(image);
            await ProcessAfterCaptureAsync(image, store, foreground.App, $"Çoklu bölge {index + 1}/{areas.Count}", token);
        }
        Context.Notify($"{areas.Count} bölge yakalandı.");
    }

    private void ShowPreview(BitmapSource image)
    {
        preview?.Close();
        var store = Store;
        var window = new CapturePreviewWindow(image, () =>
        {
            try
            {
                var media = CaptureServices.Save(store, image);
                store.ReleasePendingMedia(media);
                var path = ExportImage(image) ?? store.ResolveMedia(media);
                var info = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                info.Arguments = "/select,\"" + path + "\"";
                System.Diagnostics.Process.Start(info);
            }
            catch (Exception error) { Report(error); }
        });
        preview = window;
        window.Closed += (_, _) => { if (ReferenceEquals(preview, window)) preview = null; };
        window.Show();
    }

    private async Task ProcessAfterCaptureAsync(BitmapSource image, HistoryStore store, string app, string title, CancellationToken token)
    {
        ExportImage(image);
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
                case "copy" when Context.Settings.GetBool("copy", true):
                    await CaptureClipboard.CopyAsync(image, token);
                    break;
                case "edit" when Context.Settings.GetBool("edit", true):
                    var editor = new CaptureEditor(store, image, workflowCompletion: true,
                        featureUi: (Context as IFeatureUiContext)?.FeatureUi);
                    BitmapSource? editedImage;
                    try { editedImage = await editor.Completion.WaitAsync(token); }
                    finally { editor.Close(); }
                    if (editedImage is null) return;
                    image = editedImage;
                    break;
                case "upload" when Context.Settings.GetBool("upload"):
                    var link = await ImageUploadService.UploadAsync(image, Context.Settings.GetString("upload-url"), token);
                    Clipboard.SetText(link.AbsoluteUri);
                    Context.Notify("Görsel yüklendi; bağlantı panoya kopyalandı.");
                    break;
            }
        }
    }

    private string? ExportImage(BitmapSource image)
    {
        var directory = Context.Settings.GetString("image-directory").Trim();
        if (directory.Length == 0) return null;
        directory = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory));
        System.IO.Directory.CreateDirectory(directory);
        // Content addressing gives the workflow and preview the same file without duplicate exports.
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var bytes = new System.IO.MemoryStream(); encoder.Save(bytes);
        var data = bytes.ToArray();
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)) + ".png";
        var path = System.IO.Path.Combine(directory, name);
        if (!System.IO.File.Exists(path)) System.IO.File.WriteAllBytes(path, data);
        return path;
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
        var featureUi = (Context as IFeatureUiContext)?.FeatureUi
            ?? throw new InvalidOperationException("The React recording controls are required.");
        controls = featureUi.Open("capture-recording", "capture-recording", "Kayıt sürüyor",
            new FeatureUiWindowOptions(440, 170, Topmost: true));
        controls.MessageReceived += OnRecordingMessage;
        PushRecordingState(gif, busy: false, "Kayıt sürüyor");
        controls.Show();

        async void OnRecordingMessage(string command, JsonElement _)
        {
            switch (command)
            {
                case "capture-recording.pause":
                    recording?.TogglePause();
                    PushRecordingState(gif, busy: false, recording?.Paused == true ? "Kayıt duraklatıldı" : "Kayıt sürüyor");
                    break;
                case "capture-recording.finish":
                    await FinishRecordingAsync(gif, store);
                    break;
                case "capture-recording.cancel":
                    Cancel();
                    break;
            }
        }
    }

    private async Task FinishRecordingAsync(bool gif, HistoryStore store)
    {
        if (recording is not { } session) return;
        recording = null;
        var token = operation!.Token;
        PushRecordingState(gif, busy: true, "Kayıt tamamlanıyor…");
        try
        {
            var file = await session.FinishAsync(gif, token);
            var media = await store.ImportMediaAsync(file, token);
            token.ThrowIfCancellationRequested();
            store.Add("capture", DateTimeOffset.UtcNow, title: gif ? "GIF kaydı" : "Video kaydı", media: media);
            Context.Notify(session.Error is null ? "Kayıt geçmişe kaydedildi." : "Kısmi kayıt kaydedildi: " + session.Error);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Report(error); }
        finally
        {
            await session.DisposeAsync();
            busy = false;
            controls?.Close();
            controls = null;
        }
    }

    private void PushRecordingState(bool gif, bool busy, string status) => controls?.SetState(new
    {
        gif,
        busy,
        paused = recording?.Paused == true,
        status
    });
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
        new QuickAction(Id + ".window", "Pencere yakala", "sharex screenshot window", () => _ = CaptureAsync("window")),
        new QuickAction(Id + ".active-window", "Etkin pencereyi yakala", "sharex screenshot active window", () => _ = CaptureAsync("active-window")),
        new QuickAction(Id + ".monitor", "Monitörü yakala", "sharex screenshot display", () => _ = CaptureAsync("monitor")),
        new QuickAction(Id + ".desktop", "Tüm masaüstünü yakala", "sharex screenshot desktop", () => _ = CaptureAsync("desktop")),
        new QuickAction(Id + ".multi-region", "Birden fazla bölge yakala", "sharex multiple screenshot", () => _ = CaptureAsync("multi-region")),
        new QuickAction(Id + ".scroll", "Kayan ekranı yakala", "sharex scrolling screenshot", () => _ = CaptureAsync("scroll")),
        new QuickAction(Id + ".video", "Ekran videosu kaydet", "sharex video recording", () => _ = CaptureAsync("video")),
        new QuickAction(Id + ".gif", "Ekran GIF'i kaydet", "sharex gif recording", () => _ = CaptureAsync("gif")),
        new QuickAction(Id + ".tools", "Ekran yakalama araçları", "video gif kayan ekran", ShowTools)
    });
}
