using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

public sealed class TaskbarModule : ITweakModule, IAsyncTweakModule
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string InternationalKey = @"Control Panel\International";
    private const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

    private const string OriginalSecondsKey = "__original-show-seconds";
    private const string OriginalFormatKey = "__original-short-time";
    private const string OriginalSearchKey = "__original-search";
    private const string OriginalTaskViewKey = "__original-taskview";
    private const string OriginalWidgetsKey = "__original-widgets";
    private const string OriginalCopilotKey = "__original-copilot";
    private const string OriginalCombineKey = "__original-combine";
    private const string OriginalAlignKey = "__original-align";
    private const string OriginalEndTaskKey = "__original-end-task";
    private const string OriginalIconSizeKey = "__original-icon-size";
    private const string OriginalTransparencyKey = "__original-transparency";
    private const string OriginalAccentTaskbarKey = "__original-accent-taskbar";
    private const string OriginalAutoTrayKey = "__original-auto-tray";

    private static readonly string[] SearchOptions = { "Gizli", "Simge", "Kutu", "Simge ve etiket" };
    private static readonly string[] CombineOptions = { "Birleştir", "Dolduğunda birleştir", "Asla birleştir" };
    private static readonly string[] IconSizeOptions = { "Küçük", "Orta", "Büyük" };
    private static readonly string[] AlignOptions = { "Orta", "Sol" };

    private static IModuleContext? _context;
    private GlobalMouseHook? _hook;
    private bool _middleClickClose;
    private bool _middleClickMute;
    private bool _scrollVolume;
    private int _volumeStep = 2;
    private readonly Stopwatch _volumeClock = Stopwatch.StartNew();
    private long _lastVolumeTick;
    private const int ClickMovementTolerance = 8;
    private bool _middleButtonCaptured;
    private bool _middleReleaseReady;
    private POINT _middleDownPoint;
    private int _pendingVolumeSteps;
    private int _volumeUpdateQueued;
    private int _activationGeneration;
    private ShellDiagnosticsBroker.Lease? _diagnostics;
    private uint _diagnosticsProcessId;
    private IReadOnlyList<ShellVisualNode> _shellTree = [];

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
            Key = "middle-click-mute",
            Label = "Boş görev çubuğunda orta tuşla sessiz",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Fare",
            Description = "Yalnızca boş olduğu doğrulanan görev çubuğu alanında ana sesi açar veya kapatır; uygulama ve sistem düğmelerini etkilemez."
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
        },
        new SettingDescriptor
        {
            Key = "align",
            Label = "Görev çubuğu hizası",
            Kind = SettingKind.Choice,
            Options = AlignOptions,
            Default = "Orta",
            Section = "Yerleşim",
            Description = "Windows 11'de görev çubuğu düğmelerini ortalar veya sola yaslar."
        },
        new SettingDescriptor
        {
            Key = "icon-size",
            Label = "Görev çubuğu simge boyutu",
            Kind = SettingKind.Choice,
            Options = IconSizeOptions,
            Default = "Orta",
            Section = "Görünüm",
            Description = "Windows'un eski simge boyutu ayarıdır; bazı Windows 11 sürümlerinde uygulanmaz."
        },
        new SettingDescriptor
        {
            Key = "transparency",
            Label = "Görev çubuğu saydamlığı",
            Kind = SettingKind.Toggle,
            Default = true,
            Section = "Görünüm",
            Description = "Windows saydamlık efektlerini açar veya kapatır."
        },
        new SettingDescriptor
        {
            Key = "accent-taskbar",
            Label = "Vurgu rengini görev çubuğunda kullan",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görünüm",
            Description = "Windows vurgu rengini görev çubuğu ve Başlat menüsünde gösterir."
        },
        new SettingDescriptor
        {
            Key = "show-all-tray",
            Label = "Tüm sistem simgelerini göster",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Görünüm",
            Description = "Bildirim alanındaki gizli simgeleri her zaman gösterir. Windows 11'de tek tek simge seçimi için 'Sistem Tepsisi Simgeleri' modülünü kullanın."
        },
        new SettingDescriptor
        {
            Key = "end-task",
            Label = "Görev çubuğunda 'Görevi sonlandır'",
            Kind = SettingKind.Toggle,
            Default = false,
            Section = "Yerleşim",
            Description = "Görev çubuğu düğmesine sağ tıklayınca uygulamayı kapatma seçeneği gösterir."
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
        ApplyAppearance();

        Interlocked.Increment(ref _activationGeneration);
        _hook = new GlobalMouseHook
        {
            EventFilter = FilterMouseInput,
            ErrorHandler = exception => context.Log.Error("Görev çubuğu fare geri çağrısı başarısız.", exception)
        };
        _hook.MiddleButtonUp += OnMiddleButtonUp;
        _hook.WheelMoved += OnWheel;
        _hook.Install();
        IsActive = true;
        if (_middleClickClose || _middleClickMute)
            _ = Task.Run(() =>
            {
                try { ReadShellTree(); }
                catch (Exception exception) { _context?.Log.Error("Görev çubuğu düğme bilgileri hazırlanamadı.", exception); }
            });
        context.Log.Info("Taskbar kancası kuruldu.");
    }

    public void Deactivate()
    {
        RestoreState();
        BroadcastSettingChange();
    }

    public Task ActivateAsync() { Activate(); return Task.CompletedTask; }
    public Task ApplyAsync() { Apply(); return Task.CompletedTask; }

    public async Task DeactivateAsync()
    {
        RestoreState();
        await BroadcastSettingChangeAsync();
    }

    private void RestoreState()
    {
        IsActive = false;
        Interlocked.Increment(ref _activationGeneration);
        _middleButtonCaptured = false;
        _middleReleaseReady = false;
        Volatile.Write(ref _shellTree, []);
        Interlocked.Exchange(ref _pendingVolumeSteps, 0);
        var diagnostics = Interlocked.Exchange(ref _diagnostics, null);
        if (diagnostics is not null) _ = Task.Run(diagnostics.Dispose);

        if (_hook is not null)
        {
            _hook.MiddleButtonUp -= OnMiddleButtonUp;
            _hook.WheelMoved -= OnWheel;
            _hook.Dispose();
            _hook = null;
        }

        RestoreClock();
        RestoreVisibility();
        RestoreAppearance();
    }

    public void Apply()
    {
        ReadSettings();
        ApplyClock();
        ApplyVisibility();
        ApplyAppearance();
    }

    private void ReadSettings()
    {
        var settings = _context!.Settings;
        _middleClickClose = settings.GetBool("middle-click-close", true);
        _middleClickMute = settings.GetBool("middle-click-mute", false);
        _scrollVolume = settings.GetBool("scroll-volume", true);
        _volumeStep = Math.Clamp(settings.GetInt("volume-step", 2), 1, 10);
    }

    private void OnMiddleButtonUp(POINT point)
    {
        if (!_middleReleaseReady) return;
        _middleReleaseReady = false;
        _ = HandleMiddleClickAsync(point, Volatile.Read(ref _activationGeneration));
    }

    private bool FilterMouseInput(GlobalMouseHook.Input input)
    {
        if (!IsActive) return false;
        if (input.Message == Native.WM_MBUTTONUP)
        {
            var captured = _middleButtonCaptured;
            _middleButtonCaptured = false;
            _middleReleaseReady = captured && ShellTaskbar.IsTaskbarPoint(input.Point) &&
                Math.Abs(input.Point.X - _middleDownPoint.X) <= ClickMovementTolerance &&
                Math.Abs(input.Point.Y - _middleDownPoint.Y) <= ClickMovementTolerance;
            return captured;
        }
        if (input.Message == Native.WM_MBUTTONDOWN && (_middleClickClose || _middleClickMute) && ShellTaskbar.IsTaskbarPoint(input.Point) &&
            (_middleClickClose || ShellTaskbar.ResolveVisualHit(input.Point, Volatile.Read(ref _shellTree)).Kind == ShellTaskbar.HitKind.EmptyArea))
        {
            _middleDownPoint = input.Point;
            _middleButtonCaptured = true;
            return true;
        }
        return input.Message == Native.WM_MOUSEWHEEL && _scrollVolume && input.WheelDelta != 0 && ShellTaskbar.IsTaskbarPoint(input.Point);
    }

    private async Task HandleMiddleClickAsync(POINT point, int generation)
    {
        var foreground = Native.GetForegroundWindow();
        try
        {
            var hit = await ShellTaskbar.HitTestAsync(point, ReadShellTree);
            if (!IsActive || generation != Volatile.Read(ref _activationGeneration) || Native.GetForegroundWindow() != foreground) return;
            if (hit.Kind == ShellTaskbar.HitKind.Unavailable)
            {
                _context?.Notify("Görev çubuğu düğme bilgileri henüz hazır değil.");
                return;
            }
            if (hit.Kind == ShellTaskbar.HitKind.EmptyArea && _middleClickMute)
            {
                VolumeService.SetMuted(!VolumeService.IsMuted());
                _context?.Notify(VolumeService.IsMuted() ? "Ses kapatıldı" : "Ses açıldı");
                return;
            }
            if (!_middleClickClose || hit.Kind != ShellTaskbar.HitKind.ApplicationButton) return;
            if (hit.Target is not { } target || !ShellTaskbar.IsCurrentTarget(target))
            {
                _context?.Notify("Bu düğmede tek bir pencere belirlenemedi.");
                return;
            }
            if (!Native.PostMessage(target.Handle, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("Pencereye kapatma isteği gönderilemedi.");
            _context?.Notify($"Kapatma isteği gönderildi: {target.Title}");
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Görev çubuğu orta tuş eylemi başarısız.", exception);
            _context?.Notify("Görev çubuğu eylemi uygulanamadı.");
        }
    }

    private IReadOnlyList<ShellVisualNode> ReadShellTree()
    {
        var generation = Volatile.Read(ref _activationGeneration);
        if (!IsActive) return [];
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return [];
        Native.GetWindowThreadProcessId(taskbar, out var processId);
        var diagnostics = Volatile.Read(ref _diagnostics);
        if (diagnostics is null || _diagnosticsProcessId != processId || !diagnostics.Session.IsConnected)
        {
            var lease = ShellDiagnosticsBroker.Acquire(processId, Path.Combine(AppContext.BaseDirectory, "native"));
            var previous = Interlocked.Exchange(ref _diagnostics, lease);
            previous?.Dispose();
            _diagnosticsProcessId = processId;
            diagnostics = lease;
            if (!IsActive || generation != Volatile.Read(ref _activationGeneration))
            {
                if (ReferenceEquals(Interlocked.CompareExchange(ref _diagnostics, null, lease), lease)) lease.Dispose();
                return [];
            }
        }
        var nodes = diagnostics.Session.ReadVisualTree();
        Volatile.Write(ref _shellTree, nodes);
        return nodes;
    }

    private void OnWheel(POINT point, int delta)
    {
        if (!IsActive || !_scrollVolume || delta == 0 || !ShellTaskbar.IsTaskbarPoint(point)) return;
        Interlocked.Add(ref _pendingVolumeSteps, delta > 0 ? _volumeStep : -_volumeStep);
        if (Interlocked.Exchange(ref _volumeUpdateQueued, 1) != 0) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref _volumeUpdateQueued, 0);
            Interlocked.Exchange(ref _pendingVolumeSteps, 0);
            return;
        }
        var generation = Volatile.Read(ref _activationGeneration);
        dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _volumeUpdateQueued, 0);
            var steps = Interlocked.Exchange(ref _pendingVolumeSteps, 0);
            if (!IsActive || generation != Volatile.Read(ref _activationGeneration) || steps == 0) return;
            try
            {
                var percent = VolumeService.Step(steps);
                var now = _volumeClock.ElapsedMilliseconds;
                if (now - _lastVolumeTick < 80) return;
                _lastVolumeTick = now;
                _context?.Notify($"Ses: {percent}%");
            }
            catch (Exception exception) { _context?.Log.Error("Ses düzeyi değiştirilemedi.", exception); }
        });
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

        var align = IndexOf(AlignOptions, settings.GetString("align", AlignOptions[0]));
        WriteDword(AdvancedKey, "TaskbarAl", align == 0 ? 1 : 0);
        WriteDword(AdvancedKey, "TaskbarEndTask", settings.GetBool("end-task") ? 1 : 0);

        BroadcastSettingChange();
    }

    private void ApplyAppearance()
    {
        var settings = _context!.Settings;

        var iconSize = IndexOf(IconSizeOptions, settings.GetString("icon-size", IconSizeOptions[1]));
        WriteDword(AdvancedKey, "TaskbarSi", iconSize);

        WriteDword(PersonalizeKey, "EnableTransparency", settings.GetBool("transparency", true) ? 1 : 0);
        WriteDword(PersonalizeKey, "ColorPrevalence", settings.GetBool("accent-taskbar") ? 1 : 0);
        WriteDword(ExplorerKey, "EnableAutoTray", settings.GetBool("show-all-tray") ? 0 : 1);

        BroadcastSettingChange();
    }

    private void RestoreAppearance()
    {
        var settings = _context?.Settings;
        if (settings is null) return;

        RestoreValue(settings, OriginalIconSizeKey, AdvancedKey, "TaskbarSi");
        RestoreValue(settings, OriginalTransparencyKey, PersonalizeKey, "EnableTransparency");
        RestoreValue(settings, OriginalAccentTaskbarKey, PersonalizeKey, "ColorPrevalence");
        RestoreValue(settings, OriginalAutoTrayKey, ExplorerKey, "EnableAutoTray");
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

        if (settings.GetString("align").Length == 0)
            settings.Set("align", (ReadDword(AdvancedKey, "TaskbarAl") ?? 1) == 0 ? AlignOptions[1] : AlignOptions[0]);

        if (settings.GetString("end-task").Length == 0)
            settings.Set("end-task", (ReadDword(AdvancedKey, "TaskbarEndTask") ?? 0) == 1);

        if (settings.GetString("icon-size").Length == 0)
        {
            var size = ReadDword(AdvancedKey, "TaskbarSi") ?? 1;
            settings.Set("icon-size", IconSizeOptions[Math.Clamp(size, 0, 2)]);
        }

        if (settings.GetString("transparency").Length == 0)
            settings.Set("transparency", (ReadDword(PersonalizeKey, "EnableTransparency") ?? 1) == 1);

        if (settings.GetString("accent-taskbar").Length == 0)
            settings.Set("accent-taskbar", (ReadDword(PersonalizeKey, "ColorPrevalence") ?? 0) == 1);

        if (settings.GetString("show-all-tray").Length == 0)
            settings.Set("show-all-tray", (ReadDword(ExplorerKey, "EnableAutoTray") ?? 1) == 0);
    }

    private void CaptureOriginals()
    {
        var settings = _context!.Settings;
        Capture(settings, OriginalSearchKey, SearchKey, "SearchboxTaskbarMode");
        Capture(settings, OriginalTaskViewKey, AdvancedKey, "ShowTaskViewButton");
        Capture(settings, OriginalWidgetsKey, AdvancedKey, "TaskbarDa");
        Capture(settings, OriginalCopilotKey, AdvancedKey, "ShowCopilotButton");
        Capture(settings, OriginalCombineKey, AdvancedKey, "TaskbarGlomLevel");
        Capture(settings, OriginalAlignKey, AdvancedKey, "TaskbarAl");
        Capture(settings, OriginalEndTaskKey, AdvancedKey, "TaskbarEndTask");
        Capture(settings, OriginalIconSizeKey, AdvancedKey, "TaskbarSi");
        Capture(settings, OriginalTransparencyKey, PersonalizeKey, "EnableTransparency");
        Capture(settings, OriginalAccentTaskbarKey, PersonalizeKey, "ColorPrevalence");
        Capture(settings, OriginalAutoTrayKey, ExplorerKey, "EnableAutoTray");
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
        RestoreValue(settings, OriginalAlignKey, AdvancedKey, "TaskbarAl");
        RestoreValue(settings, OriginalEndTaskKey, AdvancedKey, "TaskbarEndTask");
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

    private static void BroadcastSettingChange() => _ = BroadcastSettingChangeAsync();

    private static Task BroadcastSettingChangeAsync() =>
        SettingsChangeBroadcaster.BroadcastAsync("TraySettings", "ImmersiveColorSet", "intl");
}
