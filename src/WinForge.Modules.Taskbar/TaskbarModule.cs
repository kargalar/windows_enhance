using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

public sealed class TaskbarModule : ITweakModule
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string InternationalKey = @"Control Panel\International";
    private const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";

    private const string OriginalSecondsKey = "__original-show-seconds";
    private const string OriginalFormatKey = "__original-short-time";
    private const string OriginalSearchKey = "__original-search";
    private const string OriginalTaskViewKey = "__original-taskview";
    private const string OriginalWidgetsKey = "__original-widgets";
    private const string OriginalCopilotKey = "__original-copilot";
    private const string OriginalCombineKey = "__original-combine";

    private static readonly string[] SearchOptions = { "Gizli", "Simge", "Kutu", "Simge ve etiket" };
    private static readonly string[] CombineOptions = { "Birleştir", "Dolduğunda birleştir", "Asla birleştir" };

    private static IModuleContext? _context;
    private GlobalMouseHook? _hook;
    private bool _middleClickClose;
    private bool _scrollVolume;
    private int _volumeStep = 2;
    private readonly Stopwatch _volumeClock = Stopwatch.StartNew();
    private long _lastVolumeTick;

    public string Id => "taskbar";
    public string Name => "Taskbar Araçları";
    public string Category => "Taskbar";
    public string Description => "Kapatma ve ses kısayolları, saat biçimi, taskbar öğelerinin görünürlüğü ve pencere birleştirme.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "middle-click-close",
            Label = "Orta tuşla pencereyi kapat",
            Kind = SettingKind.Toggle,
            Default = true,
            Section = "Fare",
            Description = "Taskbar'daki bir düğmeye orta tuşla tıklandığında o pencereyi kapatır."
        },
        new SettingDescriptor
        {
            Key = "scroll-volume",
            Label = "Taskbar üzerinde tekerlekle ses",
            Kind = SettingKind.Toggle,
            Default = true,
            Section = "Fare",
            Description = "İmleç taskbar üzerindeyken fare tekerleği ana ses düzeyini değiştirir."
        },
        new SettingDescriptor
        {
            Key = "volume-step",
            Label = "Ses adımı (%)",
            Kind = SettingKind.Number,
            Default = 2,
            Min = 1,
            Max = 10,
            Section = "Fare"
        },
        new SettingDescriptor
        {
            Key = "clock-seconds",
            Label = "Saatte saniyeleri göster",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Saat"
        },
        new SettingDescriptor
        {
            Key = "clock-format",
            Label = "Özel saat biçimi",
            Kind = SettingKind.Text,
            Default = "",
            Section = "Saat",
            Description = "Örn: HH:mm:ss dddd. Boş bırakılırsa Windows varsayılanı kullanılır. Bu ayar diğer uygulamaların saat biçimini de etkiler."
        },
        new SettingDescriptor
        {
            Key = "search-mode",
            Label = "Arama kutusu",
            Kind = SettingKind.Choice,
            Options = SearchOptions,
            Default = "Gizli",
            Section = "Görünürlük",
            Description = "Arama kutusunun taskbar'da görünüp görünmeyeceğini ve biçimini seçer."
        },
        new SettingDescriptor
        {
            Key = "taskview",
            Label = "Görev görünümü düğmesi",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görünürlük",
            Description = "Taskbar'da görev görünümü simgesini gösterir."
        },
        new SettingDescriptor
        {
            Key = "widgets",
            Label = "Widget'lar düğmesi",
            Kind = SettingKind.Toggle,
            Default = true,
            Section = "Görünürlük",
            Description = "Taskbar'da hava durumu/widget düğmesini gösterir."
        },
        new SettingDescriptor
        {
            Key = "copilot",
            Label = "Copilot düğmesi",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görünürlük",
            Description = "Taskbar'da Copilot düğmesini gösterir."
        },
        new SettingDescriptor
        {
            Key = "combine-mode",
            Label = "Pencere düğmelerini birleştirme",
            Kind = SettingKind.Choice,
            Options = CombineOptions,
            Default = "Birleştir",
            Section = "Pencere düğmeleri",
            Description = "Aynı uygulamanın pencerelerinin taskbar'da tek düğmede toplanmasını kontrol eder."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");

        ReadSettings();
        SyncFromRegistry();
        CaptureOriginals();
        ApplyClock();
        ApplyVisibility();

        _hook = new GlobalMouseHook();
        _hook.MiddleButtonUp += OnMiddleButtonUp;
        _hook.WheelMoved += OnWheel;
        _hook.Install();
        IsActive = true;
        context.Log.Info("Taskbar kancası kuruldu.");
    }

    public void Deactivate()
    {
        IsActive = false;

        if (_hook is not null)
        {
            _hook.MiddleButtonUp -= OnMiddleButtonUp;
            _hook.WheelMoved -= OnWheel;
            _hook.Dispose();
            _hook = null;
        }

        RestoreClock();
        RestoreVisibility();
    }

    public void Apply()
    {
        ReadSettings();
        ApplyClock();
        ApplyVisibility();
    }

    private void ReadSettings()
    {
        var settings = _context!.Settings;
        _middleClickClose = settings.GetBool("middle-click-close", true);
        _scrollVolume = settings.GetBool("scroll-volume", true);
        _volumeStep = Math.Clamp(settings.GetInt("volume-step", 2), 1, 10);
    }

    private void OnMiddleButtonUp(POINT point)
    {
        if (!_middleClickClose || !ShellTaskbar.IsTaskbarPoint(point)) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var target = ShellTaskbar.FindWindowUnderTaskbar(point);
                if (target == IntPtr.Zero) return;

                var title = Native.TitleOf(target);
                Native.PostMessage(target, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                _context?.Notify(string.IsNullOrWhiteSpace(title) ? "Pencere kapatıldı" : $"Kapatıldı: {title}");
            }
            catch (Exception ex)
            {
                _context?.Log.Error("Orta tuşla kapatma başarısız.", ex);
            }
        });
    }

    private void OnWheel(POINT point, int delta)
    {
        if (!_scrollVolume || !ShellTaskbar.IsTaskbarPoint(point)) return;

        try
        {
            var percent = VolumeService.Step(delta > 0 ? _volumeStep : -_volumeStep);

            var now = _volumeClock.ElapsedMilliseconds;
            if (now - _lastVolumeTick < 80) return;
            _lastVolumeTick = now;

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try { _context?.Notify($"Ses: {percent}%"); }
                catch { }
            });
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Ses düzeyi değiştirilemedi.", ex);
        }
    }

    private void ApplyClock()
    {
        var settings = _context!.Settings;

        if (settings.GetString(OriginalSecondsKey).Length == 0)
            settings.Set(OriginalSecondsKey, ReadDword(AdvancedKey, "ShowSecondsInSystemClock")?.ToString() ?? "-");

        WriteDword(AdvancedKey, "ShowSecondsInSystemClock", settings.GetBool("clock-seconds") ? 1 : 0);

        var format = settings.GetString("clock-format").Trim();
        var original = settings.GetString(OriginalFormatKey);

        if (format.Length > 0)
        {
            if (original.Length == 0)
            {
                original = ReadString(InternationalKey, "sShortTime") ?? "HH:mm";
                settings.Set(OriginalFormatKey, original);
            }

            WriteString(InternationalKey, "sShortTime", format);
        }
        else if (original.Length > 0)
        {
            WriteString(InternationalKey, "sShortTime", original);
            settings.Set(OriginalFormatKey, "");
        }

        BroadcastSettingChange();
    }

    private void RestoreClock()
    {
        var settings = _context?.Settings;
        if (settings is null) return;

        RestoreValue(settings, OriginalSecondsKey, AdvancedKey, "ShowSecondsInSystemClock");
        RestoreValue(settings, OriginalFormatKey, InternationalKey, "sShortTime", isString: true);
        BroadcastSettingChange();
    }

    private void ApplyVisibility()
    {
        var settings = _context!.Settings;

        var search = IndexOf(SearchOptions, settings.GetString("search-mode", SearchOptions[0]));
        WriteDword(SearchKey, "SearchboxTaskbarMode", search);
        WriteDword(AdvancedKey, "ShowTaskViewButton", settings.GetBool("taskview") ? 1 : 0);
        WriteDword(AdvancedKey, "TaskbarDa", settings.GetBool("widgets", true) ? 1 : 0);
        WriteDword(AdvancedKey, "ShowCopilotButton", settings.GetBool("copilot") ? 1 : 0);

        var combine = IndexOf(CombineOptions, settings.GetString("combine-mode", CombineOptions[0]));
        WriteDword(AdvancedKey, "TaskbarGlomLevel", combine);

        BroadcastSettingChange();
    }

    private void SyncFromRegistry()
    {
        var settings = _context!.Settings;

        if (settings.GetString("clock-seconds").Length == 0)
            settings.Set("clock-seconds", (ReadDword(AdvancedKey, "ShowSecondsInSystemClock") ?? 0) == 1);

        if (settings.GetString("search-mode").Length == 0)
        {
            var mode = ReadDword(SearchKey, "SearchboxTaskbarMode") ?? 0;
            settings.Set("search-mode", SearchOptions[Math.Clamp(mode, 0, 3)]);
        }

        if (settings.GetString("taskview").Length == 0)
            settings.Set("taskview", (ReadDword(AdvancedKey, "ShowTaskViewButton") ?? 0) == 1);

        if (settings.GetString("widgets").Length == 0)
            settings.Set("widgets", (ReadDword(AdvancedKey, "TaskbarDa") ?? 1) == 1);

        if (settings.GetString("copilot").Length == 0)
            settings.Set("copilot", (ReadDword(AdvancedKey, "ShowCopilotButton") ?? 0) == 1);

        if (settings.GetString("combine-mode").Length == 0)
        {
            var combine = ReadDword(AdvancedKey, "TaskbarGlomLevel") ?? 0;
            settings.Set("combine-mode", CombineOptions[Math.Clamp(combine, 0, 2)]);
        }
    }

    private void CaptureOriginals()
    {        var settings = _context!.Settings;
        Capture(settings, OriginalSearchKey, SearchKey, "SearchboxTaskbarMode");
        Capture(settings, OriginalTaskViewKey, AdvancedKey, "ShowTaskViewButton");
        Capture(settings, OriginalWidgetsKey, AdvancedKey, "TaskbarDa");
        Capture(settings, OriginalCopilotKey, AdvancedKey, "ShowCopilotButton");
        Capture(settings, OriginalCombineKey, AdvancedKey, "TaskbarGlomLevel");
    }

    private void RestoreVisibility()
    {
        var settings = _context?.Settings;
        if (settings is null) return;

        RestoreValue(settings, OriginalSearchKey, SearchKey, "SearchboxTaskbarMode");
        RestoreValue(settings, OriginalTaskViewKey, AdvancedKey, "ShowTaskViewButton");
        RestoreValue(settings, OriginalWidgetsKey, AdvancedKey, "TaskbarDa");
        RestoreValue(settings, OriginalCopilotKey, AdvancedKey, "ShowCopilotButton");
        RestoreValue(settings, OriginalCombineKey, AdvancedKey, "TaskbarGlomLevel");
        BroadcastSettingChange();
    }

    private static void Capture(IModuleSettings settings, string originalKey, string registryKey, string name)
    {
        if (settings.GetString(originalKey).Length > 0) return;
        settings.Set(originalKey, ReadDword(registryKey, name)?.ToString() ?? "-");
    }

    private static void RestoreValue(IModuleSettings settings, string originalKey, string registryKey, string name, bool isString = false)
    {
        var stored = settings.GetString(originalKey);
        if (stored.Length == 0) return;

        if (stored == "-")
        {
            DeleteValue(registryKey, name);
        }
        else if (isString)
        {
            WriteString(registryKey, name, stored);
        }
        else if (int.TryParse(stored, out var number))
        {
            WriteDword(registryKey, name, number);
        }

        settings.Set(originalKey, "");
    }

    private static int IndexOf(string[] options, string value)
    {
        var index = Array.FindIndex(options, option => string.Equals(option, value, StringComparison.CurrentCultureIgnoreCase));
        return index < 0 ? 0 : index;
    }

    private static int? ReadDword(string key, string name)
    {
        using var sub = Registry.CurrentUser.OpenSubKey(key);
        return sub?.GetValue(name) as int?;
    }

    private static string? ReadString(string key, string name)
    {
        using var sub = Registry.CurrentUser.OpenSubKey(key);
        return sub?.GetValue(name) as string;
    }

    private static void WriteDword(string key, string name, int value)
    {
        try
        {
            using var sub = Registry.CurrentUser.CreateSubKey(key);
            sub?.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            _context?.Log.Error($"Registry yazılamadı: HKCU\\{key}\\{name}", ex);
        }
    }

    private static void WriteString(string key, string name, string value)
    {
        using var sub = Registry.CurrentUser.CreateSubKey(key);
        sub?.SetValue(name, value, RegistryValueKind.String);
    }

    private static void DeleteValue(string key, string name)
    {
        using var sub = Registry.CurrentUser.OpenSubKey(key, writable: true);
        sub?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void BroadcastSettingChange()
    {
        Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE, IntPtr.Zero, "TraySettings", Native.SMTO_ABORTIFHUNG, 1000, out _);
        Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE, IntPtr.Zero, "intl", Native.SMTO_ABORTIFHUNG, 1000, out _);
    }
}
