using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ExplorerNavigationModule : ITweakModule
{
    internal const string RuntimeKey = @"Software\WinForge\ExplorerNavigation\Runtime";
    private const string BackupSetting = "namespace-backup";
    private const string PinnedValue = "System.IsPinnedToNameSpaceTree";
    internal const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    internal const string FrequentFoldersValue = "ShowFrequent";
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private static readonly (string Id, string Label, string Clsid)[] Items =
    [
        ("home", "Home / Giriş", "{f874310e-b6b7-47dc-bc84-b9e6b38f5903}"),
        ("gallery", "Galeri", "{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}"),
        ("onedrive", "OneDrive", "{018D5C66-4533-4307-9B53-224DE2ED1FE6}"),
        ("libraries", "Kitaplıklar", "{031E4825-7B94-4dc3-B131-E946B44C8DD5}"),
        ("network", "Ağ", "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}")
    ];
    private nint _hook;
    private IModuleContext? _context;
    private DispatcherTimer? _timer;
    private InstallHook? _install;
    private nint _library;
    private string? _error;
    public string Id => "explorer-navigation";
    public string Name => "Gezgin Sol Panel Tasarımı";
    public string Category => "Gezgin";
    public string Description => "Sol gezinme panelinin boşluklarını, yazı tipini, renklerini, simgelerini ve seçim görünümünü özelleştirir; Home ve Galeri gibi öğeleri gizler.";
    public string? Warning => _error ?? "Görünüm WinForge çalışırken uygulanır. Öğe görünürlüğü için Gezgin pencerelerini yeniden açmak gerekebilir. Modülü kapatınca önceki ayarlar geri yüklenir.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public ExplorerNavigationModule()
    {
        var settings = new List<SettingDescriptor>
        {
            Toggle("style-enabled", "Özel panel görünümü", true, "Görünüm"),
            new()
            {
                Key = "font-family", Label = "Yazı tipi", Kind = SettingKind.Choice, Default = "Segoe UI", Section = "Yazı",
                Options = System.Windows.Media.Fonts.SystemFontFamilies
                    .Select(font => font.Source)
                    .Append("Segoe UI")
                    .Where(name => name.Length is > 0 and <= 31 && !name.Any(char.IsControl))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()
            },
            Number("font-size", "Yazı boyutu (DIP)", 13, 9, 32, "Yazı"),
            new() { Key = "font-weight", Label = "Yazı kalınlığı", Kind = SettingKind.Choice, Default = "400", Section = "Yazı", Options = ["300", "400", "500", "600", "700"], OptionLabels = new Dictionary<string, string> { ["300"] = "İnce", ["400"] = "Normal", ["500"] = "Orta", ["600"] = "Yarı kalın", ["700"] = "Kalın" } },
            Number("padding-horizontal", "Yatay iç boşluk (DIP)", 8, 0, 48, "Boşluklar"),
            Number("padding-vertical", "Dikey iç boşluk (DIP)", 5, 0, 24, "Boşluklar"),
            Number("indent", "Alt klasör girintisi (DIP)", 18, 0, 64, "Boşluklar"),
            Number("row-margin", "Seçim kenar boşluğu (DIP)", 4, 0, 24, "Boşluklar"),
            Number("corner-radius", "Seçim köşe yarıçapı (DIP)", 6, 0, 24, "Boşluklar"),
            Toggle("show-icons", "Simgeleri göster", true, "Simgeler"),
            Toggle("show-arrows", "Klasör açma oklarını göster", true, "Simgeler"),
            Number("icon-size", "Simge boyutu (DIP)", 16, 12, 32, "Simgeler"),
            Number("icon-gap", "Simge ile yazı arası (DIP)", 6, 0, 32, "Simgeler"),
            Text("background", "Panel arka planı", "#191919", "Renkler", "#RRGGBB biçiminde renk."),
            Text("text", "Yazı rengi", "#F0F0F0", "Renkler", "#RRGGBB biçiminde renk."),
            Text("selected", "Seçili satır arka planı", "#2D2D2D", "Renkler", "#RRGGBB biçiminde renk."),
            Text("selected-text", "Seçili satır yazısı", "#FFFFFF", "Renkler", "#RRGGBB biçiminde renk."),
            Text("hover", "Fare üzerindeyken arka plan", "#262626", "Renkler", "#RRGGBB biçiminde renk.")
        };
        settings.AddRange(Items.Select(item => Toggle("hide-" + item.Id, item.Label + " öğesini gizle", false, "Görünür öğeler")));
        settings.Add(Toggle("expand-current", "Geçerli klasöre otomatik genişlet", false, "Gezinme"));
        settings.Add(Toggle("show-all", "Tüm klasörleri göster", false, "Gezinme"));
        settings.Add(new()
        {
            Key = "block-frequent-folders",
            Label = "Sık ziyaret edilen klasörlerin otomatik eklenmesini engelle",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Gezinme",
            Description = "Hızlı erişimde otomatik gösterilen sık kullanılan klasörleri kapatır; Home sayfasını da etkiler. Elle sabitlediğiniz klasörler korunur. Değişiklik için Gezgin pencerelerini yeniden açmak gerekebilir."
        });
        settings.Add(new() { Key = "reset-appearance", Label = "Görünümü varsayılana döndür", Kind = SettingKind.Action, Section = "Görünüm", Execute = ResetAppearance });
        Settings = settings;
    }

    private static SettingDescriptor Toggle(string key, string label, bool value, string section) => new() { Key = key, Label = label, Kind = SettingKind.Toggle, Default = value, Section = section };
    private static SettingDescriptor Number(string key, string label, int value, int min, int max, string section) => new() { Key = key, Label = label, Kind = SettingKind.Number, Default = value, Min = min, Max = max, Section = section };
    private static SettingDescriptor Text(string key, string label, string value, string section, string? description = null) => new() { Key = key, Label = label, Kind = SettingKind.Text, Default = value, Section = section, Description = description };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        SaveOriginals();
        IsActive = true;
        try
        {
            Apply();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch { Deactivate(); throw; }
    }

    public void Apply()
    {
        if (!IsActive) return;
        var settings = _context!.Settings;
        // Validate the whole appearance before changing either registry surface.
        var colors = Settings.Where(setting => setting.Section == "Renkler").ToDictionary(setting => setting.Key,
            setting => ParseColor(settings.GetString(setting.Key, (string)setting.Default!)));
        var family = settings.GetString("font-family", "Segoe UI").Trim();
        if (family.Length is < 1 or > 31 || family.Any(char.IsControl)) throw new ArgumentException("Yazı tipi adı 1–31 karakter olmalıdır.");
        if (settings.GetBool("style-enabled", true)) EnsureLibrary();
        using (var runtime = Registry.CurrentUser.CreateSubKey(RuntimeKey))
        {
            runtime.SetValue("active", 0);
            foreach (var setting in Settings.Where(setting => setting.Kind is SettingKind.Number or SettingKind.Toggle || setting.Key == "font-weight"))
            {
                var value = setting.Kind == SettingKind.Toggle ? (settings.GetBool(setting.Key, setting.Default is true) ? 1 : 0)
                    : setting.Kind == SettingKind.Number ? Math.Clamp(settings.GetInt(setting.Key, (int)setting.Default!), setting.Min, setting.Max)
                    : Math.Clamp(settings.GetInt(setting.Key, 400), 100, 900);
                runtime.SetValue(setting.Key, value, RegistryValueKind.DWord);
            }
            foreach (var (key, color) in colors) runtime.SetValue(key, color, RegistryValueKind.DWord);
            runtime.SetValue("font-family", family);
            runtime.SetValue("revision", Environment.TickCount, RegistryValueKind.DWord);
            runtime.SetValue("heartbeat", Environment.TickCount64, RegistryValueKind.QWord);
            runtime.SetValue("active", settings.GetBool("style-enabled", true) ? 1 : 0);
        }
        foreach (var item in Items)
        {
            if (settings.GetBool("hide-" + item.Id)) Reg.WriteDword(NamespaceKey(item.Clsid), PinnedValue, 0);
            else RestoreOriginal(NamespaceKey(item.Clsid), PinnedValue);
        }
        Reg.WriteDword(AdvancedKey, "NavPaneExpandToCurrentFolder", settings.GetBool("expand-current") ? 1 : 0);
        Reg.WriteDword(AdvancedKey, "NavPaneShowAllFolders", settings.GetBool("show-all") ? 1 : 0);
        if (settings.GetBool("block-frequent-folders")) Reg.WriteDword(ExplorerKey, FrequentFoldersValue, 0);
        else RestoreOriginal(ExplorerKey, FrequentFoldersValue);
        Reg.Broadcast("ShellState");
        RefreshHooks();
    }

    internal static int ParseColor(string text)
    {
        if (!Regex.IsMatch(text, @"\A#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant)) throw new ArgumentException("Renk #RRGGBB biçiminde olmalıdır: " + text);
        var rgb = int.Parse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
    }

    private void ResetAppearance()
    {
        foreach (var setting in Settings.Where(setting => setting.Section is not ("Görünür öğeler" or "Gezinme") && setting.Kind != SettingKind.Action))
            _context!.Settings.Set(setting.Key, setting.Default);
        Apply();
    }

    private static string NamespaceKey(string clsid) => @"Software\Classes\CLSID\" + clsid;
    private sealed record OriginalValue(string Key, string Name, bool Exists, RegistryValueKind Kind, string? Data);
    private List<OriginalValue> ReadOriginals()
    {
        var json = _context!.Settings.GetString(BackupSetting);
        return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<OriginalValue>>(json) ?? [];
    }
    private void SaveOriginals()
    {
        // Persist recovery data before the first write; retain it after an unclean application exit.
        var originals = ReadOriginals();
        var originalCount = originals.Count;
        var addresses = Items.Select(item => (Key: NamespaceKey(item.Clsid), Name: PinnedValue))
            .Concat([(Key: AdvancedKey, Name: "NavPaneExpandToCurrentFolder"), (Key: AdvancedKey, Name: "NavPaneShowAllFolders"), (Key: ExplorerKey, Name: FrequentFoldersValue)]);
        var additions = addresses.Where(address => !originals.Any(original => original.Key == address.Key && original.Name == address.Name)).Select(address =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(address.Key);
            var value = key?.GetValue(address.Name);
            var kind = value is null ? RegistryValueKind.None : key!.GetValueKind(address.Name);
            if (value is not null && kind != RegistryValueKind.DWord) throw new InvalidOperationException("Beklenmeyen Gezgin kayıt değeri: " + address.Name);
            return new OriginalValue(address.Key, address.Name, value is not null, kind, value?.ToString());
        }).ToList();
        originals.AddRange(additions);
        if (originals.Count != originalCount) _context!.Settings.Set(BackupSetting, JsonSerializer.Serialize(originals));
    }
    private static void Restore(OriginalValue original)
    {
        if (original.Exists) Reg.WriteDword(original.Key, original.Name, int.Parse(original.Data!, CultureInfo.InvariantCulture));
        else Reg.DeleteValue(original.Key, original.Name);
    }
    private void RestoreOriginal(string key, string name)
    {
        var original = ReadOriginals().Single(value => value.Key == key && value.Name == name);
        Restore(original);
    }

    public void Deactivate()
    {
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTick;
        _timer = null;
        Reg.WriteDword(RuntimeKey, "active", 0);
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
        foreach (var original in ReadOriginals()) Restore(original);
        _context!.Settings.Set(BackupSetting, null);
        IsActive = false;
        Reg.Broadcast("ShellState");
        // The DLL is deliberately kept mapped: Explorer may still be returning through a hook.
    }
    private void EnsureLibrary()
    {
        if (_library != 0) return;
        var path = Path.Combine(AppContext.BaseDirectory, "native", "WinForge.ExplorerNavigationTap.dll");
        if (!File.Exists(path)) throw new FileNotFoundException("Gezgin sol panel bileşeni bulunamadı.", path);
        using var source = File.OpenRead(path);
        var digest = SHA256.HashData(source);
        var directory = Path.Combine(AppData.DirectoryPath, "Navigation", Convert.ToHexString(digest)[..16]);
        Directory.CreateDirectory(directory);
        var cachedPath = Path.Combine(directory, Path.GetFileName(path));
        if (!File.Exists(cachedPath)) File.Copy(path, cachedPath);
        using var cached = File.OpenRead(cachedPath);
        if (!CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(cached))) throw new InvalidDataException("Sol panel bileşeninin önbellek doğrulaması başarısız.");
        _library = NativeLibrary.Load(cachedPath);
        _install = Marshal.GetDelegateForFunctionPointer<InstallHook>(NativeLibrary.GetExport(_library, "InstallNavigationHook"));
    }
    private void OnTick(object? sender, EventArgs args)
    {
        try
        {
            using var runtime = Registry.CurrentUser.CreateSubKey(RuntimeKey);
            runtime.SetValue("heartbeat", Environment.TickCount64, RegistryValueKind.QWord);
        }
        catch (Exception error) { _error = "Sol panel bağlantısı: " + error.Message; _context?.Log.Error(_error, error); }
    }
    private void RefreshHooks()
    {
        if (_install is null || !_context!.Settings.GetBool("style-enabled", true))
        {
            if (_hook != 0) UnhookWindowsHookEx(_hook);
            _hook = 0;
            return;
        }
        if (_hook == 0)
        {
            _hook = _install(0);
            if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Explorer gezinme kancası kurulamadı.");
        }
        var explorerProcesses = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process) explorerProcesses.Add((uint)process.Id);
        }
        var windows = new List<nint>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var process);
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            if (explorerProcesses.Contains(process) && name.ToString() == "CabinetWClass")
            {
                EnumChildWindows(window, (child, _) =>
                {
                    var childName = new StringBuilder(128);
                    GetClassName(child, childName, childName.Capacity);
                    if (childName.ToString() != "SysTreeView32") return true;
                    GetWindowThreadProcessId(child, out var childProcess);
                    if (!explorerProcesses.Contains(childProcess)) return true;
                    windows.Add(child);
                    return true;
                }, 0);
            }
            return true;
        }, 0);
        foreach (var window in windows) SendMessageTimeout(window, 0, 0, 0, 2, 100, out _);
        _error = null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate nint InstallHook(uint thread);
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint window, EnumWindow callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint window, uint message, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
}
