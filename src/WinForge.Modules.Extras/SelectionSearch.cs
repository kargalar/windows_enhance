namespace WinForge.Modules.Extras;

/// <summary>Waits for a new copy operation instead of searching stale clipboard contents.</summary>
internal sealed class SelectionSearch(
    Action copy,
    Func<uint> clipboardVersion,
    Func<string?> readText,
    Func<bool> sameWindow,
    Action<string> openUrl,
    Func<CancellationToken, Task> delay,
    string provider = "google",
    Action? closeTab = null,
    Func<Func<CancellationToken, Task>>? prepareBackgroundReturn = null)
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
            if (closeTab is not null)
            {
                closeTab();
                await delay(cancellationToken);
                await delay(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            var restore = prepareBackgroundReturn?.Invoke();
            openUrl(BuildUrl(text, provider));
            if (restore is not null)
                await restore(cancellationToken);
            return;
        }
        throw new InvalidOperationException("Seçili metin kopyalanamadı. Önce aranacak metni seçin.");
    }

    internal static string BuildUrl(string text, string provider = "google") => provider switch
    {
        "1337x" => "https://1337x.to/search/" + string.Join("+", text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString)) + "/1/",
        _ => "https://www.google.com/search?q=" + Uri.EscapeDataString(text.Trim())
    };
}
