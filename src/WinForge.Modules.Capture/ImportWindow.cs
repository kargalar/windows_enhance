using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
    private readonly IFeatureUiSurface? uiSurface;
    private readonly TextBox root;
    private readonly ComboBox sources = new() { DisplayMemberPath = nameof(ImportSource.Database), Margin = new(4), MinWidth = 600 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(8) };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 12, Margin = new(4) };
    private CancellationTokenSource? operation;
    private ImportPreview? preview;
    public ImportWindow(HistoryStore store, string kind, IModuleContext context, IFeatureUiService? featureUi = null)
    {
        this.store = store; this.kind = kind; this.context = context; Title = "Eski kayıtları aktar"; Width = 800; Height = 400; CaptureUi.Theme(this);
        uiSurface = featureUi?.CreateSurface("capture-import");
        if (uiSurface is not null) uiSurface.MessageReceived += OnUiMessage;
        root = new() { Text = kind == "recall" ? LegacyLocations.Recall : LegacyLocations.Whisper, Margin = new(4) };
        var panel = new StackPanel { Margin = new(18) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Kaynak uygulama klasörü (alt klasörler de aranır)" }); panel.Children.Add(root);
        panel.Children.Add(CaptureUi.Button("Kaynakları bul ve önizle", () => _ = DiscoverAsync())); panel.Children.Add(sources);
        sources.SelectionChanged += (_, _) => { preview = null; _ = PreviewAsync(); };
        panel.Children.Add(progress); panel.Children.Add(status);
        panel.Children.Add(CaptureUi.Button("Önizlenen kayıtları aktar", () => _ = ImportAsync()));
        panel.Children.Add(CaptureUi.Button("İptal", () => operation?.Cancel()));
        if (uiSurface is not null) Content = uiSurface.Element;
        Closed += (_, _) => { operation?.Cancel(); uiSurface?.Dispose(); };
        Loaded += (_, _) => _ = DiscoverAsync();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "import.discover":
                root.Text = payload.TryGetProperty("root", out var path) ? path.GetString() ?? "" : root.Text;
                preview = null;
                _ = DiscoverAsync();
                break;
            case "import.select":
                if (payload.TryGetProperty("database", out var database))
                {
                    var sourcesList = sources.ItemsSource as IEnumerable<ImportSource>;
                    var match = sourcesList?.FirstOrDefault(source => source.Database == database.GetString());
                    if (match is not null) sources.SelectedItem = match;
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
            root = root.Text,
            status = status.Text,
            progress = progress.Value,
            busy = operation is not null,
            selectedDatabase = (sources.SelectedItem as ImportSource)?.Database ?? "",
            sources = (sources.ItemsSource as IEnumerable<ImportSource>)?.Select(source => new { source.Database, source.ImagesDirectory }).ToArray() ?? [],
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
            var path = root.Text; sources.ItemsSource = await Task.Run(() => LegacyImporter.Discover(path, kind)); sources.SelectedIndex = 0;
            if (sources.Items.Count == 0) status.Text = "Bu klasörde geçmiş veritabanı bulunamadı. Kaynak klasörü değiştirin.";
        }
        catch (Exception error) { status.Text = error.Message; }
        PushUiState();
    }
    private async Task PreviewAsync()
    {
        if (operation is not null || sources.SelectedItem is not ImportSource source) return;
        operation = new(); sources.IsEnabled = false;
        try
        {
            status.Text = "Kayıtlar ve dosya boyutları hesaplanıyor…";
            PushUiState();
            preview = await new LegacyImporter(store).PreviewAsync(source, operation.Token);
            var modelBytes = ModelCopyBytes();
            status.Text = $"{preview.Records:N0} kayıt · {preview.ExistingRecords:N0} zaten aktarılmış · {(preview.Bytes + modelBytes) / 1024d / 1024 / 1024:N2} GB ek alan · {preview.MissingFiles:N0} eksik dosya\nKaynak dosyalar korunur. Hedef: {store.DirectoryPath}";
        }
        catch (OperationCanceledException) { status.Text = "Önizleme iptal edildi."; }
        catch (Exception error) { status.Text = error.Message; }
        finally { operation.Dispose(); operation = null; sources.IsEnabled = true; PushUiState(); }
    }
    private async Task ImportAsync()
    {
        if (operation is not null || preview is null) return;
        operation = new(); sources.IsEnabled = false;
        PushUiState();
        try
        {
            var modelBytes = ModelCopyBytes();
            if (!CaptureServices.HasSpace(store.DirectoryPath, preview.Bytes + modelBytes + CaptureServices.MinimumFreeBytes)) throw new IOException("Aktarım için yeterli boş alan yok.");
            var updates = new Progress<ImportProgress>(p => { progress.Value = p.Total == 0 ? 1 : (double)p.Completed / p.Total; status.Text = $"{p.Completed:N0}/{p.Total:N0} · Yeni: {p.Imported:N0} · Eksik: {p.MissingFiles:N0}"; PushUiState(); });
            var source = preview.Source;
            var result = await Task.Run(() => new LegacyImporter(store).ImportAsync(source, updates, operation.Token));
            if (kind == "dictation") await CopyModelsAsync(root.Text, operation.Token);
            ImportSettings(source);
            progress.Value = 1; status.Text = $"Tamamlandı · {result.Imported:N0} yeni kayıt · {result.MissingFiles:N0} eksik dosya. Kayıt başlatılmadı.";
        }
        catch (OperationCanceledException) { status.Text = "Aktarım iptal edildi. Tekrar başlatarak devam edebilirsiniz."; }
        catch (Exception error) { context.Log.Error("Geçmiş aktarımı", error); status.Text = error.Message; }
        finally { operation.Dispose(); operation = null; sources.IsEnabled = true; PushUiState(); }
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
        return Directory.EnumerateFiles(root.Text, "ggml*.bin", SearchOption.AllDirectories).GroupBy(Path.GetFileName).Sum(group =>
        {
            var target = Path.Combine(store.DirectoryPath, "models", group.Key!); var size = new FileInfo(group.First()).Length;
            return File.Exists(target) && new FileInfo(target).Length == size ? 0 : size;
        });
    }
}
