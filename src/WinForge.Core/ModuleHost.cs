using System.Reflection;

namespace WinForge.Core;

public sealed class ModuleHost
{
    private readonly SettingsStore _store;
    private readonly FileLogger _log;
    private readonly IHotkeyService _hotkeys;
    private readonly Action<string> _notify;
    private readonly Action<int, string>? _showDisplayBrightness;
    private readonly string _modulesDirectory;
    private readonly List<ITweakModule> _modules = new();
    private readonly Dictionary<string, IModuleSettings> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
    private readonly IQuickActionService _quickActions;

    public ModuleHost(SettingsStore store, FileLogger log, IHotkeyService hotkeys, string modulesDirectory, Action<string> notify,
        Action<int, string>? showDisplayBrightness = null)
    {
        _store = store;
        _log = log;
        _hotkeys = hotkeys;
        _modulesDirectory = modulesDirectory;
        _notify = notify;
        _showDisplayBrightness = showDisplayBrightness;
        _quickActions = new QuickActionService(() => _modules, _log, _notify);
    }

    public IReadOnlyList<ITweakModule> Modules => _modules;

    public string? ErrorOf(ITweakModule module) =>
        _errors.TryGetValue(module.Id, out var error) ? error : null;

    public IModuleSettings SettingsOf(ITweakModule module) => _settings[module.Id];

    public void Load()
    {
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
                    if (type.GetConstructor(Type.EmptyTypes) is null) continue;

                    var module = (ITweakModule)Activator.CreateInstance(type)!;
                    var settings = new ModuleSettings(_store.For(module.Id), _store.Save);
                    _settings[module.Id] = settings;
                    module.Initialize(new ModuleContext(settings, _log, _hotkeys, _notify, _quickActions, _showDisplayBrightness,
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
            if (!_store.Enabled.Contains(module.Id)) continue;

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

    public void SetEnabled(ITweakModule module, bool enabled)
    {
        if (enabled == module.IsActive) return;

        try
        {
            if (enabled)
            {
                module.Activate();
                _store.Enabled.Add(module.Id);
                _log.Info($"Etkin: {module.Id}");
            }
            else
            {
                module.Deactivate();
                _store.Enabled.Remove(module.Id);
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

            if (module.IsActive) _store.Enabled.Add(module.Id);
            else _store.Enabled.Remove(module.Id);

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
            if (!module.IsActive) continue;
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

    private sealed class ModuleContext : IModuleContext, IQuickActionContext, IWindowPlacementContext, IDisplayBrightnessOverlayContext
    {
        private readonly Action<string> _notify;
        private readonly Action<int, string>? _showDisplayBrightness;
        private readonly Func<IntPtr, POINT, bool> _placeWindow;
        private readonly Action<IntPtr> _beginWindowZoneDrag;
        private readonly Func<IntPtr, bool, bool> _endWindowZoneDrag;

        public ModuleContext(IModuleSettings settings, FileLogger log, IHotkeyService hotkeys, Action<string> notify,
            IQuickActionService quickActions, Action<int, string>? showDisplayBrightness, Func<IntPtr, POINT, bool> placeWindow,
            Action<IntPtr> beginWindowZoneDrag, Func<IntPtr, bool, bool> endWindowZoneDrag)
        {
            Settings = settings;
            Log = log;
            Hotkeys = hotkeys;
            QuickActions = quickActions;
            _notify = notify;
            _showDisplayBrightness = showDisplayBrightness;
            _placeWindow = placeWindow;
            _beginWindowZoneDrag = beginWindowZoneDrag;
            _endWindowZoneDrag = endWindowZoneDrag;
        }

        public IModuleSettings Settings { get; }
        public ILogger Log { get; }
        public IHotkeyService Hotkeys { get; }
        public IQuickActionService QuickActions { get; }

        public void Notify(string message) => _notify(message);
        public void ShowDisplayBrightness(int percent, string deviceName)
        {
            if (_showDisplayBrightness is not null) _showDisplayBrightness(percent, deviceName);
            else _notify($"Parlaklık {percent}%");
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
