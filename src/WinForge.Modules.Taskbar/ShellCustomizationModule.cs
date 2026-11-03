using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

internal sealed class ShellCustomizationModule : ITweakModule, IAsyncTweakModule
{
    internal ShellCustomizationModule() { }
    private static readonly TimeSpan ApplicationTimeout = TimeSpan.FromSeconds(3);
    private IModuleContext? _context;
    private ShellDiagnosticsSession? _session;
    private ShellDiagnosticsBroker.Lease? _lease;
    private DispatcherTimer? _timer;
    private uint _processId;
    private string _lastRules = "";
    private IReadOnlyList<ShellVisualNode> _tree = [];
    private string? _error;
    private int _observedErrors;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public string Id => "shell-customization";
    public string Name => "Görev Çubuğu Tasarımı";
    public string Category => "Taskbar";
    public string Description => "Kendi WinForge kabuk motoruyla Başlat düğmesi, renkler, simge boyutu, düğme aralığı, tepsi düzeni ve yerel saat biçimleri.";
    public string? Warning => _error;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [
        new() { Key = "hide-start", Label = "Başlat düğmesini gizle", Section = "Görünürlük", Default = false, Description = "Başlat menüsü Windows tuşuyla açılmaya devam eder." },
        new() { Key = "theme", Label = "WinForge renk düzeni", Section = "Tasarım", Kind = SettingKind.Choice, Options = TaskbarStyleCatalog.Themes, Default = TaskbarStyleCatalog.Themes[0] },
        new() { Key = "custom-background", Label = "Özel görev çubuğu arka planı", Section = "Tasarım", Default = false, Description = "Seçilen arka plan rengini ve opaklığı uygular. Kapatıldığında seçili renk düzeni kullanılır." },
        new() { Key = "background-color", Label = "Görev çubuğu arka plan rengi", Section = "Tasarım", Kind = SettingKind.Text, Default = "#171B24", Description = "Özel arka plan açıkken uygulanır. Renk seçiciyi veya #RRGGBB biçimini kullanın." },
        new() { Key = "background-opacity", Label = "Görev çubuğu arka plan opaklığı (%)", Section = "Tasarım", Kind = SettingKind.Number, Min = 0, Max = 100, Default = 100, Description = "Özel arka plan açıkken uygulanır. 0: tamamen saydam, 100: tamamen opak. Simgeleri ve yazıları etkilemez." },
        new() { Key = "app-icon-size", Label = "Uygulama simgesi boyutu (0: Windows)", Kind = SettingKind.Number, Min = 0, Max = 40, Default = 0, Section = "Tasarım" },
        new() { Key = "taskbar-height", Label = "Görev çubuğu yüksekliği (0: Windows)", Section = "Tasarım", Kind = SettingKind.Number, Min = 0, Max = 96, Default = 0, Description = "Alt yatay görev çubuğu içindir. Özel değerler 24–96 aralığında uygulanır." },
        new() { Key = "button-width", Label = "Uygulama düğmesi genişliği (0: Windows)", Section = "Tasarım", Kind = SettingKind.Number, Min = 0, Max = 160, Default = 0 },
        new() { Key = "tray-two-rows", Label = "Bildirim simgelerini iki satırda göster", Section = "Bildirim alanı", Default = false },
        new() { Key = "tray-icon-size", Label = "Görünen tepsi simgesi alan boyutu (0: Windows)", Section = "Bildirim alanı", Kind = SettingKind.Number, Min = 0, Max = 48, Default = 0, Description = "Her simgenin genişliğini ve yüksekliğini ayarlar. Özel değerler 12–48 aralığında uygulanır." },
        new() { Key = "tray-icon-padding", Label = "Görünen simgelerin iç boşluğu (-1: Windows)", Section = "Bildirim alanı", Kind = SettingKind.Number, Min = -1, Max = 16, Default = -1, Description = "Her simgenin dört tarafına uygulanır. Yatay ve dikey ayarlar bu değeri ayrı ayrı geçersiz kılabilir. Tek eksen ayarlanırsa diğer eksen ortak değeri, ortak değer yoksa 0 kullanır." },
        new() { Key = "tray-icon-padding-x", Label = "Görünen simgelerin yatay iç boşluğu (-1: ortak)", Section = "Bildirim alanı", Kind = SettingKind.Number, Min = -1, Max = 16, Default = -1 },
        new() { Key = "tray-icon-padding-y", Label = "Görünen simgelerin dikey iç boşluğu (-1: ortak)", Section = "Bildirim alanı", Kind = SettingKind.Number, Min = -1, Max = 16, Default = -1 },
        .. TaskbarTrayLayout.Settings,
        new() { Key = "tray-icon-gap", Label = "Uygulama tepsisi simgeleri arası boşluk (-1: Windows)", Section = "Bildirim alanı", Kind = SettingKind.Number, Min = -1, Max = 24, Default = -1, Description = "Her uygulama simgesinin iki yanındaki boşluk. 0: dış boşluğu kaldırır; simgenin kendi alanı korunur." },
        new() { Key = "system-icon-gap", Label = "Sistem simgeleri arası boşluk (-1: Windows)", Section = "Bildirim alanı", Kind = SettingKind.Number, Min = -1, Max = 24, Default = -1, Description = "Ses, ağ, pil, dil ve diğer sistem simgelerinin iki yanındaki boşluk." },
        new() { Key = "tray-group-gap", Label = "Sağdaki öğe grupları arası boşluk (-1: Windows)", Section = "Bildirim alanı", Kind = SettingKind.Number, Min = -1, Max = 24, Default = -1, Description = "Gizli simgeler oku, uygulama tepsisi ve Hızlı Ayarlar gruplarının iki yanındaki boşluk." },
        new() { Key = "clock-display", Label = "Saat alanında göster", Kind = SettingKind.Choice, Options = TaskbarStyleCatalog.ClockDisplays, Default = TaskbarStyleCatalog.ClockDisplays[0], Section = "Saat", Description = "Tarih ve saati birlikte veya yalnızca birini gösterir. Biçim seçimleriniz korunur." },
        new() { Key = "time-format", Label = "Saat biçimi", Kind = SettingKind.Text, Options = TaskbarStyleCatalog.TimeFormats, OptionLabels = TaskbarStyleCatalog.FormatLabels(TaskbarStyleCatalog.TimeFormats), Default = "", Section = "Saat", Description = "Listeden seçin veya elle düzenleyin. HH: 24 saat, hh: 12 saat, mm: dakika, ss: saniye, tt: gün dönemi. Boş değer Windows saatini korur." },
        new() { Key = "date-format", Label = "Tarih biçimi", Kind = SettingKind.Text, Options = TaskbarStyleCatalog.DateFormats, OptionLabels = TaskbarStyleCatalog.FormatLabels(TaskbarStyleCatalog.DateFormats), Default = "", Section = "Saat", Description = "Listeden seçin veya elle düzenleyin. dd: gün, MM: ay, MMMM: ay adı, yyyy: yıl, dddd: gün adı. Adlar Windows dilini kullanır; boş değer Windows tarihini korur." },
    ];

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        try
        {
            ApplyRules(force: true);
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
            IsActive = true;
        }
        catch { Deactivate(); throw; }
    }

    public void Apply()
    {
        if (!IsActive) return;
        ApplyRules(force: true);
    }

    public async Task ActivateAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            if (IsActive) return;
            var rules = TaskbarStyleCatalog.Build(_context!.Settings, DateTime.Now);
            var preferences = TaskbarSystemIcons.Capture(_context.Settings);
            try
            {
                await Task.Run(() => ApplyRules(force: true, rules, preferences));
                _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
                IsActive = true;
            }
            catch { await Task.Run(ReleaseConnection); throw; }
        }
        finally { _operationGate.Release(); }
    }

    public async Task ApplyAsync() => await UpdateAsync(force: true);

    private async Task UpdateAsync(bool force)
    {
        await _operationGate.WaitAsync();
        try
        {
            if (!IsActive) return;
            var rules = TaskbarStyleCatalog.Build(_context!.Settings, DateTime.Now);
            var preferences = TaskbarSystemIcons.Capture(_context.Settings);
            await Task.Run(() => ApplyRules(force, rules, preferences));
            _error = null;
        }
        finally { _operationGate.Release(); }
    }

    private async void OnTick(object? sender, EventArgs arguments)
    {
        if (_operationGate.CurrentCount == 0) return;
        try { await UpdateAsync(force: false); }
        catch (Exception exception)
        {
            if (_error != exception.Message) _context?.Log.Error("Kabuk özelleştirmesi uygulanamadı.", exception);
            _error = exception.Message;
        }
    }

    private void EnsureConnection()
    {
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) throw new InvalidOperationException("Windows görev çubuğu bulunamadı.");
        Native.GetWindowThreadProcessId(taskbar, out var processId);
        if (_session is not null && _processId == processId && _session.IsConnected) return;
        _lease?.Dispose();
        _lease = null;
        _session = null;
        _lastRules = "";
        _tree = [];
        var directory = Path.Combine(AppContext.BaseDirectory, "native");
        _lease = ShellDiagnosticsBroker.Acquire(processId, directory);
        _session = _lease.Session;
        _observedErrors = _session.Status.Errors;
        _processId = processId;
        var clock = Stopwatch.StartNew();
        while (!_session.ReadVisualTree().Any(node => node.Type == "Taskbar.TaskbarFrame"))
        {
            if (clock.Elapsed >= ApplicationTimeout) throw new InvalidOperationException("Windows görev çubuğunun görsel ağacı doğrulanamadı.");
            Thread.Sleep(20);
        }
    }

    private void ApplyRules(bool force, IReadOnlyList<ShellStyleRule>? preparedRules = null, TaskbarSystemIcons.Preferences? preparedPreferences = null)
    {
        var baseRules = preparedRules ?? TaskbarStyleCatalog.Build(_context!.Settings, DateTime.Now);
        var preferences = preparedPreferences ?? TaskbarSystemIcons.Capture(_context!.Settings);
        if (baseRules.Count == 0 && !preferences.IsConfigured && _session is null) return;
        EnsureConnection();
        var session = _session!;
        if (!force && session.Status.Errors != _observedErrors)
        {
            _observedErrors = session.Status.Errors;
            _lease!.Apply([]);
            _lastRules = "";
            throw new InvalidOperationException("Windows görev çubuğu değişikliğini koruyamadı; özelleştirme geri alındı.");
        }
        var tree = session.ReadVisualTree();
        if (tree.Count > 0) _tree = tree;
        var rules = baseRules.Concat(TaskbarSystemIcons.Build(preferences, _tree)).ToArray();
        var serialized = JsonSerializer.Serialize(rules);
        if (!force && serialized == _lastRules) return;
        var before = session.Status.Errors;
        var revision = _lease!.Apply(rules);
        var clock = Stopwatch.StartNew();
        while (session.Status.AppliedRevision < revision)
        {
            if (clock.Elapsed >= ApplicationTimeout) throw new TimeoutException("Görev çubuğu değişikliği Windows tarafından doğrulanmadı.");
            Thread.Sleep(20);
        }
        if (session.Status.Errors != before)
        {
            _observedErrors = session.Status.Errors;
            _lease.Apply([]);
            throw new InvalidOperationException("Bu Windows sürümü seçilen görev çubuğu özelliğini uygulayamadı; değişiklikler geri alındı.");
        }
        _lastRules = serialized;
        _observedErrors = session.Status.Errors;
        _error = null;
    }

    public void Deactivate()
    {
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTick;
        _timer = null;
        ReleaseConnection();
    }

    public async Task DeactivateAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            _timer?.Stop();
            if (_timer is not null) _timer.Tick -= OnTick;
            _timer = null;
            await Task.Run(ReleaseConnection);
        }
        finally { _operationGate.Release(); }
    }

    private void ReleaseConnection()
    {
        try { _lease?.Dispose(); }
        finally
        {
            _lease = null;
            _session = null;
            _processId = 0;
            _lastRules = "";
            IsActive = false;
            _tree = [];
            _error = null;
        }
    }
}
