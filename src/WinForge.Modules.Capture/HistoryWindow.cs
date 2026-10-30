using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal sealed class HistoryWindow : Window
{
    private readonly HistoryStore store;
    private readonly string kind;
    private readonly IFeatureUiService featureUi;
    private readonly IFeatureUiSurface uiSurface;
    private readonly List<HistoryRow> rows = [];
    private int offset;
    private bool busy;
    private bool deleteConfirmedFromUi;
    private bool confirmDelete;
    private bool confirmDeleteRange;
    private bool confirmDeleteAll;
    private long? selectedId;
    private string searchText = "";
    private string appText = "";
    private string tagText = "";
    private string fromText = "";
    private string untilText = "";
    private string selectedText = "";
    private string selectedTags = "";
    private string statusText = "";
    private BitmapSource? previewImage;
    private Point? cursorImagePoint;

    public HistoryWindow(HistoryStore store, string kind, string title, IFeatureUiService? featureUi = null)
    {
        this.store = store;
        this.kind = kind;
        this.featureUi = featureUi ?? throw new InvalidOperationException("The React history view is required.");
        Title = title;
        Width = 1180;
        Height = 780;
        MinWidth = 900;
        MinHeight = 600;
        CaptureUi.Theme(this);

        uiSurface = this.featureUi.CreateSurface("capture-history");
        uiSurface.MessageReceived += OnUiMessage;
        Content = uiSurface.Element;
        Loaded += (_, _) => _ = RefreshAsync();
        Closed += (_, _) => uiSurface.Dispose();
    }

    private DateTimeOffset? Start => ParseDate(fromText);
    private DateTimeOffset? End => ParseDate(untilText)?.AddDays(1);

    private static DateTimeOffset? ParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var date)
            ? new DateTimeOffset(date)
            : throw new ArgumentException("Tarihi gg.aa.yyyy biçiminde girin.");
    }

    private async Task RefreshAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            var filter = new HistoryFilter(kind, searchText, appText.Trim(), tagText.Trim(), Start, End, Offset: offset);
            var items = await Task.Run(() => store.Search(filter));
            rows.Clear();
            rows.AddRange(items.Select(item => new HistoryRow(item)));
            selectedId = rows.FirstOrDefault()?.Item.Id;
            statusText = rows.Count == 0 ? "Kayıt bulunamadı." : $"{offset + 1}–{offset + rows.Count} · {store.DirectoryPath}";
            ShowSelection();
        }
        catch (Exception error)
        {
            statusText = error.Message;
        }
        finally
        {
            busy = false;
            PushUiState();
        }
    }

    private void ShowSelection()
    {
        previewImage = null;
        cursorImagePoint = null;
        selectedText = "";
        selectedTags = "";

        if (SelectedRow is not { } row) return;
        selectedText = row.Item.Text;
        selectedTags = row.Item.Tags;
        if (row.Item.Media.Length == 0) return;

        Guard(() =>
        {
            var path = store.ResolveMedia(row.Item.Media);
            if (Path.GetExtension(path) is ".mp4" or ".gif")
            {
                selectedText += "\nVideo/GIF kaydı. Oynatmak için Medyayı aç düğmesini kullanın.";
                return;
            }

            if (!File.Exists(path))
            {
                statusText = "Görsel dosyası bulunamadı.";
                return;
            }

            previewImage = CaptureServices.Load(path);
            cursorImagePoint = ReadCursor(row.Item.Metadata, previewImage.PixelWidth, previewImage.PixelHeight);
        });
    }

    private HistoryRow? SelectedRow => selectedId is { } id ? rows.FirstOrDefault(row => row.Item.Id == id) : null;

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "history.filter":
                searchText = ReadString(payload, "search");
                appText = ReadString(payload, "app");
                tagText = ReadString(payload, "tag");
                fromText = ReadString(payload, "from");
                untilText = ReadString(payload, "until");
                offset = 0;
                _ = RefreshAsync();
                break;
            case "history.page":
                var delta = payload.TryGetProperty("delta", out var page) && page.TryGetInt32(out var amount) ? amount : 0;
                offset = Math.Max(0, offset + delta);
                _ = RefreshAsync();
                break;
            case "history.select":
                if (payload.TryGetProperty("id", out var id) && id.TryGetInt64(out var itemId))
                {
                    selectedId = rows.Any(row => row.Item.Id == itemId) ? itemId : null;
                    ShowSelection();
                }
                PushUiState();
                break;
            case "history.copy":
                Guard(() => Clipboard.SetText(selectedText));
                break;
            case "history.open-media":
                Guard(() =>
                {
                    if (SelectedRow is { Item.Media.Length: > 0 } row)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.ResolveMedia(row.Item.Media)) { UseShellExecute = true });
                });
                break;
            case "history.delete-selected": Delete(false); break;
            case "history.delete-range": Delete(true); break;
            case "history.delete-all":
                if (payload.TryGetProperty("confirm", out var all) && all.ValueKind == JsonValueKind.True)
                {
                    confirmDeleteAll = false;
                    Guard(() => store.Delete(kind));
                    _ = RefreshAsync();
                }
                else
                {
                    confirmDeleteAll = true;
                    PushUiState();
                }
                break;
            case "history.delete-confirm":
                var accepted = payload.TryGetProperty("confirm", out var confirm) && confirm.ValueKind == JsonValueKind.True;
                if (confirmDeleteAll)
                {
                    confirmDeleteAll = false;
                    if (accepted)
                    {
                        Guard(() => store.Delete(kind));
                        _ = RefreshAsync();
                    }
                    else PushUiState();
                    break;
                }

                var range = payload.TryGetProperty("range", out var rangeValue) && rangeValue.ValueKind == JsonValueKind.True;
                confirmDelete = false;
                if (accepted)
                {
                    deleteConfirmedFromUi = true;
                    Delete(range);
                    deleteConfirmedFromUi = false;
                }
                else PushUiState();
                break;
            case "history.save-tags":
                if (SelectedRow is { } selected)
                {
                    selectedTags = ReadString(payload, "tags");
                    Guard(() => store.SetTags(selected.Item.App, selectedTags.Split(',')));
                }
                _ = RefreshAsync();
                break;
            case "history.retry-ocr":
                Guard(store.RetryOcr);
                _ = RefreshAsync();
                break;
            case "history.usage": new UsageStatisticsWindow(store, Start, End, featureUi).Show(); break;
        }
    }

    private static string ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private void PushUiState()
    {
        var selected = SelectedRow;
        uiSurface.SetState(new
        {
            title = Title,
            kind,
            search = searchText,
            app = appText,
            tag = tagText,
            from = fromText,
            until = untilText,
            offset,
            status = statusText,
            selectedId = selected?.Item.Id,
            selectedText,
            selectedTags,
            cursor = cursorImagePoint is { } point ? new { x = point.X, y = point.Y, width = previewImage?.PixelWidth ?? 0, height = previewImage?.PixelHeight ?? 0 } : null,
            confirmDelete,
            confirmDeleteRange,
            confirmDeleteAll,
            preview = BitmapToDataUri(previewImage),
            mediaType = selected is null ? "" : Path.GetExtension(selected.Item.Media),
            rows = rows.Select(row => new
            {
                id = row.Item.Id,
                label = row.Label,
                app = row.Item.App,
                title = row.Item.Title,
                created = row.Item.Created.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"),
                hasMedia = row.Item.Media.Length > 0,
                mediaType = Path.GetExtension(row.Item.Media),
                tags = row.Item.Tags
            }).ToArray()
        });
    }

    private static string? BitmapToDataUri(BitmapSource? bitmap)
    {
        if (bitmap is null) return null;
        const double maximumPreviewDimension = 1200;
        var scale = Math.Min(1, maximumPreviewDimension / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        BitmapSource source = bitmap;
        if (scale < 1)
        {
            var resized = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
            resized.Freeze();
            source = resized;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private static Point? ReadCursor(string metadata, int imageWidth, int imageHeight)
    {
        if (metadata.Length == 0) return null;
        try
        {
            using var json = JsonDocument.Parse(metadata);
            var root = json.RootElement;
            if (TryCoordinate(root, "cursorX", out var x) && TryCoordinate(root, "cursorY", out var y) &&
                x >= 0 && y >= 0 && x < imageWidth && y < imageHeight)
                return new(x, y);
            if (!TryCoordinate(root, "mouse_x", out x) || !TryCoordinate(root, "mouse_y", out y)) return null;
            var point = new POINT((int)x, (int)y);
            var bounds = DesktopDisplays.ForPoint(point).Bounds;
            var localX = (x - bounds.Left) * imageWidth / Math.Max(1, bounds.Right - bounds.Left);
            var localY = (y - bounds.Top) * imageHeight / Math.Max(1, bounds.Bottom - bounds.Top);
            return localX >= 0 && localY >= 0 && localX < imageWidth && localY < imageHeight ? new(localX, localY) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryCoordinate(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var item)) return false;
        if (item.ValueKind == JsonValueKind.Number) return item.TryGetDouble(out value);
        return item.ValueKind == JsonValueKind.String && double.TryParse(item.GetString(), CultureInfo.InvariantCulture, out value);
    }

    private void Delete(bool range)
    {
        try
        {
            if (range && Start is null && End is null)
            {
                statusText = "Önce tarih aralığı seçin.";
                PushUiState();
                return;
            }
            if (!range && SelectedRow is null) return;
            if (!deleteConfirmedFromUi)
            {
                confirmDelete = true;
                confirmDeleteRange = range;
                PushUiState();
                return;
            }

            Guard(() => store.Delete(kind, range ? null : SelectedRow!.Item.Id, range ? Start : null, range ? End : null));
            _ = RefreshAsync();
        }
        catch (Exception error)
        {
            statusText = error.Message;
            PushUiState();
        }
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            statusText = error.Message;
            PushUiState();
        }
    }

    private sealed record HistoryRow(HistoryItem Item)
    {
        public string Label
        {
            get
            {
                var flattenedText = Item.Text.ReplaceLineEndings(" ");
                var summary = Item.Kind == "dictation" ? flattenedText[..Math.Min(100, flattenedText.Length)] : Item.Title;
                return $"{Item.Created.LocalDateTime:dd.MM.yyyy HH:mm:ss} · {Item.App}\n{summary}{(Item.OcrState == "failed" ? " · OCR hatası" : "")}";
            }
        }
    }
}
