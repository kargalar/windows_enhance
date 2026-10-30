using System.Text.Json;
using System.Windows;
using WinForge.Core;

namespace WinForge.FileTools.UI;

internal sealed class QrResultsWindow : Window
{
    private readonly IReadOnlyList<(string File, string Value)> results;
    private readonly IFeatureUiSurface uiSurface;

    public QrResultsWindow(Window owner, IReadOnlyList<(string File, string Value)> results,
        IFeatureUiService? featureUi)
    {
        this.results = results;
        Owner = owner;
        Title = "QR kod sonuçları · Nashi";
        Width = 760;
        Height = 620;
        MinWidth = 580;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        uiSurface = featureUi?.CreateSurface("nashi-qr-results")
            ?? throw new InvalidOperationException("QR sonuçları için React arayüzü gerekli.");
        uiSurface.MessageReceived += OnUiMessage;
        Content = uiSurface.Element;
        Closed += (_, _) => uiSurface.Dispose();
        PushState();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        if (command == "window.close")
        {
            Close();
            return;
        }

        if (!payload.TryGetProperty("index", out var indexValue) || !indexValue.TryGetInt32(out var index) ||
            index < 0 || index >= results.Count) return;

        var result = results[index];
        if (command == "qr.copy")
        {
            try
            {
                Clipboard.SetText(result.Value);
                uiSurface.SetState(new { results = BuildResults(), status = "Panoya kopyalandı." });
            }
            catch (Exception error) when (ClipboardImport.IsClipboardError(error))
            {
                uiSurface.SetState(new { results = BuildResults(), status = "Pano meşgul. Yeniden deneyin." });
            }
        }
        else if (command == "qr.open" && TryGetWebAddress(result.Value, out var address))
        {
            MainWindow.OpenUrl(address);
        }
    }

    private void PushState() => uiSurface.SetState(new
    {
        results = BuildResults(),
        status = results.Count == 0 ? "Bu dosyalarda QR kodu bulunamadı." : $"{results.Count} QR kodu bulundu."
    });

    private object[] BuildResults() => results.Select((result, index) => new
    {
        index,
        file = result.File,
        value = result.Value,
        canOpen = TryGetWebAddress(result.Value, out _)
    }).Cast<object>().ToArray();

    private static bool TryGetWebAddress(string value, out string address)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            address = uri.AbsoluteUri;
            return true;
        }

        address = "";
        return false;
    }
}
