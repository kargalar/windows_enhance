using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class WindhawkModule : ITweakModule
{
    private const string HideStartMod = "hide-start-button";
    private const string TrayMod = "taskbar-notification-icon-spacing";
    private const string HeightMod = "taskbar-icon-size";
    private const string StylerMod = "windows-11-taskbar-styler";
    private const string GroupingMod = "taskbar-grouping";
    private const string ClockMod = "taskbar-clock-customization";
    private const string VolumeMod = "taskbar-volume-control";
    private const string NotificationsMod = "notifications-placement";
    private const string ChromePopupMod = "remove-fullscreen-popup-chrome";
    private const string OriginalPrefix = "__original-";

    private static readonly string[] TrayOptions = { "Kapalı", "Mevcut", "2 satır (18 px)" };
    private static readonly string[] HeightOptions = { "Kapalı", "Mevcut", "Büyük (52 px)" };
    private static readonly string[] ThemeOptions =
    {
        "Kapalı", "Mevcut", "TranslucentTaskbar", "SimplyTransparent", "DockLike",
        "WinXP", "Windows7", "RosePine", "FluentGlass"
    };

    private static readonly string[] ManagedMods =
    {
        HideStartMod, TrayMod, HeightMod, StylerMod, GroupingMod, ClockMod,
        VolumeMod, NotificationsMod, ChromePopupMod
    };

    private IModuleContext? _context;
    private string? _cli;
    private bool _explorerPatcher;
    private bool _warned;

    public string Id => "windhawk-taskbar";
    public string Name => "Windhawk Görev Çubuğu";
    public string Category => "Taskbar";
    public string Description => _explorerPatcher
        ? "Windhawk 2.0 CLI ile görev çubuğu, tray, saat, ses, bildirim konumu ve tarayıcı tam ekran modlarını yönetir. ExplorerPatcher algılandı; XAML çakışmalarına dikkat edin."
        : "Windhawk 2.0 CLI ile görev çubuğu, tray, saat, ses, bildirim konumu ve tarayıcı tam ekran modlarını yönetir.";
    public string? Warning => _cli is null
        ? "Windhawk 2.0 CLI bulunamadı. Gelişmiş görev çubuğu modları için Windhawk 2.0 kurun: https://windhawk.net"
        : null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "hide-start",
            Label = "Başlat düğmesini gizle",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Windhawk",
            Description = "Windhawk'un 'Hide Start Button' modunu kurar ve etkinleştirir. Başlat menüsü Win tuşuyla açılmaya devam eder."
        },
        new SettingDescriptor
        {
            Key = "tray-grid",
            Label = "Tepsi simgeleri",
            Kind = SettingKind.Choice,
            Options = TrayOptions,
            Default = TrayOptions[0],
            Section = "Windhawk",
            Description = "Tray simgelerini iki satırlı ızgarada gösterir. 'Mevcut' seçeneği yüklü modun ayarlarını değiştirmez."
        },
        new SettingDescriptor
        {
            Key = "taskbar-height",
            Label = "Görev çubuğu boyutu",
            Kind = SettingKind.Choice,
            Options = HeightOptions,
            Default = HeightOptions[0],
            Section = "Windhawk",
            Description = "Görev çubuğunu 52 px, simgeleri 32 px yapar. 'Mevcut' seçeneği yüklü modun ayarlarını değiştirmez."
        },
        new SettingDescriptor
        {
            Key = "styler-theme",
            Label = "Görev çubuğu teması",
            Kind = SettingKind.Choice,
            Options = ThemeOptions,
            Default = ThemeOptions[0],
            Section = "Windhawk",
            Description = "Windhawk Taskbar Styler temasını seçer. 'Mevcut' seçeneği yüklü temayı korur."
        },
        new SettingDescriptor
        {
            Key = "disable-grouping",
            Label = "Pencere düğmelerini gruplama",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görev çubuğu modları",
            Description = "Windhawk 'Taskbar Grouping' modunu açar; aynı uygulamanın pencereleri ayrı düğmeler olarak görünür."
        },
        new SettingDescriptor
        {
            Key = "clock-custom",
            Label = "Gelişmiş saat biçimi",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görev çubuğu modları",
            Description = "Windhawk 'Taskbar Clock Customization' modunu açar. Saat biçimini modun kendi panelinden ayarlayın."
        },
        new SettingDescriptor
        {
            Key = "volume-mod",
            Label = "Görev çubuğunda ses denetimi",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görev çubuğu modları",
            Description = "Windhawk 'Taskbar Volume Control' modunu açar; görev çubuğu üzerinde tekerlekle ses, orta tıkla sessize alma."
        },
        new SettingDescriptor
        {
            Key = "notifications-placement",
            Label = "Bildirim konumunu özelleştir",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görev çubuğu modları",
            Description = "Windhawk 'Notifications Placement' modunu açar; bildirimlerin ekrandaki konumu modun panelinden seçilir."
        },
        new SettingDescriptor
        {
            Key = "chrome-fullscreen-popup",
            Label = "Tarayıcı tam ekran balonunu kaldır",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görev çubuğu modları",
            Description = "Windhawk 'Remove Fullscreen Popup' modunu açar; Chrome/Edge tam ekranda çıkan 'Çıkmak için Esc' balonunu gizler."
        }
    };

    public void Initialize(IModuleContext context)
    {
        _context = context;
        _cli = FindCli();
        _explorerPatcher = IsExplorerPatcherInstalled();
    }

    public void Activate()
    {
        _cli = FindCli();
        _explorerPatcher = IsExplorerPatcherInstalled();

        if (_cli is null)
        {
            WarnOnce("Windhawk 2.0 CLI bulunamadı; gelişmiş görev çubuğu modları devre dışı.");
            IsActive = true;
            return;
        }

        SyncFromActual();
        CaptureOriginals();
        ApplySettings();
        IsActive = true;
        _context?.Log.Info($"Windhawk entegrasyonu etkin: {_cli}");
    }

    public void Deactivate()
    {
        if (_cli is not null)
            RestoreOriginals();

        IsActive = false;
    }

    public void Apply()
    {
        if (_cli is null)
        {
            WarnOnce("Windhawk 2.0 CLI bulunamadı; gelişmiş görev çubuğu modları devre dışı.");
            return;
        }

        ApplySettings();
    }

    private void ApplySettings()
    {
        var settings = _context!.Settings;
        var mods = GetInstalledMods();
        if (mods is null)
        {
            WarnOnce("Windhawk mod listesi okunamadı; Windhawk servisinin çalıştığını kontrol edin.");
            return;
        }

        Ensure(HideStartMod, settings.GetBool("hide-start"), mods, null);

        var tray = settings.GetString("tray-grid", TrayOptions[0]);
        Ensure(TrayMod, !IsOff(tray), mods,
            string.Equals(tray, TrayOptions[2], StringComparison.CurrentCultureIgnoreCase)
                ? new[] { "notificationIconWidth=18", "notificationIconRows=2" }
                : null);

        var height = settings.GetString("taskbar-height", HeightOptions[0]);
        Ensure(HeightMod, !IsOff(height), mods,
            string.Equals(height, HeightOptions[2], StringComparison.CurrentCultureIgnoreCase)
                ? new[] { "TaskbarHeight=52", "IconSize=32", "TaskbarButtonWidth=44" }
                : null);

        var theme = settings.GetString("styler-theme", ThemeOptions[0]);
        Ensure(StylerMod, !IsOff(theme), mods,
            !IsOff(theme) && !string.Equals(theme, ThemeOptions[1], StringComparison.CurrentCultureIgnoreCase)
                ? new[] { "theme=" + theme }
                : null);

        Ensure(GroupingMod, settings.GetBool("disable-grouping"), mods, null);
        Ensure(ClockMod, settings.GetBool("clock-custom"), mods, null);
        Ensure(VolumeMod, settings.GetBool("volume-mod"), mods, null);
        Ensure(NotificationsMod, settings.GetBool("notifications-placement"), mods, null);
        Ensure(ChromePopupMod, settings.GetBool("chrome-fullscreen-popup"), mods, null);
    }

    private void Ensure(string id, bool desired, Dictionary<string, bool> mods, string[]? values)
    {
        if (desired)
        {
            if (!mods.ContainsKey(id))
            {
                if (!Install(id)) return;
                mods[id] = false;
            }

            if (!mods[id])
            {
                if (!Enable(id)) return;
                mods[id] = true;
            }

            if (values is { Length: > 0 })
                SetModSettings(id, values);
        }
        else if (mods.TryGetValue(id, out var enabled) && enabled)
        {
            if (Disable(id)) mods[id] = false;
        }
    }

    private void SyncFromActual()
    {
        var mods = GetInstalledMods();
        if (mods is null) return;

        var settings = _context!.Settings;

        if (settings.GetString("hide-start").Length == 0)
            settings.Set("hide-start", mods.TryGetValue(HideStartMod, out var hideStart) && hideStart);

        if (settings.GetString("tray-grid").Length == 0)
            settings.Set("tray-grid", mods.TryGetValue(TrayMod, out var tray) && tray ? TrayOptions[1] : TrayOptions[0]);

        if (settings.GetString("taskbar-height").Length == 0)
            settings.Set("taskbar-height", mods.TryGetValue(HeightMod, out var height) && height ? HeightOptions[1] : HeightOptions[0]);

        if (settings.GetString("styler-theme").Length == 0)
            settings.Set("styler-theme", mods.TryGetValue(StylerMod, out var styler) && styler ? ThemeOptions[1] : ThemeOptions[0]);

        SyncToggle(settings, "disable-grouping", GroupingMod, mods);
        SyncToggle(settings, "clock-custom", ClockMod, mods);
        SyncToggle(settings, "volume-mod", VolumeMod, mods);
        SyncToggle(settings, "notifications-placement", NotificationsMod, mods);
        SyncToggle(settings, "chrome-fullscreen-popup", ChromePopupMod, mods);
    }

    private static void SyncToggle(IModuleSettings settings, string key, string modId, Dictionary<string, bool> mods)
    {
        if (settings.GetString(key).Length > 0) return;
        settings.Set(key, mods.TryGetValue(modId, out var enabled) && enabled);
    }

    private void CaptureOriginals()
    {
        var mods = GetInstalledMods();
        if (mods is null) return;

        var settings = _context!.Settings;
        foreach (var id in ManagedMods)
        {
            var key = OriginalPrefix + id;
            if (settings.GetString(key).Length > 0) continue;

            settings.Set(key, mods.TryGetValue(id, out var enabled) ? (enabled ? "1" : "0") : "-");
        }
    }

    private void RestoreOriginals()
    {
        var mods = GetInstalledMods();
        if (mods is null) return;

        var settings = _context!.Settings;
        foreach (var id in ManagedMods)
        {
            var key = OriginalPrefix + id;
            var original = settings.GetString(key);
            if (original.Length == 0) continue;

            if (original == "1")
            {
                if (mods.TryGetValue(id, out var enabled) && !enabled)
                    Enable(id);
            }
            else if (mods.TryGetValue(id, out var enabled) && enabled)
            {
                Disable(id);
            }

            settings.Set(key, "");
        }
    }

    private Dictionary<string, bool>? GetInstalledMods()
    {
        var result = RunCli(20000, "mod", "list", "--json");
        var mods = ParseModList(result.Output);
        if (result.ExitCode == 0 && mods is not null) return mods;

        result = RunCli(20000, "--json", "mod", "list");
        mods = ParseModList(result.Output);
        if (result.ExitCode == 0 && mods is not null) return mods;

        Succeeded(result, "mod listesi okunamadı");
        return null;
    }

    private static Dictionary<string, bool>? ParseModList(string output)
    {
        var text = output.Trim();
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var mods = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement)) continue;
                var id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;

                var enabled = item.TryGetProperty("enabled", out var enabledElement) && enabledElement.GetBoolean();
                mods[id] = enabled;
            }

            return mods;
        }
        catch
        {
            return null;
        }
    }

    private bool Install(string id)
    {
        var result = RunCli(60000, "mod", "install", "--repo", id, "--yes");
        return Succeeded(result, $"mod kurulamadı: {id}", allowAlreadyInstalled: true);
    }

    private bool Enable(string id) =>
        Succeeded(RunCli(20000, "mod", "enable", id), $"mod etkinleştirilemedi: {id}");

    private bool Disable(string id) =>
        Succeeded(RunCli(20000, "mod", "disable", id), $"mod devre dışı bırakılamadı: {id}");

    private void SetModSettings(string id, string[] values)
    {
        var args = new List<string> { "mod", "settings", "set", id };
        args.AddRange(values);
        Succeeded(RunCli(20000, args.ToArray()), $"mod ayarları yazılamadı: {id}");
    }

    private CliResult RunCli(int timeoutMs, params string[] args)
    {
        if (_cli is null)
            return new CliResult(-1, "", "CLI bulunamadı");

        try
        {
            var info = new ProcessStartInfo(_cli)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_cli) ?? AppContext.BaseDirectory
            };

            foreach (var arg in args)
                info.ArgumentList.Add(arg);

            using var process = Process.Start(info);
            if (process is null)
                return new CliResult(-1, "", "süreç başlatılamadı");

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new CliResult(-1, "", "zaman aşımı");
            }

            return new CliResult(
                process.ExitCode,
                output.GetAwaiter().GetResult(),
                error.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Windhawk CLI çalıştırılamadı.", ex);
            return new CliResult(-1, "", ex.Message);
        }
    }

    private bool Succeeded(CliResult result, string action, bool allowAlreadyInstalled = false)
    {
        if (result.ExitCode == 0 || (allowAlreadyInstalled && result.ExitCode == 7))
            return true;

        var details = string.Join(' ', new[] { result.Error.Trim(), result.Output.Trim() }
            .Where(part => part.Length > 0));
        _context?.Log.Error($"Windhawk: {action} (kod {result.ExitCode}). {details}".Trim());
        return false;
    }

    private void WarnOnce(string message)
    {
        if (_warned) return;
        _warned = true;
        _context?.Notify(message);
        _context?.Log.Info(message);
    }

    private static bool IsOff(string value) =>
        string.Equals(value, TrayOptions[0], StringComparison.CurrentCultureIgnoreCase);

    private static string? FindCli()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            Path.Combine(programFiles, "Windhawk", "windhawk-cli.exe"),
            Path.Combine(localAppData, "Programs", "Windhawk", "windhawk-cli.exe"),
            Path.Combine(localAppData, "Windhawk", "windhawk-cli.exe"),
            Path.Combine(AppContext.BaseDirectory, "windhawk-cli.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = Path.Combine(dir, "windhawk-cli.exe");
            if (File.Exists(path)) return path;
        }

        try
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var sub = uninstall.OpenSubKey(name);
                    if (sub?.GetValue("DisplayName") is not string display ||
                        !display.Contains("Windhawk", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (sub.GetValue("InstallLocation") is not string location ||
                        string.IsNullOrWhiteSpace(location))
                        continue;

                    var path = Path.Combine(location, "windhawk-cli.exe");
                    if (File.Exists(path)) return path;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsExplorerPatcherInstalled()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (File.Exists(Path.Combine(windows, "ep_taskbar.dll")) ||
            File.Exists(Path.Combine(windows, "dxgi.dll")))
            return true;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\ExplorerPatcher");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct CliResult(int ExitCode, string Output, string Error);
}
