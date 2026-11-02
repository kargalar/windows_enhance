using System.Reflection;

namespace WinForge.Core;

public sealed class ModuleHost
{
    private readonly SettingsStore _store;
    private readonly FileLogger _log;
    private readonly IHotkeyService _hotkeys;
    private readonly Action<string> _notify;
    private readonly Action<int, string, Action<int>?, string?>? _showDisplayBrightness;
    private readonly Action<int, bool, Action?, int>? _showAudioVolume;
    private readonly string _modulesDirectory;
    private readonly List<ITweakModule> _modules = new();
    private readonly Dictionary<string, IModuleSettings> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
    private readonly IQuickActionService _quickActions;
    private readonly IFeatureUiService? _featureUi;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _stopping;

    public ModuleHost(SettingsStore store, FileLogger log, IHotkeyService hotkeys, string modulesDirectory, Action<string> notify,
        Action<int, string, Action<int>?, string?>? showDisplayBrightness = null, Action<int, bool, Action?, int>? showAudioVolume = null,
        IFeatureUiService? featureUi = null)
    {
        _store = store;
        _log = log;
        _hotkeys = hotkeys;
        _modulesDirectory = modulesDirectory;
        _notify = notify;
        _showDisplayBrightness = showDisplayBrightness;
        _showAudioVolume = showAudioVolume;
        _featureUi = featureUi;
        _quickActions = new QuickActionService(() => _modules, _log, _notify);
    }

    public IReadOnlyList<ITweakModule> Modules => _modules;

    public string? ErrorOf(ITweakModule module) =>
        _errors.TryGetValue(module.Id, out var error) ? error : null;

    public IModuleSettings SettingsOf(ITweakModule module) => _settings[module.Id];

    public void Load()
    {
        using var loadTiming = PerformanceTrace.Measure("modules.load");
        _modules.Clear();
        _settings.Clear();
        _errors.Clear();

        if (!Directory.Exists(_modulesDirectory))
        {
            _log.Error($"Modül klasörü bulunamadı: {_modulesDirectory}");
            return;
        }

        foreach (var file in Directory.GetFiles(_modulesDirectory, "WinForge.Modules.*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(file);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(ITweakModule).IsAssignableFrom(type)) continue;
                    if (typeof(IModuleComponent).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) is null) continue;

                    using var moduleTiming = PerformanceTrace.Measure("module.initialize", type.Name);
                    var module = (ITweakModule)Activator.CreateInstance(type)!;
                    var settings = new ModuleSettings(_store.For(module.Id), _store.ScheduleSave, _store.SyncRoot);
                    _settings[module.Id] = settings;
                    module.Initialize(new ModuleContext(settings, _log, new ModuleHotkeyService(_hotkeys, module.Name), _notify, _quickActions, _showDisplayBrightness, _showAudioVolume, _featureUi,
                        TryPlaceWindowInZone, BeginWindowZoneDrag, EndWindowZoneDrag));
                    _modules.Add(module);
                    _log.Info($"Modül yüklendi: {module.Id} ({module.Name})");
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Modül dosyası yüklenemedi: {file}", ex);
            }
        }

