using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using WinForge.Core;
using WinForge.Modules.Extras;
using static WinForge.Modules.Extras.KeyboardInputSender;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var commands = AutomationCommands.Parse(AutomationCommands.Example);
            Check(commands.Count == 2 && commands[0].Shortcut == new AutomationShortcut(0, 0x70), "Default includes plain F1");
            Check(commands[1].Shortcut == new AutomationShortcut(HotkeyModifiers.Alt, 0x53) && !commands[1].Repeat && commands[1].Steps[0].Kind == "search", "Alt+S searches selected text once");
            Reject("Alt+S = toggle|100|search google");
            Reject("Alt+S = once|100|search bing");
            Reject("Alt+S = once|100|search google\nCtrl+C = once|100|text test");
            SelectionSearchChecks();
            ActiveIndicatorChecks();
            ForegroundBindingChecks();
            Check(ForegroundApplication.Matches("GAME.exe", "game"), "Process scope matches case-insensitively with or without exe");
            Check(!ForegroundApplication.Matches("game.exe", "other.exe"), "Process scope excludes other applications");
            Check(ForegroundApplication.Matches(@"C:\Games\game.exe", "game", @"C:\Games\game.exe") && !ForegroundApplication.Matches(@"C:\Games\game.exe", "game", @"D:\Other\game.exe"), "Full executable path distinguishes identical process names");
            Check(commands[0].Repeat && commands[0].Interval == 100 && commands[0].Steps[0].Value == "left", "Default repeats left clicks");
            var modified = AutomationCommands.Parse("Shift+F1 = once|10|click right")[0];
            Check(modified.Shortcut.Modifiers == HotkeyModifiers.Shift, "Shift+F1 is a distinct shortcut");
            Reject("F1 = toggle|0|click left");
            Reject("F1 = toggle|100|click left\nf1 = once|20|click right");
            Reject("F1 = toggle|100|key F1");
            Reject("F1 = toggle|100|wait -1");
            Reject("F1 = toggle|100|unknown");
            Reject("Ctrl+Alt+Delete = once|100|click left");
            Reject("Ctrl+Ctrl+F1 = once|100|click left");
            Reject("F12 = toggle|100|click left");
            Reject("F1 = toggle|100|click left;");

            var count = 0;
            var run = new AutomationRun(commands[0]);
            run.Advance(0, true, _ => count++);
            Check(count == 0, "Held trigger/modifier pauses input");
            run.Advance(0, false, _ => count++);
            run.Advance(99, false, _ => count++);
            Check(count == 1, "Click interval respected");
            run.Advance(100, false, _ => count++);
            Check(count == 2 && !run.Finished, "Toggle continues");
            var once = new AutomationRun(AutomationCommands.Parse("Ctrl+Alt+T = once|100|text Merhaba; wait 500; key Enter")[0]);
            var steps = new List<string>();
            once.Advance(0, false, step => steps.Add(step.Kind));
            once.Advance(0, false, step => steps.Add(step.Kind));
            once.Advance(499, false, step => steps.Add(step.Kind));
            Check(steps.SequenceEqual(new[] { "text" }), "Wait delays next step");
            once.Advance(500, false, step => steps.Add(step.Kind));
            once.Advance(1000, false, step => steps.Add(step.Kind));
            Check(steps.SequenceEqual(new[] { "text", "key" }) && once.Finished, "Once executes in order and finishes");

            var context = new TestContext();
            var module = new AutomationModule();
            module.Initialize(context);
            module.Activate();
            Check(module.IsActive && context.HotkeyItems.Count == 3 && context.HotkeyItems[1].Modifiers == 0, "Module registers master, exact F1 and Alt+S");
            context.HotkeyItems[1].Callback();
            Check(Running(module) == 1, "First F1 starts automation");
            context.HotkeyItems[1].Callback();
            Check(Running(module) == 0, "Second F1 stops automation");
            context.HotkeyItems[1].Callback();
            module.Apply();
            Check(Running(module) == 0 && context.HotkeyItems.Count == 3, "Apply stops runs and replaces registrations");
            PanelChecks(module, context);
            context.HotkeyItems[1].Callback();
            module.Deactivate();
            Check(!module.IsActive && Running(module) == 0 && context.HotkeyItems.Count == 0, "Deactivate cancels all runs and hotkeys");
            context.FailAt = 2;
            context.Values["commands"] = "F1 = toggle|100|click left\nF2 = toggle|100|click right";
            try { module.Activate(); throw new Exception("Expected registration failure"); }
            catch (InvalidOperationException) { }
            Check(!module.IsActive && context.HotkeyItems.Count == 0, "Partial registration failure rolls back");
            Check(Marshal.SizeOf<INPUT>() == 40 && Marshal.SizeOf<MOUSEINPUT>() == 32, "Native input sizes match x64 ABI");
            // Interactive checks require an idle keyboard: physical typing can reset Windows' no-repeat state.
            if (args.Contains("--native-hotkeys")) NativeHotkeyChecks();
            Console.WriteLine($"PASS: {_checks} automation checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static int Running(AutomationModule module) => ((System.Collections.IDictionary)typeof(AutomationModule)
        .GetField("_running", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(module)!).Count;

    private static void ForegroundBindingChecks()
    {
        var hits = 0;
        using var binding = new ForegroundShortcutBinding(new(2, 0x86), "", () => hits++);
        var key = new KBDLLHOOKSTRUCT { vkCode = 0x86 };
        Check(!binding.ProcessInput(0x100, key, 2, false), "Scoped shortcut passes through outside its exe");
        Check(!binding.ProcessInput(0x100, key, 6, true), "Scoped shortcut requires exact modifiers");
        Check(!binding.ProcessInput(0x100, new KBDLLHOOKSTRUCT { vkCode = 0x86, flags = 0x10 }, 2, true), "Injected keys do not trigger scoped automation");
        Check(binding.ProcessInput(0x100, key, 2, true) && binding.ProcessInput(0x100, key, 2, true), "Scoped press and repeats are consumed in target exe");
        Check(binding.ProcessInput(0x101, key, 0, false), "Scoped release remains paired after focus/modifier change");
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
        Check(hits == 1, "Held scoped shortcut dispatches only once");
        using var hotkeys = new HotkeyService();
        using (hotkeys.Suspend()) Check(!binding.ProcessInput(0x100, key, 2, true), "Capture leaves scoped key available to recorder");
    }

    private static void ActiveIndicatorChecks()
    {
        var ui = new IndicatorUi();
        var context = new TestContext { FeatureUi = ui };
        context.Values["commands"] = "F1 = toggle|100|wait 1000\nF2 = toggle|100|wait 1000\nF3 = once|100|wait 1000";
        var module = new AutomationModule(); module.Initialize(context); module.Activate();
        context.HotkeyItems[3].Callback();
        Check(ui.Window is null, "One-shot automation does not show persistent indicator");
        Command(module, "stop-all");
        context.HotkeyItems[1].Callback();
        var window = ui.Window!;
        Check(window.IsVisible && window.State!.Value.GetProperty("active")[0].GetProperty("shortcut").GetString() == "F1", "Toggle shows its actual shortcut while running");
        Check(ui.Options is { Topmost: true, ActivateOnShow: false, Transparent: true, ClickThrough: true, ShowInTaskbar: false } && ui.Options.Left == CursorPanelPlacement.WorkArea.Left + 12, "Indicator uses the cursor monitor without stealing focus or clicks");
        context.HotkeyItems[2].Callback();
        Check(window.State!.Value.GetProperty("active").GetArrayLength() == 2, "Concurrent toggles share the indicator");
        context.HotkeyItems[1].Callback();
        Check(window.IsVisible && window.State!.Value.GetProperty("active").GetArrayLength() == 1 && window.State.Value.GetProperty("active")[0].GetProperty("shortcut").GetString() == "F2", "Stopping one toggle preserves the other indicator");
        context.HotkeyItems[2].Callback();
        Check(!window.IsVisible, "Stopping last toggle hides indicator immediately");
        context.HotkeyItems[1].Callback(); Command(module, "pause", new { paused = true });
        Check(!window.IsVisible, "Pause hides indicator");
        Command(module, "pause", new { paused = false });
        Check(!window.IsVisible, "Resume does not restore stopped indicator");
        context.HotkeyItems[1].Callback(); Command(module, "capture-begin");
        Check(!window.IsVisible, "Shortcut capture hides indicator");
        Command(module, "capture-end"); context.HotkeyItems[1].Callback(); module.Apply();
        Check(!window.IsVisible, "Settings apply clears indicator");
        context.HotkeyItems[1].Callback(); module.Deactivate();
        Check(window.Closed && !window.IsVisible, "Deactivate closes indicator resources");
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static JsonElement Panel(AutomationModule module) => JsonSerializer.SerializeToElement(module.GetPanelState(), WebJson);
    private static void Command(AutomationModule module, string action, object? data = null) => module.HandlePanelCommand(action, JsonSerializer.SerializeToElement(data ?? new { }, WebJson));
    private static AutomationRule[] Rules(AutomationModule module) => Panel(module).GetProperty("rules").Deserialize<AutomationRule[]>(WebJson)!;

    private static void PanelChecks(AutomationModule module, TestContext context)
    {
        var updates = 0;
        module.PanelStateChanged += () => updates++;
        Check(module.Settings.Count == 0 && context.Values.ContainsKey("rules-v2"), "Legacy commands migrate to inline rules; no editor action");
        var rule = Rules(module)[0];
        context.HotkeyItems[1].Callback();
        Command(module, "save", new { rule = rule with { Enabled = false } });
        Check(Running(module) == 0 && context.HotkeyItems.Count == 2 && !Rules(module)[0].Enabled, "Per-rule switch stops and unregisters disabled automation");
        rule = rule with { Shortcut = "F3", Interval = 500, Steps = [new("click", "right")] };
        Command(module, "save", new { rule });
        Check(Rules(module)[0].Interval == 500 && Rules(module)[0].Shortcut == "F3" && Rules(module)[0].Steps[0].Value == "right", "Shortcut, interval, and commands edit independently");
        Check(context.HotkeyItems.Count == 3, "Enabling restores registration");
        try { Command(module, "save", new { rule = rule with { Shortcut = "Alt+S" } }); throw new Exception("Expected duplicate shortcut error"); }
        catch (FormatException) { Check(Rules(module)[0].Shortcut == "F3", "Invalid update preserves prior rule"); }
        context.FailAt = 2;
        try { Command(module, "save", new { rule = rule with { Shortcut = "F4" } }); throw new Exception("Expected occupied shortcut error"); }
        catch (InvalidOperationException) { Check(Rules(module)[0].Shortcut == "F3" && context.HotkeyItems.Count == 3, "Occupied shortcut rolls back settings and registrations"); }
        Command(module, "master", new { shortcut = "Ctrl+Alt+F9" });
        Check(Panel(module).GetProperty("masterShortcut").GetString() == "Ctrl+Alt+F9", "Master shortcut is configurable");
        Command(module, "pause", new { paused = true });
        Check(context.HotkeyItems.Count == 1 && Panel(module).GetProperty("paused").GetBoolean(), "Pause releases rule shortcuts but retains resume shortcut");
        module.Apply();
        Check(context.HotkeyItems.Count == 1, "Pause preference survives Apply");
        context.HotkeyItems[0].Callback();
        Check(context.HotkeyItems.Count == 3 && !Panel(module).GetProperty("paused").GetBoolean() && Running(module) == 0, "Master shortcut resumes armed rules without running commands");
        Command(module, "capture-begin");
        Check(context.HotkeyItems.Count == 0 && Panel(module).GetProperty("capturing").GetBoolean(), "Shortcut capture releases conflicting native bindings");
        Command(module, "capture-end");
        Check(context.HotkeyItems.Count == 3, "Shortcut capture restores bindings");
        Command(module, "add");
        var added = Rules(module).Last();
        Check(Rules(module).Length == 3 && !added.Enabled && context.HotkeyItems.Count == 3, "New automation starts disabled");
        Command(module, "delete", new { id = added.Id });
        Check(Rules(module).Length == 2 && updates > 10, "Delete and runtime changes notify the inline panel");
        module.Apply();
        Check(Rules(module)[0].Interval == 500 && Rules(module)[0].Steps[0].Value == "right", "Structured edits persist across reload");
        var scoped = Rules(module)[0] with { ProcessName = "winforge-nonexistent-focus-fixture.exe" };
        Command(module, "save", new { rule = scoped });
        Check(context.HotkeyItems.Count == 2 && Rules(module)[0].ProcessName == scoped.ProcessName, "Scoped rule avoids globally reserving its hotkey and persists its exe");
        typeof(AutomationModule).GetMethod("Trigger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(module, [scoped, scoped.Compile()]);
        Check(Running(module) == 0, "Scoped rule cannot start outside its target exe");
        var executionType = typeof(AutomationModule).GetNestedType("Execution", System.Reflection.BindingFlags.NonPublic)!;
        var execution = Activator.CreateInstance(executionType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            null, [scoped.Compile(), scoped.ProcessName, CancellationToken.None], null)!;
        var runs = (System.Collections.IDictionary)typeof(AutomationModule).GetField("_running", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(module)!;
        runs.Add(scoped.Id, execution);
        typeof(AutomationModule).GetMethod("Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(module, null);
        Check(Running(module) == 0, "Losing target exe focus cancels an already-running automation before its next step");
        Command(module, "save", new { rule = scoped with { ProcessName = "" } });
        Check(context.HotkeyItems.Count == 3, "Clearing scope restores global automation shortcut");
        var legacyContext = new TestContext();
        legacyContext.Values["commands"] = "F5 = once|250|key Enter";
        var legacy = new AutomationModule();
        legacy.Initialize(legacyContext);
        Check(Rules(legacy)[0].Shortcut == "F5" && Rules(legacy)[0].Steps[0].Value == "Return", "Migration preserves custom key commands");
    }

    private static void SelectionSearchChecks()
    {
        var copies = 0;
        var polls = 0;
        uint version = 1;
        string? opened = null;
        var search = new SelectionSearch(() => copies++, () => version, () => " Türkçe & + #? ", () => true,
            url => opened = url, token => { token.ThrowIfCancellationRequested(); if (++polls == 3) version++; return Task.CompletedTask; });
        search.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(copies == 1 && polls == 3, "Waits for freshly copied selection");
        Check(opened == "https://www.google.com/search?q=T%C3%BCrk%C3%A7e%20%26%20%2B%20%23%3F", "Google URL encodes Unicode and query punctuation");
        opened = null;
        search = new SelectionSearch(() => { }, () => 1, () => "old private clipboard", () => true, url => opened = url, _ => Task.CompletedTask);
        try { search.RunAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Expected copy timeout"); }
        catch (InvalidOperationException) { Check(opened is null, "Unchanged clipboard is never searched"); }
        search = new SelectionSearch(() => throw new Exception("Should not copy"), () => 1, () => "", () => false, url => opened = url, _ => Task.CompletedTask);
        search.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(opened is null, "Changed foreground window cancels search");
        using var cancellation = new CancellationTokenSource();
        search = new SelectionSearch(() => { }, () => 1, () => "", () => true, url => opened = url,
            token => { cancellation.Cancel(); return Task.CompletedTask; });
        try { search.RunAsync(cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Check(opened is null, "Module stop cancels pending clipboard search"); }
    }
    private static void Reject(string text)
    {
        try { AutomationCommands.Parse(text); throw new Exception("Accepted invalid command: " + text); }
        catch (FormatException) { _checks++; }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    private static void NativeHotkeyChecks()
    {
        using var service = new HotkeyService();
        var plain = 0;
        var shifted = 0;
        // F23 exercises the real Windows registration without taking over the user's F1.
        using var a = service.Register(0, 0x86, () => plain++);
        using var b = service.Register(HotkeyModifiers.Shift, 0x86, () => shifted++);
        try
        {
            Send(0x10, false); Send(0x86, false); Send(0x86, true); Send(0x10, true); Pump();
            Check(plain == 0 && shifted == 1, "Real Windows: Shift+key does not invoke plain key");
            Send(0x86, false); Pump();
            Send(0x86, false); Pump();
            Check(plain == 1, $"Real Windows: held-key repeat does not toggle twice (plain={plain}, shifted={shifted})");
            Send(0x86, true); Send(0x86, false); Send(0x86, true); Pump();
            Check(plain == 2 && shifted == 1, $"Real Windows: release and repress toggles again (plain={plain}, shifted={shifted})");
        }
        finally { Send(0x86, true); Send(0x10, true); }
    }

    private static void Send(ushort key, bool up)
    {
        INPUT[] input = [new() { Type = 1, Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = up ? 2u : 0u } } }];
        Check(SendInput(1, input, Marshal.SizeOf<INPUT>()) == 1, "Test input accepted");
        Pump();
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed record Registration(TestContext Owner, uint Modifiers, Action Callback) : IDisposable
    {
        public void Dispose() => Owner.HotkeyItems.Remove(this);
    }
    private sealed class IndicatorUi : IFeatureUiService
    {
        public IndicatorWindow? Window;
        public FeatureUiWindowOptions? Options;
        public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options) { Options = options; return Window = new(); }
        public IFeatureUiSurface CreateSurface(string view) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel") => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel = "Close") => throw new NotSupportedException();
    }
    private sealed class IndicatorWindow : IFeatureUiWindow
    {
        public bool IsVisible { get; private set; }
        public bool Closed;
        public JsonElement? State;
        public event Action<string, JsonElement>? MessageReceived { add { } remove { } }
        public event Action? ClosedByHost { add { } remove { } }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close() { IsVisible = false; Closed = true; }
        public void Dispose() => Close();
        public void SetBounds(double left, double top, double width, double height) { }
        public void SetState(object state) => State = JsonSerializer.SerializeToElement(state, WebJson);
    }
    private sealed class TestContext : IModuleContext, IModuleSettings, IHotkeyService, ILogger, IFeatureUiContext
    {
        public IFeatureUiService? FeatureUi { get; init; }
        public readonly List<Registration> HotkeyItems = new();
        public readonly Dictionary<string, string> Values = new();
        public int FailAt;
        public IModuleSettings Settings => this;
        public IHotkeyService Hotkeys => this;
        public ILogger Log => this;
        public string Path => "";
        public void Notify(string message) { }
        public bool GetBool(string key, bool fallback = false) => Values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
        public int GetInt(string key, int fallback = 0) => fallback;
        public string GetString(string key, string fallback = "") => Values.GetValueOrDefault(key, fallback);
        public void Set(string key, object? value) => Values[key] = value?.ToString() ?? "";
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public IDisposable Register(uint modifiers, uint key, Action callback)
        {
            if (HotkeyItems.Count + 1 == FailAt) { FailAt = 0; throw new InvalidOperationException("Occupied shortcut"); }
            var registration = new Registration(this, modifiers, callback);
            HotkeyItems.Add(registration);
            return registration;
        }
    }
}
