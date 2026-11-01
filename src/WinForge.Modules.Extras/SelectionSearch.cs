namespace WinForge.Modules.Extras;

/// <summary>Waits for a new copy operation instead of searching stale clipboard contents.</summary>
internal sealed class SelectionSearch(
    Action copy,
    Func<uint> clipboardVersion,
    Func<string?> readText,
    Func<bool> sameWindow,
    Action<string> openUrl,
    Func<CancellationToken, Task> delay)
{
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!sameWindow()) return;
        var previous = clipboardVersion();
        copy();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await delay(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!sameWindow()) return;
            if (clipboardVersion() == previous) continue;
            string? text;
            try { text = readText(); }
            catch (System.Runtime.InteropServices.ExternalException) { continue; }
            if (string.IsNullOrWhiteSpace(text)) continue;
            openUrl(BuildUrl(text));
            return;
        }
        throw new InvalidOperationException("Seçili metin kopyalanamadı. Önce aranacak metni seçin.");
    }

    internal static string BuildUrl(string text) => "https://www.google.com/search?q=" + Uri.EscapeDataString(text.Trim());
}
