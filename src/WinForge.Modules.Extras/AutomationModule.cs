using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using WinForge.Core;
using static WinForge.Modules.Extras.KeyboardInputSender;

namespace WinForge.Modules.Extras;

public sealed class AutomationModule : ITweakModule, IModulePanel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IModuleContext? _context;
    private readonly List<IDisposable> _registrations = new();
    private readonly Dictionary<string, Execution> _running = new();
    private List<AutomationRule> _rules = new();
    private CancellationTokenSource _cancellation = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _captureTimer;
    private bool _paused;
    private bool _capturing;
    private string _masterShortcut = "Ctrl+Alt+F8";
    private IFeatureUiWindow? _activeIndicator;
    private sealed class Execution(AutomationCommand command, string processName, CancellationToken cancellation, bool closeTab, bool backgroundTab = false)
    {
        internal Execution(AutomationCommand command, string processName, CancellationToken cancellation)
            : this(command, processName, cancellation, false, false) { }
        internal string ProcessName { get; } = processName;
        internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        internal AutomationRun Run { get; } = new(command);
        internal IntPtr Target { get; } = Native.GetForegroundWindow();
        internal Task? Pending { get; set; }
        internal AutomationCommand Command { get; } = command;
        internal bool CloseTab { get; set; } = closeTab;
        internal bool BackgroundTab { get; set; } = backgroundTab;
    }

    public string Id => "automation";
    public string Name => "Otomasyon · Kısayol Komutları";
    public string Category => "Verimlilik";
    public string Description => "Otomasyonları bu panelden açıp kapatın; kısayol, komut adımları ve tekrar aralığını ayrı ayrı düzenleyin.";
    public string? Warning => "Kısayollar tam eşleşir: F1, Shift+F1 ile tetiklenmez. Komutlar ön plandaki uygulamaya gider. Yönetici uygulamaları için WinForge da yönetici olarak çalışmalıdır.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => Array.Empty<SettingDescriptor>();
    public event Action? PanelStateChanged;

    private void PublishState()
    {
        UpdateActiveIndicator();
        PanelStateChanged?.Invoke();
    }

    private void UpdateActiveIndicator()
    {
        var active = _rules.Where(rule => rule.Repeat && _running.ContainsKey(rule.Id))
            .Select(rule => new { rule.Id, rule.Name, rule.Shortcut }).ToArray();
        try
        {
            if (active.Length == 0 || !IsActive || _paused || _capturing)
            {
                _activeIndicator?.Hide();
                return;
            }
            if (_context is not IFeatureUiContext { FeatureUi: { } ui }) return;
            var area = CursorPanelPlacement.WorkArea;
            const double width = 320;
            var height = Math.Min(area.Height - 24, 16 + active.Length * 52);
            _activeIndicator ??= ui.Open("automation-active", "automation-active", "Aktif otomasyonlar",
                new(width, height, Topmost: true, ActivateOnShow: false, Transparent: true,
                    ClickThrough: true, Left: area.Left + 12, Top: area.Top + 12));
            _activeIndicator.SetBounds(area.Left + 12, area.Top + 12, width, height);
            _activeIndicator.SetState(new { active });
            _activeIndicator.Show();
        }
        catch (Exception ex) { _context?.Log.Error("Otomasyon göstergesi güncellenemedi.", ex); }
    }

    public AutomationModule()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        _timer.Tick += (_, _) => Tick();
        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _captureTimer.Tick += (_, _) => EndCapture();
    }

    public void Initialize(IModuleContext context)
    {
        _context = context;
        LoadRules();
    }

    private void LoadRules()
    {
        if (_context is null) return;
        var saved = _context.Settings.GetString("rules-v2", "");
        _rules = string.IsNullOrWhiteSpace(saved)
            ? AutomationCommands.Parse(_context.Settings.GetString("commands", AutomationCommands.Example)).Select(AutomationRule.FromLegacy).ToList()
            : JsonSerializer.Deserialize<List<AutomationRule>>(saved, JsonOptions) ?? new();
        _masterShortcut = _context.Settings.GetString("master-shortcut", "Ctrl+Alt+F8");
        _paused = _context.Settings.GetBool("paused", false);
        var added1337x = false;
        if (!string.IsNullOrWhiteSpace(saved) && !_context.Settings.GetBool("search-1337x-added"))
        {
            if (_rules.Count < 64 && !_rules.Any(r => r.Steps.Any(s => s.Kind == "search" && s.Value == "1337x")))
            {
                var shortcut = new[] { "Alt+G", "Alt+Shift+S", "Ctrl+Alt+S", "Alt+D" }
                    .Concat(Enumerable.Range(2, 23).Where(k => k != 12).Select(k => "F" + k))
                    .FirstOrDefault(s => !_rules.Any(r => r.Shortcut.Equals(s, StringComparison.OrdinalIgnoreCase)) && !s.Equals(_masterShortcut, StringComparison.OrdinalIgnoreCase)) ?? "Alt+Shift+S";
                _rules.Add(new AutomationRule
                {
                    Id = "1337x-selection-search",
                    Name = "Seçili metni 1337x’te ara",
                    Enabled = true,
                    Shortcut = shortcut,
                    Repeat = false,
                    Interval = 100,
                    Steps = [new("search", "1337x", 0)]
                });
            }
            _context.Settings.Set("search-1337x-added", true);
            added1337x = true;
        }
        Validate(_rules, _masterShortcut);
        if (string.IsNullOrWhiteSpace(saved))
        {
            _context.Settings.Set("search-1337x-added", true);
            Persist();
        }
        else if (added1337x)
        {
            Persist();
        }
    }

    private static void Validate(List<AutomationRule> rules, string masterShortcut)
    {
        if (rules.Count > 64 || rules.Select(rule => rule.Id).Distinct().Count() != rules.Count)
            throw new FormatException("En fazla 64 otomasyon ve her biri için farklı bir kimlik gereklidir.");
        foreach (var rule in rules) rule.Compile();
        var commands = AutomationCommands.Parse(string.Join("\n", rules.Where(rule => rule.Enabled).Select(rule => rule.ToLine())));
        var master = AutomationCommands.ParseShortcut(masterShortcut);
        if (commands.Any(command => command.Shortcut == master || command.Steps.Any(step => step.Kind == "key" && int.Parse(step.Value) == master.Key)))
            throw new FormatException("Genel başlat/durdur kısayolu otomasyon kısayollarından ve gönderilen tuşlardan farklı olmalıdır.");
        if (commands.Any(command => command.Steps.Any(step => step.Kind == "search")) &&
            master == new AutomationShortcut(HotkeyModifiers.Control, 0x43))
            throw new FormatException("Ctrl+C, Google aramasında kopyalama için kullanılır.");
    }

    private void Persist()
    {
        _context!.Settings.Set("rules-v2", JsonSerializer.Serialize(_rules, JsonOptions));
        _context.Settings.Set("master-shortcut", _masterShortcut);
        _context.Settings.Set("paused", _paused);
    }

    public object GetPanelState() => new
    {
        paused = _paused, capturing = _capturing, masterShortcut = _masterShortcut,
        rules = _rules.Select(rule => new
        {
            rule.Id, rule.Name, rule.Enabled, rule.Shortcut, rule.Repeat, rule.Interval, rule.Steps, rule.ProcessName,
            running = _running.ContainsKey(rule.Id)
        }).ToArray()
    };

    public void Activate()
    {
        IsActive = true;
        try { Apply(); }
        catch { Deactivate(); throw; }
    }

    public void Deactivate()
    {
        IsActive = false;
        _capturing = false;
        _captureTimer.Stop();
        StopAll();
        ClearRegistrations();
        _activeIndicator?.Close();
        _activeIndicator = null;
        PublishState();
    }

    public void Apply()
    {
        StopAll();
        ClearRegistrations();
        LoadRules();
        RegisterShortcuts();
        PublishState();
    }

    private void RegisterShortcuts()
    {
        if (!IsActive || _context is null || _capturing) return;
        try
        {
            var master = AutomationCommands.ParseShortcut(_masterShortcut);
            _registrations.Add(_context.Hotkeys.Register(master.Modifiers, (uint)master.Key, () => SetPaused(!_paused)));
            if (_paused) return;
            var enabled = _rules.Where(rule => rule.Enabled).Select(rule => (Rule: rule, Command: rule.Compile())).ToList();
            var occupied = enabled.Select(item => item.Command.Shortcut).Append(master).ToHashSet();
            foreach (var (rule, command) in enabled)
            {
                _registrations.Add(string.IsNullOrWhiteSpace(rule.ProcessName)
                    ? _context.Hotkeys.Register(command.Shortcut.Modifiers, (uint)command.Shortcut.Key, () => Trigger(rule, command))
                    : new ForegroundShortcutBinding(command.Shortcut, rule.ProcessName, () => Trigger(rule, command)));
                if (ShouldRegisterChromeShiftSearch(rule, command, occupied, out var shiftShortcut, out var scope))
                {
                    occupied.Add(shiftShortcut);
                    _registrations.Add(new ForegroundShortcutBinding(shiftShortcut, scope, () => StartExecution(rule, command, closeTab: true, backgroundTab: false)));
                }
                if (ShouldRegisterBackgroundCtrlSearch(rule, command, occupied, out var ctrlShortcut))
                {
                    occupied.Add(ctrlShortcut);
                    _registrations.Add(new ForegroundShortcutBinding(ctrlShortcut, rule.ProcessName, () => StartExecution(rule, command, closeTab: false, backgroundTab: true)));
                }
            }
        }
        catch { ClearRegistrations(); throw; }
    }

    internal static bool ShouldRegisterChromeShiftSearch(
        AutomationRule rule,
        AutomationCommand command,
        IReadOnlySet<AutomationShortcut> occupied,
        out AutomationShortcut shiftShortcut,
        out string scope)
    {
        shiftShortcut = default!;
        scope = "";
        if (command.Repeat ||
            !command.Steps.Any(step => step.Kind == "search") ||
            (command.Shortcut.Modifiers & HotkeyModifiers.Shift) != 0)
            return false;
        if (!string.IsNullOrWhiteSpace(rule.ProcessName) && !ForegroundApplication.Matches(rule.ProcessName, "chrome"))
            return false;
        shiftShortcut = command.Shortcut with { Modifiers = command.Shortcut.Modifiers | HotkeyModifiers.Shift };
        if (occupied.Contains(shiftShortcut)) return false;
        scope = string.IsNullOrWhiteSpace(rule.ProcessName) ? "chrome.exe" : rule.ProcessName;
        return true;
    }

    internal static bool ShouldRegisterBackgroundCtrlSearch(
        AutomationRule rule,
        AutomationCommand command,
        IReadOnlySet<AutomationShortcut> occupied,
        out AutomationShortcut ctrlShortcut)
    {
        ctrlShortcut = default!;
        if (command.Repeat ||
            !command.Steps.Any(step => step.Kind == "search") ||
            (command.Shortcut.Modifiers & HotkeyModifiers.Control) != 0)
            return false;
        ctrlShortcut = command.Shortcut with { Modifiers = command.Shortcut.Modifiers | HotkeyModifiers.Control };
        return !occupied.Contains(ctrlShortcut);
    }

    private void ClearRegistrations()
    {
        foreach (var registration in _registrations) registration.Dispose();
        _registrations.Clear();
    }

    private void Trigger(AutomationRule rule, AutomationCommand command) => StartExecution(rule, command, closeTab: false, backgroundTab: false);

    private void StartExecution(AutomationRule rule, AutomationCommand command, bool closeTab, bool backgroundTab)
    {
        var scope = closeTab && string.IsNullOrWhiteSpace(rule.ProcessName) ? "chrome.exe" : rule.ProcessName;
        if (!IsActive || _paused || _capturing || !rule.Enabled || !ForegroundApplication.IsFocused(scope)) return;
        if (_running.ContainsKey(rule.Id))
        {
            if (command.Repeat) RemoveRun(rule.Id);
        }
        else _running.Add(rule.Id, new Execution(command, scope, _cancellation.Token, closeTab, backgroundTab));
        if (_running.Count > 0) _timer.Start(); else _timer.Stop();
        PublishState();
    }

    private void StopAll()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        _timer.Stop();
        foreach (var id in _running.Keys.ToArray()) RemoveRun(id);
        UpdateActiveIndicator();
    }

    private void RemoveRun(string id)
    {
        if (!_running.Remove(id, out var execution)) return;
        execution.Cancellation.Cancel();
        execution.Cancellation.Dispose();
    }

    private void SetPaused(bool paused)
    {
        var previous = _paused;
        try
        {
            _paused = paused;
            StopAll();
            ClearRegistrations();
            RegisterShortcuts();
            Persist();
        }
        catch
        {
            _paused = previous;
            ClearRegistrations();
            RegisterShortcuts();
            throw;
        }
        finally { PublishState(); }
    }

    public void HandlePanelCommand(string action, JsonElement payload)
    {
        if (action == "stop-all") { StopAll(); PublishState(); return; }
        if (action == "pause") { SetPaused(payload.GetProperty("paused").GetBoolean()); return; }
        if (action == "capture-begin")
        {
            _capturing = true;
            StopAll();
            ClearRegistrations();
            _captureTimer.Start();
            PublishState();
            return;
        }
        if (action == "capture-end")
        {
            EndCapture();
            return;
        }
        var next = _rules.ToList();
        var master = _masterShortcut;
        switch (action)
        {
            case "save":
                var rule = payload.GetProperty("rule").Deserialize<AutomationRule>(JsonOptions) ?? throw new FormatException("Otomasyon eksik.");
                var index = next.FindIndex(item => item.Id == rule.Id);
                if (index < 0) throw new FormatException("Otomasyon bulunamadı.");
                next[index] = rule;
                break;
            case "add":
                var available = Enumerable.Range(2, 23).Where(key => key != 12).Select(key => "F" + key)
                    .FirstOrDefault(shortcut => !next.Any(item => item.Shortcut.Equals(shortcut, StringComparison.OrdinalIgnoreCase))) ?? "Ctrl+Alt+F2";
                next.Add(new AutomationRule { Enabled = false, Shortcut = available });
                break;
            case "delete": next.RemoveAll(item => item.Id == payload.GetProperty("id").GetString()); break;
            case "master": master = payload.GetProperty("shortcut").GetString() ?? ""; break;
            default: throw new InvalidOperationException("Bilinmeyen otomasyon eylemi.");
        }
        Validate(next, master);
        var previousRules = _rules;
        var previousMaster = _masterShortcut;
        StopAll();
        ClearRegistrations();
        try
        {
            _rules = next;
            _masterShortcut = master;
            RegisterShortcuts();
            Persist();
        }
        catch
        {
            _rules = previousRules;
            _masterShortcut = previousMaster;
            ClearRegistrations();
            RegisterShortcuts();
            throw;
        }
        finally { PublishState(); }
    }

    private void EndCapture()
    {
        _captureTimer.Stop();
        _capturing = false;
        ClearRegistrations();
        try { RegisterShortcuts(); }
        catch (Exception ex) { _context?.Notify("Otomasyon kısayolu geri açılamadı: " + ex.Message); }
        PublishState();
    }

    private void Tick()
    {
        if (!IsActive || _paused || _capturing) { StopAll(); return; }
        var changed = false;
        foreach (var (id, execution) in _running.ToArray())
        {
            try
            {
                if (!ForegroundApplication.IsFocused(execution.ProcessName)) { RemoveRun(id); changed = true; continue; }
                if (execution.Pending is { IsCompleted: false }) continue;
                execution.Pending?.GetAwaiter().GetResult();
                execution.Pending = null;
                var blocked = IsDown(execution.Command.Shortcut.Key) || new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(IsDown);
                execution.Run.Advance(Environment.TickCount64, blocked, step =>
                {
                    if (step.Kind == "search")
                    {
                        var closeTab = execution.CloseTab;
                        var backgroundTab = execution.BackgroundTab;
                        execution.CloseTab = false;
                        execution.BackgroundTab = false;
                        execution.Pending = SearchSelectionAsync(execution.Target, step.Value, closeTab, backgroundTab, execution.Cancellation.Token);
                    }
                    else SendStep(step);
                });
                if (execution.Run.Finished && execution.Pending is null) { RemoveRun(id); changed = true; }
            }
            catch (Exception ex)
            {
                RemoveRun(id);
                changed = true;
                _context?.Log.Error("Otomasyon komutu durduruldu.", ex);
                _context?.Notify("Otomasyon durduruldu: " + ex.Message);
            }
        }
        if (_running.Count == 0) _timer.Stop();
        if (changed) PublishState();
    }

    private static bool IsDown(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    private static Task SearchSelectionAsync(IntPtr target, string provider, bool closeTab, bool backgroundTab, CancellationToken cancellationToken) => new SelectionSearch(
        CopySelection,
        GetClipboardSequenceNumber,
        () => Clipboard.ContainsText() ? Clipboard.GetText() : null,
        () => Native.GetForegroundWindow() == target,
        url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }),
        token => Task.Delay(25, token),
        provider,
        closeTab ? CloseActiveTab : null,
        backgroundTab ? () => BackgroundTabReturn.Prepare(target) : null).RunAsync(cancellationToken);

    private static void CopySelection() => SendControlChord(0x43, "Seçili metin için kopyalama tuşları gönderilemedi.");
    private static void CloseActiveTab() => SendControlChord(0x57, "Chrome sekmesini kapatma tuşları gönderilemedi.");

    private static void SendControlChord(ushort key, string errorMessage)
    {
        INPUT[] inputs = [KeyInput(0x11, 0, 0), KeyInput(key, 0, 0), KeyInput(key, 0, 2), KeyInput(0x11, 0, 2)];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
        {
            INPUT[] release = [KeyInput(key, 0, 2), KeyInput(0x11, 0, 2)];
            SendInput(2, release, Marshal.SizeOf<INPUT>());
            throw new InvalidOperationException(errorMessage);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private static void SendStep(AutomationStep step)
    {
        INPUT[] inputs;
        if (step.Kind == "click")
        {
            uint down = step.Value switch { "right" => 0x0008u, "middle" => 0x0020u, _ => 0x0002u };
            inputs = [Mouse(down), Mouse(down << 1)];
        }
        else if (step.Kind == "key")
        {
            var key = ushort.Parse(step.Value, System.Globalization.CultureInfo.InvariantCulture);
            uint extended = key is >= 0x21 and <= 0x2E or 0x6F or 0x90 ? 1u : 0u;
            inputs = [KeyInput(key, 0, extended), KeyInput(key, 0, extended | 2)];
        }
        else
        {
            inputs = step.Value.SelectMany(character => new[] { KeyInput(0, character, 4), KeyInput(0, character, 6) }).ToArray();
        }
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
        {
            // Best effort release in case SendInput accepted only part of the batch.
            var releases = inputs.Where(input => input.Type == 0 ? (input.Union.Mouse.Flags & 0x54) != 0 : (input.Union.Keyboard.Flags & 2) != 0).ToArray();
            SendInput((uint)releases.Length, releases, Marshal.SizeOf<INPUT>());
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Girdi hedef uygulamaya gönderilemedi.");
        }
    }

    private static INPUT Mouse(uint flags) => new() { Type = 0, Union = new InputUnion { Mouse = new MOUSEINPUT { Flags = flags } } };
    private static INPUT KeyInput(ushort key, ushort scan, uint flags) => new()
    {
        Type = 1, Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = key, ScanCode = scan, Flags = flags } }
    };

}
