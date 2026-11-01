using System.Globalization;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

internal static class TaskbarStyleCatalog
{
    internal static readonly string[] Themes = ["Varsayılan", "Saydam", "Cam", "Gece", "Klasik Mavi", "Gül", "Dock"];
    internal static readonly string[] ClockDisplays = ["Tarih ve saat", "Yalnızca tarih", "Yalnızca saat"];
    internal static readonly string[] TimeFormats = ["", "HH:mm", "HH:mm:ss", "H:mm", "H:mm:ss", "hh:mm tt", "hh:mm:ss tt", "h:mm tt", "h:mm:ss tt", "HH'.'mm", "HH'.'mm'.'ss", "HH:mm 'saat'"];
    internal static readonly string[] DateFormats = ["", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yy", "d MMM yyyy", "dd MMMM yyyy", "d MMM", "d MMMM", "ddd, dd.MM.yyyy", "dddd, d MMMM yyyy", "ddd, d MMM", "dddd, d MMMM", "dddd"];

    internal static IReadOnlyDictionary<string, string> FormatLabels(string[] formats)
    {
        var example = new DateTime(2026, 11, 1, 14, 5, 9);
        return formats.ToDictionary(format => format, format => format.Length == 0
            ? "Windows varsayılanı"
            : $"{example.ToString(format, CultureInfo.CurrentCulture)} · {format}");
    }
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
        if (settings.GetBool("custom-background"))
        {
            var color = settings.GetString("background-color", "#171B24").Trim();
            if (color.Length != 7 || color[0] != '#' ||
                !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
                throw new ArgumentException("Görev çubuğu arka plan rengi #RRGGBB biçiminde olmalıdır.");
            var opacity = Math.Clamp(settings.GetInt("background-opacity", 100), 0, 100);
            var alpha = (opacity * 255 + 50) / 100;
            fill = $"#{alpha:X2}{color[1..].ToUpperInvariant()}";
        }
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
            Add("Taskbar.ExperienceToggleButton", ("MinHeight", "0"), ("Height", value), ("MaxHeight", value), ("Padding", "0"), ("VerticalContentAlignment", "Center"));
            Add("Taskbar.TaskListButtonPanel", ("MinHeight", "0"), ("Height", value), ("MaxHeight", value));
            Add("Taskbar.TaskListButton Grid#IconPanel", ("MinHeight", "0"), ("Height", value), ("MaxHeight", value), ("Margin", "0"));
            Add("Taskbar.TaskListButton Image#Icon", ("Margin", "0"), ("VerticalAlignment", "Center"));
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
            Add("Taskbar.TaskListButton", ("MinWidth", "0"), ("Width", value), ("MaxWidth", value), ("Padding", "0"), ("HorizontalContentAlignment", "Stretch"));
            var backgroundWidth = (Math.Clamp(buttonWidth, 28, 160) - 4).ToString(CultureInfo.InvariantCulture);
            Add("Taskbar.TaskListButton Grid#IconPanel", ("MinWidth", "0"), ("Width", value), ("MaxWidth", value), ("Margin", "0"), ("IconColumnWidth", backgroundWidth));
            Add("Taskbar.TaskListButton Image#Icon", ("Margin", "0"), ("HorizontalAlignment", "Center"));
            Add("Taskbar.TaskListButton Border", ("MinWidth", "0"), ("Width", backgroundWidth), ("MaxWidth", backgroundWidth));
        }
        if (settings.GetBool("tray-two-rows"))
        {
            Add("SystemTray.NotificationAreaIcons", ("ItemsPanel", TrayPanel), ("Height", "40"));
            Add("SystemTray.NotificationAreaIcons SystemTray.NotifyIconView", ("MinWidth", "0"), ("MinHeight", "0"), ("Width", "20"), ("Height", "20"), ("Padding", "0"));
        }
        var time = settings.GetString("time-format").Trim();
        AddGap("tray-icon-gap", "SystemTray.NotificationAreaIcons SystemTray.NotifyIconView");
        foreach (var selector in new[] { "SystemTray.OmniButton#ControlCenterButton SystemTray.IconView", "SystemTray.Stack#MainStack SystemTray.IconView", "SystemTray.Stack#NonActivatableStack SystemTray.IconView" })
            AddGap("system-icon-gap", selector);
        foreach (var selector in new[] { "SystemTray.Stack#NotifyIconStack", "SystemTray.NotificationAreaIcons", "SystemTray.OmniButton#ControlCenterButton", "SystemTray.Stack#MainStack", "SystemTray.Stack#NonActivatableStack" })
            AddGap("tray-group-gap", selector);
        var clockGap = settings.GetInt("clock-gap", -1);
        if (clockGap >= 0)
            Add("SystemTray.OmniButton#NotificationCenterButton", ("Margin", $"{Math.Clamp(clockGap, 0, 32)},0,0,0"), ("Padding", "0"));
        var date = settings.GetString("date-format").Trim();
        var display = settings.GetString("clock-display", ClockDisplays[0]);
        var hideTime = display == ClockDisplays[1];
        var hideDate = display == ClockDisplays[2];
        if (hideTime) Add("TextBlock#TimeInnerTextBlock", ("Visibility", "Collapsed"));
        else if (time.Length > 0) Add("TextBlock#TimeInnerTextBlock", ("Text", Format(now, time)));
        if (hideDate) Add("TextBlock#DateInnerTextBlock", ("Visibility", "Collapsed"));
        else if (date.Length > 0) Add("TextBlock#DateInnerTextBlock", ("Text", Format(now, date)));
        // Formatting must not reserve a fixed-width tile beside the tray.
        // Keep native content sizing unless the user explicitly requests a width.
        var clockWidth = settings.GetInt("clock-width", 0);
        if (clockWidth > 0 && (time.Length > 0 || date.Length > 0 || hideTime || hideDate))
            Add("SystemTray.DateTimeIconContent", ("Width", Math.Clamp(clockWidth, 60, 360).ToString(CultureInfo.InvariantCulture)));
        if (tree is not null) rules.AddRange(TaskbarSystemIcons.Build(settings, tree));
        if (TaskbarDockSettings.Build(settings) is { } dockRule) rules.Add(dockRule);
        return rules;

        void Add(string selector, params (string Name, string Value)[] properties) =>
            rules.Add(new ShellStyleRule(selector, properties.ToDictionary(property => property.Name, property => property.Value)));

        void AddGap(string key, string selector)
        {
            var gap = settings.GetInt(key, -1);
            if (gap < 0) return;
            var half = (Math.Clamp(gap, 0, 24) / 2.0).ToString(CultureInfo.InvariantCulture);
            Add(selector, ("Margin", $"{half},0,{half},0"), ("Padding", "0"));
        }
    }

    private static string Format(DateTime now, string format)
    {
        if (format.Length > 128) throw new ArgumentException("Saat biçimi en fazla 128 karakter olabilir.");
        try { return now.ToString(format, CultureInfo.CurrentCulture); }
        catch (FormatException exception) { throw new ArgumentException("Saat veya tarih biçimi geçerli değil.", exception); }
    }
}
