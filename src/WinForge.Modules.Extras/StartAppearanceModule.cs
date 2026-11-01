using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Customizes the actual Windows Start XAML tree using the shared shell diagnostics engine.</summary>
public sealed class StartAppearanceModule : ITweakModule, IAsyncTweakModule
{
    private const int MinimumIconSize = 16;
    private const int MaximumIconSize = 48;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IModuleContext? _context;
    private ShellDiagnosticsBroker.Lease? _lease;
    private DispatcherTimer? _timer;
    private uint _processId;
    private string _lastRules = "";
    private string? _warning;
    private int _errors;

    public string Id => "start-appearance";
    public string Name => "Başlat Menüsü Tasarımı";
    public string Category => "Başlat";
    public string Description => "Windows Başlat menüsünün boyutunu ve görünümünü özelleştirin.";
    public string? Warning => _warning;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [
        new() { Key = "width", Label = "Menü genişliği (0: Windows)", Kind = SettingKind.Number, Min = 0, Max = 2000, Default = 0, Section = "Boyut", Description = "Mantıksal piksel; özel değerler 320–2000 aralığındadır." },
        new() { Key = "height", Label = "Menü yüksekliği (0: Windows)", Kind = SettingKind.Number, Min = 0, Max = 1600, Default = 0, Section = "Boyut", Description = "Mantıksal piksel; özel değerler 320–1600 aralığındadır." },
        new() { Key = "icon-size", Label = "Uygulama simgesi boyutu (0: Windows)", Kind = SettingKind.Number, Min = 0, Max = MaximumIconSize, Default = 0, Section = "Boyut", Description = "Yeni Windows 11 Başlat menüsündeki uygulama simgelerini 16–48 mantıksal piksel arasında ayarlar. Klasörlerin küçük önizleme simgeleri korunur." },
        new() { Key = "opacity", Label = "Menü opaklığı (%)", Kind = SettingKind.Number, Min = 20, Max = 100, Default = 100, Section = "Görünüm" },
        new() { Key = "hide-labels", Label = "Simge altındaki adları gizle", Default = false, Section = "Görünüm", Description = "Yeni Windows 11 Başlat menüsünde sabitlenmiş uygulama ve klasörlerin adlarını gizleyerek yalnızca simgelerini gösterir." },
        new() { Key = "hide-search", Label = "Arama alanını gizle", Default = false, Section = "Bölümler" },
        new() { Key = "pinned-only", Label = "Yalnızca sabitlenmiş uygulamaları göster", Default = false, Section = "Bölümler", Description = "Yeni Windows 11 Başlat menüsünde Tüm uygulamalar başlığını, görünüm seçicisini, uygulama listesini/kategorilerini ve önerileri gizler." },
        new() { Key = "hide-user", Label = "Kullanıcı alanını gizle", Default = false, Section = "Bölümler" },
        new() { Key = "hide-power", Label = "Güç düğmesini gizle", Default = false, Section = "Bölümler" },
        new() { Key = "custom-rules", Label = "Özel XAML kuralları (JSON)", Kind = SettingKind.Text, Default = "", Section = "Gelişmiş", Description = "Örnek: [{\"selector\":\"#MainMenu\",\"properties\":{\"Opacity\":\"0.8\"}}]. Özellik desteği Windows sürümüne ve öğe türüne bağlıdır." }
    ];

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate()
    {
        if (IsActive) return;
        try
        {
            Update(ReadSettings(), true);
            IsActive = true;
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
        }
        catch { Release(); throw; }
    }

    public void Apply() { if (IsActive) Update(ReadSettings(), true); }
    public void Deactivate()
    {
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTick;
        _timer = null;
        Release();
    }

