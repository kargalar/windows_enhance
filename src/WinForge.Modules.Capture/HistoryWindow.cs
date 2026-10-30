using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal static class CaptureUi
{
    public static void Theme(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "BgBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        window.FontFamily = new FontFamily("Segoe UI Variable Text"); window.FontSize = 14;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Loaded += (_, _) =>
        {
            foreach (var control in Descendants(window).OfType<Control>())
            {
                if (control is ListBox or DatePicker or DataGrid)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
                    control.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                    control.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
                }
            }
        };
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    public static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(10, 6, 10, 6) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "GhostButton");
        button.Click += (_, _) => action(); return button;
    }
    public static bool Confirm(Window owner, string message) => MessageBox.Show(owner, message, "WinForge", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

internal sealed class HistoryWindow : Window
{
    private readonly HistoryStore store;
    private readonly string kind;
    private readonly IFeatureUiService? featureUi;
    private readonly IFeatureUiSurface? uiSurface;
    private readonly TextBox search = new() { Width = 220, Margin = new(4), ToolTip = "Metin/OCR ara" };
    private readonly TextBox app = new() { Width = 140, Margin = new(4), ToolTip = "Uygulama adı (örn. Code.exe)" };
    private readonly TextBox tag = new() { Width = 110, Margin = new(4), ToolTip = "Etiket filtresi" };
    private readonly TextBox from = new() { Width = 130, Margin = new(4), ToolTip = "gg.aa.yyyy" };
    private readonly TextBox until = new() { Width = 130, Margin = new(4), ToolTip = "gg.aa.yyyy" };
    private readonly ListBox rows = new();
    private readonly Image image = new() { Stretch = Stretch.Uniform, MaxHeight = 500 };
    private readonly Canvas cursorOverlay = new() { IsHitTestVisible = false };
    private readonly Ellipse cursorMarker = new() { Width = 20, Height = 20, Stroke = Brushes.OrangeRed, StrokeThickness = 3, Fill = Brushes.Transparent, ToolTip = "Kaydedilen imleç konumu", Visibility = Visibility.Collapsed };
    private readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100 };
    private readonly TextBox tags = new() { ToolTip = "Seçili uygulamanın etiketleri; virgülle ayırın", Margin = new(4) };
    private readonly TextBlock status = new() { Margin = new(6) };
    private readonly Slider timeline = new() { Minimum = 0, Maximum = 0, TickFrequency = 1, IsSnapToTickEnabled = true, IsDirectionReversed = true, Margin = new(8), ToolTip = "Bu sayfadaki kayıtlar arasında gezin" };
    private int offset;
    private bool busy;
    private bool deleteConfirmedFromUi;
    private bool confirmDelete;
    private bool confirmDeleteRange;
    private bool confirmDeleteAll;
    private Point? cursorImagePoint;
    private BitmapSource? previewImage;
    public HistoryWindow(HistoryStore store, string kind, string title, IFeatureUiService? featureUi = null)
    {
        this.store = store; this.kind = kind; this.featureUi = featureUi; Title = title; Width = 1180; Height = 780; MinWidth = 900; MinHeight = 600; CaptureUi.Theme(this);
        uiSurface = featureUi?.CreateSurface("capture-history");
        if (uiSurface is not null) uiSurface.MessageReceived += OnUiMessage;
        var root = new DockPanel { Margin = new(12) }; Content = root;
        var itemTemplate = new DataTemplate(typeof(HistoryRow));
        var rowPanel = new FrameworkElementFactory(typeof(DockPanel));
        var icon = new FrameworkElementFactory(typeof(Image)); icon.SetValue(Image.WidthProperty, 30d); icon.SetValue(Image.HeightProperty, 30d);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(4)); icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(HistoryRow.Icon))); rowPanel.AppendChild(icon);
        var labelFactory = new FrameworkElementFactory(typeof(TextBlock)); labelFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        labelFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4)); labelFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(HistoryRow.Label))); rowPanel.AppendChild(labelFactory);
        itemTemplate.VisualTree = rowPanel; rows.ItemTemplate = itemTemplate;
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        foreach (var (label, control) in new (string, Control)[] { ("Metin/OCR", search), ("Uygulama", app), ("Etiket", tag), ("Başlangıç", from), ("Bitiş", until) })
        {
            var field = new StackPanel(); field.Children.Add(new TextBlock { Text = label, Margin = new(4, 0, 4, 0) }); field.Children.Add(control); toolbar.Children.Add(field);
        }
        toolbar.Children.Add(CaptureUi.Button("Ara", () => { offset = 0; _ = RefreshAsync(); }));
        if (kind == "recall")
        {
            timeline.SetResourceReference(FrameworkElement.StyleProperty, "DarkSlider");
            DockPanel.SetDock(timeline, Dock.Top); root.Children.Add(timeline);
            timeline.ValueChanged += (_, _) => { if (rows.Items.Count > 0) rows.SelectedIndex = (int)timeline.Value; };
        }
        var bottom = new WrapPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(CaptureUi.Button("Önceki", () => { offset = Math.Max(0, offset - 200); _ = RefreshAsync(); }));
        bottom.Children.Add(CaptureUi.Button("Sonraki", () => { offset += 200; _ = RefreshAsync(); }));
        bottom.Children.Add(CaptureUi.Button("Metni kopyala", () => Guard(() => Clipboard.SetText(detail.Text))));
        bottom.Children.Add(CaptureUi.Button("Medyayı aç", () => Guard(() =>
        {
            if (rows.SelectedItem is HistoryRow row && row.Item.Media.Length > 0)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.ResolveMedia(row.Item.Media)) { UseShellExecute = true });
        })));
        bottom.Children.Add(CaptureUi.Button("Büyük önizleme", () => { if (image.Source is System.Windows.Media.Imaging.BitmapSource source) new ImagePreviewWindow(source, false).Show(); }));
        bottom.Children.Add(CaptureUi.Button("Seçiliyi sil", () => Delete(false)));
        bottom.Children.Add(CaptureUi.Button("Tarih aralığını sil", () => Delete(true)));
        bottom.Children.Add(CaptureUi.Button("Tüm geçmişi sil", () =>
        {
            if (!CaptureUi.Confirm(this, "Bu modülün bütün geçmişi silinsin mi?")) return;
            Guard(() => store.Delete(kind)); _ = RefreshAsync();
        }));
        if (kind == "recall")
        {
            bottom.Children.Add(CaptureUi.Button("Kullanım istatistikleri", () => new UsageStatisticsWindow(store, Start, End, featureUi).Show()));
            bottom.Children.Add(CaptureUi.Button("OCR yeniden dene", () => Guard(store.RetryOcr)));
        }
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) }); root.Children.Add(grid);
        grid.Children.Add(rows); var preview = new StackPanel { Margin = new(10) }; Grid.SetColumn(preview, 1); grid.Children.Add(new ScrollViewer { Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Grid.SetColumn(grid.Children[^1], 1);
        var imageFrame = new Grid { MaxHeight = 500, MinHeight = 0 };
        imageFrame.Children.Add(image); cursorOverlay.Children.Add(cursorMarker); imageFrame.Children.Add(cursorOverlay);
        imageFrame.SizeChanged += (_, _) => PositionCursor();
        preview.Children.Add(imageFrame); preview.Children.Add(detail);
        if (kind == "recall")
        {
            preview.Children.Add(tags);
            preview.Children.Add(CaptureUi.Button("Uygulama etiketlerini kaydet", () =>
            {
                if (rows.SelectedItem is HistoryRow row) Guard(() => store.SetTags(row.Item.App, tags.Text.Split(','))); _ = RefreshAsync();
            }));
        }
        rows.SelectionChanged += (_, _) => { timeline.Value = Math.Max(0, rows.SelectedIndex); ShowSelection(); PushUiState(); };
        if (uiSurface is not null) Content = uiSurface.Element;
        Loaded += (_, _) => _ = RefreshAsync();
        Closed += (_, _) => uiSurface?.Dispose();
    }
    private DateTimeOffset? Start => ParseDate(from.Text);
    private DateTimeOffset? End => ParseDate(until.Text)?.AddDays(1);
    private static DateTimeOffset? ParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var date)
            ? new DateTimeOffset(date) : throw new ArgumentException("Tarihi gg.aa.yyyy biçiminde girin.");
    }
    private async Task RefreshAsync()
    {
        if (busy) return; busy = true;
        try
        {
            var filter = new HistoryFilter(kind, search.Text, app.Text.Trim(), tag.Text.Trim(), Start, End, Offset: offset);
            var items = await Task.Run(() => store.Search(filter)); rows.ItemsSource = items.Select(item => new HistoryRow(item, LoadIcon(item.Icon))).ToArray();
            timeline.Maximum = Math.Max(0, items.Count - 1);
            rows.SelectedIndex = items.Count > 0 ? 0 : -1;
            status.Text = items.Count == 0 ? "Kayıt bulunamadı." : $"{offset + 1}–{offset + items.Count} · {store.DirectoryPath}";
        }
        catch (Exception error) { status.Text = error.Message; }
        finally { busy = false; PushUiState(); }
    }
    private void ShowSelection()
    {
        image.Source = null; previewImage = null; cursorImagePoint = null; cursorMarker.Visibility = Visibility.Collapsed; detail.Clear();
        if (rows.SelectedItem is not HistoryRow row) return;
        detail.Text = row.Item.Text; tags.Text = row.Item.Tags;
        if (row.Item.Media.Length == 0) return;
        Guard(() =>
        {
            var path = store.ResolveMedia(row.Item.Media);
            if (System.IO.Path.GetExtension(path) is ".mp4" or ".gif")
            {
                detail.Text += "\nVideo/GIF kaydı. Oynatmak için Medyayı aç düğmesini kullanın."; return;
            }
            if (File.Exists(path))
            {
                previewImage = CaptureServices.Load(path); image.Source = previewImage;
                cursorImagePoint = ReadCursor(row.Item.Metadata, previewImage.PixelWidth, previewImage.PixelHeight);
                PositionCursor();
            }
            else status.Text = "Görsel dosyası bulunamadı.";
        });
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "history.filter":
                search.Text = ReadString(payload, "search"); app.Text = ReadString(payload, "app");
                tag.Text = ReadString(payload, "tag"); from.Text = ReadString(payload, "from"); until.Text = ReadString(payload, "until");
                offset = 0; _ = RefreshAsync(); break;
            case "history.page":
                var delta = payload.TryGetProperty("delta", out var page) && page.TryGetInt32(out var amount) ? amount : 0;
                offset = Math.Max(0, offset + delta); _ = RefreshAsync(); break;
            case "history.select":
                if (payload.TryGetProperty("id", out var id) && id.TryGetInt64(out var itemId))
                    rows.SelectedItem = (rows.ItemsSource as IEnumerable<HistoryRow>)?.FirstOrDefault(row => row.Item.Id == itemId);
                PushUiState(); break;
            case "history.copy": Guard(() => Clipboard.SetText(detail.Text)); break;
            case "history.open-media": Guard(() =>
            {
                if (rows.SelectedItem is HistoryRow row && row.Item.Media.Length > 0)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.ResolveMedia(row.Item.Media)) { UseShellExecute = true });
            }); break;
            case "history.delete-selected": Delete(false); break;
            case "history.delete-range": Delete(true); break;
            case "history.delete-all":
                if (payload.TryGetProperty("confirm", out var all) && all.ValueKind == JsonValueKind.True)
                { confirmDeleteAll = false; Guard(() => store.Delete(kind)); _ = RefreshAsync(); }
                else { confirmDeleteAll = true; PushUiState(); }
                break;
            case "history.delete-confirm":
                var accepted = payload.TryGetProperty("confirm", out var confirm) && confirm.ValueKind == JsonValueKind.True;
                if (confirmDeleteAll)
                {
                    confirmDeleteAll = false;
                    if (accepted) { Guard(() => store.Delete(kind)); _ = RefreshAsync(); }
                    else PushUiState();
                }
                else
                {
                    var range = payload.TryGetProperty("range", out var rangeValue) && rangeValue.ValueKind == JsonValueKind.True;
                    confirmDelete = false;
                    if (accepted) { deleteConfirmedFromUi = true; Delete(range); deleteConfirmedFromUi = false; }
                    else PushUiState();
                }
                break;
            case "history.save-tags":
                if (rows.SelectedItem is HistoryRow selected) Guard(() => store.SetTags(selected.Item.App, ReadString(payload, "tags").Split(',')));
                _ = RefreshAsync(); break;
            case "history.retry-ocr": Guard(store.RetryOcr); _ = RefreshAsync(); break;
            case "history.usage": new UsageStatisticsWindow(store, Start, End, featureUi).Show(); break;
        }
    }

    private static string ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private void PushUiState()
    {
        if (uiSurface is null) return;
        var selected = rows.SelectedItem as HistoryRow;
        uiSurface.SetState(new
        {
            title = Title, kind, search = search.Text, app = app.Text, tag = tag.Text, from = from.Text, until = until.Text,
            offset, status = status.Text, selectedId = selected?.Item.Id, selectedText = detail.Text, selectedTags = tags.Text,
            confirmDelete, confirmDeleteRange, confirmDeleteAll,
            preview = BitmapToDataUri(previewImage), mediaType = selected is null ? "" : System.IO.Path.GetExtension(selected.Item.Media),
            rows = (rows.ItemsSource as IEnumerable<HistoryRow>)?.Select(row => new
            {
                id = row.Item.Id, label = row.Label, app = row.Item.App, title = row.Item.Title,
                created = row.Item.Created.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"),
                hasMedia = row.Item.Media.Length > 0, mediaType = System.IO.Path.GetExtension(row.Item.Media), tags = row.Item.Tags
            }).ToArray() ?? []
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
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream(); encoder.Save(stream);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private BitmapSource? LoadIcon(string name)
    {
        if (name.Length == 0) return null;
        try { var path = store.ResolveMedia(name); return File.Exists(path) ? CaptureServices.Load(path) : null; }
        catch { return null; }
    }
    private static Point? ReadCursor(string metadata, int imageWidth, int imageHeight)
    {
        if (metadata.Length == 0) return null;
        try
        {
            using var json = JsonDocument.Parse(metadata); var root = json.RootElement;
            if (TryCoordinate(root, "cursorX", out var x) && TryCoordinate(root, "cursorY", out var y) &&
                x >= 0 && y >= 0 && x < imageWidth && y < imageHeight) return new(x, y);
            if (!TryCoordinate(root, "mouse_x", out x) || !TryCoordinate(root, "mouse_y", out y)) return null;
            var point = new POINT((int)x, (int)y); var display = DesktopDisplays.ForPoint(point); var bounds = display.Bounds;
            var localX = (x - bounds.Left) * imageWidth / Math.Max(1, bounds.Right - bounds.Left);
            var localY = (y - bounds.Top) * imageHeight / Math.Max(1, bounds.Bottom - bounds.Top);
            return localX >= 0 && localY >= 0 && localX < imageWidth && localY < imageHeight ? new(localX, localY) : null;
        }
        catch (JsonException) { return null; }
    }
    private static bool TryCoordinate(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var item)) return false;
        if (item.ValueKind == JsonValueKind.Number) return item.TryGetDouble(out value);
        return item.ValueKind == JsonValueKind.String && double.TryParse(item.GetString(), CultureInfo.InvariantCulture, out value);
    }
    private void PositionCursor()
    {
        cursorOverlay.Width = image.ActualWidth; cursorOverlay.Height = image.ActualHeight;
        if (cursorImagePoint is not { } point || previewImage is null || image.ActualWidth <= 0 || image.ActualHeight <= 0)
        { cursorMarker.Visibility = Visibility.Collapsed; return; }
        var scale = Math.Min(image.ActualWidth / previewImage.PixelWidth, image.ActualHeight / previewImage.PixelHeight);
        var left = (image.ActualWidth - previewImage.PixelWidth * scale) / 2 + point.X * scale - cursorMarker.Width / 2;
        var top = (image.ActualHeight - previewImage.PixelHeight * scale) / 2 + point.Y * scale - cursorMarker.Height / 2;
        Canvas.SetLeft(cursorMarker, left); Canvas.SetTop(cursorMarker, top); cursorMarker.Visibility = Visibility.Visible;
    }
    private void Delete(bool range)
    {
        try
        {
            if (range && Start is null && End is null) { status.Text = "Önce tarih aralığı seçin."; PushUiState(); return; }
            if (!range && rows.SelectedItem is not HistoryRow) return;
            if (uiSurface is not null && !deleteConfirmedFromUi)
            {
                confirmDelete = true;
                confirmDeleteRange = range;
                PushUiState();
                return;
            }
            if (!deleteConfirmedFromUi && !CaptureUi.Confirm(this, range ? "Seçilen tarih aralığı silinsin mi?" : "Seçilen kayıt silinsin mi?")) return;
            Guard(() => store.Delete(kind, range ? null : ((HistoryRow)rows.SelectedItem).Item.Id, range ? Start : null, range ? End : null)); _ = RefreshAsync();
        }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void Guard(Action action) { try { action(); } catch (Exception error) { status.Text = error.Message; } }
    private sealed record HistoryRow(HistoryItem Item, BitmapSource? Icon)
    {
        public string Label => $"{Item.Created.LocalDateTime:dd.MM.yyyy HH:mm:ss} · {Item.App}\n{(Item.Kind == "dictation" ? Item.Text.ReplaceLineEndings(" ")[..Math.Min(100, Item.Text.ReplaceLineEndings(" ").Length)] : Item.Title)}{(Item.OcrState == "failed" ? " · OCR hatası" : "")}";
    }
}
