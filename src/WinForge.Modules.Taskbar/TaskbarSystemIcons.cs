using System.Globalization;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

internal static class TaskbarSystemIcons
{
    internal static readonly string[] BellModes = ["never", "always", "when-empty"];
    private const string Section = "Sistem simgeleri";
    private const int MaximumDesktopWidth = 100;
    private const int MaximumTreeDepth = 32;
    internal static readonly IReadOnlyList<SettingDescriptor> Settings =
    [
        Hide("volume", "Ses simgesini gizle"),
        Hide("network", "Ağ simgesini gizle"),
        Hide("battery", "Pil simgesini gizle"),
        Hide("microphone", "Mikrofon simgesini gizle"),
        Hide("location", "Konum / GPS simgesini gizle"),
        Hide("studio-effects", "Studio Effects simgesini gizle"),
        Hide("recall", "Recall simgesini gizle"),
        Hide("language", "Dil çubuğunu gizle"),
        new() { Key = "hide-bell", Label = "Bildirim zili", Section = Section, Kind = SettingKind.Choice,
            Default = BellModes[0], Options = BellModes, OptionLabels = new Dictionary<string, string>
            { ["never"] = "Windows varsayılanı", ["always"] = "Her zaman gizle", ["when-empty"] = "Yeni bildirim yoksa gizle" } },
        new() { Key = "hide-show-desktop", Label = "Masaüstünü göster düğmesini gizle", Section = Section, Default = false },
        new() { Key = "show-desktop-width", Label = "Masaüstünü göster genişliği (0: Windows)", Section = Section,
            Kind = SettingKind.Number, Default = 0, Min = 0, Max = MaximumDesktopWidth,
            Description = "Genişlik mantıksal piksel cinsindendir. Gizleme açıkken genişlik uygulanmaz." }
    ];

    private static SettingDescriptor Hide(string icon, string label) =>
        new() { Key = "hide-" + icon, Label = label, Section = Section, Default = false };

    internal sealed record Preferences(IReadOnlySet<string> Hidden, string BellMode, int DesktopWidth)
    {
        internal bool IsConfigured => Hidden.Count > 0 || BellMode is "always" or "when-empty" || DesktopWidth > 0;
    }

    internal static Preferences Capture(IModuleSettings settings) => new(
        Settings.Where(setting => setting.Kind == SettingKind.Toggle && settings.GetBool(setting.Key)).Select(setting => setting.Key).ToHashSet(),
        settings.GetString("hide-bell", BellModes[0]), settings.GetInt("show-desktop-width"));

    internal static IReadOnlyList<ShellStyleRule> Build(IModuleSettings settings, IReadOnlyList<ShellVisualNode> tree) => Build(Capture(settings), tree);

    internal static IReadOnlyList<ShellStyleRule> Build(Preferences preferences, IReadOnlyList<ShellVisualNode> tree)
    {
        var rules = new List<ShellStyleRule>();
        var nodes = tree.ToDictionary(node => node.Handle);
        var children = tree.ToLookup(node => node.Parent);
        foreach (var icon in tree.Where(node => node.Type == "SystemTray.IconView"))
        {
            var descendants = Descendants(icon.Handle, 0).ToArray();
            var kinds = descendants.Select(node => IdentifyGlyph(node.Text)).Where(kind => kind.Length > 0).ToHashSet();
            if (descendants.Any(node => node.Type is "SystemTray.LanguageTextIconContent" or "SystemTray.LanguageImageIconContent")) kinds.Add("language");
            if (descendants.Any(node => node.Type == "SystemTray.BatteryIconContent")) kinds.Add("battery");
            var isBell = HasAncestor(icon, "NotificationCenterButton") &&
                !descendants.Any(node => node.Type == "SystemTray.DateTimeIconContent");
            var hide = kinds.Any(kind => kind != "microphone-location" && preferences.Hidden.Contains("hide-" + kind));
            if (kinds.Contains("microphone-location"))
                hide |= preferences.Hidden.Contains("hide-microphone") && preferences.Hidden.Contains("hide-location");
            if (isBell)
            {
                var mode = preferences.BellMode;
                hide |= mode == "always" || mode == "when-empty" && kinds.Contains("bell-empty");
            }
            var isDesktop = HasAncestor(icon, "ShowDesktopStack");
            if (isDesktop) hide |= preferences.Hidden.Contains("hide-show-desktop");
            if (hide) Add(icon, new() { ["Visibility"] = "Collapsed" });
            else if (isDesktop && preferences.DesktopWidth is > 0 and var width)
            {
                var value = Math.Clamp(width, 1, MaximumDesktopWidth).ToString(CultureInfo.InvariantCulture);
                Add(icon, new() { ["Width"] = value, ["MinWidth"] = value, ["MaxWidth"] = value });
            }
        }
        return rules;

        IEnumerable<ShellVisualNode> Descendants(ulong parent, int depth)
        {
            if (depth >= MaximumTreeDepth) yield break;
            foreach (var child in children[parent])
            {
                yield return child;
                foreach (var descendant in Descendants(child.Handle, depth + 1)) yield return descendant;
            }
        }

        bool HasAncestor(ShellVisualNode node, string name)
        {
            for (var depth = 0; depth < MaximumTreeDepth && nodes.TryGetValue(node.Parent, out node!); depth++)
                if (node.Name == name) return true;
            return false;
        }

        void Add(ShellVisualNode node, Dictionary<string, string> properties) =>
            rules.Add(new("@" + node.Handle.ToString(CultureInfo.InvariantCulture), properties));
    }

    // Segoe Fluent/MDL2 icon code points. Unknown glyphs are deliberately left untouched.
    internal static string IdentifyGlyph(string text)
    {
        if (text.Length != 1) return "";
        return text[0] switch
        {
            '\uE74F' or >= '\uE992' and <= '\uE995' or '\uEA85' or '\uEBC5' => "volume",
            '\uE709' or '\uE7F4' or '\uE839' or >= '\uE86C' and <= '\uE870' or
                >= '\uEAA1' and <= '\uEAA5' or '\uEAA8' or '\uEC1E' or
                >= '\uEC3C' and <= '\uEC3F' or '\uF384' or >= '\uF8C3' and <= '\uF8CC' => "network",
            >= '\uE3C1' and <= '\uE3CB' or >= '\uE408' and <= '\uE41D' or
                >= '\uEBA0' and <= '\uEBC0' or '\uEB17' or '\uEC02' or '\uF1E8' => "battery",
            '\uE361' or '\uE720' or '\uEC71' => "microphone",
            '\uE37A' => "location",
            '\uF47F' => "microphone-location",
            '\uEABC' => "studio-effects",
            '\uEC83' or '\uEADD' or '\uEB16' or '\uEF97' or '\uF1C6' => "recall",
            '\uF2A3' or '\uF285' => "bell-empty",
            '\uF2A5' or '\uF2A8' => "bell-full",
            _ => ""
        };
    }
}
