using WinForge.Core;

namespace WinForge.Modules.Taskbar;

/// <summary>One lifecycle and settings panel for taskbar input, styles and tray icons.</summary>
public sealed class TaskbarModule : ITweakModule, IAsyncTweakModule
{
    private readonly TaskbarBehaviorEngine _behavior = new();
    private readonly ShellCustomizationModule _design = new();
    private readonly TrayIconsModule _tray = new();
    private IModuleSettings? _settings;

    public string Id => "taskbar";
    public string Name => "Görev Çubuğu";
    public string Category => "Taskbar";
    public string Description => "Başlat ve sistem simgeleri, dock animasyonu, tasarım, saat, fare kısayolları ve uygulama tepsisi tek panelde.";
    public string? Warning => _design.Warning;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings =>
        [.. ComponentSettings, .. _behavior.Settings, .. _design.Settings, .. TaskbarSystemIcons.Settings, .. TaskbarDockSettings.Settings, .. _tray.Settings];

    private static readonly SettingDescriptor[] ComponentSettings =
    [
        new() { Key = "behavior-enabled", Label = "Fare ve Windows görev çubuğu ayarları", Section = "Bileşenler", Default = true },
        new() { Key = "design-enabled", Label = "Tasarım, sistem simgeleri ve dock animasyonu", Section = "Bileşenler", Default = true },
        new() { Key = "tray-enabled", Label = "Uygulama tepsisi seçimleri", Section = "Bileşenler", Default = true }
    ];

    public void Initialize(IModuleContext context)
    {
        _settings = context.Settings;
        if (_settings.GetString("__taskbar-unified").Length == 0) _settings.Set("__taskbar-unified", "1");
        _behavior.Initialize(context);
        _design.Initialize(context);
        _tray.Initialize(context);
    }

    public void Activate() => ActivateCoreAsync(false).GetAwaiter().GetResult();
    public Task ActivateAsync() => ActivateCoreAsync(true);

    private async Task ActivateCoreAsync(bool asynchronous)
    {
        if (IsActive) return;
        try
        {
            await UpdateComponentsAsync(asynchronous);
            IsActive = true;
        }
        catch
        {
            if (asynchronous) await DeactivateAsync();
            else Deactivate();
            throw;
        }
    }

    public void Apply()
    {
        if (!IsActive) return;
        UpdateComponentsAsync(false).GetAwaiter().GetResult();
    }

    public async Task ApplyAsync()
    {
        if (!IsActive) return;
        await UpdateComponentsAsync(true);
    }

    private async Task UpdateComponentsAsync(bool asynchronous)
    {
        foreach (var (component, key) in new (ITweakModule, string)[]
                 { (_behavior, "behavior-enabled"), (_tray, "tray-enabled"), (_design, "design-enabled") })
        {
            var enabled = _settings!.GetBool(key, true);
            if (asynchronous && component is IAsyncTweakModule asyncComponent)
            {
                if (!enabled) { if (component.IsActive) await asyncComponent.DeactivateAsync(); }
                else if (component.IsActive) await asyncComponent.ApplyAsync();
                else await asyncComponent.ActivateAsync();
            }
            else if (!enabled) { if (component.IsActive) component.Deactivate(); }
            else if (component.IsActive) component.Apply();
            else component.Activate();
        }
    }

    public void Deactivate()
    {
        try { _design.Deactivate(); }
        finally
        {
            try { _tray.Deactivate(); }
            finally
            {
                try { _behavior.Deactivate(); }
                finally { IsActive = false; }
            }
        }
    }

    public async Task DeactivateAsync()
    {
        try { await _design.DeactivateAsync(); }
        finally
        {
            try { _tray.Deactivate(); }
            finally
            {
                try { await _behavior.DeactivateAsync(); }
                finally { IsActive = false; }
            }
        }
    }
}
