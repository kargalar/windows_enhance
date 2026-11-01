using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Shares one settings surface while retaining independently selectable feature groups.</summary>
public abstract class CombinedModule : ITweakModule, IAsyncTweakModule
{
    protected sealed record Part(string Key, string Label, ITweakModule Module, bool DefaultEnabled = true, string? EnabledLabel = null);
    private readonly Part[] _parts;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IModuleContext? _context;

    protected CombinedModule(params Part[] parts)
    {
        _parts = parts;
        Settings = parts.SelectMany(part => new[]
        {
            new SettingDescriptor { Key = part.Key, Label = part.EnabledLabel ?? part.Label + " etkin", Default = part.DefaultEnabled, Section = part.Label }
        }.Concat(part.Module.Settings.Select(setting => new SettingDescriptor
        {
            Key = setting.Key, Label = setting.Label, Kind = setting.Kind, Default = setting.Default,
            Description = setting.Description, Section = setting.Section is null ? part.Label : part.Label + " · " + setting.Section,
            Min = setting.Min, Max = setting.Max, Options = setting.Options, OptionLabels = setting.OptionLabels, Execute = setting.Execute
        }))).ToArray();
    }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Category { get; }
    public abstract string Description { get; }
    public string? Warning => string.Join(" ", _parts.Where(part => part.Module.IsActive)
        .Select(part => part.Module.Warning).Where(warning => !string.IsNullOrWhiteSpace(warning))) is { Length: > 0 } warning ? warning : null;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public void Initialize(IModuleContext context)
    {
        _context = context;
        foreach (var part in _parts) part.Module.Initialize(context);
    }

    public void Activate() => ChangeAsync(true, false).GetAwaiter().GetResult();
    public void Apply() => ChangeAsync(false, false).GetAwaiter().GetResult();
    public void Deactivate() => StopAsync(false).GetAwaiter().GetResult();
    public Task ActivateAsync() => ChangeAsync(true, true);
    public Task ApplyAsync() => ChangeAsync(false, true);
    public Task DeactivateAsync() => StopAsync(true);

    private async Task ChangeAsync(bool activating, bool asynchronous)
    {
        await _gate.WaitAsync();
        try
        {
            if (activating ? IsActive : !IsActive) return;
            try
            {
                foreach (var part in _parts)
                {
                    var module = part.Module;
                    if (!_context!.Settings.GetBool(part.Key, part.DefaultEnabled))
                    {
                        if (module.IsActive) await StopPartAsync(module, asynchronous);
                        continue;
                    }
                    if (asynchronous && module is IAsyncTweakModule asyncModule)
                    {
                        if (module.IsActive) await asyncModule.ApplyAsync();
                        else await asyncModule.ActivateAsync();
                    }
                    else if (module.IsActive) module.Apply();
                    else module.Activate();
                }
                IsActive = true;
            }
            catch
            {
                // Also clean a component which failed before it could mark itself active.
                await StopPartsAsync(asynchronous);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task StopAsync(bool asynchronous)
    {
        await _gate.WaitAsync();
        try { await StopPartsAsync(asynchronous); }
        finally { _gate.Release(); }
    }

    private async Task StopPartsAsync(bool asynchronous)
    {
        IsActive = false;
        List<Exception> errors = [];
        foreach (var part in _parts.Reverse())
        {
            try { await StopPartAsync(part.Module, asynchronous); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count > 0) throw new AggregateException(errors);
    }

    private static Task StopPartAsync(ITweakModule module, bool asynchronous)
    {
        if (asynchronous && module is IAsyncTweakModule asyncModule) return asyncModule.DeactivateAsync();
        module.Deactivate();
        return Task.CompletedTask;
    }
}
