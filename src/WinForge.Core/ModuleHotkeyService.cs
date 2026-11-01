namespace WinForge.Core;

/// <summary>Keep the module and optional rule name attached to each registration.</summary>
public sealed class ModuleHotkeyService(IHotkeyService hotkeys, string moduleName) : IHotkeyService
{
    public IDisposable Register(uint modifiers, uint virtualKey, Action callback) =>
        hotkeys.Register(modifiers, virtualKey, callback, moduleName);

    public IDisposable Register(uint modifiers, uint virtualKey, Action callback, string owner) =>
        hotkeys.Register(modifiers, virtualKey, callback, $"{moduleName} → {owner}");
}