        _modules.Sort((a, b) =>
        {
            var category = string.Compare(a.Category, b.Category, StringComparison.CurrentCultureIgnoreCase);
            return category != 0 ? category : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
    }

    public void StartEnabled()
    {
        foreach (var module in _modules)
        {
            if (!_store.IsModuleEnabled(module.Id)) continue;

            try
            {
                module.Activate();
                _errors.Remove(module.Id);
                _log.Info($"Modül etkinleştirildi: {module.Id}");
            }
            catch (Exception ex)
            {
                _errors[module.Id] = ex.Message;
                _log.Error($"Modül etkinleştirilemedi: {module.Id}", ex);
                CleanupFailedActivation(module);
            }
        }
    }

    public async Task StartEnabledAsync()
    {
        foreach (var module in _modules)
        {
            await _lifecycleGate.WaitAsync();
            try
            {
                if (_stopping) return;
                if (!_store.IsModuleEnabled(module.Id) || module.IsActive) continue;
                try
                {
                    _log.Info($"Modül başlatılıyor: {module.Id}");
                    using var timing = PerformanceTrace.Measure("module.activate", module.Id);
                    if (module is IAsyncTweakModule asyncModule) await asyncModule.ActivateAsync();
                    else module.Activate();
                    _errors.Remove(module.Id);
                    _log.Info($"Modül etkinleştirildi: {module.Id}");
                }
                catch (Exception ex)
                {
                    _errors[module.Id] = ex.Message;
                    _log.Error($"Modül etkinleştirilemedi: {module.Id}", ex);
                    if (module is IAsyncTweakModule asyncModule)
                    {
                        try { await asyncModule.DeactivateAsync(); }
                        catch (Exception cleanup) { _log.Error($"Geri alma başarısız: {module.Id}", cleanup); }
                    }
                    else CleanupFailedActivation(module);
                }
            }
            finally { _lifecycleGate.Release(); }
            await Task.Yield();
        }
    }

    public async Task SetEnabledAsync(ITweakModule module, bool enabled)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (!enabled && !module.IsActive && _store.SetModuleEnabled(module.Id, false))
            {
                _store.Save();
                return;
            }
            if (module is not IAsyncTweakModule asyncModule) { SetEnabled(module, enabled); return; }
            if (enabled == module.IsActive) return;
            try
            {
                if (enabled) await asyncModule.ActivateAsync();
                else await asyncModule.DeactivateAsync();
                if (module.IsActive) _store.SetModuleEnabled(module.Id, true); else _store.SetModuleEnabled(module.Id, false);
                _errors.Remove(module.Id);
                _store.Save();
            }
            catch (Exception ex)
            {
                _errors[module.Id] = ex.Message;
                _log.Error($"Modül değiştirilemedi: {module.Id}", ex);
                if (enabled) { try { await asyncModule.DeactivateAsync(); } catch (Exception cleanup) { _log.Error($"Geri alma başarısız: {module.Id}", cleanup); } }
                if (module.IsActive) _store.SetModuleEnabled(module.Id, true); else _store.SetModuleEnabled(module.Id, false);
                _store.Save();
                throw;
            }
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task UpdateSettingAsync(ITweakModule module, SettingDescriptor descriptor, object value)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_stopping) throw new InvalidOperationException("Uygulama kapanıyor.");
            var settings = SettingsOf(module);
            object previous = descriptor.Kind switch
            {
                SettingKind.Toggle => settings.GetBool(descriptor.Key, descriptor.Default is true),
                SettingKind.Number => settings.GetInt(descriptor.Key, Convert.ToInt32(descriptor.Default ?? 0)),
                _ => settings.GetString(descriptor.Key, descriptor.Default as string ?? "")
            };
            async Task ApplyCurrent()
            {
                if (!module.IsActive) return;
                if (module is IAsyncTweakModule asyncModule) await asyncModule.ApplyAsync();
                else module.Apply();
            }
            IDisposable? batch = _store.BatchWrites();
            try
            {
                if (module is ISettingChangePreparation preparation)
                    preparation.PrepareSettingChange(descriptor.Key, previous, value);
                settings.Set(descriptor.Key, value);
                await ApplyCurrent();
                batch.Dispose();
                batch = null;
                await _store.FlushAsync();
                _errors.Remove(module.Id);
            }
            catch (Exception error)
            {
                // The lifecycle gate covers capture, apply and rollback. A later
                // UI edit cannot be overwritten by this operation's failure.
                _errors[module.Id] = error.Message;
                var restored = true;
                try
                {
                    batch ??= _store.BatchWrites();
                    settings.Set(descriptor.Key, previous);
                    await ApplyCurrent();
                    batch.Dispose();
                    batch = null;
                    await _store.FlushAsync();
                }
                catch (Exception restore) { restored = false; _log.Error($"Ayar geri alma başarısız: {module.Id}", restore); }
                throw new InvalidOperationException((restored ? "Ayar uygulanamadı; önceki değer geri yüklendi. " : "Ayar uygulanamadı ve geri alma tamamlanamadı; günlükleri kontrol edin. ") + error.Message, error);
            }
            finally { batch?.Dispose(); }
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task ApplyAsync(ITweakModule module)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (module is not IAsyncTweakModule asyncModule) { Apply(module); return; }
            try { if (module.IsActive) await asyncModule.ApplyAsync(); _errors.Remove(module.Id); }
            catch (Exception ex) { _errors[module.Id] = ex.Message; _log.Error($"Ayar uygulanamadı: {module.Id}", ex); }
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task StopAllAsync()
    {
        _stopping = true;
        await _lifecycleGate.WaitAsync();
        try
        {
            foreach (var module in _modules)
            {
                if (!module.IsActive || module.IsPersistent) continue;
                try
                {
                    if (module is IAsyncTweakModule asyncModule) await asyncModule.DeactivateAsync();
                    else module.Deactivate();
                }
                catch (Exception ex) { _log.Error($"Modül kapatılamadı: {module.Id}", ex); }
                await Task.Yield();
            }
        }
        finally { _lifecycleGate.Release(); }
    }

