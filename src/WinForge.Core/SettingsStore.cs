using System.Globalization;
using System.Text.Json;

namespace WinForge.Core;

public sealed record SettingsPersistenceState(long RequestedVersion, long SavedVersion, bool Pending, string? Error, string? Recovery);

public sealed class SettingsStore : IDisposable
{
    private sealed class Persisted
    {
        public int SchemaVersion { get; set; } = 1;
        public List<string> Enabled { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> Values { get; set; } = new();
    }

    private readonly object _gate = new();
    private readonly object _writerGate = new();
    private readonly Timer _saveTimer;
    private Persisted? _pending;
    private long _pendingVersion;
    private long _requestedVersion;
    private long _savedVersion;
    private string? _saveError;
    private string? _recovery;
    private string? _blockedReason;
    private bool _disposed;
    private int _batchDepth;
    private TaskCompletionSource? _batchFinished;
    private readonly Action<byte[]>? _writeOverride;
    internal object SyncRoot => _gate;
    public bool DeferWrites { get; set; }
    public event Action<SettingsPersistenceState>? PersistenceChanged;
    public SettingsPersistenceState Persistence
    {
        get { lock (_gate) return new(_requestedVersion, _savedVersion, _savedVersion < _requestedVersion, _saveError, _recovery); }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }
    public HashSet<string> Enabled { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SettingsStore(string filePath) : this(filePath, null) { }

    internal SettingsStore(string filePath, Action<byte[]>? writeOverride)
    {
        FilePath = filePath;
        _writeOverride = writeOverride;
        _saveTimer = new Timer(_ => { try { Flush(); } catch { /* Failure is retained and published by Flush. */ } }, null, Timeout.Infinite, Timeout.Infinite);
        Load();
    }

    public Dictionary<string, string> For(string moduleId)
    {
        lock (_gate)
        {
            if (!Values.TryGetValue(moduleId, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Values[moduleId] = values;
            }

            return values;
        }
    }

    private void Load()
    {
        Persisted? data = null;
        try
        {
            if (File.Exists(FilePath)) data = ReadPersisted(FilePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _recovery = "Ayar dosyası okunamadı: " + error.Message;
            if (error is NotSupportedException) _blockedReason = _recovery;
            else
            {
                try { PreserveCorruptFile(); }
                catch (Exception preserveError) { _recovery += " Bozuk ayar dosyası henüz korunamadı: " + preserveError.Message; }
            }
        }
        if (data is null && File.Exists(FilePath + ".bak"))
        {
            try { data = ReadPersisted(FilePath + ".bak"); _recovery = "Son sağlam ayar yedeği yüklendi. " + _recovery; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            { _recovery = "Ayar yedeği de okunamadı: " + error.Message + " " + _recovery; }
        }
        if (data is null) return;

        foreach (var id in data.Enabled ?? [])
            if (!string.IsNullOrWhiteSpace(id)) Enabled.Add(id);
        foreach (var (id, values) in data.Values ?? [])
        {
            if (values is null) continue;
            var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in values) target[key] = value ?? "";
            Values[id] = target;
        }

        MigrateWindowControl();
        MigrateTaskbar();
        MigrateCombinedModules();
        Enabled.Remove("peek-preview");
        Values.Remove("peek-preview");
        Enabled.Remove("keyboard-manager");
        Values.Remove("keyboard-manager");
    }

    private static Persisted ReadPersisted(string path)
    {
        var data = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(path), JsonOptions)
            ?? throw new JsonException("Ayar dosyası boş bir JSON değeri içeriyor.");
        if (data.SchemaVersion is < 0 or > 1) throw new NotSupportedException("Bu ayar şeması desteklenmiyor; dosya korunuyor.");
        return data;
    }

    private void PreserveCorruptFile() => File.Copy(FilePath,
        FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N"), false);

    public bool IsModuleEnabled(string id) { lock (_gate) return Enabled.Contains(id); }
    public bool SetModuleEnabled(string id, bool enabled)
    {
        lock (_gate) return enabled ? Enabled.Add(id) : Enabled.Remove(id);
    }

    private void MigrateWindowControl()
    {
        const string legacyModule = "altsnap";
        const string currentModule = "window-control";
        if (Enabled.Remove(legacyModule)) Enabled.Add(currentModule);
        if (Values.Remove(legacyModule, out var legacy))
        {
            var current = For(currentModule);
            foreach (var (key, value) in legacy)
                if (!key.StartsWith("__native-", StringComparison.OrdinalIgnoreCase)) current.TryAdd(key, value);
        }
        Enabled.Remove("windhawk-taskbar");
        Values.Remove("windhawk-taskbar");
    }

    private void MigrateTaskbar()
    {
        const string schemaKey = "__taskbar-unified";
        if (Values.TryGetValue("taskbar", out var existing) && existing.ContainsKey(schemaKey)) return;
        var hasLegacySettings = Values.ContainsKey("taskbar") || Values.ContainsKey("shell-customization") || Values.ContainsKey("tray-icons") ||
            Enabled.Overlaps(["taskbar", "shell-customization", "tray-icons"]);
        if (!hasLegacySettings) return;
        var taskbar = For("taskbar");
        taskbar.TryAdd("behavior-enabled", Enabled.Contains("taskbar") ? "true" : "false");
        taskbar.TryAdd("design-enabled", Enabled.Contains("shell-customization") ? "true" : "false");
        taskbar.TryAdd("tray-enabled", Enabled.Contains("tray-icons") ? "true" : "false");
        foreach (var legacyId in new[] { "shell-customization", "tray-icons" })
        {
            if (Enabled.Remove(legacyId)) Enabled.Add("taskbar");
            if (!Values.Remove(legacyId, out var legacy)) continue;
            var current = For("taskbar");
            foreach (var (key, value) in legacy)
            {
                var targetKey = legacyId == "shell-customization" && key == "icon-size" ? "app-icon-size" : key;
                current.TryAdd(targetKey, value);
            }
        }
        taskbar[schemaKey] = "1";
    }

    private void MigrateCombinedModules()
    {
        Merge("start-menu", [("start-appearance", "appearance-enabled"), ("start-tweaks", "cleanup-enabled"), ("search-tweaks", "search-enabled")]);
        Merge("mouse", [("mouse-tweaks", "tweaks-enabled"), ("mouse-spotlight", "spotlight-enabled")]);
        Enabled.Remove("mouse-jump");
        Values.Remove("mouse-jump");
    }

    private void Merge(string targetId, (string Id, string Toggle)[] sources)
    {
        if (!sources.Any(source => Values.ContainsKey(source.Id) || Enabled.Contains(source.Id))) return;
        var current = For(targetId);
        var alreadyUnified = current.ContainsKey("__combined");
        foreach (var (id, toggle) in sources)
        {
            var enabled = Enabled.Remove(id);
            if (!alreadyUnified)
            {
                current.TryAdd(toggle, enabled ? "true" : "false");
                if (enabled) Enabled.Add(targetId);
            }
            if (!Values.Remove(id, out var legacy)) continue;
            foreach (var (key, value) in legacy) current.TryAdd(key, value);
        }
        current["__combined"] = "1";
    }

    public void Save()
    {
        QueueSnapshot();
        Flush();
    }

    public void ScheduleSave()
    {
        QueueSnapshot();
        lock (_gate) { if (_batchDepth > 0) return; }
        if (DeferWrites) _saveTimer.Change(150, Timeout.Infinite);
        else Flush();
    }

    internal IDisposable BatchWrites()
    {
        lock (_writerGate)
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_batchDepth++ == 0) _batchFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return new WriteBatch(this);
        }
    }

    private sealed class WriteBatch(SettingsStore store) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (store._gate)
            {
                if (--store._batchDepth != 0) return;
                store._batchFinished!.TrySetResult();
                if (!store._disposed && store.DeferWrites && store._pending is not null)
                    store._saveTimer.Change(150, Timeout.Infinite);
            }
        }
    }

