using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal sealed class ModelWindow : Window
{
    public ModelWindow(HistoryStore store)
    {
        Title = "Whisper modelleri"; Width = 680; Height = 300; CaptureUi.Theme(this);
        var panel = new StackPanel { Margin = new(20) }; Content = panel;
        var directory = Path.Combine(store.DirectoryPath, "models"); Directory.CreateDirectory(directory);
        panel.Children.Add(new TextBlock { Text = "Modeller yerel çalışır. GGML Turbo ve Large dosyalarını buraya ekleyin.", TextWrapping = TextWrapping.Wrap });
        var status = new TextBlock { Margin = new(6), TextWrapping = TextWrapping.Wrap };
        foreach (var name in DictationDefaults.ModelFiles)
        {
            panel.Children.Add(CaptureUi.Button(name + (File.Exists(Path.Combine(directory, name)) ? " · Hazır" : " · Ekle"), async () =>
            {
                var dialog = new OpenFileDialog { Filter = "Whisper GGML modeli|*.bin", FileName = name };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    status.Text = "Model kopyalanıyor…";
                    await WhisperModels.CopyAsync(dialog.FileName, directory, name, CancellationToken.None); status.Text = name + " hazır.";
                }
                catch (Exception error) { status.Text = error.Message; }
            }));
        }
        panel.Children.Add(status);
    }
}
