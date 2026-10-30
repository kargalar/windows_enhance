using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

public sealed class ShellCustomizationModule : ITweakModule, IAsyncTweakModule
{
    private static readonly TimeSpan ApplicationTimeout = TimeSpan.FromSeconds(3);
    private IModuleContext? _context;
    private ShellDiagnosticsSession? _session;
    private ShellDiagnosticsBroker.Lease? _lease;
    private DispatcherTimer? _timer;
    private uint _processId;
    private string _lastRules = "";
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
        new() { Key = "hide-start", Label = "Başlat düğmesini gizle", Default = false, Description = "Başlat menüsü Windows tuşuyla açılmaya devam eder." },
        new() { Key = "theme", Label = "WinForge renk düzeni", Kind = SettingKind.Choice, Options = TaskbarStyleCatalog.Themes, Default = TaskbarStyleCatalog.Themes[0] },
        new() { Key = "icon-size", Label = "Uygulama simgesi boyutu (0: Windows)", Kind = SettingKind.Number, Min = 0, Max = 40, Default = 0 },
        new() { Key = "taskbar-height", Label = "Görev çubuğu yüksekliği (0: Windows)", Kind = SettingKind.Number, Min = 0, Max = 96, Default = 0, Description = "Alt yatay görev çubuğu içindir. Özel değerler 48–96 aralığında uygulanır." },
        new() { Key = "button-width", Label = "Uygulama düğmesi genişliği (0: Windows)", Kind = SettingKind.Number, Min = 0, Max = 160, Default = 0 },
        new() { Key = "tray-two-rows", Label = "Bildirim simgelerini iki satırda göster", Default = false },
        new() { Key = "time-format", Label = "Saat biçimi", Kind = SettingKind.Text, Default = "", Section = "Saat", Description = "Örn: HH:mm:ss. Yalnızca görev çubuğunu etkiler; boş değer Windows saatini korur." },
        new() { Key = "date-format", Label = "Tarih biçimi", Kind = SettingKind.Text, Default = "", Section = "Saat", Description = "Örn: dd MMM dddd. Boş değer Windows tarihini korur." },
        new() { Key = "clock-width", Label = "Saat alanı genişliği", Kind = SettingKind.Number, Default = 120, Min = 60, Max = 360, Section = "Saat" }
    ];

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        try
        {
            EnsureConnection();
            ApplyRules(force: true);
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
            IsActive = true;
        }
        catch { Deactivate(); throw; }
    }

    public void Apply()
    {
        if (!IsActive) return;
        EnsureConnection();
        ApplyRules(force: true);
    }

    public async Task ActivateAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            if (IsActive) return;
            var rules = TaskbarStyleCatalog.Build(_context!.Settings, DateTime.Now);
            try
            {
                await Task.Run(() => { EnsureConnection(); ApplyRules(force: true, rules); });
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
            await Task.Run(() => { EnsureConnection(); ApplyRules(force, rules); });
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

    private void ApplyRules(bool force, IReadOnlyList<ShellStyleRule>? preparedRules = null)
    {
        var session = _session!;
        if (!force && session.Status.Errors != _observedErrors)
        {
            _observedErrors = session.Status.Errors;
            _lease!.Apply([]);
            _lastRules = "";
            throw new InvalidOperationException("Windows görev çubuğu değişikliğini koruyamadı; özelleştirme geri alındı.");
        }
        var rules = preparedRules ?? TaskbarStyleCatalog.Build(_context!.Settings, DateTime.Now);
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
        }
    }
}
