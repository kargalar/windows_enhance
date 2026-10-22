using System.Reflection;

namespace WinForge.Core;

public sealed class ModuleHost
{
    private readonly SettingsStore _store;
    private readonly FileLogger _log;
    private readonly IHotkeyService _hotkeys;
    private readonly Action<string> _notify;
    private readonly string _modulesDirectory;
    private readonly List<ITweakModule> _modules = new();
    private readonly Dictionary<string, IModuleSettings> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);

    public ModuleHost(SettingsStore store, FileLogger log, IHotkeyService hotkeys, string modulesDirectory, Action<string> notify)
    {
        _store = store;
        _log = log;
        _hotkeys = hotkeys;
        _modulesDirectory = modulesDirectory;
        _notify = notify;
    }

    public IReadOnlyList<ITweakModule> Modules => _modules;

    public string? ErrorOf(ITweakModule module) =>
        _errors.TryGetValue(module.Id, out var error) ? error : null;

    public IModuleSettings SettingsOf(ITweakModule module) => _settings[module.Id];

    public void Load()
    {
        _modules.Clear();
        _settings.Clear();

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
                    module.Initialize(new ModuleContext(settings, _log, _hotkeys, _notify));
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

            // Roll back to a consistent state.
            try
            {
                if (module.IsActive)
                {
                    module.Deactivate();
                    _store.Enabled.Remove(module.Id);
                }
            }
            catch (Exception rollback)
            {
                _log.Error($"Geri alma başarısız: {module.Id}", rollback);
            }

            _store.Save();
            throw;
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

    private sealed class ModuleContext : IModuleContext
    {
        private readonly Action<string> _notify;

        public ModuleContext(IModuleSettings settings, FileLogger log, IHotkeyService hotkeys, Action<string> notify)
        {
            Settings = settings;
            Log = log;
            Hotkeys = hotkeys;
            _notify = notify;
        }

        public IModuleSettings Settings { get; }
        public ILogger Log { get; }
        public IHotkeyService Hotkeys { get; }

        public void Notify(string message) => _notify(message);
    }
}
