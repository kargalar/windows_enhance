using System.Text.Json;
using System.Windows;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal static class LegacyLocations
{
    public const string Recall = @"C:\Kargalar\Projeler\Other Apps\RecallClone";
    public const string Whisper = @"C:\Kargalar\Projeler\Other Apps\whisper";
}

internal sealed class ImportWindow : Window
{
    private readonly HistoryStore store;
    private readonly string kind;
    private readonly IModuleContext context;
    private readonly IFeatureUiSurface uiSurface;
    private string sourceRoot;
    private string statusText = "";
    private double progressValue;
    private IReadOnlyList<ImportSource> sources = [];
    private string? selectedDatabase;
    private CancellationTokenSource? operation;
    private ImportPreview? preview;
    public ImportWindow(HistoryStore store, string kind, IModuleContext context, IFeatureUiService? featureUi = null)
    {
        this.store = store; this.kind = kind; this.context = context; Title = "Eski kayıtları aktar"; Width = 800; Height = 400; CaptureUi.Theme(this);
        uiSurface = featureUi?.CreateSurface("capture-import")
            ?? throw new InvalidOperationException("The React import view is required.");
        uiSurface.MessageReceived += OnUiMessage;
        sourceRoot = kind == "recall" ? LegacyLocations.Recall : LegacyLocations.Whisper;
        Content = uiSurface.Element;
        Closed += (_, _) => { operation?.Cancel(); uiSurface.Dispose(); };
        Loaded += (_, _) => _ = DiscoverAsync();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "import.discover":
                sourceRoot = payload.TryGetProperty("root", out var path) ? path.GetString() ?? "" : sourceRoot;
                preview = null;
                _ = DiscoverAsync();
                break;
            case "import.select":
                if (payload.TryGetProperty("database", out var database))
                {
                    var match = sources.FirstOrDefault(source => source.Database == database.GetString());
                    if (match is not null)
                    {
                        selectedDatabase = match.Database;
                        preview = null;
                        _ = PreviewAsync();
                    }
                }
                break;
            case "import.start": _ = ImportAsync(); break;
            case "import.cancel": operation?.Cancel(); break;
            case "import.close": Close(); break;
        }
    }

    private void PushUiState()
    {
        uiSurface?.SetState(new
        {
            title = Title,
            kind,
            root = sourceRoot,
            status = statusText,
            progress = progressValue,
            busy = operation is not null,
            selectedDatabase = selectedDatabase ?? "",
            sources = sources.Select(source => new { source.Database, source.ImagesDirectory }).ToArray(),
            preview = preview is null ? null : new
            {
                preview.Records,
                preview.ExistingRecords,
                preview.Bytes,
                preview.MissingFiles,
                destination = store.DirectoryPath
            }
        });
    }
    private async Task DiscoverAsync()
    {
        if (operation is not null) return;
        try
        {
            var path = sourceRoot;
            sources = await Task.Run(() => LegacyImporter.Discover(path, kind));
            selectedDatabase = sources.FirstOrDefault()?.Database;
            preview = null;
            if (sources.Count == 0) statusText = "Bu klasörde geçmiş veritabanı bulunamadı. Kaynak klasörü değiştirin.";
        }
        catch (Exception error) { statusText = error.Message; }
        PushUiState();
        if (selectedDatabase is not null) _ = PreviewAsync();
    }
    private async Task PreviewAsync()
    {
        if (operation is not null || selectedDatabase is null) return;
        var source = sources.FirstOrDefault(item => item.Database == selectedDatabase);
        if (source is null) return;
        operation = new();
        try
        {
            statusText = "Kayıtlar ve dosya boyutları hesaplanıyor…";
            PushUiState();
            preview = await new LegacyImporter(store).PreviewAsync(source, operation.Token);
            var modelBytes = ModelCopyBytes();
            statusText = $"{preview.Records:N0} kayıt · {preview.ExistingRecords:N0} zaten aktarılmış · {(preview.Bytes + modelBytes) / 1024d / 1024 / 1024:N2} GB ek alan · {preview.MissingFiles:N0} eksik dosya\nKaynak dosyalar korunur. Hedef: {store.DirectoryPath}";
        }
        catch (OperationCanceledException) { statusText = "Önizleme iptal edildi."; }
        catch (Exception error) { statusText = error.Message; }
        finally { operation.Dispose(); operation = null; PushUiState(); }
    }
    private async Task ImportAsync()
    {
        if (operation is not null || preview is null) return;
        operation = new();
        PushUiState();
        try
        {
            var modelBytes = ModelCopyBytes();
            if (!CaptureServices.HasSpace(store.DirectoryPath, preview.Bytes + modelBytes + CaptureServices.MinimumFreeBytes)) throw new IOException("Aktarım için yeterli boş alan yok.");
            var updates = new Progress<ImportProgress>(p => { progressValue = p.Total == 0 ? 1 : (double)p.Completed / p.Total; statusText = $"{p.Completed:N0}/{p.Total:N0} · Yeni: {p.Imported:N0} · Eksik: {p.MissingFiles:N0}"; PushUiState(); });
            var source = preview.Source;
            var result = await Task.Run(() => new LegacyImporter(store).ImportAsync(source, updates, operation.Token));
            if (kind == "dictation") await CopyModelsAsync(sourceRoot, operation.Token);
            ImportSettings(source);
            progressValue = 1; statusText = $"Tamamlandı · {result.Imported:N0} yeni kayıt · {result.MissingFiles:N0} eksik dosya. Kayıt başlatılmadı.";
        }
        catch (OperationCanceledException) { statusText = "Aktarım iptal edildi. Tekrar başlatarak devam edebilirsiniz."; }
        catch (Exception error) { context.Log.Error("Geçmiş aktarımı", error); statusText = error.Message; }
        finally { operation.Dispose(); operation = null; PushUiState(); }
    }
    private async Task CopyModelsAsync(string sourceRoot, CancellationToken token)
    {
        var target = Path.Combine(store.DirectoryPath, "models"); Directory.CreateDirectory(target);
        foreach (var name in DictationDefaults.ModelFiles)
        {
            var source = Directory.EnumerateFiles(sourceRoot, name, SearchOption.AllDirectories).FirstOrDefault();
            if (source is null) continue;
            await WhisperModels.CopyAsync(source, target, name, token);
        }
    }
    private void ImportSettings(ImportSource source)
    {
        foreach (var (key, value) in LegacySettings.Read(source)) context.Settings.Set(key, value);
    }
    private long ModelCopyBytes()
    {
        if (kind != "dictation") return 0;
        return Directory.EnumerateFiles(sourceRoot, "ggml*.bin", SearchOption.AllDirectories).GroupBy(Path.GetFileName).Sum(group =>
        {
            var target = Path.Combine(store.DirectoryPath, "models", group.Key!); var size = new FileInfo(group.First()).Length;
            return File.Exists(target) && new FileInfo(target).Length == size ? 0 : size;
        });
    }
}