    public async Task ActivateAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (IsActive) return;
            var settings = ReadSettings();
            try
            {
                await Task.Run(() => Update(settings, true));
                IsActive = true;
                _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
            }
            catch { Release(); throw; }
        }
        finally { _gate.Release(); }
    }

    public async Task ApplyAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsActive) return;
            var settings = ReadSettings();
            await Task.Run(() => Update(settings, true));
        }
        finally { _gate.Release(); }
    }

    public async Task DeactivateAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _timer?.Stop();
            if (_timer is not null) _timer.Tick -= OnTick;
            _timer = null;
            await Task.Run(Release);
        }
        finally { _gate.Release(); }
    }

    private async void OnTick(object? sender, EventArgs args)
    {
        if (_gate.CurrentCount == 0) return;
        try
        {
            await _gate.WaitAsync();
            try { if (IsActive) { var settings = ReadSettings(); await Task.Run(() => Update(settings, false)); } }
            finally { _gate.Release(); }
        }
        catch (Exception exception)
        {
            if (_warning != exception.Message) _context?.Log.Error("Başlat menüsü tasarımı uygulanamadı.", exception);
            _warning = exception.Message;
        }
    }

    private AppearanceSettings ReadSettings()
    {
        var settings = _context!.Settings;
        return new(settings.GetInt("width"), settings.GetInt("height"), settings.GetInt("icon-size"), settings.GetInt("opacity", 100),
            settings.GetBool("hide-search"), settings.GetBool("pinned-only"), settings.GetBool("hide-labels"), settings.GetBool("hide-user"), settings.GetBool("hide-power"), settings.GetString("custom-rules"));
    }

    private void Update(AppearanceSettings settings, bool force)
    {
        using var process = Process.GetProcessesByName("StartMenuExperienceHost").FirstOrDefault()
            ?? throw new InvalidOperationException("Windows Başlat menüsü işlemi bulunamadı. Başlat menüsünü açıp tekrar deneyin.");
        if (_lease is null || !_lease.Session.IsConnected || _processId != (uint)process.Id)
        {
            _lease?.Dispose();
            _lease = ShellDiagnosticsBroker.Acquire((uint)process.Id, Path.Combine(AppContext.BaseDirectory, "native"));
            _processId = (uint)process.Id;
            _lastRules = "";
            _errors = _lease.Session.Status.Errors;
        }
        var session = _lease.Session;
        var clock = Stopwatch.StartNew();
        IReadOnlyList<ShellVisualNode> tree;
        do
        {
            tree = session.ReadVisualTree();
            if (tree.Any(node => node.Type is "StartMenu.StartBlendedFlexFrame" or "StartDocked.StartSizingFrame")) break;
            if (clock.Elapsed >= Timeout) throw new InvalidOperationException("Başlat menüsünün desteklenen görsel ağacı bulunamadı. Menüyü açıp tekrar deneyin.");
            Thread.Sleep(50);
        } while (true);
        var redesigned = tree.Any(node => node.Type == "StartMenu.StartBlendedFlexFrame");
        var rules = BuildRules(settings, redesigned);
        var serialized = JsonSerializer.Serialize(rules);
        if (!force && session.Status.Errors != _errors)
        {
            _lease.Apply([]);
            _lastRules = "";
            _errors = session.Status.Errors;
            throw new InvalidOperationException("Windows Başlat menüsü stilini uygulayamadı; değişiklikler geri alındı.");
        }
        if (!force && serialized == _lastRules) return;
        var before = session.Status.Errors;
        var revision = _lease.Apply(rules);
        clock.Restart();
        while (session.Status.AppliedRevision < revision)
        {
            if (clock.Elapsed >= Timeout) throw new TimeoutException("Başlat menüsü değişikliği Windows tarafından doğrulanmadı.");
            Thread.Sleep(20);
        }
        if (session.Status.Errors != before)
        {
            _lease.Apply([]);
            _errors = session.Status.Errors;
            _lastRules = "";
            throw new InvalidOperationException("Bu Windows sürümü seçilen Başlat menüsü stilini uygulayamadı; değişiklikler geri alındı.");
        }
        _lastRules = serialized;
        _errors = session.Status.Errors;
        _warning = null;
    }

    private static IReadOnlyList<ShellStyleRule> BuildRules(AppearanceSettings settings, bool redesigned)
    {
        var rules = new List<ShellStyleRule>();
        var width = settings.Width == 0 ? 0 : Math.Clamp(settings.Width, 320, 2000);
        var height = settings.Height == 0 ? 0 : Math.Clamp(settings.Height, 320, 1600);
        var widthTarget = redesigned ? "StartMenu.StartBlendedFlexFrame #MainMenu" : "StartDocked.StartSizingFrame";
        var heightTarget = redesigned ? "StartMenu.StartBlendedFlexFrame #FrameRoot" : "StartDocked.StartSizingFrame";
        if (width > 0)
        {
            var value = (width - 24).ToString(CultureInfo.InvariantCulture);
            Add(widthTarget, ("Width", value), ("MinWidth", value), ("MaxWidth", value));
            if (!redesigned) Add("StartDocked.LauncherFrame #RootGrid", ("Width", value), ("MinWidth", value), ("MaxWidth", value));
        }
        if (height > 0)
        {
            var value = (redesigned ? height : height - 24).ToString(CultureInfo.InvariantCulture);
            if (redesigned) Add(heightTarget, ("Height", value), ("MinHeight", value), ("MaxHeight", value), ("Margin", "0"));
            else Add(heightTarget, ("Height", value), ("MinHeight", value), ("MaxHeight", value));
        }
        if (settings.IconSize != 0)
        {
            if (!redesigned) throw new InvalidOperationException("Uygulama simgesi boyutu ayarı yeni Windows 11 Başlat menüsünü gerektirir.");
            var iconSize = Math.Clamp(settings.IconSize, MinimumIconSize, MaximumIconSize).ToString(CultureInfo.InvariantCulture);
            // Resize the surrounding plates as well so the image stays within its layout bounds.
            foreach (var name in new[] { "Logo", "LogoContainer", "LogoPlate", "FolderHintPlate" })
                Add($"StartMenu.PinnedListTile #{name}", ("Width", iconSize), ("Height", iconSize));
            Add("StartMenu.StartHome Image#AllAppsTileLogo", ("Width", iconSize), ("Height", iconSize));
        }
        if (settings.Opacity < 100) Add(redesigned ? "StartMenu.StartBlendedFlexFrame #MainMenu" : "StartDocked.StartSizingFrame", ("Opacity", (Math.Clamp(settings.Opacity, 20, 100) / 100.0).ToString(CultureInfo.InvariantCulture)));
        if (settings.HideLabels)
        {
            if (!redesigned) throw new InvalidOperationException("Simge altındaki adları gizleme özelliği yeni Windows 11 Başlat menüsünü gerektirir.");
            Add("StartMenu.PinnedList TextBlock#DisplayName", ("Visibility", "Collapsed"));
        }
        if (settings.HideSearch) Add("StartMenu.SearchBoxToggleButton", ("Visibility", "Collapsed"));
        if (settings.PinnedOnly)
        {
            if (!redesigned) throw new InvalidOperationException("Yalnızca sabitlenmiş uygulamaları gösterme özelliği yeni Windows 11 Başlat menüsünü gerektirir.");
            Add("StartMenu.StartHome #AllListHeading", ("Visibility", "Collapsed"));
            Add("StartMenu.StartHome #TopLevelSuggestionsRoot", ("Visibility", "Collapsed"));
            // The pinned section is the grid's header: hide only its items panel, preserving the header.
            Add("ItemsWrapGrid[ItemsControl=AllAppsGrid]", ("Visibility", "Collapsed"));
        }
        if (settings.HideUser) Add("StartDocked.UserTileView", ("Visibility", "Collapsed"));
        if (settings.HidePower) Add("StartDocked.PowerOptionsView", ("Visibility", "Collapsed"));
        if (!string.IsNullOrWhiteSpace(settings.CustomRules))
        {
            try
            {
                var custom = JsonSerializer.Deserialize<List<ShellStyleRule>>(settings.CustomRules, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new JsonException();
                if (custom.Count > 100 || custom.Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Selector) || rule.Properties is null || rule.Properties.Count == 0 || rule.Properties.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null))) throw new JsonException();
                rules.AddRange(custom);
            }
            catch (JsonException exception) { throw new ArgumentException("Özel XAML kuralları geçerli bir JSON kural listesi olmalıdır.", exception); }
        }
        return rules;

        void Add(string selector, params (string Name, string Value)[] properties) =>
            rules.Add(new ShellStyleRule(selector, properties.ToDictionary(property => property.Name, property => property.Value)));
    }

    private void Release()
    {
        try { _lease?.Dispose(); }
        finally { _lease = null; _processId = 0; _lastRules = ""; IsActive = false; }
    }

    private sealed record AppearanceSettings(int Width, int Height, int IconSize, int Opacity, bool HideSearch, bool PinnedOnly, bool HideLabels, bool HideUser, bool HidePower, string CustomRules);
}
