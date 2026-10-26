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
    public IReadOnlyDictionary<string, string> OptionLabels { get; init; } = new Dictionary<string, string>();
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

/// <summary>A searchable action contributed by an active module.</summary>
public sealed record QuickAction(string Id, string Name, string SearchTerms, Action Execute);

/// <summary>Contributes lightweight searchable actions to the quick launcher.</summary>
public interface IQuickActionProvider
{
    IEnumerable<QuickAction> GetQuickActions(string query);
}

/// <summary>Available only to modules hosted by an application that supports quick actions.</summary>
public interface IQuickActionContext
{
    IQuickActionService QuickActions { get; }
}

public interface IQuickActionService
{
    IReadOnlyList<QuickAction> Search(string query);
}

public interface IWindowPlacementContext : IModuleContext
{
    bool TryPlaceWindowInZone(IntPtr window, POINT point);
}

public interface IWindowZoneProvider
{
    bool TryPlaceWindowInZone(IntPtr window, POINT point);
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

/// <summary>Handles a small, validated command forwarded to the running application.</summary>
public interface IAppCommandModule
{
    bool HandleCommand(string command, IReadOnlyList<string> arguments);
}

public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;
    public const uint NoRepeat = 0x4000;
}
