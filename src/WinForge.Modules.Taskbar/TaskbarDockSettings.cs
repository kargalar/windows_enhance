using System.Globalization;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

internal static class TaskbarDockSettings
{
    private const string Section = "Dock animasyonu";
    private const string EnabledKey = "dock-animation";
    private const string ScaleKey = "dock-scale";
    private const string DurationKey = "dock-duration";
    private const int MinimumScale = 105;
    private const int MaximumScale = 160;
    private const int DefaultScale = 125;
    private const int MinimumDuration = 50;
    private const int MaximumDuration = 500;
    private const int DefaultDuration = 160;

    internal static readonly IReadOnlyList<SettingDescriptor> Settings =
    [
        new() { Key = EnabledKey, Label = "Dock animasyonu", Section = Section, Default = false,
            Description = "İmleç uygulama düğmesine geldiğinde simge büyür ve ayrıldığında eski boyutuna döner. Windows animasyonları kapalıysa geçiş anında uygulanır." },
        new() { Key = ScaleKey, Label = "Büyütme (%)", Section = Section, Kind = SettingKind.Number,
            Default = DefaultScale, Min = MinimumScale, Max = MaximumScale },
        new() { Key = DurationKey, Label = "Geçiş süresi (ms)", Section = Section, Kind = SettingKind.Number,
            Default = DefaultDuration, Min = MinimumDuration, Max = MaximumDuration }
    ];

    internal static ShellStyleRule? Build(IModuleSettings settings)
    {
        if (!settings.GetBool(EnabledKey)) return null;
        var scale = Math.Clamp(settings.GetInt(ScaleKey, DefaultScale), MinimumScale, MaximumScale);
        var duration = Math.Clamp(settings.GetInt(DurationKey, DefaultDuration), MinimumDuration, MaximumDuration);
        return new("Taskbar.TaskListButton", new Dictionary<string, string>
        {
            ["DockAnimation"] = string.Create(CultureInfo.InvariantCulture, $"{scale},{duration}")
        });
    }
}
