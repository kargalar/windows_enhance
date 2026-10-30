using System.Windows.Controls;
using System.Windows.Threading;

namespace WinForge.ClipboardLine;

internal sealed partial class ClipboardShelfWindow
{
    private readonly ComboBox _searchMode = new() { Width = 120, ItemsSource = new[] { "Metin / OCR", "Görsel", "Metin + görsel" }, SelectedIndex = 0 };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private SemanticImageSearch? _semantic;
    private CancellationTokenSource? _searchCancellation;
    private IReadOnlyList<Guid> _visualIds = [];
    private int _searchGeneration;
    private bool _searchClosed;

    private void InitializeSemanticSearch()
    {
        _semantic = new SemanticImageSearch(_library.DirectoryPath);
        _searchMode.SelectionChanged += (_, _) => ResetAndRender();
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await SearchImagesAsync(); };
        Closed += (_, _) => { _searchClosed = true; _searchTimer.Stop(); _searchCancellation?.Cancel(); _ = Task.Run(_semantic.Dispose); };
    }

    private IReadOnlyList<ClipItem> Results()
    {
        var text = Scope(_search.Text);
        if (!_libraryMode) return text;
        IReadOnlyList<ClipItem> results = text;
        if (_searchMode.SelectedIndex > 0 && !string.IsNullOrWhiteSpace(_search.Text))
        {
            var scope = Scope("").ToDictionary(item => item.Id);
            var visual = _visualIds.Where(scope.ContainsKey).Select(id => scope[id]);
            results = (_searchMode.SelectedIndex == 1 ? visual : visual.Concat(text)).DistinctBy(item => item.Id).ToArray();
        }
        return SortLibraryResults(results, _sort.SelectedIndex);
    }

    private void RequestSemanticSearch()
    {
        _searchGeneration++; _searchCancellation?.Cancel(); _searchTimer.Stop(); _visualIds = [];
        if (_libraryMode && _searchMode.SelectedIndex > 0 && !string.IsNullOrWhiteSpace(_search.Text) && !_searchClosed) _searchTimer.Start();
    }

    private async Task SearchImagesAsync()
    {
        var generation = _searchGeneration; var query = _search.Text;
        var cancellation = new CancellationTokenSource(); _searchCancellation = cancellation;
        try
        {
            if (!_library.Preferences.IndexImages) { _status.Text = "Görsel indeksleme Ayarlar’da kapalı."; return; }
            var images = Scope("").Where(item => item.Kind == ClipKind.Image).Select(item => (item.Id, File: _library.MediaPath(item))).ToArray();
            _status.Text = "Görseller bu cihazda aranıyor…";
            var matches = await _semantic!.SearchAsync(query, images, cancellation.Token);
            if (generation != _searchGeneration || _searchClosed) return;
            _visualIds = matches.Select(match => match.Id).ToArray(); Render(); _status.Text = $"{matches.Count} görsel eşleşmesi. Arama çevrimdışı yapıldı.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_searchClosed && generation == _searchGeneration) { _context.Log.Error("Yerel görsel arama tamamlanamadı.", error); _status.Text = "Görsel arama: " + error.Message; } }
        finally { if (ReferenceEquals(_searchCancellation, cancellation)) _searchCancellation = null; cancellation.Dispose(); }
    }
}
