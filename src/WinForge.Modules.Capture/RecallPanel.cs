using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using WinForge.FileTools.Engine;
using WinForge.History;

namespace WinForge.Modules.Capture;

public sealed partial class RecallModule
{
    private const int PanelPageSize = 200;
    private const int StatisticsPageSize = 100;
    private string panelSearch = "", panelApp = "", panelTag = "", panelFrom = "", panelUntil = "";
    private string panelDirectory = "", panelSection = "history";
    private int panelOffset, statisticsOffset;
    private long? panelSelected;
    private long panelRefreshVersion, panelDataVersion;
    private bool panelLoading;
    private string? panelError;
    private PanelData? panelData;

    // Only the serial background reader accesses these caches.
    private HistoryStore? panelReadStore;
    private (PanelRange Range, RecallUsageSummary Summary, IReadOnlyList<UsageTotal> Apps)? panelOverview;
    private (PanelRange Range, HistoryFilter Filter, IReadOnlyList<HistoryListItem> Rows, bool HasMore)? panelRows;
    private (PanelRange Range, long Id, HistoryItem Item)? panelDetail;
    private (string Directory, string Media, string DataUri)? panelPreview;
    private (PanelRange Range, string Section, IReadOnlyList<object> Rows)? panelStatistics;

    public event Action? PanelStateChanged;