    public void SetEnabled(ITweakModule module, bool enabled)
    {
        if (enabled == module.IsActive) return;

        try
        {
            if (enabled)
            {
                module.Activate();
                _store.SetModuleEnabled(module.Id, true);
                _log.Info($"Etkin: {module.Id}");
            }
            else
            {
                module.Deactivate();
                _store.SetModuleEnabled(module.Id, false);
                _log.Info($"Devre dışı: {module.Id}");
            }

            _errors.Remove(module.Id);
            _store.Save();
        }
        catch (Exception ex)
        {
            _errors[module.Id] = ex.Message;
            _log.Error($"Modül değiştirilemedi: {module.Id}", ex);

            if (enabled) CleanupFailedActivation(module);

            if (module.IsActive) _store.SetModuleEnabled(module.Id, true);
            else _store.SetModuleEnabled(module.Id, false);

            _store.Save();
            throw;
        }
    }

    private void CleanupFailedActivation(ITweakModule module)
    {
        // Activation may allocate resources before it sets IsActive.
        try { module.Deactivate(); }
        catch (Exception rollback)
        {
            _log.Error($"Geri alma başarısız: {module.Id}", rollback);
        }
    }

    public void Apply(ITweakModule module)
    {
        try
        {
            if (module.IsActive) module.Apply();
            _errors.Remove(module.Id);
        }
        catch (Exception ex)
        {
            _errors[module.Id] = ex.Message;
            _log.Error($"Ayar uygulanamadı: {module.Id}", ex);
        }
    }

    public void StopAll()
    {
        foreach (var module in _modules)
        {
            if (!module.IsActive || module.IsPersistent) continue;
            try
            {
                module.Deactivate();
            }
            catch (Exception ex)
            {
                _log.Error($"Modül kapatılamadı: {module.Id}", ex);
            }
        }
    }

    private bool TryPlaceWindowInZone(IntPtr window, POINT point)
    {
        foreach (var module in _modules.Where(module => module.IsActive))
            if (module is IWindowZoneProvider provider && provider.TryPlaceWindowInZone(window, point)) return true;
        return false;
    }

    private void BeginWindowZoneDrag(IntPtr window)
    {
        foreach (var module in _modules.Where(module => module.IsActive))
            if (module is IWindowZoneDragProvider provider) provider.BeginWindowZoneDrag(window);
    }

    private bool EndWindowZoneDrag(IntPtr window, bool canceled)
    {
        var handled = false;
        foreach (var module in _modules.Where(module => module.IsActive))
            if (module is IWindowZoneDragProvider provider)
                handled |= provider.EndWindowZoneDrag(window, canceled);
        return handled;
    }

