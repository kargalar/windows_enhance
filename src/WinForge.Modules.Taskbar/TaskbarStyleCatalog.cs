using System.Globalization;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

internal static class TaskbarStyleCatalog
{
    internal static readonly string[] Themes = ["Varsayılan", "Saydam", "Cam", "Gece", "Klasik Mavi", "Gül", "Dock"];
    private const string TrayPanel = "<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><WrapGrid Orientation=\"Vertical\" MaximumRowsOrColumns=\"2\" /></ItemsPanelTemplate>";

    internal static IReadOnlyList<ShellStyleRule> Build(IModuleSettings settings, DateTime now, IReadOnlyList<ShellVisualNode>? tree = null)
    {
        var rules = new List<ShellStyleRule>();
        if (settings.GetBool("hide-start")) Add("[AutomationId=StartButton]", ("Visibility", "Collapsed"));

        var theme = settings.GetString("theme", Themes[0]);
        var fill = theme switch
        {
            "Saydam" => "#00000000",
            "Cam" => "#68303B4B",
            "Gece" => "#F0171B24",
            "Klasik Mavi" => "#EF174C91",
            "Gül" => "#F023203B",
            "Dock" => "#E6222936",
            _ => null
        };
        if (fill is not null)
        {
            Add("Rectangle#BackgroundFill", ("Fill", fill));
            Add("Rectangle#BackgroundStroke", ("Fill", theme == "Dock" ? "#705F7088" : "#00000000"));
        }
        if (theme == "Dock")
        {
            Add("Taskbar.TaskbarFrame", ("Margin", "8,4,8,4"));
            Add("Taskbar.TaskListButton", ("Margin", "2,0,2,0"));
        }
        var iconSize = settings.GetInt("app-icon-size", 0);
        var height = settings.GetInt("taskbar-height", 0);
        if (height is > 0 and < 48 && iconSize == 0)
            iconSize = Math.Clamp(height - 8, 16, 32);
        if (height != 0)
        {
            var value = Math.Clamp(height, 24, 96).ToString(CultureInfo.InvariantCulture);
            Add("Taskbar.TaskbarFrame", ("Height", value), ("MinHeight", value), ("MaxHeight", value));
            Add("Taskbar.TaskListButton", ("MinHeight", "0"), ("Height", value), ("MaxHeight", value), ("Padding", "0"), ("VerticalContentAlignment", "Center"));
            Add("SystemTray.SystemTrayFrame", ("MinHeight", "0"), ("Height", value), ("MaxHeight", value));
            // Tray content can retain its native 48-DIP minimum even after the
            // frame shrinks, leaving glyphs centered below the visible bar.
            foreach (var selector in new[] { "SystemTray.IconView", "SystemTray.TextIconContent", "SystemTray.BatteryIconContent", "SystemTray.DateTimeIconContent" })
                Add(selector, ("MinHeight", "0"), ("Height", value), ("MaxHeight", value), ("VerticalAlignment", "Center"), ("VerticalContentAlignment", "Center"));
        }
        if (iconSize != 0)
        {
            var value = Math.Clamp(iconSize, 16, 40).ToString(CultureInfo.InvariantCulture);
            Add("Image#Icon", ("Width", value), ("Height", value));
        }
        var buttonWidth = settings.GetInt("button-width", 0);
        if (buttonWidth != 0)
        {
            var value = Math.Clamp(buttonWidth, 28, 160).ToString(CultureInfo.InvariantCulture);
            Add("Taskbar.TaskListButton", ("MinWidth", value), ("Width", value));
        }
        if (settings.GetBool("tray-two-rows"))
        {
            Add("SystemTray.NotificationAreaIcons", ("ItemsPanel", TrayPanel), ("Height", "40"));
            Add("SystemTray.NotificationAreaIcons SystemTray.NotifyIconView", ("MinWidth", "0"), ("MinHeight", "0"), ("Width", "20"), ("Height", "20"), ("Padding", "0"));
        }
        var time = settings.GetString("time-format").Trim();
        var date = settings.GetString("date-format").Trim();
        if (time.Length > 0) Add("TextBlock#TimeInnerTextBlock", ("Text", Format(now, time)));
        if (date.Length > 0) Add("TextBlock#DateInnerTextBlock", ("Text", Format(now, date)));
        if (time.Length > 0 || date.Length > 0)
            Add("SystemTray.DateTimeIconContent", ("Width", Math.Clamp(settings.GetInt("clock-width", 120), 60, 360).ToString(CultureInfo.InvariantCulture)));
        if (tree is not null) rules.AddRange(TaskbarSystemIcons.Build(settings, tree));
        if (TaskbarDockSettings.Build(settings) is { } dockRule) rules.Add(dockRule);
        return rules;

        void Add(string selector, params (string Name, string Value)[] properties) =>
            rules.Add(new ShellStyleRule(selector, properties.ToDictionary(property => property.Name, property => property.Value)));
    }

    private static string Format(DateTime now, string format)
    {
        if (format.Length > 128) throw new ArgumentException("Saat biçimi en fazla 128 karakter olabilir.");
        try { return now.ToString(format, CultureInfo.CurrentCulture); }
        catch (FormatException exception) { throw new ArgumentException("Saat veya tarih biçimi geçerli değil.", exception); }
    }
}
