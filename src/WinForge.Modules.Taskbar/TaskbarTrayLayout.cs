using System.Globalization;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

internal static class TaskbarTrayLayout
{
    private const string Overflow = "SystemTray.NotificationAreaOverflow";
    internal static readonly SettingDescriptor[] Settings =
    [
        Number("overflow-columns", "Satır başına gizli simge sayısı (0: Windows)", 0, 20, 0),
        Number("overflow-icon-size", "Gizli simge alan boyutu (0: Windows)", 0, 64, 0),
        Number("overflow-icon-padding", "Gizli simgelerin iç boşluğu (-1: Windows)", -1, 24, -1),
        Number("overflow-icon-padding-x", "Gizli simgelerin yatay iç boşluğu (-1: ortak)", -1, 24, -1),
        Number("overflow-icon-padding-y", "Gizli simgelerin dikey iç boşluğu (-1: ortak)", -1, 24, -1),
        Number("overflow-icon-gap-x", "Gizli simgeler arası yatay boşluk (-1: Windows)", -1, 32, -1),
        Number("overflow-icon-gap-y", "Gizli simgeler arası dikey boşluk (-1: Windows)", -1, 32, -1),
        Number("overflow-panel-padding", "Gizli panelin kenar boşluğu (-1: Windows)", -1, 48, -1),
        Number("overflow-panel-radius", "Gizli panelin köşe yuvarlaklığı (-1: Windows)", -1, 48, -1),
    ];

    private static SettingDescriptor Number(string key, string label, int min, int max, int value) => new()
    {
        Key = key, Label = label, Section = "Gizli simgeler paneli", Kind = SettingKind.Number,
        Min = min, Max = max, Default = value,
        Description = key == "overflow-icon-size" ? "Her gizli simgenin alanını 16–64 arasında ayarlar. İç boşluk bu alanın içinde kalır."
            : key == "overflow-columns" ? "Simgeler soldan sağa dizilir; satır dolunca alt satıra geçer. Görünen tepsi düzeninden bağımsızdır."
            : key.Contains("padding-") ? "Ortak iç boşluğu bu eksende geçersiz kılar. Ortak değer ayarlanmamışsa diğer eksen 0 kullanır."
            : key.Contains("gap-") ? "İki simge arasındaki toplam boşluk. Tek eksen ayarlanırsa diğer eksen 0 kullanır. İkisi de -1 ise Windows düzeni korunur."
            : "Görünen tepsi simgelerinden bağımsız uygulanır. -1: varsayılanı korur; 0: boşluğu kaldırır."
    };

    internal static string? Padding(IModuleSettings settings, string prefix)
    {
        var max = prefix == "tray-icon" ? 16 : 24;
        var common = settings.GetInt(prefix + "-padding", -1);
        var x = settings.GetInt(prefix + "-padding-x", -1);
        var y = settings.GetInt(prefix + "-padding-y", -1);
        if (common < 0 && x < 0 && y < 0) return null;
        // An untouched axis uses the common padding, or zero when only one axis is customized.
        x = Math.Clamp(x < 0 ? Math.Max(common, 0) : x, 0, max);
        y = Math.Clamp(y < 0 ? Math.Max(common, 0) : y, 0, max);
        return x == y ? N(x) : $"{N(x)},{N(y)},{N(x)},{N(y)}";
    }

    internal static IReadOnlyList<ShellStyleRule> Build(IModuleSettings settings)
    {
        var rules = new List<ShellStyleRule>();
        var icon = new Dictionary<string, string>();
        var size = settings.GetInt("overflow-icon-size", 0);
        if (size > 0)
        {
            icon["MinWidth"] = icon["MinHeight"] = "0";
            icon["Width"] = icon["Height"] = N(Math.Clamp(size, 16, 64));
        }
        if (Padding(settings, "overflow-icon") is { } padding) icon["Padding"] = padding;
        var gapX = settings.GetInt("overflow-icon-gap-x", -1);
        var gapY = settings.GetInt("overflow-icon-gap-y", -1);
        if (gapX >= 0 || gapY >= 0)
        {
            var x = N(Math.Clamp(gapX, 0, 32) / 2.0);
            var y = N(Math.Clamp(gapY, 0, 32) / 2.0);
            icon["Margin"] = $"{x},{y},{x},{y}";
        }
        if (icon.Count > 0) rules.Add(new($"{Overflow} SystemTray.NotifyIconView", icon));

        // Native overflow grids may cache a fixed item extent. Remeasure it when
        // icon size or margins change so larger icons and gaps cannot overlap.
        if (size > 0 || gapX >= 0 || gapY >= 0)
        {
            Add("WrapGrid", ("ItemWidth", "NaN"), ("ItemHeight", "NaN"));
        }

        var columns = settings.GetInt("overflow-columns", 0);
        if (columns > 0)
        {
            Add("WrapGrid", ("Orientation", "Horizontal"), ("MaximumRowsOrColumns", N(Math.Clamp(columns, 1, 20))));
            // Release the native fixed width so the panel measures the chosen columns and icon spacing.
            rules.Add(new(Overflow, new Dictionary<string, string> { ["Width"] = "Auto", ["MinWidth"] = "0" }));
            foreach (var selector in new[] { "Grid#OverflowRootGrid", "Border#OverflowFlyoutBackgroundBorder", "ItemsControl" })
                Add(selector, ("Width", "Auto"), ("MinWidth", "0"));
        }
        var panelPadding = settings.GetInt("overflow-panel-padding", -1);
        if (panelPadding >= 0) Add("ItemsControl", ("Padding", N(Math.Clamp(panelPadding, 0, 48))));
        var radius = settings.GetInt("overflow-panel-radius", -1);
        if (radius >= 0) Add("Border#OverflowFlyoutBackgroundBorder", ("CornerRadius", N(Math.Clamp(radius, 0, 48))));
        return rules;

        void Add(string selector, params (string Name, string Value)[] properties) => rules.Add(new(
            $"{Overflow} {selector}", properties.ToDictionary(p => p.Name, p => p.Value)));
    }

    private static string N(double value) => value.ToString(CultureInfo.InvariantCulture);
}
