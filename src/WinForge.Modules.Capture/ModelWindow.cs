using System.Windows;
using System.Text.Json;
using WinForge.Core;
using Microsoft.Win32;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal sealed class ModelWindow : Window
{
    private readonly string directory;
    private readonly IFeatureUiSurface uiSurface;
    private string statusText = "";
    private CancellationTokenSource? operation;
    private bool closed;

    public ModelWindow(HistoryStore store, IFeatureUiService? featureUi = null)
    {
        uiSurface = featureUi?.CreateSurface("capture-models")
            ?? throw new InvalidOperationException("The React models view is required.");
        uiSurface.MessageReceived += OnUiMessage;
        Title = "Whisper modelleri"; Width = 680; Height = 300; CaptureUi.Theme(this);
        directory = Path.Combine(store.DirectoryPath, "models"); Directory.CreateDirectory(directory);
        Content = uiSurface.Element;
        Loaded += (_, _) => PushUiState();
        Closed += (_, _) => { closed = true; operation?.Cancel(); uiSurface.Dispose(); };
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        if (command == "models.close") { Close(); return; }
        if (command == "models.cancel") { operation?.Cancel(); return; }
        if (command == "models.add" && payload.TryGetProperty("name", out var name)) _ = AddModelAsync(name.GetString() ?? "");
        if (command == "models.download" && payload.TryGetProperty("name", out var downloadName)) _ = DownloadModelAsync(downloadName.GetString() ?? "");
    }

    private async Task AddModelAsync(string name)
    {
        if (operation is not null || !DictationDefaults.ModelFiles.Contains(name, StringComparer.Ordinal)) return;
        var dialog = new OpenFileDialog { Filter = "Whisper GGML modeli|*.bin", FileName = name };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(name, async (token, _) => await WhisperModels.CopyAsync(dialog.FileName, directory, name, token));
    }

    private async Task DownloadModelAsync(string name)
    {
        if (operation is not null || !DictationDefaults.ModelFiles.Contains(name, StringComparer.Ordinal)) return;
        await RunAsync(name, (token, progress) => WhisperModels.DownloadAsync(directory, name, progress, token));
    }

    private async Task RunAsync(string name, Func<CancellationToken, IProgress<double>, Task> work)
    {
        using var cancellation = new CancellationTokenSource(); operation = cancellation;
        var progress = new Progress<double>(value =>
        {
            if (!closed && operation == cancellation) { statusText = $"İndiriliyor… %{value * 100:F0}"; PushUiState(); }
        });
        try
        {
            statusText = "Model hazırlanıyor…"; PushUiState();
            await work(cancellation.Token, progress);
            statusText = name + " hazır.";
        }
        catch (OperationCanceledException) { statusText = "İşlem iptal edildi."; }
        catch (Exception error) { statusText = error.Message; }
        finally { operation = null; PushUiState(); }
    }

    private void PushUiState()
    {
        if (closed) return;
        uiSurface.SetState(new {
        directory,
        status = statusText,
        busy = operation is not null,
        models = DictationDefaults.ModelFiles.Select(name => new { name, installed = File.Exists(Path.Combine(directory, name)) }).ToArray()
        });
    }
}
