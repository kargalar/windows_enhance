using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WinForge.Modules.Extras;

internal sealed class UserEnvironmentProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Yeni profil";
    public List<UserEnvironmentVariable> Variables { get; set; } = new();
}

internal sealed class EnvironmentProfileSnapshot
{
    public string Name { get; set; } = "";
    public bool Exists { get; set; }
    public string? Value { get; set; }
    public bool Expandable { get; set; }
}

internal sealed class EnvironmentProfilesDocument
{
    public List<UserEnvironmentProfile> Profiles { get; set; } = new();
    public string? ActiveProfileId { get; set; }
    public List<EnvironmentProfileSnapshot> OriginalValues { get; set; } = new();
    public List<UserEnvironmentVariable> AppliedValues { get; set; } = new();
}

internal sealed class EnvironmentProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _registryPath;
    private readonly string _filePath;
    private readonly Action _notifyEnvironmentChanged;
    private EnvironmentProfilesDocument _document;

    public EnvironmentProfileService(string registryPath, string filePath, Action? notifyEnvironmentChanged = null)
    {
        _registryPath = registryPath;
        _filePath = filePath;
        _notifyEnvironmentChanged = notifyEnvironmentChanged ?? UserEnvironmentVariableStore.BroadcastEnvironmentChange;
        _document = LoadDocument(filePath);
        NormalizeDocument();
    }

    public IReadOnlyList<UserEnvironmentProfile> Profiles => _document.Profiles;
    public string? ActiveProfileId => _document.ActiveProfileId;

    public string CreateProfile(string name)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0) throw new FormatException("Profil adı boş olamaz.");
        if (_document.Profiles.Any(profile => profile.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("Bu profil adı zaten kullanılıyor.");

        var profile = new UserEnvironmentProfile { Name = normalizedName };
        _document.Profiles.Add(profile);
        SaveDocument();
        return profile.Id;
    }

    public void DeleteProfile(string id)
    {
        var profile = FindProfile(id);
        if (_document.ActiveProfileId == id) SetActiveProfile(null);
        _document.Profiles.Remove(profile);
        SaveDocument();
    }

    public void SaveProfile(string id, string name, IReadOnlyCollection<UserEnvironmentVariable> variables)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0) throw new FormatException("Profil adı boş olamaz.");
        if (_document.Profiles.Any(profile => profile.Id != id &&
            profile.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("Bu profil adı zaten kullanılıyor.");

        var validated = UserEnvironmentVariableStore.Validate(variables);
        var profile = FindProfile(id);
        var wasActive = _document.ActiveProfileId == id;
        if (wasActive) SetActiveProfile(null);
        profile.Name = normalizedName;
        profile.Variables = validated.ToList();
        SaveDocument();
        if (wasActive) SetActiveProfile(id);
    }

    public void SetActiveProfile(string? id)
    {
        if (id == _document.ActiveProfileId) return;
        if (id is not null) _ = FindProfile(id);

        RestoreActiveProfile();
        _document.ActiveProfileId = id;
        _document.OriginalValues.Clear();
        _document.AppliedValues.Clear();

        if (id is not null)
        {
            var profile = FindProfile(id);
            var validated = UserEnvironmentVariableStore.Validate(profile.Variables);
            profile.Variables = validated.ToList();
            var originalValues = CaptureValues(validated.Select(entry => entry.Name));
            _document.OriginalValues = originalValues;
            _document.AppliedValues = validated.ToList();
            SaveDocument();
            try
            {
                WriteValues(validated);
                _notifyEnvironmentChanged();
                return;
            }
            catch
            {
                RestoreValues(originalValues);
                _document.ActiveProfileId = null;
                _document.OriginalValues.Clear();
                _document.AppliedValues.Clear();
                SaveDocument();
                throw;
            }
        }

        SaveDocument();
    }

    public void ReapplyActiveProfile()
    {
        if (_document.ActiveProfileId is not { } activeId) return;
        var profile = FindProfile(activeId);
        var validated = UserEnvironmentVariableStore.Validate(profile.Variables);
        profile.Variables = validated.ToList();

        var previousApplied = _document.AppliedValues.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        var originalNames = _document.OriginalValues.Select(entry => entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in validated)
        {
            if (originalNames.Contains(entry.Name)) continue;
            var original = CaptureValues(new[] { entry.Name }).Single();
            _document.OriginalValues.Add(original);
        }

        using var key = Registry.CurrentUser.CreateSubKey(_registryPath, writable: true)
            ?? throw new InvalidOperationException("Kullanıcı ortam değişkenleri anahtarı açılamadı.");
        var removedValues = previousApplied.Values
            .Where(previous => !validated.Any(entry => entry.Name.Equals(previous.Name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var before = CaptureValues(validated.Select(entry => entry.Name).Concat(removedValues.Select(entry => entry.Name)));
        try
        {
            foreach (var previous in removedValues)
            {
                var current = ReadOne(key, previous.Name);
                if (current is null || current.Value != previous.Value || current.Expandable != previous.Expandable) continue;
                if (!originalNames.Contains(previous.Name)) continue;
                var original = _document.OriginalValues.Single(entry => entry.Name.Equals(previous.Name, StringComparison.OrdinalIgnoreCase));
                RestoreOne(key, original);
            }
            foreach (var entry in validated) WriteOne(key, entry);
        }
        catch
        {
            RestoreValues(before);
            throw;
        }

        _document.AppliedValues = validated.ToList();
        SaveDocument();
        _notifyEnvironmentChanged();
    }

    private void RestoreActiveProfile()
    {
        if (_document.ActiveProfileId is null || _document.AppliedValues.Count == 0) return;

        using var key = Registry.CurrentUser.CreateSubKey(_registryPath, writable: true)
            ?? throw new InvalidOperationException("Kullanıcı ortam değişkenleri anahtarı açılamadı.");
        var originals = _document.OriginalValues.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var applied in _document.AppliedValues)
        {
            var current = ReadOne(key, applied.Name);
            if (current is null || current.Value != applied.Value || current.Expandable != applied.Expandable)
                continue;
            if (!originals.TryGetValue(applied.Name, out var original)) continue;

            RestoreOne(key, original);
            changed = true;
        }

        if (changed) _notifyEnvironmentChanged();
    }

    private List<EnvironmentProfileSnapshot> CaptureValues(IEnumerable<string> names)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_registryPath, writable: false);
        var snapshots = new List<EnvironmentProfileSnapshot>();
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (key is not null && key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                var kind = key.GetValueKind(name);
                if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                    throw new InvalidOperationException($"{name} değişkeninin kayıt türü desteklenmiyor; değer değiştirilmedi.");
            }
            var current = key is null ? null : ReadOne(key, name);
            snapshots.Add(current is null
                ? new EnvironmentProfileSnapshot { Name = name }
                : new EnvironmentProfileSnapshot { Name = name, Exists = true, Value = current.Value, Expandable = current.Expandable });
        }
        return snapshots;
    }

    private static UserEnvironmentVariable? ReadOne(RegistryKey key, string name)
    {
        try
        {
            var kind = key.GetValueKind(name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) return null;
            var value = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
            return new UserEnvironmentVariable(name, value, kind == RegistryValueKind.ExpandString);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return null;
        }
    }

    private void WriteValues(IEnumerable<UserEnvironmentVariable> values)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_registryPath, writable: true)
            ?? throw new InvalidOperationException("Kullanıcı ortam değişkenleri anahtarı açılamadı.");
        foreach (var value in values) WriteOne(key, value);
    }

    private void RestoreValues(IEnumerable<EnvironmentProfileSnapshot> values)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_registryPath, writable: true)
            ?? throw new InvalidOperationException("Kullanıcı ortam değişkenleri anahtarı açılamadı.");
        foreach (var value in values) RestoreOne(key, value);
    }

    private static void WriteOne(RegistryKey key, UserEnvironmentVariable value) =>
        key.SetValue(value.Name, value.Value, value.Expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);

    private static void RestoreOne(RegistryKey key, EnvironmentProfileSnapshot original)
    {
        if (!original.Exists) key.DeleteValue(original.Name, throwOnMissingValue: false);
        else key.SetValue(original.Name, original.Value ?? "",
            original.Expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }

    private UserEnvironmentProfile FindProfile(string id) =>
        _document.Profiles.FirstOrDefault(profile => profile.Id == id)
        ?? throw new KeyNotFoundException("Ortam değişkeni profili bulunamadı.");

    private void NormalizeDocument()
    {
        _document.Profiles ??= new List<UserEnvironmentProfile>();
        _document.OriginalValues ??= new List<EnvironmentProfileSnapshot>();
        _document.AppliedValues ??= new List<UserEnvironmentVariable>();
        if (_document.ActiveProfileId is not null && _document.Profiles.All(profile => profile.Id != _document.ActiveProfileId))
        {
            _document.ActiveProfileId = null;
            _document.OriginalValues.Clear();
            _document.AppliedValues.Clear();
        }
        foreach (var profile in _document.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
            profile.Variables ??= new List<UserEnvironmentVariable>();
        }
    }

    private void SaveDocument()
    {
        var folder = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_document, JsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static EnvironmentProfilesDocument LoadDocument(string filePath)
    {
        if (!File.Exists(filePath)) return new EnvironmentProfilesDocument();
        return JsonSerializer.Deserialize<EnvironmentProfilesDocument>(File.ReadAllText(filePath))
            ?? new EnvironmentProfilesDocument();
    }
}