    private static DateTimeOffset? PanelDate(string value, int daysToAdd = 0)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("Geçersiz tarih.");
        date = date.AddDays(daysToAdd);
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }

    public object GetPanelState()
    {
        EnsurePanelDirectory();
        var data = panelData;
        return new
        {
            kind = "pc-history", search = panelSearch, app = panelApp, tag = panelTag, from = panelFrom, until = panelUntil,
            offset = panelOffset, section = panelSection, statisticsOffset, loading = panelLoading, error = panelError,
            summary = data?.Summary, rows = data?.Rows ?? [], hasMore = data?.HasMore ?? false,
            selected = data?.Selected, preview = data?.Preview, statistics = data?.Statistics ?? [],
            statisticsTotal = data?.StatisticsTotal ?? 0, maximumApplicationSeconds = data?.MaximumApplicationSeconds ?? 1
        };
    }

    private void EnsurePanelDirectory()
    {
        var directory = HistoryDirectory;
        if (string.Equals(panelDirectory, directory, StringComparison.OrdinalIgnoreCase)) return;
        panelDirectory = directory;
        panelSelected = null;
        panelOffset = statisticsOffset = 0;
        panelData = null;
        RequestPanelRefresh();
    }

    private void RequestPanelRefresh()
    {
        panelRefreshVersion++;
        panelError = null;
        if (!panelLoading) _ = RefreshPanelAsync();
    }

    private async Task RefreshPanelAsync()
    {
        panelLoading = true;
        try
        {
            while (true)
            {
                var version = panelRefreshVersion;
                try
                {
                    var filter = new HistoryFilter("recall", panelSearch, panelApp, panelTag, PanelDate(panelFrom),
                        PanelDate(panelUntil, 1), Limit: PanelPageSize + 1, Offset: panelOffset);
                    var range = new PanelRange(panelDirectory, filter.From, filter.Until, panelDataVersion);
                    var request = new PanelRequest(range, filter, panelSelected, panelSection, statisticsOffset);
                    var data = await Task.Run(() => ReadPanel(request));
                    if (version != panelRefreshVersion) continue;
                    panelData = data;
                    if (request.Section == "history") panelSelected = data.Selected?.Id;
                    else statisticsOffset = data.StatisticsOffset;
                }
                catch (Exception error)
                {
                    if (version != panelRefreshVersion) continue;
                    Context.Log.Error("PC geçmişi paneli okunamadı", error);
                    panelError = error.Message;
                }
                break;
            }
        }
        finally
        {
            panelLoading = false;
            PanelStateChanged?.Invoke();
        }
    }

    private PanelData ReadPanel(PanelRequest request)
    {
        var range = request.Range;
        if (panelReadStore is null || panelReadStore.DirectoryPath != range.Directory)
            panelReadStore = new HistoryStore(range.Directory);
        var history = panelReadStore;
        if (panelOverview is not { } overview || overview.Range != range)
        {
            var (summary, apps) = history.RecallOverview(range.From, range.Until);
            panelOverview = overview = (range, summary, apps);
        }

        if (request.Section != "history")
        {
            if (panelStatistics is not { } stats || stats.Range != range || stats.Section != request.Section)
            {
                IReadOnlyList<object> result = request.Section switch
                {
                    "apps" => overview.Apps,
                    "days" => history.UsageByDay(range.From, range.Until),
                    "contexts" => history.ContextUsageByDay(range.From, range.Until),
                    "tags" => history.TagUsage(overview.Apps),
                    "network" => history.NetworkUsageByDay(range.From, range.Until),
                    _ => throw new ArgumentException("Bilinmeyen istatistik bölümü.")
                };
                panelStatistics = stats = (range, request.Section, result);
            }
            var lastPage = Math.Max(0, (stats.Rows.Count - 1) / StatisticsPageSize * StatisticsPageSize);
            var offset = Math.Min(request.StatisticsOffset, lastPage);
            return new(overview.Summary, [], false, null, null,
                stats.Rows.Skip(offset).Take(StatisticsPageSize).ToArray(),
                stats.Rows.Count, offset, Math.Max(1, overview.Apps.FirstOrDefault()?.Seconds ?? 0));
        }

        if (panelRows is not { } page || page.Range != range || page.Filter != request.Filter)
        {
            var list = history.SearchList(request.Filter);
            panelRows = page = (range, request.Filter, list.Take(PanelPageSize).ToArray(), list.Count > PanelPageSize);
        }
        var selected = page.Rows.FirstOrDefault(x => x.Id == request.Selected) ?? page.Rows.FirstOrDefault();
        HistoryItem? item = null;
        if (selected is not null)
        {
            if (panelDetail is { } detail && detail.Range == range && detail.Id == selected.Id)
                item = detail.Item;
            else if ((item = history.FindItem("recall", selected.Id)) is not null)
                panelDetail = (range, selected.Id, item);
        }
        string? preview = null;
        if (item is { Media.Length: > 0 })
        {
            if (panelPreview is { } cached && cached.Directory == range.Directory && cached.Media == item.Media)
                preview = cached.DataUri;
            else if ((preview = ReadPanelPreview(history, item.Media)) is not null)
                panelPreview = (range.Directory, item.Media, preview);
        }
        return new(overview.Summary, page.Rows, page.HasMore, item, preview, [], 0, 0, 1);
    }

    private static string? ReadPanelPreview(HistoryStore history, string media)
    {
        try
        {
            var path = history.ResolveMedia(media);
            const int maximumEdge = 1000;
            if (Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase))
            {
                using var image = PhotoCodec.Read(path);
                if (Math.Max(image.Width, image.Height) > maximumEdge) image.Resize(maximumEdge, maximumEdge);
                image.Strip();
                image.Quality = 82;
                return "data:image/jpeg;base64," + Convert.ToBase64String(image.ToByteArray(MagickFormat.Jpeg));
            }

            var bitmap = CaptureServices.Load(path);
            var scale = Math.Min(1, (double)maximumEdge / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
            BitmapSource source = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
            var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return "data:image/jpeg;base64," + Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or FileFormatException or MagickException)
        {
            return null;
        }
    }

    public void HandlePanelCommand(string action, JsonElement payload)
    {
        EnsurePanelDirectory();
        string Read(string key) => payload.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
        var selected = panelData?.Selected;
        switch (action)
        {
            case "filter":
                var from = Read("from");
                var until = Read("until");
                if (PanelDate(from) >= PanelDate(until, 1)) throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.");
                panelSearch = Read("search"); panelApp = Read("app"); panelTag = Read("tag"); panelFrom = from; panelUntil = until;
                panelOffset = statisticsOffset = 0;
                panelSelected = null;
                panelData = null;
                break;
            case "section":
                var section = Read("section");
                if (section is not ("history" or "apps" or "days" or "contexts" or "tags" or "network"))
                    throw new ArgumentException("Bilinmeyen istatistik bölümü.");
                if (section == panelSection) return;
                panelSection = section;
                statisticsOffset = 0;
                break;
            case "select":
                panelSelected = payload.GetProperty("id").GetInt64();
                break;
            case "page":
                panelOffset = Math.Max(0, panelOffset + payload.GetProperty("delta").GetInt32());
                panelSelected = null;
                break;
            case "statistics-page":
                statisticsOffset = Math.Max(0, statisticsOffset + payload.GetProperty("delta").GetInt32());
                break;
            case "delete":
                if (selected is null) return;
                Store.Delete("recall", selected.Id);
                panelSelected = null;
                panelDataVersion++;
                break;
            case "tags":
                if (selected is null) return;
                Store.SetTags(selected.App, Read("tags").Split(','));
                panelDataVersion++;
                break;
            case "open-media":
                if (selected is { Media.Length: > 0 })
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(new HistoryStore(panelDirectory).ResolveMedia(selected.Media)) { UseShellExecute = true });
                return;
            case "delete-range":
                if (PanelDate(panelFrom) is null && PanelDate(panelUntil) is null) throw new ArgumentException("Önce tarih aralığı seçin.");
                Store.Delete("recall", from: PanelDate(panelFrom), until: PanelDate(panelUntil, 1));
                panelSelected = null; panelOffset = 0;
                panelDataVersion++;
                break;
            case "delete-all":
                Store.Delete("recall");
                panelSelected = null; panelOffset = 0;
                panelDataVersion++;
                break;
            case "retry-ocr":
                Store.RetryOcr();
                panelDataVersion++;
                break;
            case "refresh":
                panelDataVersion++;
                break;
            default:
                throw new ArgumentException("Bilinmeyen geçmiş işlemi.");
        }
        RequestPanelRefresh();
        PanelStateChanged?.Invoke();
    }

    private sealed record PanelRange(string Directory, DateTimeOffset? From, DateTimeOffset? Until, long Version);
    private sealed record PanelRequest(PanelRange Range, HistoryFilter Filter, long? Selected, string Section, int StatisticsOffset);
    private sealed record PanelData(RecallUsageSummary Summary, IReadOnlyList<HistoryListItem> Rows, bool HasMore,
        HistoryItem? Selected, string? Preview, IReadOnlyList<object> Statistics, int StatisticsTotal, int StatisticsOffset,
        double MaximumApplicationSeconds);
}
