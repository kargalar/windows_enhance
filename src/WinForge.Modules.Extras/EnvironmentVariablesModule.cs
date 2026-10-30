using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed class UserEnvironmentVariable(string name, string value, bool expandable)
{
    public string Name { get; set; } = name;
    public string Value { get; set; } = value;
    public bool Expandable { get; set; } = expandable;
}

internal interface IEnvironmentVariableStore
{
    IReadOnlyList<UserEnvironmentVariable> Read();
    void Save(IReadOnlyCollection<string> originalNames, IReadOnlyCollection<UserEnvironmentVariable> desired);
}

internal sealed class UserEnvironmentVariableStore(string registryPath, Action? notifyEnvironmentChanged = null) : IEnvironmentVariableStore
{
    private const string UserEnvironmentKey = @"Environment";

    public IReadOnlyList<UserEnvironmentVariable> Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        if (key is null) return Array.Empty<UserEnvironmentVariable>();

        return key.GetValueNames()
            .Select(name => ReadEntry(key, name))
            .Where(entry => entry is not null)
            .Cast<UserEnvironmentVariable>()
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Save(IReadOnlyCollection<string> originalNames, IReadOnlyCollection<UserEnvironmentVariable> desired)
    {
        var validated = Validate(desired);
        using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true)
            ?? throw new InvalidOperationException("Kullanıcı ortam değişkenleri anahtarı açılamadı.");

