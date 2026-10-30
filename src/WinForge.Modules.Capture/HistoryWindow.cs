using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                if (control is ListBox or DatePicker)
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
    private readonly TextBox search = new() { Width = 220, Margin = new(4), ToolTip = "Metin/OCR ara" };
    private readonly TextBox app = new() { Width = 140, Margin = new(4), ToolTip = "Uygulama adı (örn. Code.exe)" };
    private readonly TextBox tag = new() { Width = 110, Margin = new(4), ToolTip = "Etiket filtresi" };
    private readonly TextBox from = new() { Width = 130, Margin = new(4), ToolTip = "gg.aa.yyyy" };
    private readonly TextBox until = new() { Width = 130, Margin = new(4), ToolTip = "gg.aa.yyyy" };
    private readonly ListBox rows = new() { DisplayMemberPath = nameof(HistoryRow.Label) };
    private readonly Image image = new() { Stretch = Stretch.Uniform, MaxHeight = 500 };
    private readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100 };
    private readonly TextBox tags = new() { ToolTip = "Seçili uygulamanın etiketleri; virgülle ayırın", Margin = new(4) };
    private readonly TextBlock status = new() { Margin = new(6) };
    private readonly Slider timeline = new() { Minimum = 0, Maximum = 0, TickFrequency = 1, IsSnapToTickEnabled = true, IsDirectionReversed = true, Margin = new(8), ToolTip = "Bu sayfadaki kayıtlar arasında gezin" };
    private int offset;
    private bool busy;
    public HistoryWindow(HistoryStore store, string kind, string title)
    {
        this.store = store; this.kind = kind; Title = title; Width = 1180; Height = 780; MinWidth = 900; MinHeight = 600; CaptureUi.Theme(this);
        var root = new DockPanel { Margin = new(12) }; Content = root;
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
            bottom.Children.Add(CaptureUi.Button("Kullanım süreleri", () => Guard(() =>
                MessageBox.Show(this, string.Join(Environment.NewLine, store.Usage(Start, End).Select(u => $"{u.App}: {TimeSpan.FromSeconds(u.Seconds):g}{(u.Estimated ? " (tahmini)" : "")}")), "Uygulama kullanımı"))));
            bottom.Children.Add(CaptureUi.Button("OCR yeniden dene", () => Guard(store.RetryOcr)));
        }
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) }); root.Children.Add(grid);
        grid.Children.Add(rows); var preview = new StackPanel { Margin = new(10) }; Grid.SetColumn(preview, 1); grid.Children.Add(new ScrollViewer { Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Grid.SetColumn(grid.Children[^1], 1);
        preview.Children.Add(image); preview.Children.Add(detail);
        if (kind == "recall")
        {
            preview.Children.Add(tags);
            preview.Children.Add(CaptureUi.Button("Uygulama etiketlerini kaydet", () =>
            {
                if (rows.SelectedItem is HistoryRow row) Guard(() => store.SetTags(row.Item.App, tags.Text.Split(','))); _ = RefreshAsync();
            }));
        }
        rows.SelectionChanged += (_, _) => { timeline.Value = Math.Max(0, rows.SelectedIndex); ShowSelection(); }; Loaded += (_, _) => _ = RefreshAsync();
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
            var items = await Task.Run(() => store.Search(filter)); rows.ItemsSource = items.Select(item => new HistoryRow(item)).ToArray();
            timeline.Maximum = Math.Max(0, items.Count - 1);
            rows.SelectedIndex = items.Count > 0 ? 0 : -1;
            status.Text = items.Count == 0 ? "Kayıt bulunamadı." : $"{offset + 1}–{offset + items.Count} · {store.DirectoryPath}";
        }
        catch (Exception error) { status.Text = error.Message; }
        finally { busy = false; }
    }
    private void ShowSelection()
    {
        image.Source = null; detail.Clear();
        if (rows.SelectedItem is not HistoryRow row) return;
        detail.Text = row.Item.Text; tags.Text = row.Item.Tags;
        if (row.Item.Media.Length == 0) return;
        Guard(() =>
        {
            var path = store.ResolveMedia(row.Item.Media);
            if (Path.GetExtension(path) is ".mp4" or ".gif")
            {
                detail.Text += "\nVideo/GIF kaydı. Oynatmak için Medyayı aç düğmesini kullanın."; return;
            }
            if (File.Exists(path)) image.Source = CaptureServices.Load(path); else status.Text = "Görsel dosyası bulunamadı.";
        });
    }
    private void Delete(bool range)
    {
        try
        {
            if (range && Start is null && End is null) { status.Text = "Önce tarih aralığı seçin."; return; }
            if (!range && rows.SelectedItem is not HistoryRow) return;
            if (!CaptureUi.Confirm(this, range ? "Seçilen tarih aralığı silinsin mi?" : "Seçilen kayıt silinsin mi?")) return;
            Guard(() => store.Delete(kind, range ? null : ((HistoryRow)rows.SelectedItem).Item.Id, range ? Start : null, range ? End : null)); _ = RefreshAsync();
        }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void Guard(Action action) { try { action(); } catch (Exception error) { status.Text = error.Message; } }
    private sealed record HistoryRow(HistoryItem Item)
    {
        public string Label => $"{Item.Created.LocalDateTime:dd.MM.yyyy HH:mm:ss} · {Item.App}\n{(Item.Kind == "dictation" ? Item.Text.ReplaceLineEndings(" ")[..Math.Min(100, Item.Text.ReplaceLineEndings(" ").Length)] : Item.Title)}{(Item.OcrState == "failed" ? " · OCR hatası" : "")}";
    }
}
