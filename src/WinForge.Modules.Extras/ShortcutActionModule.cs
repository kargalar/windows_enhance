using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed record ShortcutActionRule(string Id, string Name, bool Enabled, string Shortcut, string Action, string Value = "", string Arguments = "");

public abstract class ShortcutActionModule : ITweakModule, IModulePanel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static event Action? CaptureChanged;
    private static bool _capturing;
    private static readonly DispatcherTimer CaptureTimer = CreateCaptureTimer();
    private readonly List<IDisposable> _bindings = new();
    private List<ShortcutActionRule> _rules = new();
    protected IReadOnlyList<ShortcutActionRule> Rules => _rules;
    protected static bool IsCapturing => _capturing;
    private readonly Dictionary<string, string> _saveErrors = new();
    private CancellationTokenSource _cancel = new();
    private bool _busy;
    private IFeatureUiWindow? _displayConfirmation;
    protected IModuleContext Context = null!;
    public abstract string Id { get; }
    public abstract string Name { get; }
    public string Category => "Verimlilik";
    public string Description => Id switch
    {
        "monitor-shortcuts" => "Monitör düzenlerini kaydedin; kısayolla ekranları etkinleştirin ve düzenler arasında geçin. Harici program gerekmez.",
        "media-shortcuts" => "Oynat/duraklat, parça geçişi ve ses için klavye veya fare kısayolları atayın.",
        "window-shortcuts" => "Etkin pencereyi kapatma, küçültme ve büyütme kısayollarını belirleyin.",
        "desktop-shortcuts" => "Sanal masaüstleri arasında klavye veya fare kısayollarıyla geçin.",
        "text-shortcuts" => "Kısayolla metin yazdırın veya kısaltmaları boşlukla genişletin. Metinler bu Windows hesabına bağlı şifreleme ile saklanır; panoya kopyalanmaz.",
        "combination-translator" => "Sabit atanmış bir kombinasyonu başka kombinasyona çevirin: örneğin Ctrl+D → Alt+W.",
        _ => "İstediğiniz programı kendi kısayolunuzla açın; isteğe bağlı argüman ekleyin."
    };
    public string? Warning => null;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => Array.Empty<SettingDescriptor>();
    public event Action? PanelStateChanged;
    protected abstract IReadOnlyDictionary<string, string> Actions { get; }
    protected virtual IEnumerable<ShortcutActionRule> Defaults => [];
    private static DispatcherTimer CreateCaptureTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => SetCapture(false);
        return timer;
    }
    private static void SetCapture(bool value)
    {
        _capturing = value; CaptureTimer.Stop(); if (value) CaptureTimer.Start();
        CaptureChanged?.Invoke();
    }
    public void Initialize(IModuleContext context)
    {
        Context = context;
        if (Id == "monitor-shortcuts") MonitorLayouts.ErrorHandler = ex => context.Notify("Ekran düzeni geri alınamadı: " + ex.Message);
        _rules = JsonSerializer.Deserialize<List<ShortcutActionRule>>(context.Settings.GetString("rules", "null"), Json) ?? Defaults.ToList();
        var defaultRuleId = Id switch
        {
            "media-shortcuts" => "mouse-volume-down-5",
            "program-shortcuts" => "mouse-downloads",
            _ => null
        };
        if (defaultRuleId is not null && !context.Settings.GetBool(defaultRuleId + "-added"))
        {
            var shortcut = Defaults.First(r => r.Id == defaultRuleId);
            if (_rules.Count < 64 && !_rules.Any(r => r.Id == shortcut.Id))
                _rules.Add(shortcut with { Enabled = !_rules.Any(r => r.Enabled && ActionShortcut.Parse(r.Shortcut) == ActionShortcut.Parse(shortcut.Shortcut)) });
            context.Settings.Set("rules", JsonSerializer.Serialize(_rules, Json));
            context.Settings.Set(defaultRuleId + "-added", true);
        }
        Validate(_rules);
        CaptureChanged += CaptureUpdate;
    }
    private void CaptureUpdate()
    {
        CancelPending(); Clear();
        try { Bind(); } catch (Exception ex) { Context.Notify(ex.Message); }
        PanelStateChanged?.Invoke();
    }
    public object GetPanelState() => new
    {
        kind = "shortcut-actions", capturing = _capturing, saveErrors = _saveErrors,
        actions = Actions.Select(a => new { id = a.Key, name = a.Value }),
        rules = _rules.Select(r => new { r.Id, r.Name, r.Enabled, r.Shortcut, r.Action,
            value = Id == "monitor-shortcuts" ? "" : Id == "text-shortcuts" && r.Value.Length > 0 ? ShortcutSecret.Unprotect(r.Value) : r.Value,
            r.Arguments, hasSecret = Id == "text-shortcuts" && r.Value.Length > 0 }),
        pendingDisplay = Id == "monitor-shortcuts" && MonitorLayouts.Pending
    };
    public void Activate() { IsActive = true; try { Bind(); } catch { Deactivate(); throw; } }
    public void Deactivate() { IsActive = false; CancelPending(); Clear(); if (Id == "monitor-shortcuts") MonitorLayouts.Revert(); }
    public void Apply() { CancelPending(); Clear(); Bind(); }
    private void CancelPending() { _cancel.Cancel(); _cancel.Dispose(); _cancel = new(); }
    protected virtual void Clear() { foreach (var b in _bindings) b.Dispose(); _bindings.Clear(); }
    protected virtual void Bind()
    {
        if (!IsActive || _capturing) return;
        try
        {
            foreach (var r in _rules.Where(r => r.Enabled && r.Action != "text-expand"))
            {
                var shortcut = ActionShortcut.Parse(r.Shortcut);
                Action callback = () => Trigger(r);
                _bindings.Add(shortcut.Mouse.Length > 0 ? ActionMouseBindings.Register(shortcut, callback) : Context.Hotkeys.Register(shortcut.Modifiers, (uint)shortcut.Key, callback, r.Name));
            }
        }
        catch { Clear(); throw; }
    }
    private async void Trigger(ShortcutActionRule rule)
    {
        if (_busy || !IsActive || _capturing || !_rules.Contains(rule)) return;
        _busy = true;
        var token = _cancel.Token;
        var target = Native.GetForegroundWindow();
        try
        {
            // Never send text/Windows chords while the user's physical modifiers remain down.
            var deadline = Environment.TickCount64 + 3000;
            while (Id != "media-shortcuts" && !(Id == "desktop-shortcuts" && rule.Action is "left" or "right") && ActionInput.Modifiers != 0)
            {
                if (Environment.TickCount64 >= deadline) return;
                await Task.Delay(15, token);
            }
            token.ThrowIfCancellationRequested();
            if (target != Native.GetForegroundWindow()) return;
            Execute(rule);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Context.Notify("Kısayol çalıştırılamadı: " + ex.Message); }
        finally { _busy = false; PanelStateChanged?.Invoke(); }
    }
    private void Execute(ShortcutActionRule r)
    {
        switch (r.Action)
        {
            case "play": ActionInput.Chord(0xB3); break;
            case "next": ActionInput.Chord(0xB0); break;
            case "previous": ActionInput.Chord(0xB1); break;
            case "stop": ActionInput.Chord(0xB2); break;
            case "volume-up": VolumeService.Step(VolumeStep(r)); break;
            case "volume-down": VolumeService.Step(-VolumeStep(r)); break;
            case "volume-down-5": VolumeService.Step(-VolumeStep(r)); break;
            case "mute": VolumeService.SetMuted(!VolumeService.IsMuted()); break;
            case "close": ActionInput.Chord(0x12, 0x73); break;
            case "minimize": ShowWindow(Native.GetForegroundWindow(), 6); break;
            case "maximize": ShowWindow(Native.GetForegroundWindow(), IsZoomed(Native.GetForegroundWindow()) ? 9 : 3); break;
            case "left": ActionInput.SwitchDesktop(0x25); break;
            case "right": ActionInput.SwitchDesktop(0x27); break;
            case "create": ActionInput.Chord(0x11, 0x5B, 0x44); break;
            case "text": ActionInput.Text(ShortcutSecret.Unprotect(r.Value)); break;
            case "launch":
            case "downloads":
            case "explorer": Process.Start(CreateLaunchInfo(r)); break;
            case "profile":
                MonitorLayouts.Apply(r.Value, DisplayChanged);
                ConfirmDisplay();
                break;
        }
    }
    internal static int VolumeStep(ShortcutActionRule rule) => string.IsNullOrWhiteSpace(rule.Value)
        ? rule.Action == "volume-down-5" ? 5 : 2
        : int.TryParse(rule.Value, out var step) && step is >= 1 and <= 100 ? step
        : throw new FormatException("Ses değişim miktarı 1–100 arasında olmalı.");

    internal static ProcessStartInfo CreateLaunchInfo(ShortcutActionRule rule)
    {
        if (rule.Action == "downloads")
        {
            var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
            info.ArgumentList.Add("shell:Downloads");
            return info;
        }
        var target = Environment.ExpandEnvironmentVariables(rule.Value.Trim().Trim('"'));
        if (rule.Action == "explorer" || System.IO.Directory.Exists(target))
        {
            if (!System.IO.Directory.Exists(target)) throw new FormatException("Gezgin için mevcut bir klasör yolu girin.");
            var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
            info.ArgumentList.Add(System.IO.Path.GetFullPath(target));
            return info;
        }
        return new ProcessStartInfo(target) { UseShellExecute = true, Arguments = rule.Arguments };
    }
    private void DisplayChanged()
    {
        if (!MonitorLayouts.Pending)
        {
            var window = _displayConfirmation; _displayConfirmation = null; window?.Close();
        }
        PanelStateChanged?.Invoke();
    }
    private void ConfirmDisplay()
    {
        try
        {
            if (Context is IFeatureUiContext { FeatureUi: { } ui })
            {
                var generation = MonitorLayouts.Generation;
                var window = ui.Open("monitor-layout-confirmation", "app-dialog", "Monitör düzeni", new(520, 280, Topmost: true));
                _displayConfirmation = window;
                window.MessageReceived += (command, payload) =>
                {
                    if (command != "dialog.resolve" || generation != MonitorLayouts.Generation || !MonitorLayouts.Pending) return;
                    try
                    {
                        if (payload.TryGetProperty("confirmed", out var value) && value.ValueKind == JsonValueKind.True) MonitorLayouts.Confirm();
                        else MonitorLayouts.Revert();
                    }
                    catch (Exception ex) { Context.Notify(ex.Message); }
                };
                window.ClosedByHost += () =>
                {
                    if (generation != MonitorLayouts.Generation || !MonitorLayouts.Pending) return;
                    try { MonitorLayouts.Revert(); } catch (Exception ex) { Context.Notify(ex.Message); }
                };
                window.SetState(new { title = "Monitör düzeni", message = "Bu ekran düzeni korunsun mu? 20 saniye içinde onaylanmazsa önceki düzene dönülür.", confirmLabel = "Düzeni koru", cancelLabel = "Geri al", isConfirmation = true });
            }
            else Context.Notify("Monitör düzenini 20 saniye içinde modül panelinden onaylayın; aksi halde geri alınır.");
        }
        catch (Exception ex) { Context.Notify(ex.Message); }
    }
    public void HandlePanelCommand(string action, JsonElement payload)
    {
        var ruleId = action == "save" && payload.TryGetProperty("rule", out var rule) && rule.TryGetProperty("id", out var id)
            ? id.GetString() : null;
        try
        {
            ExecutePanelCommand(action, payload);
            if (ruleId is not null) _saveErrors.Remove(ruleId);
        }
        catch (Exception ex)
        {
            if (ruleId is not null) _saveErrors[ruleId] = ex.Message;
            throw;
        }
        finally
        {
            if (ruleId is not null) PanelStateChanged?.Invoke();
        }
    }
    private void ExecutePanelCommand(string action, JsonElement payload)
    {
        if (action == "capture-begin") { SetCapture(true); return; }
        if (action == "capture-end") { SetCapture(false); return; }
        if (Id == "monitor-shortcuts" && action is "confirm-display" or "revert-display")
        {
            if (action == "confirm-display") MonitorLayouts.Confirm(); else MonitorLayouts.Revert();
            PanelStateChanged?.Invoke(); return;
        }
        var next = _rules.ToList();
        switch (action)
        {
            case "add":
                next.Add(new(Guid.NewGuid().ToString("N"), Id == "monitor-shortcuts" ? "Yeni monitör düzeni" : "Yeni kısayol", false, "Ctrl+Alt+F9", Actions.Keys.First(), Id == "monitor-shortcuts" ? MonitorLayouts.Capture() : Id == "combination-translator" ? "Alt+W" : ""));
                break;
            case "snapshot" when Id == "monitor-shortcuts":
                var si = next.FindIndex(r => r.Id == payload.GetProperty("id").GetString());
                if (si < 0) throw new FormatException("Profil bulunamadı.");
                next[si] = next[si] with { Value = MonitorLayouts.Capture() }; break;
            case "delete": next.RemoveAll(r => r.Id == payload.GetProperty("id").GetString()); break;
            case "save":
                var rule = payload.GetProperty("rule").Deserialize<ShortcutActionRule>(Json) ?? throw new FormatException("Kısayol eksik.");
                var index = next.FindIndex(r => r.Id == rule.Id);
                if (index < 0) throw new FormatException("Kısayol bulunamadı.");
                if (Id == "text-shortcuts") rule = rule with { Value = rule.Value.Length == 0 ? next[index].Value : ShortcutSecret.Protect(rule.Value) };
                if (Id == "monitor-shortcuts") rule = rule with { Value = next[index].Value };
                next[index] = rule; break;
            default: throw new FormatException("Bilinmeyen kısayol işlemi.");
        }
        Validate(next);
        var previous = _rules;
        CancelPending(); Clear(); _rules = next;
        try { Bind(); Context.Settings.Set("rules", JsonSerializer.Serialize(_rules, Json)); }
        catch { Clear(); _rules = previous; Bind(); throw; }
        finally { PanelStateChanged?.Invoke(); }
    }
    private void Validate(List<ShortcutActionRule> rules)
    {
        if (rules.Count > 64 || rules.Select(r => r.Id).Distinct().Count() != rules.Count) throw new FormatException("En fazla 64 farklı kısayol eklenebilir.");
        var used = new HashSet<ActionShortcut>();
        var abbreviations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100 || !Actions.ContainsKey(r.Action) || r.Value.Length > 65536 || r.Arguments.Length > 4096) throw new FormatException("Geçersiz kısayol alanı.");
            if (r.Action == "text-expand")
            {
                TextExpansionBinding.Validate(r.Shortcut);
                if (r.Enabled && !abbreviations.Add(r.Shortcut)) throw new FormatException("Aynı kısaltma iki kez kullanılamaz.");
            }
            else
            {
                var key = ActionShortcut.Parse(r.Shortcut);
                if (r.Enabled && !used.Add(key)) throw new FormatException("Aynı kısayol iki kez kullanılamaz.");
            }
            if (r.Enabled && r.Action is "text" or "text-expand" or "launch" or "explorer" && string.IsNullOrWhiteSpace(r.Value)) throw new FormatException("Metin veya program yolu gerekli.");
            if (r.Enabled && r.Action == "explorer") _ = CreateLaunchInfo(r);
            if (r.Action is "volume-up" or "volume-down" or "volume-down-5") _ = VolumeStep(r);
            if (r.Action == "translate") ValidateTranslation(r);
            if (r.Action == "profile") MonitorLayouts.Validate(r.Value);
        }
    }
    protected virtual void ValidateTranslation(ShortcutActionRule rule) { }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
}