    private void QueueSnapshot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pending = new Persisted { Enabled = Enabled.ToList(), Values = Values.ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value)) };
            _pendingVersion = ++_requestedVersion;
        }
    }

    public async Task FlushAsync()
    {
        while (true)
        {
            Task? transaction;
            lock (_gate) transaction = _batchDepth > 0 ? _batchFinished!.Task : null;
            if (transaction is not null) await transaction.ConfigureAwait(false);
            try { await Task.Run(Flush).ConfigureAwait(false); return; }
            catch (WriteInProgressException) { /* A new transaction started before the worker acquired the writer gate. */ }
        }
    }

    public void Flush()
    {
        lock (_writerGate)
        {
            lock (_gate) { if (_batchDepth > 0) throw new WriteInProgressException(); }
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            while (true)
            {
                Persisted? data;
                long version;
                lock (_gate) { data = _pending; version = _pendingVersion; _pending = null; }
                if (data is null) return;
                try
                {
                    using var timing = PerformanceTrace.Measure("settings.persist");
                    if (_blockedReason is { } blocked) throw new IOException(blocked);
                    WriteSnapshot(data);
                    lock (_gate) { _savedVersion = version; _saveError = null; }
                    PublishPersistence();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
                {
                    lock (_gate)
                    {
                        _saveError = error.Message;
                        if (_pending is null) { _pending = data; _pendingVersion = version; }
                    }
                    PublishPersistence();
                    throw new IOException("Ayarlar kaydedilemedi: " + error.Message, error);
                }
            }
        }
    }

    private void WriteSnapshot(Persisted data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        if (_writeOverride is not null) { _writeOverride(bytes); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            var trusted = false;
            if (File.Exists(FilePath))
            {
                try { _ = ReadPersisted(FilePath); trusted = true; }
                catch (JsonException) { PreserveCorruptFile(); }
            }
            if (trusted) File.Replace(tmp, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
            else File.Move(tmp, FilePath, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    private sealed class WriteInProgressException() : InvalidOperationException("Ayar uygulaması tamamlanmadan kayıt yapılamaz.");

    private void PublishPersistence()
    {
        try { PersistenceChanged?.Invoke(Persistence); } catch { /* UI observers cannot break durable writes. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { Save(); }
        finally { lock (_gate) _disposed = true; _saveTimer.Dispose(); }
    }
}

internal sealed class ModuleSettings : IModuleSettings
{
    private readonly Dictionary<string, string> _values;
    private readonly Action _save;
    private readonly object _gate;

    public ModuleSettings(Dictionary<string, string> values, Action save, object? gate = null)
    {
        _values = values;
        _save = save;
        _gate = gate ?? new object();
    }

    public bool GetBool(string key, bool fallback = false)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
            if (bool.TryParse(raw, out var parsed)) return parsed;
            if (raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw == "0" || raw.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }
    }

    public int GetInt(string key, int fallback = 0)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out var raw)) return fallback;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        }
    }

    public string GetString(string key, string fallback = "")
    {
        lock (_gate) return _values.TryGetValue(key, out var raw) ? raw : fallback;
    }

    public void Set(string key, object? value)
    {
        lock (_gate) _values[key] = value switch
        {
            null => "",
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        _save();
    }
}