        var changedNames = originalNames.Concat(validated.Select(entry => entry.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var before = changedNames.ToDictionary(name => name, name => ReadEntry(key, name), StringComparer.OrdinalIgnoreCase);

        try
        {
            var desiredByName = validated.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var originalName in originalNames)
                if (!desiredByName.ContainsKey(originalName)) key.DeleteValue(originalName, throwOnMissingValue: false);

            foreach (var entry in validated)
            {
                var current = ReadEntry(key, entry.Name);
                if (current is not null && current.Value == entry.Value && current.Expandable == entry.Expandable) continue;
                key.SetValue(entry.Name, entry.Value,
                    entry.Expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
            }
        }
        catch
        {
            foreach (var (name, snapshot) in before)
            {
                if (snapshot is null) key.DeleteValue(name, throwOnMissingValue: false);
                else key.SetValue(name, snapshot.Value,
                    snapshot.Expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
            }
            throw;
        }

        (notifyEnvironmentChanged ?? BroadcastEnvironmentChange)();
    }

    internal static IReadOnlyList<UserEnvironmentVariable> Validate(IReadOnlyCollection<UserEnvironmentVariable> entries)
    {
        var result = new List<UserEnvironmentVariable>(entries.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var name = entry.Name.Trim();
            if (name.Length == 0) throw new FormatException("Değişken adı boş olamaz.");
            if (name.Contains('=') || name.Contains('\0')) throw new FormatException($"Geçersiz değişken adı: {name}");
            if (!names.Add(name)) throw new FormatException($"Yinelenen değişken adı: {name}");
            if (entry.Value.Contains('\0')) throw new FormatException($"Değişken değeri NUL karakteri içeremez: {name}");

            result.Add(new UserEnvironmentVariable(name, entry.Value, entry.Expandable));
        }

        return result;
    }

    private static UserEnvironmentVariable? ReadEntry(RegistryKey key, string name)
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

    internal static void BroadcastEnvironmentChange()
    {
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, UserEnvironmentKey,
            0x0002, 5000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        string lParam, uint flags, uint timeout, out IntPtr result);
}

public sealed class EnvironmentVariablesModule : WinForge.Core.ITweakModule
{
    private WinForge.Core.IModuleContext? _context;
    private readonly UserEnvironmentVariableStore _store = new(@"Environment");
    private readonly EnvironmentProfileService _profileService = new(@"Environment",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForge", "environment-profiles.json"));
    private readonly WinForge.Core.SettingDescriptor[] _settings;
    private EnvironmentVariablesWindow? _window;
    private EnvironmentVariablesWindow? _systemWindow;
    private EnvironmentProfilesWindow? _profilesWindow;

    public EnvironmentVariablesModule()
    {
        _settings =
        [
            new WinForge.Core.SettingDescriptor
            {
                Key = "open-editor",
                Label = "Kullanıcı değişkenlerini düzenle",
                Kind = WinForge.Core.SettingKind.Action,
                Description = "Kullanıcı kapsamındaki değişkenleri ekleyin, düzenleyin veya silin.",
                Execute = OpenEditor
            },
            new WinForge.Core.SettingDescriptor
            {
                Key = "manage-profiles",
                Label = "Değişken profillerini yönet",
                Kind = WinForge.Core.SettingKind.Action,
                Description = "Değişken kümelerini kaydedin ve tek bir profili kullanıcı ortamında etkinleştirin.",
                Execute = OpenProfiles
            },
            new WinForge.Core.SettingDescriptor
            {
                Key = "open-system-editor",
                Label = "Sistem değişkenlerini düzenle (yönetici)",
                Kind = WinForge.Core.SettingKind.Action,
                Description = "Kaydetme sırasında Windows yönetici izni ister; sistem değişkenleri tüm kullanıcıları ve uygulamaları etkiler.",
                Execute = OpenSystemEditor
            }
        ];
    }

    public string Id => "environment-variables";
    public string Name => "Ortam Değişkenleri";
    public string Category => "Sistem";
    public string Description => "Kullanıcı ortam değişkenlerini ve etkinleştirilebilir profilleri yönetir; değişiklikler yeni başlatılan uygulamalara bildirilir.";
    public string? Warning => "Bu modül yalnızca geçerli kullanıcının değişkenlerini değiştirir. Sistem değişkenleri yönetici izni gerektirir.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<WinForge.Core.SettingDescriptor> Settings => _settings;

    public void Initialize(WinForge.Core.IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        try { _profileService.ReapplyActiveProfile(); }
        catch (Exception exception)
        {
            _context?.Log.Error("Etkin ortam değişkeni profili uygulanamadı.", exception);
            _context?.Notify("Etkin ortam değişkeni profili uygulanamadı: " + exception.Message);
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        _window?.Close();
        _window = null;
        _systemWindow?.Close();
        _systemWindow = null;
        _profilesWindow?.Close();
        _profilesWindow = null;
    }

    public void Apply() { }

    private void OpenEditor()
    {
        try
        {
            _window ??= new EnvironmentVariablesWindow(_store, featureUi: (_context as IFeatureUiContext)?.FeatureUi);
            _window.Closed += (_, _) => _window = null;
            if (!_window.IsVisible) _window.Show();
            _window.Activate();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Ortam değişkenleri penceresi açılamadı.", exception);
            _context?.Notify("Ortam değişkenleri açılamadı: " + exception.Message);
        }
    }

    private void OpenProfiles()
    {
        try
        {
            _profilesWindow ??= new EnvironmentProfilesWindow(_profileService, (_context as IFeatureUiContext)?.FeatureUi);
            _profilesWindow.Closed += (_, _) => _profilesWindow = null;
            if (!_profilesWindow.IsVisible) _profilesWindow.Show();
            _profilesWindow.Activate();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Ortam değişkeni profilleri açılamadı.", exception);
            _context?.Notify("Ortam değişkeni profilleri açılamadı: " + exception.Message);
        }
    }

    private void OpenSystemEditor()
    {
        try
        {
            _systemWindow ??= new EnvironmentVariablesWindow(new SystemEnvironmentVariableStore(),
                "Sistem Ortam Değişkenleri",
                "Sistem kapsamındaki değerler tüm kullanıcıları etkiler. Kaydet düğmesi Windows yönetici izni ister.",
                (_context as IFeatureUiContext)?.FeatureUi);
            _systemWindow.Closed += (_, _) => _systemWindow = null;
            if (!_systemWindow.IsVisible) _systemWindow.Show();
            _systemWindow.Activate();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Sistem ortam değişkenleri penceresi açılamadı.", exception);
            _context?.Notify("Sistem ortam değişkenleri açılamadı: " + exception.Message);
        }
    }
}
