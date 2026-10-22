namespace WinForge.Core;

public enum SettingKind
{
    Toggle,
    Number,
    Text,
    Choice,
    Action
}

public sealed class SettingDescriptor
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public SettingKind Kind { get; init; } = SettingKind.Toggle;
    public object? Default { get; init; }
    public string? Description { get; init; }
    public string? Section { get; init; }
    public int Min { get; init; }
    public int Max { get; init; } = 100;
    public string[] Options { get; init; } = Array.Empty<string>();
    public Action? Execute { get; init; }
}

public interface IModuleSettings
{
    bool GetBool(string key, bool fallback = false);
    int GetInt(string key, int fallback = 0);
    string GetString(string key, string fallback = "");
    void Set(string key, object? value);
}

public interface IHotkeyService
{
    IDisposable Register(uint modifiers, uint virtualKey, Action callback);
}

public interface ILogger
{
    string Path { get; }
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public interface IModuleContext
{
    IModuleSettings Settings { get; }
    ILogger Log { get; }
    IHotkeyService Hotkeys { get; }
    void Notify(string message);
}

public interface ITweakModule
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    string Description { get; }
    string? Warning { get; }
    IReadOnlyList<SettingDescriptor> Settings { get; }

    bool IsActive { get; }

    void Initialize(IModuleContext context);
    void Activate();
    void Deactivate();

    /// <summary>Settings changed; re-read them and apply live.</summary>
    void Apply();
}

public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;
    public const uint NoRepeat = 0x4000;
}
