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
            var search1337x = AutomationCommands.Parse("Alt+Shift+S = once|100|search 1337x");
            Check(search1337x.Count == 1 && !search1337x[0].Repeat && search1337x[0].Steps[0] is { Kind: "search", Value: "1337x" }, "Alt+Shift+S searches selected text on 1337x once");
            Reject("Alt+S = toggle|100|search google");
            Reject("Alt+Shift+S = toggle|100|search 1337x");
            Reject("Alt+S = once|100|search bing");
            Reject("Alt+S = once|100|search google\nCtrl+C = once|100|text test");
            SelectionSearchChecks();
            SelectionTranslationChecks();
            ChromeShiftSearchChecks();
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
        Check(SelectionSearch.BuildUrl("the odyssey", "1337x") == "https://1337x.to/search/the+odyssey/1/", "1337x URL joins terms with pluses");
        opened = null;
        version = 1;
        polls = 0;
        search = new SelectionSearch(() => { }, () => version, () => "  the   odyssey ", () => true,
            url => opened = url, token => { token.ThrowIfCancellationRequested(); if (++polls == 2) version++; return Task.CompletedTask; }, "1337x");
        search.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(opened == "https://1337x.to/search/the+odyssey/1/", "1337x search opens expected URL");
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

        var events = new List<string>();
        version = 1;
        polls = 0;
        search = new SelectionSearch(() => events.Add("copy"), () => version, () => "the odyssey", () => true,
            url => events.Add("open:" + url), token => { token.ThrowIfCancellationRequested(); if (++polls == 1) version++; return Task.CompletedTask; },
            "1337x", () => events.Add("close-tab"));
        search.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(events.SequenceEqual(["copy", "close-tab", "open:https://1337x.to/search/the+odyssey/1/"]), "Chrome Shift search closes tab after copy and before opening URL");

        var closed = 0;
        search = new SelectionSearch(() => { }, () => 1, () => "stale", () => true, _ => { }, _ => Task.CompletedTask, "google", () => closed++);
        try { search.RunAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Expected copy timeout"); }
        catch (InvalidOperationException) { Check(closed == 0, "Failed copy never closes the active tab"); }

        search = new SelectionSearch(() => { }, () => 1, () => "", () => false, _ => { }, _ => Task.CompletedTask, "google", () => closed++);
        search.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(closed == 0, "Window change never closes the active tab");

        events.Clear();
        version = 1;
        polls = 0;
        search = new SelectionSearch(() => events.Add("copy"), () => version, () => "the odyssey", () => true,
            url => events.Add("open:" + url), token => { token.ThrowIfCancellationRequested(); if (++polls == 1) version++; return Task.CompletedTask; },
            "google", null, () =>
            {
                events.Add("prepare");
                return _ => { events.Add("restore"); return Task.CompletedTask; };
            });
        search.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(events.SequenceEqual(["copy", "prepare", "open:https://www.google.com/search?q=the%20odyssey", "restore"]), "Ctrl background search prepares before open and restores after open");
    }

    private static void ChromeShiftSearchChecks()
    {
        var googleRule = new AutomationRule { Shortcut = "Alt+S", Repeat = false, Steps = [new("search", "google")] };
        var torrentRule = new AutomationRule { Shortcut = "Alt+G", Repeat = false, Steps = [new("search", "1337x")] };
        var occupied = new HashSet<AutomationShortcut> { googleRule.Compile().Shortcut, torrentRule.Compile().Shortcut };

        Check(AutomationModule.ShouldRegisterChromeShiftSearch(googleRule, googleRule.Compile(), occupied, out var googleShift, out var googleScope) &&
              googleShift == AutomationCommands.ParseShortcut("Alt+Shift+S") && googleScope == "chrome.exe",
            "Google search registers Alt+Shift+S scoped to Chrome");
        Check(AutomationModule.ShouldRegisterChromeShiftSearch(torrentRule, torrentRule.Compile(), occupied, out var torrentShift, out var torrentScope) &&
              torrentShift == AutomationCommands.ParseShortcut("Alt+Shift+G") && torrentScope == "chrome.exe",
            "1337x search registers Alt+Shift+G scoped to Chrome");

        var alreadyShifted = new AutomationRule { Shortcut = "Alt+Shift+S", Repeat = false, Steps = [new("search", "1337x")] };
        Check(!AutomationModule.ShouldRegisterChromeShiftSearch(alreadyShifted, alreadyShifted.Compile(), occupied, out _, out _),
            "Search shortcut already containing Shift does not add another Shift variant");

        var withCollision = new HashSet<AutomationShortcut>(occupied) { AutomationCommands.ParseShortcut("Alt+Shift+S") };
        Check(!AutomationModule.ShouldRegisterChromeShiftSearch(googleRule, googleRule.Compile(), withCollision, out _, out _),
            "Explicitly assigned Shift shortcut takes precedence over Chrome tab-close variant");

        var nonChromeRule = googleRule with { ProcessName = "notepad.exe" };
        Check(!AutomationModule.ShouldRegisterChromeShiftSearch(nonChromeRule, nonChromeRule.Compile(), occupied, out _, out _),
            "Search scoped to a non-Chrome app does not register Chrome Shift variant");

        var chromeRule = googleRule with { ProcessName = "chrome.exe" };
        Check(AutomationModule.ShouldRegisterChromeShiftSearch(chromeRule, chromeRule.Compile(), occupied, out _, out var explicitChromeScope) &&
              explicitChromeScope == "chrome.exe",
            "Search already scoped to Chrome keeps its Chrome Shift variant");

        Check(AutomationModule.ShouldRegisterBackgroundCtrlSearch(googleRule, googleRule.Compile(), occupied, out var googleCtrl) &&
              googleCtrl == AutomationCommands.ParseShortcut("Ctrl+Alt+S"),
            "Google search registers Ctrl+Alt+S background variant");
        Check(AutomationModule.ShouldRegisterBackgroundCtrlSearch(torrentRule, torrentRule.Compile(), occupied, out var torrentCtrl) &&
              torrentCtrl == AutomationCommands.ParseShortcut("Ctrl+Alt+G"),
            "1337x search registers Ctrl+Alt+G background variant");

        var alreadyCtrled = new AutomationRule { Shortcut = "Ctrl+Alt+S", Repeat = false, Steps = [new("search", "google")] };
        Check(!AutomationModule.ShouldRegisterBackgroundCtrlSearch(alreadyCtrled, alreadyCtrled.Compile(), occupied, out _),
            "Search shortcut already containing Ctrl does not add another Ctrl variant");

        var withCtrlCollision = new HashSet<AutomationShortcut>(occupied) { AutomationCommands.ParseShortcut("Ctrl+Alt+S") };
        Check(!AutomationModule.ShouldRegisterBackgroundCtrlSearch(googleRule, googleRule.Compile(), withCtrlCollision, out _),
            "Explicitly assigned Ctrl shortcut takes precedence over background variant");
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

    private static void SelectionTranslationChecks()
    {
        var input = "  " + string.Concat(Enumerable.Repeat("Türkçe 😀 sözcük ", 400)) + "\r\n";
        var emptyCopy = CustomClipboardPickle("{\"version\":1,\"isFromEmptySelection\":true}");
        Check(ChromiumClipboardData.IsVsCodeEmptySelection(emptyCopy), "VS Code copy without selection is detected");
        Check(!ChromiumClipboardData.IsVsCodeEmptySelection(CustomClipboardPickle("{\"version\":1,\"isFromEmptySelection\":false}")), "A real VS Code selection is accepted");
        Check(!ChromiumClipboardData.IsVsCodeEmptySelection(CustomClipboardPickle("{\"version\":\"bad\",\"isFromEmptySelection\":true}")), "Unexpected clipboard metadata types are ignored");
        Check(!ChromiumClipboardData.IsVsCodeEmptySelection(emptyCopy[..^1]), "Truncated custom clipboard data is ignored");
        var random = new Random(12345);
        for (var i = 0; i < 200; i++) { var bytes = new byte[random.Next(0, 100)]; random.NextBytes(bytes); ChromiumClipboardData.IsVsCodeEmptySelection(bytes); }
        Check(true, "Malformed custom clipboard data does not throw");
        var chunks = SelectionTranslationService.Split(input).ToArray();
        Check(SelectionTranslationService.Language("zh-Hans-CN") == "zh-Hans-CN", "Language codes may include script and region");
        try { SelectionTranslationService.Language("en&tl=tr"); throw new Exception("Invalid language was accepted"); }
        catch (FormatException) { Check(true, "Language code rejects request delimiters"); }
        Check(string.Concat(chunks.Select(c => c.Text + c.Whitespace)) == input, "Translation splitting preserves exact input and whitespace");
        Check(chunks.All(c => System.Text.Encoding.UTF8.GetByteCount(c.Text) <= SelectionTranslationService.ChunkBytes), "Translation chunks obey byte limits");
        Check(chunks.All(c => !c.Text.Any(char.IsSurrogate) || !c.Text.Contains('\uFFFD')), "Translation chunks preserve Unicode");
        var handler = new TranslationHandler();
        var service = new SelectionTranslationService(new System.Net.Http.HttpClient(handler));
        var output = service.TranslateAsync("  hello  ", "auto", "tr", default).GetAwaiter().GetResult();
        Check(output == "  merhaba  " && handler.LastUrl!.Contains("sl=auto") && handler.LastUrl.Contains("tl=tr"), "Translation parses segments, preserves whitespace and passes languages");
        handler.Response = "{\"translatedText\":\"bonjour\"}";
        output = service.TranslateAsync("hello", "auto", "fr", default, "libretranslate", "https://example.com/base/", "private-key").GetAwaiter().GetResult();
        using (var payload = JsonDocument.Parse(handler.LastBody!))
            Check(output == "bonjour" && handler.LastMethod == "POST" && handler.LastUrl == "https://example.com/base/translate" &&
                  payload.RootElement.GetProperty("q").GetString() == "hello" && payload.RootElement.GetProperty("api_key").GetString() == "private-key" && !handler.LastUrl.Contains("private-key"), "LibreTranslate uses its documented POST body and endpoint");
        handler.Response = "{\"responseStatus\":200,\"quotaFinished\":false,\"responseData\":{\"translatedText\":\"Merhaba &amp; dünya\"}}";
        output = service.TranslateAsync("hello & world", "en", "tr", default, "mymemory").GetAwaiter().GetResult();
        Check(output == "Merhaba & dünya" && handler.LastMethod == "GET" && handler.LastUrl!.StartsWith("https://api.mymemory.translated.net/get?") && Uri.UnescapeDataString(handler.LastUrl).Contains("langpair=en|tr"), "MyMemory uses the documented language pair and decodes HTML entities");
        var myMemoryPieces = SelectionTranslationService.Split(input, SelectionTranslationService.MyMemoryChunkBytes).ToArray();
        Check(string.Concat(myMemoryPieces.Select(piece => piece.Text + piece.Whitespace)) == input && myMemoryPieces.All(piece => System.Text.Encoding.UTF8.GetByteCount(piece.Text) <= 500), "MyMemory splits long Unicode input within 500-byte request limits");
        output = service.TranslateAsync("hello", "auto", "tr", default, "mymemory").GetAwaiter().GetResult();
        Check(output == "Merhaba & dünya" && Uri.UnescapeDataString(handler.LastUrl!).Contains("langpair=autodetect|tr"), "MyMemory maps automatic source to its observed autodetect behavior");
        var lines = SelectionTranslationService.Split("one\r\ntwo\nthree", 500, true).ToArray();
        Check(string.Concat(lines.Select(piece => piece.Text + piece.Whitespace)) == "one\r\ntwo\nthree" && lines.All(piece => !piece.Text.Contains('\n') && !piece.Text.Contains('\r')), "MyMemory keeps line boundaries outside requests");
        handler.Response = "{\"responseStatus\":200,\"quotaFinished\":true,\"responseData\":{\"translatedText\":\"warning\"}}";
        try { service.TranslateAsync("hello", "en", "tr", default, "mymemory").GetAwaiter().GetResult(); throw new Exception("MyMemory quota warning was accepted as translation"); }
        catch (InvalidOperationException) { Check(true, "MyMemory quota exhaustion is not inserted as translated text"); }
        handler.Response = "{\"responseStatus\":200,\"quotaFinished\":false,\"responseData\":{\"translatedText\":\"MYMEMORY WARNING: quota exhausted\"}}";
        try { service.TranslateAsync("hello", "en", "tr", default, "mymemory").GetAwaiter().GetResult(); throw new Exception("MyMemory warning text was accepted as translation"); }
        catch (InvalidOperationException) { Check(true, "MyMemory warning text is never inserted as a translation"); }
        var partialHandler = new MyMemoryPartialHandler();
        var partialService = new SelectionTranslationService(new System.Net.Http.HttpClient(partialHandler));
        try { partialService.TranslateAsync(new string('a', 700), "en", "tr", default, "mymemory").GetAwaiter().GetResult(); throw new Exception("Partial MyMemory translation was accepted"); }
        catch (InvalidOperationException) { Check(partialHandler.RequestCount == 2, "Failure in a later MyMemory chunk prevents a partial result"); }
        Check(SelectionTranslationService.LibreEndpoint("http://localhost:5000/translate").ToString() == "http://localhost:5000/translate", "Local LibreTranslate HTTP and explicit endpoint are supported");
        try { SelectionTranslationService.LibreEndpoint("http://example.com/"); throw new Exception("Remote HTTP was accepted"); }
        catch (FormatException) { Check(true, "Remote LibreTranslate requires HTTPS"); }
        try { SelectionTranslationService.LibreEndpoint("https://example.com/?key=secret"); throw new Exception("Endpoint query was accepted"); }
        catch (FormatException) { Check(true, "LibreTranslate URL rejects query secrets"); }
        LocalLibreTranslationCheck();
        handler.Status = System.Net.HttpStatusCode.TooManyRequests;
        try { service.TranslateAsync("hello", "auto", "tr", default).GetAwaiter().GetResult(); throw new Exception("Rate limit was accepted"); }
        catch (InvalidOperationException error) { Check(error.Message.Contains("istek sınırına") && !error.Message.Contains("hello"), "Rate limit gives actionable feedback without echoing selected text"); }
        handler.Status = System.Net.HttpStatusCode.Forbidden;
        try { service.TranslateAsync("hello", "en", "tr", default, "libretranslate", "http://localhost:5000", "private-key").GetAwaiter().GetResult(); throw new Exception("Forbidden response was accepted"); }
        catch (InvalidOperationException error) { Check(error.Message.Contains("API anahtarını") && !error.Message.Contains("private-key"), "LibreTranslate authentication failure does not expose the key"); }
        handler.Status = System.Net.HttpStatusCode.OK;
        handler.Response = "[[],null,\"en\"]";
        try { service.TranslateAsync("hello", "auto", "tr", default).GetAwaiter().GetResult(); throw new Exception("Empty response was accepted"); }
        catch (InvalidOperationException) { Check(true, "Empty translation is rejected"); }
        try { service.TranslateAsync(new string('x', 30001), "en", "tr", default).GetAwaiter().GetResult(); throw new Exception("Oversized selection was accepted"); }
        catch (InvalidOperationException) { Check(true, "Oversized selection is rejected"); }
        BoundedTranslationResponseChecks();
    }

    private static void BoundedTranslationResponseChecks()
    {
        var bomResponse = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes("[[[\"merhaba\",\"hello\"]],null,\"en\"]")).ToArray();
        using (var client = new System.Net.Http.HttpClient(new ResponseBodyHandler(() => new System.Net.Http.ByteArrayContent(bomResponse))))
            Check(new SelectionTranslationService(client).TranslateAsync("hello", "en", "tr", default).GetAwaiter().GetResult() == "merhaba", "UTF-8 JSON byte-order mark is accepted");
        var large = new byte[SelectionTranslationService.MaxResponseBytes + 1];
        using (var client = new System.Net.Http.HttpClient(new ResponseBodyHandler(() => new System.Net.Http.ByteArrayContent(large))))
        {
            try { new SelectionTranslationService(client).TranslateAsync("hello", "en", "tr", default).GetAwaiter().GetResult(); throw new Exception("Oversized declared response was accepted"); }
            catch (InvalidOperationException error) { Check(error.Message.Contains("çok büyük"), "Oversized declared response is rejected"); }
        }
        using (var client = new System.Net.Http.HttpClient(new ResponseBodyHandler(() => new System.Net.Http.StreamContent(new NonSeekableReadStream(large)))))
        {
            try { new SelectionTranslationService(client).TranslateAsync("hello", "en", "tr", default).GetAwaiter().GetResult(); throw new Exception("Oversized streaming response was accepted"); }
            catch (InvalidOperationException error) { Check(error.Message.Contains("çok büyük"), "Oversized response without content length is rejected"); }
        }
        using (var client = new System.Net.Http.HttpClient(new ResponseBodyHandler(() => new System.Net.Http.StreamContent(new StalledReadStream()))) { Timeout = TimeSpan.FromMilliseconds(100) })
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try { new SelectionTranslationService(client).TranslateAsync("hello", "en", "tr", default).GetAwaiter().GetResult(); throw new Exception("Stalled body did not time out"); }
            catch (OperationCanceledException) { Check(elapsed.Elapsed < TimeSpan.FromSeconds(3), "Timeout remains active while reading the response body"); }
        }
        using (var client = new System.Net.Http.HttpClient(new ResponseBodyHandler(() => new System.Net.Http.StreamContent(new StalledReadStream()))) { Timeout = TimeSpan.FromSeconds(5) })
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            try { new SelectionTranslationService(client).TranslateAsync("hello", "en", "tr", cancel.Token).GetAwaiter().GetResult(); throw new Exception("Stalled body ignored cancellation"); }
            catch (OperationCanceledException) { Check(cancel.IsCancellationRequested, "Caller cancellation interrupts the response body"); }
        }
    }

    private static byte[] CustomClipboardPickle(string json)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.Unicode, true);
        writer.Write(0u); // Chromium Pickle payload length, filled after writing.
        writer.Write(2u);
        void String16(string text)
        {
            writer.Write((uint)text.Length);
            var bytes = System.Text.Encoding.Unicode.GetBytes(text);
            writer.Write(bytes);
            for (var i = bytes.Length; (i & 3) != 0; i++) writer.Write((byte)0);
        }
        String16("unrelated"); String16("value");
        String16("vscode-editor-data"); String16(json);
        writer.Flush();
        stream.Position = 0;
        writer.Write((uint)(stream.Length - 4)); writer.Flush();
        return stream.ToArray();
    }

    private static void LocalLibreTranslationCheck()
    {
        using var listener = new System.Net.HttpListener();
        var temporary = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        temporary.Start();
        var port = ((System.Net.IPEndPoint)temporary.LocalEndpoint).Port;
        temporary.Stop();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var reader = new System.IO.StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            var method = context.Request.HttpMethod;
            var path = context.Request.Url!.AbsolutePath;
            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"translatedText\":\"merhaba\"}");
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
            return (method, path, body);
        });
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var result = new SelectionTranslationService(client).TranslateAsync("hello", "auto", "tr", default, "libretranslate", $"http://127.0.0.1:{port}").GetAwaiter().GetResult();
        var request = server.GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(request.body);
        Check(result == "merhaba" && request.method == "POST" && request.path == "/translate" && document.RootElement.GetProperty("source").GetString() == "auto", "LibreTranslate runs through a real local HTTP request");
    }

    private sealed class TranslationHandler : System.Net.Http.HttpMessageHandler
    {
        public string Response = "[[[\"mer\",\"hel\"],[\"haba\",\"lo\"]],null,\"en\"]";
        public System.Net.HttpStatusCode Status = System.Net.HttpStatusCode.OK;
        public string? LastUrl;
        public string? LastMethod, LastBody;
        public int RequestCount;
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            LastUrl = request.RequestUri!.ToString();
            RequestCount++;
            LastMethod = request.Method.Method;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            return new System.Net.Http.HttpResponseMessage(Status) { Content = new System.Net.Http.StringContent(Response) };
        }
    }
    private sealed class MyMemoryPartialHandler : System.Net.Http.HttpMessageHandler
    {
        public int RequestCount;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            var json = RequestCount == 1 ? "{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"translated\"}}" : "{\"responseStatus\":200,\"quotaFinished\":true,\"responseData\":{\"translatedText\":\"warning\"}}";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(json) });
        }
    }
    private sealed class ResponseBodyHandler(Func<System.Net.Http.HttpContent> content) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content() });
    }
    private sealed class NonSeekableReadStream(byte[] bytes) : System.IO.Stream
    {
        private readonly System.IO.MemoryStream _inner = new(bytes, false);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _inner.ReadAsync(buffer, token);
        public override void Flush() { }
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
    private sealed class StalledReadStream : System.IO.Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(Timeout.Infinite, token); return 0; }
        public override void Flush() { }
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
