using System.Windows.Input;

namespace WinForge.Modules.Extras;

internal sealed record AutomationRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Yeni otomasyon";
    public bool Enabled { get; init; } = true;
    public string Shortcut { get; init; } = "F2";
    public bool Repeat { get; init; } = true;
    public int Interval { get; init; } = 200;
    public string ProcessName { get; init; } = "";
    public List<AutomationStep> Steps { get; init; } = [new("click", "left")];

    internal string ToLine()
    {
        ForegroundApplication.Validate(ProcessName);
        if (Id.Length is < 1 or > 64 || Name.Length is < 1 or > 100 || Steps is null || Steps.Count is < 1 or > 100)
            throw new FormatException("Otomasyon adı ve 1–100 komut adımı gereklidir.");
        if (Shortcut.Contains('\n') || Steps.Any(step => step.Value.Contains(';') || step.Value.Contains('\n') || step.Kind.Contains(' ')))
            throw new FormatException("Komut alanında yeni satır veya ; kullanılamaz.");
        return $"{Shortcut} = {(Repeat ? "toggle" : "once")}|{Interval}|" + string.Join("; ", Steps.Select(step =>
            step.Kind == "wait" ? $"wait {step.Delay}" : $"{step.Kind} {step.Value}"));
    }

    internal AutomationCommand Compile() => AutomationCommands.Parse(ToLine()).Single();

    internal static AutomationRule FromLegacy(AutomationCommand command) => new()
    {
        Name = command.Steps.Any(step => step.Kind == "search") ? "Seçili metni Google’da ara" : "Otomatik tıklama / komut",
        Shortcut = FormatShortcut(command.Shortcut), Repeat = command.Repeat, Interval = command.Interval,
        Steps = command.Steps.Select(step => step.Kind == "key" ? step with
        { Value = KeyInterop.KeyFromVirtualKey(int.Parse(step.Value)).ToString() } : step).ToList()
    };

    private static string FormatShortcut(AutomationShortcut shortcut) => string.Join("+", new[]
    {
        (shortcut.Modifiers & WinForge.Core.HotkeyModifiers.Control) != 0 ? "Ctrl" : null,
        (shortcut.Modifiers & WinForge.Core.HotkeyModifiers.Alt) != 0 ? "Alt" : null,
        (shortcut.Modifiers & WinForge.Core.HotkeyModifiers.Shift) != 0 ? "Shift" : null,
        (shortcut.Modifiers & WinForge.Core.HotkeyModifiers.Win) != 0 ? "Win" : null,
        KeyInterop.KeyFromVirtualKey(shortcut.Key).ToString()
    }.Where(part => part is not null));
}
