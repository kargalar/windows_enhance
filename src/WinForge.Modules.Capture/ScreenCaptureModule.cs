using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Text.Json;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

public sealed class ScreenCaptureModule : CaptureModuleBase, ISettingChangePreparation
{
    private static readonly string[] WorkflowActions = ["save", "copy", "edit", "upload"];
    private static readonly string[] WorkflowOptions = BuildWorkflowOptions();
    private static readonly Dictionary<string, string> WorkflowLabels = WorkflowOptions.ToDictionary(
        value => value,
        value => string.Join(" → ", value.Split('-').Select(WorkflowActionLabel)));
    private readonly List<IDisposable> shortcuts = [];
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
            Description = "Boş bırakılırsa geçmişin Görseller klasörü kullanılır. Video ve GIF kayıtları geçmişin Videolar klasörüne yazılır." },
        new() { Key = "choose-image-directory", Label = "Yakalanan görseller için klasör seç", Kind = SettingKind.Action, Execute = () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Yakalama klasörünü seç" };
            if (picker.ShowDialog() != true) return;
            try
            {
                PrepareSettingChange("image-directory", Context.Settings.GetString("image-directory"), picker.FolderName);
                Context.Settings.Set("image-directory", picker.FolderName);
            }
            catch (Exception error) { Report(error); }
        } },
        new() { Key = "capture", Label = "Yakalama araçlarını aç", Kind = SettingKind.Action, Execute = ShowTools },
        new() { Key = "hotkey", Label = "Bölge yakalama kısayolu", Kind = SettingKind.Text, Default = "Win+Alt+S" },
        new() { Key = "monitor-hotkey", Label = "İmleçteki monitörü yakalama kısayolu", Kind = SettingKind.Text, Default = "",
            Description = "Fare imlecinin bulunduğu monitörün tamamını alan seçmeden yakalar. Kapatmak için boş bırakın." },
        new() { Key = "edit", Label = "Yakalama sonrası düzenle", Default = true },
        new() { Key = "copy", Label = "Yakalama sonrası panoya kopyala", Default = true },
        new() { Key = "save-history", Label = "Yakalama sonrası geçmişe kaydet", Default = true },
        new() { Key = "upload", Label = "Yakalama sonrası özel adrese yükle", Default = false,
            Description = "Yükleme varsayılan olarak kapalıdır; yalnızca aşağıdaki http/https adresi ve işlem sırasında yükle adımı seçilirse çalışır." },
        new() { Key = "upload-url", Label = "Özel yükleme HTTP adresi", Kind = SettingKind.Text, Default = "",
            Description = "PNG dosyasını multipart form alanı 'file' ile gönderir. Yanıt URL metni, Location başlığı veya url/link/direct_url JSON alanı olmalıdır." },
        new() { Key = "workflow", Label = "Yakalama sonrası işlem sırası", Kind = SettingKind.Choice, Default = "save-copy-edit-upload",
            Options = WorkflowOptions, OptionLabels = WorkflowLabels },
        new() { Key = "delay", Label = "Yakalama gecikmesi (saniye)", Kind = SettingKind.Number, Min = 0, Max = 30, Default = 0 },
        new() { Key = "video-region-hotkey", Label = "Bölge video kaydı kısayolu", Kind = SettingKind.Text, Default = "",
            Description = "Bir alan seçtikten sonra video kaydını başlatır. Kapatmak için boş bırakın." },
        new() { Key = "video-monitor-hotkey", Label = "Tam ekran video kaydı kısayolu", Kind = SettingKind.Text, Default = "",
            Description = "Fare imlecinin bulunduğu monitörün tamamını kaydetmeye başlar. Kapatmak için boş bırakın." },
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
    public void PrepareSettingChange(string key, object previous, object next)
    {
        if (key != "image-directory") return;
        if (busy || recording is not null) throw new InvalidOperationException("Klas?r? de?i?tirmeden ?nce devam eden yakalamay? bitirin.");
        CaptureStorageMigration.Images(Store, previous?.ToString() ?? "", next?.ToString() ?? "");
    }
    public override void Activate()
    {
        if (IsActive) return;
        RegisterShortcuts();
        cancellationHook = new(); cancellationHook.KeyStateChanged += (key, down) => { if (key == 0x1B && down && busy) Cancel(); }; cancellationHook.Install();
        base.Activate();
    }
    private void RegisterShortcuts()
    {
        (string Key, string Mode, string Default)[] bindings =
        [
            ("hotkey", "region", "Win+Alt+S"),
            ("monitor-hotkey", "monitor", ""),
            ("video-region-hotkey", "video", ""),
            ("video-monitor-hotkey", "video-monitor", "")
        ];
        var configured = new Dictionary<CaptureShortcut, string>();
        foreach (var (key, mode, fallback) in bindings)
        {
            var value = Context.Settings.GetString(key, fallback);
            if (fallback.Length == 0 && string.IsNullOrWhiteSpace(value)) continue;
            if (!configured.TryAdd(CaptureShortcut.Parse(value), mode))
                throw new InvalidOperationException("Her ekran yakalama işlemi için farklı bir kısayol seçin.");
        }

        UnregisterShortcuts();
        try
        {
            foreach (var (shortcut, mode) in configured)
                shortcuts.Add(shortcut.Register(Context.Hotkeys, () => _ = CaptureAsync(mode)));
        }
        catch
        {
            UnregisterShortcuts();
            throw;
        }
    }

    private void UnregisterShortcuts()
    {
        foreach (var shortcut in shortcuts) shortcut.Dispose();
        shortcuts.Clear();
    }

    public override void Apply() { if (IsActive) RegisterShortcuts(); }
    public override void Deactivate() { IsActive = false; UnregisterShortcuts(); cancellationHook?.Dispose(); cancellationHook = null; Cancel(); preview?.Close(); controls?.Close(); controls = null; toolsWindow?.Close(); toolsWindow = null; base.Deactivate(); }
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
            // Use the monitor under the pointer when capture is triggered, even with a delay.
            CaptureArea? monitorArea = mode is "monitor" or "video-monitor" ? CaptureServices.Monitor() : null;
            preview?.Close();
            await Task.Delay(200 + Math.Clamp(Context.Settings.GetInt("delay"), 0, 30) * 1000, token);
            var foreground = CaptureServices.Foreground();
            if (mode == "multi-region")
            {
                var areas = CaptureServices.SelectMultipleAreas(out var multiSnapshot);
                await CaptureMultipleAreasAsync(areas, multiSnapshot, token);
                return;
            }
            var annotationTool = mode switch
            {
                "annotate-text" => DrawingTool.Text,
                "annotate-shape" => DrawingTool.Rectangle,
                "annotate-freehand" => DrawingTool.Freehand,
                _ => (DrawingTool?)null
            };
            FrozenDesktop? snapshot = null;
            CaptureAnnotations? annotations = null;
            CaptureArea? area;
            nint selectedWindow = 0;
            if (mode == "region")
            {
                var selection = CaptureServices.SelectAnnotatedArea();
                area = selection.Area; annotations = selection.Annotations; snapshot = selection.Snapshot;
            }
            else area = annotationTool is not null ? DesktopArea() : mode switch
            {
                "last" => lastArea,
                "monitor" or "video-monitor" => monitorArea,
                "window" => CaptureServices.SelectWindow(out snapshot, out selectedWindow),
                "active-window" => CaptureServices.WindowArea(foreground.Window),
                "desktop" => DesktopArea(),
                _ => CaptureServices.SelectArea(true, out snapshot)
            };
            if (area is not { IsValid: true } selected)
            {
                if (mode == "last") Context.Notify("Önce bir bölge yakalayın; henüz kayıtlı bir son bölge yok.");
                return;
            }
            token.ThrowIfCancellationRequested();
            if (annotationTool is null) lastArea = selected;
            await Task.Delay(120, token);
            if (mode is "video" or "video-monitor" or "gif")
            {
                var recordingStore = Store;
                if (!CaptureServices.HasSpace(recordingStore.DirectoryPath)) throw new IOException("Kayıt için yeterli disk alanı yok.");
                recording = new(recordingStore.DirectoryPath, selected, Context.Settings.GetBool("microphone"), Context.Settings.GetBool("system-audio"), Context.Settings.GetBool("cursor", true), Math.Clamp(Context.Settings.GetInt("fps", 15), 5, 120));
                ShowRecordingControls(mode == "gif", recordingStore); return;
            }
            var fullscreenWindow = mode is "monitor" or "desktop"
                ? CaptureServices.FindFullscreenWindow(selected, foreground.Window) : 0;
            var target = fullscreenWindow != 0 ? fullscreenWindow : selectedWindow;
            if (mode == "active-window") target = foreground.Window;
            if (target == 0)
                target = CaptureServices.WindowIn(selected, snapshot?.Windows ?? CaptureServices.VisibleWindows());
            var application = CaptureServices.DescribeWindow(target);
            var file = new CaptureImageFile(CaptureImageFile.ApplicationName(application),
                mode == "scroll" ? DateTimeOffset.Now : snapshot?.CapturedAt ?? DateTimeOffset.Now, fullscreenWindow != 0);
            var image = mode == "scroll" ? null : snapshot?.Crop(selected) ?? CaptureServices.Capture(selected);
            if (image is not null && annotations is { HasMarks: true }) image = annotations.Apply(image, selected);
            var title = "Ekran görüntüsü";
            if (mode == "scroll")
            {
                var result = await ScrollingCapture.RunAsync(selected, token); image = result.Image;
                title = $"Kayan yakalama · %{result.Confidence * 100:0} güven{(result.Partial ? " · KISMİ SONUÇ" : "")}"; Context.Notify(title);
            }
            token.ThrowIfCancellationRequested();
            var store = new Lazy<HistoryStore>(() => Store);
            ShowPreview(image!, store, file);
            if (annotationTool is null) await ProcessAfterCaptureAsync(image!, store, application.App, title, file, token);
            else
            {
                if (Context.Settings.GetBool("copy", true)) await CaptureClipboard.CopyAsync(image!, file, token);
                new CaptureEditor(store.Value, image!, annotationTool, featureUi: (Context as IFeatureUiContext)?.FeatureUi, file: file).Show();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Report(error); }
        finally { if (recording is null) busy = false; }
    }
    private async Task CaptureMultipleAreasAsync(IReadOnlyList<CaptureArea> areas, FrozenDesktop? snapshot, CancellationToken token)
    {
        if (areas.Count == 0) return;
        var store = new Lazy<HistoryStore>(() => Store);
        var windows = snapshot?.Windows ?? CaptureServices.VisibleWindows();
        for (var index = 0; index < areas.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            preview?.Close();
            var application = CaptureServices.DescribeWindow(CaptureServices.WindowIn(areas[index], windows));
            var file = new CaptureImageFile(CaptureImageFile.ApplicationName(application), snapshot?.CapturedAt ?? DateTimeOffset.Now);
            var image = snapshot?.Crop(areas[index]) ?? CaptureServices.Capture(areas[index]); lastArea = areas[index];
            ShowPreview(image, store, file);
            await ProcessAfterCaptureAsync(image, store, application.App, $"Çoklu bölge {index + 1}/{areas.Count}", file, token);
        }
        Context.Notify($"{areas.Count} bölge yakalandı.");
    }

    private void ShowPreview(BitmapSource image, Lazy<HistoryStore> store, CaptureImageFile file)
    {
        preview?.Close();
        var window = new CapturePreviewWindow(image, () =>
        {
            try
            {
                var path = ExportImage(image, file);
                if (path is null)
                {
                    var history = store.Value;
                    var media = file.Save(history, image);
                    path = history.ResolveMedia(media);
                    history.ReleasePendingMedia(media);
                }
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

    private async Task ProcessAfterCaptureAsync(BitmapSource image, Lazy<HistoryStore> store, string app, string title,
        CaptureImageFile file, CancellationToken token)
    {
        try { ExportImage(image, file); }
        catch (Exception error) when (error is not OperationCanceledException) { Report(error); }
        var sequence = Context.Settings.GetString("workflow", "save-copy-edit-upload").Split('-');
        foreach (var step in sequence)
        {
            token.ThrowIfCancellationRequested();
            switch (step)
            {
                case "save" when Context.Settings.GetBool("save-history", true):
                    try { await SaveHistoryAsync(image, store, app, title, file, token); }
                    catch (Exception error) when (error is not OperationCanceledException) { Report(error); }
                    break;
                case "copy" when Context.Settings.GetBool("copy", true):
                    await CaptureClipboard.CopyAsync(image, file, token);
                    break;
                case "edit" when Context.Settings.GetBool("edit", true):
                    var editor = new CaptureEditor(store.Value, image, workflowCompletion: true,
                        featureUi: (Context as IFeatureUiContext)?.FeatureUi, file: file);
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

    private static async Task SaveHistoryAsync(BitmapSource image, Lazy<HistoryStore> store, string app, string title,
        CaptureImageFile file, CancellationToken token)
    {
        await Task.Run(() =>
        {
            var history = store.Value;
            if (!CaptureServices.HasSpace(history.DirectoryPath)) throw new IOException("Kayıt için yeterli disk alanı yok.");
            var media = file.Save(history, image);
            try
            {
                token.ThrowIfCancellationRequested();
                history.Add("capture", file.CapturedAt, app, title, media: media);
            }
            catch
            {
                history.ReleasePendingMedia(media);
                throw;
            }
        }, token);
    }

    internal string? ExportImage(BitmapSource image, CaptureImageFile file)
    {
        var directory = Context.Settings.GetString("image-directory").Trim();
        if (directory.Length == 0) return null;
        directory = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory));
        return file.Export(directory, image);
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
            var media = await store.ImportMediaAsync(file, token, HistoryStore.CaptureVideos);
            if (token.IsCancellationRequested) store.ReleasePendingMedia(media);
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