    private sealed class ModuleContext : IModuleContext, IQuickActionContext, IWindowPlacementContext,
        IDisplayBrightnessOverlayContext, IAudioVolumeOverlayContext, IFeatureUiContext
    {
        private readonly Action<string> _notify;
        private readonly Action<int, string, Action<int>?, string?>? _showDisplayBrightness;
        private readonly Action<int, bool, Action?, int>? _showAudioVolume;
        private readonly IFeatureUiService? _featureUi;
        private readonly Func<IntPtr, POINT, bool> _placeWindow;
        private readonly Action<IntPtr> _beginWindowZoneDrag;
        private readonly Func<IntPtr, bool, bool> _endWindowZoneDrag;

        public ModuleContext(IModuleSettings settings, FileLogger log, IHotkeyService hotkeys, Action<string> notify,
            IQuickActionService quickActions, Action<int, string, Action<int>?, string?>? showDisplayBrightness,
            Action<int, bool, Action?, int>? showAudioVolume, IFeatureUiService? featureUi,
            Func<IntPtr, POINT, bool> placeWindow,
            Action<IntPtr> beginWindowZoneDrag, Func<IntPtr, bool, bool> endWindowZoneDrag)
        {
            Settings = settings;
            Log = log;
            Hotkeys = hotkeys;
            QuickActions = quickActions;
            _notify = notify;
            _showDisplayBrightness = showDisplayBrightness;
            _showAudioVolume = showAudioVolume;
            _featureUi = featureUi;
            _placeWindow = placeWindow;
            _beginWindowZoneDrag = beginWindowZoneDrag;
            _endWindowZoneDrag = endWindowZoneDrag;
        }

        public IModuleSettings Settings { get; }
        public ILogger Log { get; }
        public IHotkeyService Hotkeys { get; }
        public IQuickActionService QuickActions { get; }
        public IFeatureUiService? FeatureUi => _featureUi;

        public void Notify(string message) => _notify(message);
        public void ShowDisplayBrightness(int percent, string deviceName, Action<int>? onBrightnessChanged = null, string? status = null)
        {
            if (_showDisplayBrightness is not null) _showDisplayBrightness(percent, deviceName, onBrightnessChanged, status);
            else _notify($"Parlaklık {percent}%");
        }
        public void ShowAudioVolume(int percent, bool isMuted, Action? onVolumeChanged = null, int dismissAfterSeconds = 3)
        {
            if (_showAudioVolume is not null) _showAudioVolume(percent, isMuted, onVolumeChanged, dismissAfterSeconds);
            else _notify(isMuted ? $"Ses kapalı · {percent}%" : $"Ses {percent}%");
        }
        public bool TryPlaceWindowInZone(IntPtr window, POINT point) => _placeWindow(window, point);
        public void BeginWindowZoneDrag(IntPtr window) => _beginWindowZoneDrag(window);
        public bool EndWindowZoneDrag(IntPtr window, bool canceled) => _endWindowZoneDrag(window, canceled);
    }

    private sealed class QuickActionService(Func<IEnumerable<ITweakModule>> modules, ILogger log, Action<string> notify)
        : IQuickActionService
    {
        private const int MaximumActionsPerProvider = 40;

        public IReadOnlyList<QuickAction> Search(string query)
        {
            var actions = new List<QuickAction>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules().Where(module => module.IsActive))
            {
                if (module is not IQuickActionProvider provider) continue;
                var moduleId = module.Id;
                try
                {
                    foreach (var action in provider.GetQuickActions(query).Take(MaximumActionsPerProvider))
                    {
                        if (action is null || string.IsNullOrWhiteSpace(action.Id) || string.IsNullOrWhiteSpace(action.Name) ||
                            action.Execute is null) continue;
                        var key = moduleId + ":" + action.Id.Trim();
                        if (!seen.Add(key)) continue;
                        actions.Add(action with
                        {
                            Name = action.Name.Trim(),
                            SearchTerms = string.IsNullOrWhiteSpace(action.SearchTerms) ? action.Name.Trim() : action.SearchTerms.Trim(),
                            Execute = () => ExecuteAction(moduleId, action)
                        });
                    }
                }
                catch (Exception exception)
                {
                    log.Error($"Hızlı eylem sağlayıcısı başarısız: {moduleId}", exception);
                    notify($"‘{module.Name}’ hızlı eylemleri yüklenemedi.");
                }
            }
            return actions;
        }

        private void ExecuteAction(string moduleId, QuickAction action)
        {
            try { action.Execute(); }
            catch (Exception exception)
            {
                log.Error($"Hızlı eylem çalıştırılamadı: {moduleId}/{action.Id}", exception);
                notify($"‘{action.Name}’ eylemi çalıştırılamadı.");
            }
        }
    }
}
