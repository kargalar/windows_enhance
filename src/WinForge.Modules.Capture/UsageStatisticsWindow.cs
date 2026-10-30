using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal sealed class UsageStatisticsWindow : Window
{
    private readonly HistoryStore store;
    private readonly IFeatureUiSurface? uiSurface;
    private readonly TextBox from = new() { Width = 125, Margin = new(4), ToolTip = "gg.aa.yyyy" };
    private readonly TextBox until = new() { Width = 125, Margin = new(4), ToolTip = "gg.aa.yyyy" };
    private readonly DataGrid applications = CreateGrid();
    private readonly DataGrid byDay = CreateGrid();
    private readonly DataGrid contexts = CreateGrid();
    private readonly DataGrid tags = CreateGrid();
    private readonly Canvas applicationChart = CreateChart();
    private readonly Canvas dailyChart = CreateChart();
    private readonly Canvas tagChart = CreateChart();
    private readonly TextBlock summary = new() { Margin = new(10), FontSize = 16 };
    private readonly TextBlock status = new() { Margin = new(8) };
    private (string Label, double Seconds)[] appChartData = [];
    private (string Label, double Active, double Idle)[] dailyChartData = [];
    private (string Label, double Seconds)[] tagChartData = [];

    public UsageStatisticsWindow(HistoryStore store, DateTimeOffset? from, DateTimeOffset? until, IFeatureUiService? featureUi = null)
    {
        this.store = store;
        uiSurface = featureUi?.CreateSurface("capture-usage-statistics");
        if (uiSurface is not null) uiSurface.MessageReceived += OnUiMessage;
        if (from is { } start) this.from.Text = start.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR"));
        if (until is { } end) this.until.Text = end.AddDays(-1).ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR"));
        Title = "Kullanım istatistikleri"; Width = 1080; Height = 700; MinWidth = 820; MinHeight = 500; CaptureUi.Theme(this);
        var root = new DockPanel { Margin = new(12) }; Content = root;
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        AddDateField(toolbar, "Başlangıç (boş = tümü)", this.from);
        AddDateField(toolbar, "Bitiş (boş = bugün dahil)", this.until);
        toolbar.Children.Add(CaptureUi.Button("İstatistikleri yenile", Refresh));
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var tabs = new TabControl(); tabs.SetResourceReference(Control.BackgroundProperty, "BgBrush"); root.Children.Add(tabs);
        tabs.Items.Add(CreateTab("Uygulamalar", CreateChartPanel(summary, applicationChart, applications)));
        tabs.Items.Add(CreateTab("Günlük kullanım", CreateChartPanel(null, dailyChart, byDay)));
        tabs.Items.Add(CreateTab("Web siteleri ve klasörler", contexts));
        tabs.Items.Add(CreateTab("Etiketler", CreateChartPanel(null, tagChart, tags)));
        applicationChart.SizeChanged += (_, _) => DrawHorizontal(applicationChart, appChartData);
        dailyChart.SizeChanged += (_, _) => DrawDaily(dailyChart, dailyChartData);
        tagChart.SizeChanged += (_, _) => DrawHorizontal(tagChart, tagChartData);
        if (uiSurface is not null) Content = uiSurface.Element;
        Loaded += (_, _) => Refresh();
        Closed += (_, _) => uiSurface?.Dispose();
    }

    private static Canvas CreateChart() => new() { Height = 210, Margin = new(12), ClipToBounds = true };

    private static TabItem CreateTab(string header, UIElement content)
    {
        var tab = new TabItem { Header = header, Content = content };
        tab.SetResourceReference(Control.BackgroundProperty, "BgBrush");
        tab.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        return tab;
    }

    private static Grid CreateChartPanel(TextBlock? heading, Canvas chart, DataGrid grid)
    {
        var layout = new Grid(); layout.SetResourceReference(Control.BackgroundProperty, "BgBrush"); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(220) }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        if (heading is not null) { Grid.SetRow(heading, 0); layout.Children.Add(heading); }
        Grid.SetRow(chart, 1); layout.Children.Add(chart); Grid.SetRow(grid, 2); layout.Children.Add(grid); return layout;
    }

    private static DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Margin = new(4)
        };
        grid.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        grid.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        grid.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "PanelBrush");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "BgBrush");
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "BorderBrush");
        grid.ColumnHeaderStyle = StyleFor(typeof(DataGridColumnHeader));
        grid.RowStyle = StyleFor(typeof(DataGridRow));
        grid.CellStyle = StyleFor(typeof(DataGridCell));
        return grid;
    }

    private static Style StyleFor(Type controlType)
    {
        var style = new Style(controlType);
        style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("PanelBrush")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
        return style;
    }

    private static void AddDateField(Panel panel, string label, TextBox input)
    {
        var field = new StackPanel(); field.Children.Add(new TextBlock { Text = label, Margin = new(4, 0, 4, 0) }); field.Children.Add(input); panel.Children.Add(field);
    }

    private DateTimeOffset? Start => ParseDate(from.Text);
    private DateTimeOffset? End => ParseDate(until.Text)?.AddDays(1);

    private static DateTimeOffset? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParseExact(value.Trim(), "dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var date))
            throw new ArgumentException("Tarihi gg.aa.yyyy biçiminde girin.");
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        if (command == "statistics.refresh")
        {
            from.Text = payload.TryGetProperty("from", out var start) ? start.GetString() ?? "" : "";
            until.Text = payload.TryGetProperty("until", out var end) ? end.GetString() ?? "" : "";
            Refresh();
        }
        else if (command == "statistics.close")
            Close();
    }

    private void PushUiState(RecallUsageSummary summaryData, IReadOnlyList<UsageTotal> appTotals,
        IReadOnlyList<UsageDayTotal> activeByDay, IReadOnlyList<UsageDayTotal> idleByDay,
        IReadOnlyList<ContextUsageDayTotal> contextTotals, IReadOnlyList<TagUsageTotal> tagTotals)
    {
        uiSurface?.SetState(new
        {
            from = from.Text,
            until = until.Text,
            status = status.Text,
            summary = summary.Text,
            applications = appTotals.Select(row => new { app = row.App, seconds = row.Seconds, duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"), estimated = row.Estimated }).ToArray(),
            days = dailyChartData.Select(row => new { date = row.Label, active = row.Active, idle = row.Idle }).ToArray(),
            dayApplications = activeByDay.Select(row => new { date = row.Date, app = row.App, duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"), estimated = row.Estimated }).ToArray(),
            contexts = contextTotals.Select(row => new { date = row.Date, type = row.Type, app = row.App, context = row.Context, duration = TimeSpan.FromSeconds(row.Seconds).ToString("g") }).ToArray(),
            tags = tagTotals.Select(row => new { tag = row.Tag, seconds = row.Seconds, duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"), applications = row.ApplicationCount }).ToArray()
        });
    }

    private void Refresh()
    {
        try
        {
            if (Start is { } start && End is { } end && end <= start) throw new ArgumentException("Bitiş tarihi başlangıçtan sonra olmalı.");
            var summaryData = store.RecallSummary(Start, End);
            summary.Text = $"Etkin: {TimeSpan.FromSeconds(summaryData.ActiveSeconds):g}     Boşta: {TimeSpan.FromSeconds(summaryData.IdleSeconds):g}     " +
                $"Ekran görüntüsü: {summaryData.Captures:N0}     En çok kullanılan: {summaryData.MostUsedApp}";
            var appTotals = store.Usage(Start, End);
            appChartData = appTotals.Take(8).Select(row => (row.App, row.Seconds)).ToArray();
            DrawHorizontal(applicationChart, appChartData);
            applications.ItemsSource = appTotals.Select(row => new
            {
                Uygulama = row.App,
                Süre = TimeSpan.FromSeconds(row.Seconds).ToString("g"),
                Tahmini = row.Estimated ? "Evet" : "Hayır"
            }).ToArray();
            var activeByDay = store.UsageByDay(Start, End);
            var idleByDay = store.IdleUsageByDay(Start, End);
            var dates = activeByDay.Select(row => row.Date).Concat(idleByDay.Select(row => row.Date)).Distinct(StringComparer.Ordinal);
            dailyChartData = dates.Select(date => (date, activeByDay.Where(row => row.Date == date).Sum(row => row.Seconds), idleByDay.Where(row => row.Date == date).Sum(row => row.Seconds)))
                .OrderBy(row => DateTime.ParseExact(row.date, "dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR")))
                .TakeLast(14).Select(row => (row.date, row.Item2, row.Item3)).ToArray();
            DrawDaily(dailyChart, dailyChartData);
            byDay.ItemsSource = activeByDay.Select(row => new
            {
                Tarih = row.Date,
                Uygulama = row.App,
                Süre = TimeSpan.FromSeconds(row.Seconds).ToString("g"),
                Tahmini = row.Estimated ? "Evet" : "Hayır"
            }).ToArray();
            var contextTotals = store.ContextUsageByDay(Start, End);
            contexts.ItemsSource = contextTotals.Select(row => new
            {
                Tarih = row.Date,
                Tür = row.Type == "website" ? "Web sitesi" : "Klasör",
                Uygulama = row.App,
                Konum = row.Context,
                Süre = TimeSpan.FromSeconds(row.Seconds).ToString("g")
            }).ToArray();
            var tagTotals = store.TagUsage(Start, End);
            tagChartData = tagTotals.Take(8).Select(row => (row.Tag, row.Seconds)).ToArray();
            DrawHorizontal(tagChart, tagChartData);
            tags.ItemsSource = tagTotals.Select(row => new
            {
                Etiket = row.Tag,
                Süre = TimeSpan.FromSeconds(row.Seconds).ToString("g"),
                Uygulama = row.ApplicationCount
            }).ToArray();
            status.Text = $"Aralık: {(Start?.ToLocalTime().ToString("dd.MM.yyyy") ?? "başlangıçtan beri")} – {(End?.AddDays(-1).ToLocalTime().ToString("dd.MM.yyyy") ?? "bugün dahil")} · İstatistikler yerel kullanım zamanlarından hesaplanır.";
            PushUiState(summaryData, appTotals, activeByDay, idleByDay, contextTotals, tagTotals);
        }
        catch (Exception error) { status.Text = error.Message; }
    }

    private static void DrawHorizontal(Canvas canvas, IReadOnlyList<(string Label, double Seconds)> values)
    {
        canvas.Children.Clear(); if (values.Count == 0 || canvas.ActualWidth <= 0) return;
        var max = Math.Max(1, values.Max(row => row.Seconds)); var labelWidth = Math.Min(180, canvas.ActualWidth * 0.3);
        var barWidth = Math.Max(80, canvas.ActualWidth - labelWidth - 105); var rowHeight = 24d;
        for (var index = 0; index < values.Count; index++)
        {
            var row = values[index]; var y = index * rowHeight;
            var label = new TextBlock { Text = row.Label.Length > 24 ? row.Label[..24] + "…" : row.Label, Width = labelWidth, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            var bar = new Border { Width = Math.Max(2, barWidth * row.Seconds / max), Height = 13, CornerRadius = new(3), Background = new SolidColorBrush(Color.FromRgb(99, 156, 255)) };
            var duration = new TextBlock { Text = TimeSpan.FromSeconds(row.Seconds).ToString("g"), FontSize = 10 };
            Canvas.SetLeft(label, 0); Canvas.SetTop(label, y); Canvas.SetLeft(bar, labelWidth + 6); Canvas.SetTop(bar, y + 3);
            Canvas.SetLeft(duration, labelWidth + barWidth + 14); Canvas.SetTop(duration, y + 1);
            canvas.Children.Add(label); canvas.Children.Add(bar); canvas.Children.Add(duration);
        }
    }

    private static void DrawDaily(Canvas canvas, IReadOnlyList<(string Label, double Active, double Idle)> values)
    {
        canvas.Children.Clear(); if (values.Count == 0 || canvas.ActualWidth <= 0) return;
        var max = Math.Max(1, values.Max(row => row.Active + row.Idle)); var slot = canvas.ActualWidth / values.Count;
        for (var index = 0; index < values.Count; index++)
        {
            var row = values[index]; var totalHeight = 150 * (row.Active + row.Idle) / max; var activeHeight = totalHeight * row.Active / Math.Max(1, row.Active + row.Idle);
            var x = index * slot + 4; var width = Math.Max(4, slot - 8); var y = 158 - totalHeight;
            var idle = new Border { Width = width, Height = Math.Max(0, totalHeight - activeHeight), Background = new SolidColorBrush(Color.FromRgb(103, 112, 128)) };
            var active = new Border { Width = width, Height = activeHeight, Background = new SolidColorBrush(Color.FromRgb(99, 156, 255)) };
            var label = new TextBlock { Text = row.Label[..Math.Min(5, row.Label.Length)], FontSize = 9, Width = slot, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(idle, x); Canvas.SetTop(idle, y); Canvas.SetLeft(active, x); Canvas.SetTop(active, y + totalHeight - activeHeight);
            Canvas.SetLeft(label, index * slot); Canvas.SetTop(label, 164);
            canvas.Children.Add(idle); canvas.Children.Add(active); canvas.Children.Add(label);
        }
        var legendActive = new TextBlock { Text = "● Etkin", Foreground = new SolidColorBrush(Color.FromRgb(99, 156, 255)), FontSize = 10 };
        var legendIdle = new TextBlock { Text = "● Boşta", Foreground = new SolidColorBrush(Color.FromRgb(160, 168, 181)), FontSize = 10 };
        Canvas.SetRight(legendActive, 84); Canvas.SetTop(legendActive, 0); Canvas.SetRight(legendIdle, 4); Canvas.SetTop(legendIdle, 0);
        canvas.Children.Add(legendActive); canvas.Children.Add(legendIdle);
    }
}
