namespace WinForge.Core;

public enum SettingKind
{
    Toggle,
    Number,
    Text,
    Choice,
    Palette,
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
    IDisposable Register(uint modifiers, uint virtualKey, Action callback, string owner) =>
        Register(modifiers, virtualKey, callback);
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

public sealed record FeatureUiWindowOptions(
    double Width,
    double Height,
    bool Topmost = false,
    bool ActivateOnShow = true,
    bool ShowInTaskbar = false,
    bool Transparent = false,
    bool ClickThrough = false,
    bool Resizable = false,
    double? Left = null,
    double? Top = null,
    bool ShowOnOpen = true);

/// <summary>Controls a React-rendered feature window hosted by the desktop application.</summary>
public interface IFeatureUiWindow : IDisposable
{
    bool IsVisible { get; }
    event Action<string, System.Text.Json.JsonElement>? MessageReceived;
    event Action? ClosedByHost;
    void Show();
    void Hide();
    void Close();
    void SetBounds(double left, double top, double width, double height);
    void SetState(object state);
}

/// <summary>Creates feature windows using the application's shared React UI runtime.</summary>
public interface IFeatureUiService
{
    IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options);
    IFeatureUiSurface CreateSurface(string view);
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel");
    Task ShowMessageAsync(string title, string message, string closeLabel = "Close");
}

/// <summary>Embeds a React-rendered feature view inside a native window owned by a module.</summary>
public interface IFeatureUiSurface : IDisposable
{
    System.Windows.FrameworkElement Element { get; }
    event Action<string, System.Text.Json.JsonElement>? MessageReceived;
    void SetState(object state);
}

/// <summary>Allows trusted feature views to load a specific local file through a WebView2 virtual host.</summary>
public interface IFeatureUiAssetSurface : IFeatureUiSurface
{
    Uri MapLocalAsset(string filePath);
}

/// <summary>Available to modules hosted by an application with the shared React UI runtime.</summary>
public interface IFeatureUiContext
{
    IFeatureUiService? FeatureUi { get; }
}

/// <summary>Shows a transient brightness indicator on the display being adjusted.</summary>
public sealed record BrightnessOverlayMonitor(string Id, string Name, int Percent);

public interface IDisplayBrightnessOverlayContext
{
    void ShowDisplayBrightness(int percent, string deviceName, Action<int>? onBrightnessChanged = null, string? status = null, IReadOnlyList<BrightnessOverlayMonitor>? monitors = null, Action<string, int>? onMonitorBrightnessChanged = null);
}

/// <summary>Shows a transient audio volume indicator.</summary>
public interface IAudioVolumeOverlayContext
{
    void ShowAudioVolume(int percent, bool isMuted, Action? onVolumeChanged = null, int dismissAfterSeconds = 3);
}

/// <summary>A searchable action contributed by an active module.</summary>
public sealed record QuickAction(string Id, string Name, string SearchTerms, Action Execute);

/// <summary>Contributes lightweight searchable module actions.</summary>
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
    void BeginWindowZoneDrag(IntPtr window);
    bool EndWindowZoneDrag(IntPtr window, bool canceled);
}

public interface IWindowZoneProvider
{
    bool TryPlaceWindowInZone(IntPtr window, POINT point);
}

public interface IWindowZoneDragProvider : IWindowZoneProvider
{
    void BeginWindowZoneDrag(IntPtr window);
    bool EndWindowZoneDrag(IntPtr window, bool canceled);
}

/// <summary>A lifecycle component owned by a combined module, excluded from module discovery.</summary>
public interface IModuleComponent : ITweakModule { }

public interface ITweakModule
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    string Description { get; }
    string? Warning { get; }
    IReadOnlyList<SettingDescriptor> Settings { get; }

    bool IsActive { get; }
    bool IsPersistent => false;

    void Initialize(IModuleContext context);
    void Activate();
    void Deactivate();

    /// <summary>Settings changed; re-read them and apply live.</summary>
    void Apply();
}

/// <summary>Prepares durable data before a setting value is changed.</summary>
public interface ISettingChangePreparation
{
    void PrepareSettingChange(string key, object previous, object next);
}

/// <summary>Provides an interactive panel inside the main module settings surface.</summary>
public interface IModulePanel
{
    object GetPanelState();
    void HandlePanelCommand(string action, System.Text.Json.JsonElement payload);
    event Action? PanelStateChanged;
}

/// <summary>Modules with potentially slow lifecycle operations can keep the UI dispatcher responsive.</summary>
public interface IAsyncTweakModule
{
    Task ActivateAsync();
    Task DeactivateAsync();
    Task ApplyAsync();
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