public sealed class MediaShortcutsModule : ShortcutActionModule
{
    public override string Id => "media-shortcuts";
    public override string Name => "Medya Kısayolları";
    protected override IReadOnlyDictionary<string, string> Actions => new Dictionary<string, string> { ["play"] = "Oynat / duraklat", ["next"] = "Sonraki parça", ["previous"] = "Önceki parça", ["stop"] = "Durdur", ["volume-up"] = "Sesi artır", ["volume-down"] = "Sesi azalt", ["volume-down-5"] = "Sesi 5 azalt", ["mute"] = "Sessiz / sesli" };
    protected override IEnumerable<ShortcutActionRule> Defaults => new[] { new ShortcutActionRule("play", "Oynat / duraklat", false, "Ctrl+Space", "play"), new("next", "Sonraki parça", false, "Ctrl+N", "next"), new("previous", "Önceki parça", false, "Ctrl+Shift+N", "previous"), new("up", "Sesi artır", false, "Alt+WheelUp", "volume-up"), new("down", "Sesi azalt", false, "Alt+WheelDown", "volume-down"), new("mouse-volume-down-5", "Mouse XButton1 · sesi 5 azalt", true, "MouseX1", "volume-down-5") };
}
public sealed class WindowShortcutsModule : ShortcutActionModule
{
    public override string Id => "window-shortcuts";
    public override string Name => "Pencere Kısayolları";
    protected override IReadOnlyDictionary<string, string> Actions => new Dictionary<string, string> { ["close"] = "Pencereyi kapat (Alt+F4)", ["minimize"] = "Küçült", ["maximize"] = "Büyüt / geri yükle" };
    protected override IEnumerable<ShortcutActionRule> Defaults => [new("close", "Pencereyi kapat", false, "Alt+W", "close")];
}
public sealed class DesktopShortcutsModule : ShortcutActionModule
{
    public override string Id => "desktop-shortcuts";
    public override string Name => "Sanal Masaüstü Kısayolları";
    protected override IReadOnlyDictionary<string, string> Actions => new Dictionary<string, string> { ["left"] = "Soldaki masaüstü", ["right"] = "Sağdaki masaüstü", ["create"] = "Yeni masaüstü" };
    protected override IEnumerable<ShortcutActionRule> Defaults => [new("left", "Soldaki masaüstü", false, "Ctrl+Alt+MouseLeft", "left"), new("right", "Sağdaki masaüstü", false, "Ctrl+Alt+MouseRight", "right")];
}
public sealed class TextShortcutsModule : ShortcutActionModule
{
    private TextExpansionBinding? _expansion;
    protected override void Clear() { _expansion?.Dispose(); _expansion = null; base.Clear(); }
    protected override void Bind()
    {
        base.Bind();
        if (!IsActive || IsCapturing) return;
        var rules = Rules.Where(r => r.Enabled && r.Action == "text-expand").ToArray();
        if (rules.Length > 0) _expansion = new TextExpansionBinding(rules, Context.Notify);
    }
    public override string Id => "text-shortcuts";
    public override string Name => "Metin Yazma Kısayolları";
    protected override IReadOnlyDictionary<string, string> Actions => new Dictionary<string, string> { ["text"] = "Kısayolla metni yaz", ["text-expand"] = "Kısaltmayı boşlukla genişlet" };
    protected override IEnumerable<ShortcutActionRule> Defaults => [new("text", "Metin / şifre yaz", false, "Ctrl+P", "text")];
}
public sealed class ProgramShortcutsModule : ShortcutActionModule
{
    public override string Id => "program-shortcuts";
    public override string Name => "Program Açma Kısayolları";
    protected override IReadOnlyDictionary<string, string> Actions => new Dictionary<string, string> { ["launch"] = "Program / dosya / klasör aç", ["explorer"] = "Gezgin’de klasör aç", ["downloads"] = "İndirilenler klasörünü aç" };
    protected override IEnumerable<ShortcutActionRule> Defaults => [new("mouse-downloads", "Mouse XButton2 · İndirilenler klasörünü aç", true, "MouseX2", "downloads")];
}
public sealed class MonitorShortcutsModule : ShortcutActionModule
{
    public override string Id => "monitor-shortcuts";
    public override string Name => "Monitör Düzeni Kısayolları";
    protected override IReadOnlyDictionary<string, string> Actions => new Dictionary<string, string> { ["profile"] = "Monitör düzenini uygula" };
}
