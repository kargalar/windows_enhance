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

    public ModelWindow(HistoryStore store, IFeatureUiService? featureUi = null)
    {
        uiSurface = featureUi?.CreateSurface("capture-models")
            ?? throw new InvalidOperationException("The React models view is required.");
        uiSurface.MessageReceived += OnUiMessage;
        Title = "Whisper modelleri"; Width = 680; Height = 300; CaptureUi.Theme(this);
        directory = Path.Combine(store.DirectoryPath, "models"); Directory.CreateDirectory(directory);
        Content = uiSurface.Element;
        Loaded += (_, _) => PushUiState();
        Closed += (_, _) => uiSurface.Dispose();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        if (command == "models.close") { Close(); return; }
        if (command == "models.add" && payload.TryGetProperty("name", out var name)) _ = AddModelAsync(name.GetString() ?? "");
    }

    private async Task AddModelAsync(string name)
    {
        if (!DictationDefaults.ModelFiles.Contains(name, StringComparer.Ordinal)) return;
        var dialog = new OpenFileDialog { Filter = "Whisper GGML modeli|*.bin", FileName = name };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            statusText = "Model kopyalanıyor…"; PushUiState();
            await WhisperModels.CopyAsync(dialog.FileName, directory, name, CancellationToken.None);
            statusText = name + " hazır.";
        }
        catch (Exception error) { statusText = error.Message; }
        PushUiState();
    }

    private void PushUiState() => uiSurface?.SetState(new
    {
        directory,
        status = statusText,
        models = DictationDefaults.ModelFiles.Select(name => new { name, installed = File.Exists(Path.Combine(directory, name)) }).ToArray()
    });
}
