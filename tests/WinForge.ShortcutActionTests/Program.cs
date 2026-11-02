using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;
using WinForge.Modules.Extras;

internal static class Program
{
    private static int _checks;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--capture-context-only"))
            {
                NativeRecordingChecks(); ContextMenuChecks(); PreparedActionsChecks();
                Check(ActionShortcut.Parse("Alt+XButton1") == new ActionShortcut(1, 0, "mousex1"), "Alt+XButton1 manual alias matches recorded side button");
                Console.WriteLine($"PASS: {_checks} native recording, context menu and prepared action checks.");
                return 0;
            }
            Check(ActionShortcut.Parse("ctrl+alt+MouseLeft") == new ActionShortcut(3, 0, "mouseleft"), "Mouse shortcut normalization");
            Check(ActionShortcut.Parse("Ctrl+Space") == new ActionShortcut(2, 32, ""), "Keyboard shortcut parsing");
            _checks += HotkeyConflictChecks.Run();
            DesktopChordChecks();
            Check(ActionShortcut.Parse("MouseXButton1") == new ActionShortcut(0, 0, "mousex1"), "Bare XButton1 alias accepted");
            Check(ActionShortcut.Parse("MouseX2") == new ActionShortcut(0, 0, "mousex2"), "Bare XButton2 accepted");
            DownloadsShortcutChecks();
            TranslationChecks();
            NativeRecordingChecks();
            TextExpansionChecks();
            ContextMenuChecks();
            Check(ExplorerMouseNavigationModule.ShouldNavigate("CabinetWClass", new(1), new(1), 0), "Middle click navigates the focused Explorer window");
            Check(ExplorerMouseNavigationModule.ShouldNavigate("ExploreWClass", new(1), new(1), 0), "Legacy Explorer window class supported");
            Check(!ExplorerMouseNavigationModule.ShouldNavigate("Progman", new(1), new(1), 0), "Desktop middle click remains unchanged");
            Check(!ExplorerMouseNavigationModule.ShouldNavigate("Chrome_WidgetWin_1", new(1), new(1), 0), "Other applications retain middle click");
            Check(!ExplorerMouseNavigationModule.ShouldNavigate("CabinetWClass", new(1), new(2), 0), "Clicking another window does not navigate Explorer");
            Check(!ExplorerMouseNavigationModule.ShouldNavigate("CabinetWClass", new(1), new(1), 2), "Modified middle clicks remain unchanged");
            var explorerNavigation = new ExplorerMouseNavigationModule(); explorerNavigation.Initialize(new Context()); explorerNavigation.Activate();
            Check(explorerNavigation.IsActive, "Explorer mouse navigation installs its native hook");
            explorerNavigation.Deactivate(); Check(!explorerNavigation.IsActive, "Explorer mouse navigation releases its native hook");
            var folder = System.IO.Path.GetTempPath();
            var launch = ShortcutActionModule.CreateLaunchInfo(new("folder", "Folder", true, "F23", "explorer", folder));
            Check(launch.FileName.EndsWith("explorer.exe") && launch.ArgumentList.Single() == System.IO.Path.GetFullPath(folder), "Explorer receives folder as a single argument");
            Check(ShortcutActionModule.CreateLaunchInfo(new("folder", "Folder", true, "F23", "launch", folder)).FileName.EndsWith("explorer.exe"), "Directory launch automatically uses Explorer");
            var program = ShortcutActionModule.CreateLaunchInfo(new("program", "Program", true, "F23", "launch", "notepad.exe", "--new-window"));
            Check(program.FileName == "notepad.exe" && program.Arguments == "--new-window", "Existing program arguments preserved");
            foreach (var (name, virtualKey) in new (string, int)[] { ("-", 0xBD), ("Minus", 0xBD), ("OemMinus", 0xBD), ("=", 0xBB), ("+", 0xBB), (";", 0xBA), ("'", 0xDE), ("/", 0xBF), ("\\", 0xDC), ("[", 0xDB), ("]", 0xDD), ("`", 0xC0), (",", 0xBC), (".", 0xBE), ("MediaTrackNext", 0xB0), ("MediaTrackPrevious", 0xB1), ("AudioVolumeMute", 0xAD), ("NumPad1", 0x61), ("Subtract", 0x6D) })
                Check(ActionShortcut.Parse("Ctrl+" + name) == new ActionShortcut(2, virtualKey, ""), "Captured/manual key accepted: " + name);
            foreach (var invalid in new[] { "MouseLeft", "Alt+Alt+WheelUp", "Ctrl++Space", "Ctrl+Alt+Delete", "F12", "Win+L", "Ctrl+MouseOther" }) Reject(() => ActionShortcut.Parse(invalid));
            var secret = "Türkçe şifre🔑\nİkinci satır; | +";
            var encrypted = ShortcutSecret.Protect(secret);
            Check(!encrypted.Contains(secret) && ShortcutSecret.Unprotect(encrypted) == secret, "DPAPI Unicode round trip");
            Reject(() => ShortcutSecret.Unprotect("invalid"));

            foreach (var module in new ShortcutActionModule[] { new MediaShortcutsModule(), new WindowShortcutsModule(), new DesktopShortcutsModule(), new TextShortcutsModule(), new ProgramShortcutsModule(), new MonitorShortcutsModule() })
            {
                var context = new Context(); module.Initialize(context); module.Activate();
                Check(context.HotkeyItems.Count == 0, module.Id + " defaults do not intercept existing shortcuts");
                if (Panel(module).GetProperty("rules").GetArrayLength() == 0) Command(module, "add", new { });
                var first = Panel(module).GetProperty("rules")[0].Deserialize<ShortcutActionRule>(Json)!;
                var configured = first with { Enabled = true, Shortcut = "F23", Value = module.Id == "text-shortcuts" ? "test" : module.Id == "program-shortcuts" ? "notepad.exe" : first.Value };
                Save(module, configured);
                Check(context.HotkeyItems.Single().Key == 0x86, module.Id + " registers saved shortcut");
                Save(module, configured with { Shortcut = "F24", Value = module.Id == "text-shortcuts" ? "" : configured.Value });
                Check(context.HotkeyItems.Single().Key == 0x87, module.Id + " replaces previous shortcut");
                module.Deactivate();
                var reload = (ShortcutActionModule)Activator.CreateInstance(module.GetType())!;
                reload.Initialize(context); reload.Activate();
                Check(context.HotkeyItems.Single().Key == 0x87, module.Id + " preserves replacement shortcut on reload");
                reload.Deactivate();
            }
            var c = new Context(); var text = new TextShortcutsModule(); text.Initialize(c); text.Activate();
            Save(text, new("text", "Test", true, "Ctrl+P", "text", secret));
            Check(c.HotkeyItems.Count == 1, "Enabled text shortcut registered");
            Check(!c.Values["rules"].Contains("Türkçe") && Panel(text).GetProperty("rules")[0].GetProperty("value").GetString() == secret, "Text remains encrypted in settings and is readable in panel");
            var stored = JsonSerializer.Deserialize<List<ShortcutActionRule>>(c.Values["rules"], Json)![0];
            Check(ShortcutSecret.Unprotect(stored.Value) == secret, "Persisted text decrypts");
            Save(text, stored with { Name = "Renamed", Value = "" });
            Check(JsonSerializer.Deserialize<List<ShortcutActionRule>>(c.Values["rules"], Json)![0].Value == stored.Value, "Blank edit preserves protected text");
            var previous = c.Values["rules"]; c.RejectKey = 0x51;
            Reject(() => Save(text, stored with { Shortcut = "Ctrl+Q", Value = "" }));
            Check(c.Values["rules"] == previous && c.HotkeyItems.Single().Key == 0x50, "Registration failure restores old settings and registration");
            Command(text, "capture-begin", new { });
            Check(c.HotkeyItems.Count == 0, "Capture releases active hotkeys");
            Command(text, "capture-end", new { });
            Check(c.HotkeyItems.Count == 1, "Capture end restores active hotkeys");
            text.Deactivate(); Check(c.HotkeyItems.Count == 0, "Deactivate releases hotkeys");
            var reloaded = new TextShortcutsModule(); reloaded.Initialize(c);
            Check(Panel(reloaded).GetProperty("rules")[0].GetProperty("hasSecret").GetBoolean(), "Encrypted text survives module reload");

            var media = new MediaShortcutsModule(); var mc = new Context(); media.Initialize(mc); media.Activate();
            var volumeDefault = Panel(media).GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("id").GetString() == "mouse-volume-down-5");
            Check(volumeDefault.GetProperty("enabled").GetBoolean() && volumeDefault.GetProperty("shortcut").GetString() == "MouseX1" && volumeDefault.GetProperty("action").GetString() == "volume-down-5", "XButton1 defaults to volume minus five");
            Save(media, new("play", "Play", true, "Ctrl+Space", "play"));
            Reject(() => Save(media, new("next", "Next", true, "Control+Space", "next")));
            Check(mc.HotkeyItems.Count == 1, "Equivalent duplicate rejected without losing hotkey");
            Save(media, new("play", "Play", true, "Ctrl+-", "play"));
            Check(mc.HotkeyItems.Single().Key == 0xBD, "Minus shortcut saves and registers");
            var mediaSettings = mc.Values["rules"];
            Reject(() => Save(media, new("play", "Play", true, "MouseLeft", "play")));
            Check(mc.Values["rules"] == mediaSettings && mc.HotkeyItems.Single().Key == 0xBD, "Rejected save preserves persisted shortcut and registration");
            Check(Panel(media).GetProperty("saveErrors").GetProperty("play").GetString()!.Length > 0, "Rejected save exposes inline card error");
            Save(media, new("play", "Play", true, "Ctrl+F23", "play"));
            Check(!Panel(media).GetProperty("saveErrors").TryGetProperty("play", out _), "Successful retry clears card error");
            media.Deactivate();
            NativeMediaShortcutCheck();

            var hits = 0; var key = ActionShortcut.Parse("Ctrl+Alt+MouseLeft");
            using (var binding = ActionMouseBindings.Register(key, () => hits++))
            {
                Reject(() => ActionMouseBindings.Register(key, () => { }));
                Check(!ActionMouseBindings.ProcessInput(0x201, default, 7), "Extra modifier does not trigger mouse binding");
                Check(!ActionMouseBindings.ProcessInput(0x201, new MSLLHOOKSTRUCT { flags = 1 }, 3), "Injected mouse ignored");
                Check(ActionMouseBindings.ProcessInput(0x201, default, 3), "Matched mouse down consumed");
                Check(ActionMouseBindings.ProcessInput(0x202, default, 0), "Paired up consumed after modifiers released");
                Pump(); Check(hits == 1, "Mouse action dispatched once");
            }
            Check(!ActionMouseBindings.ProcessInput(0x201, default, 3), "Disposed mouse shortcut released");
            using (ActionMouseBindings.Register(ActionShortcut.Parse("Alt+WheelUp"), () => hits++))
            {
                Check(ActionMouseBindings.ProcessInput(0x20A, new MSLLHOOKSTRUCT { mouseData = 120u << 16 }, 1), "Wheel up matched");
                Check(!ActionMouseBindings.ProcessInput(0x20A, new MSLLHOOKSTRUCT { mouseData = unchecked((uint)-120) << 16 }, 1), "Wheel down distinct");
                Pump(); Check(hits == 2, "Wheel action dispatched");
            }
            var capture = MonitorLayouts.Capture(); MonitorLayouts.Validate(capture);
            Check(JsonSerializer.Deserialize<List<MonitorLayouts.Display>>(capture)!.Any(d => d.Active), "Native monitor capture has active display");
            Reject(() => MonitorLayouts.Validate("[]"));
            var monitor = new MonitorShortcutsModule(); var monitorContext = new Context(); monitor.Initialize(monitorContext);
            Command(monitor, "add", new { });
            var profile = JsonSerializer.Deserialize<List<ShortcutActionRule>>(monitorContext.Values["rules"], Json)!.Single();
            MonitorLayouts.Validate(profile.Value);
            Check(!profile.Enabled && profile.Action == "profile", "Native layout saved as disabled shortcut card");
            var savedDisplays = JsonSerializer.Deserialize<List<MonitorLayouts.Display>>(profile.Value)!;
            var capturedDisplays = JsonSerializer.Deserialize<List<MonitorLayouts.Display>>(capture)!;
            Check(savedDisplays.Count == capturedDisplays.Count && savedDisplays.Zip(capturedDisplays).All(pair =>
                pair.First.Name == pair.Second.Name && pair.First.Active == pair.Second.Active && pair.First.Primary == pair.Second.Primary && pair.First.Mode.SequenceEqual(pair.Second.Mode)),
                "Native layout preserves complete display modes without source files");
            monitor.Activate();
            Save(monitor, profile with { Name = "Native profile", Enabled = true, Value = "" });
            var enabledProfile = JsonSerializer.Deserialize<List<ShortcutActionRule>>(monitorContext.Values["rules"], Json)!.Single();
            Check(enabledProfile.Value == profile.Value && monitorContext.HotkeyItems.Count == 1, "Editing and binding preserve native layout");
            Command(monitor, "snapshot", new { id = profile.Id });
            var updatedProfile = JsonSerializer.Deserialize<List<ShortcutActionRule>>(monitorContext.Values["rules"], Json)!.Single();
            MonitorLayouts.Validate(updatedProfile.Value);
            Check(updatedProfile.Enabled && updatedProfile.Shortcut == profile.Shortcut && monitorContext.HotkeyItems.Count == 1, "Snapshot refresh preserves shortcut and enabled state");
            var savedSettings = monitorContext.Values["rules"];
            Reject(() => Command(monitor, "import", new { }));
            Check(monitorContext.Values["rules"] == savedSettings, "Removed import command cannot mutate native profiles");
            monitor.Deactivate();
            var restoredMonitor = new MonitorShortcutsModule(); restoredMonitor.Initialize(monitorContext); restoredMonitor.Activate();
            Check(monitorContext.HotkeyItems.Count == 1 && Panel(restoredMonitor).GetProperty("rules")[0].GetProperty("name").GetString() == "Native profile", "Native profile and binding survive module reload");
            restoredMonitor.Deactivate();
            Command(restoredMonitor, "delete", new { id = profile.Id });
            Check(JsonSerializer.Deserialize<List<ShortcutActionRule>>(monitorContext.Values["rules"], Json)!.Count == 0, "Native profile can be deleted");
            Console.WriteLine($"PASS: {_checks} shortcut action checks. Monitor switching, media playback and foreground-window actions were not performed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Save(ShortcutActionModule m, ShortcutActionRule rule) => Command(m, "save", new { rule });
    private static void DesktopChordChecks()
    {
        static (ushort Key, uint Flags)[] Events(ushort arrow, params ushort[] held) =>
            ActionInput.DesktopChord(arrow, held).Select(i => (i.Union.Keyboard.VirtualKey, i.Union.Keyboard.Flags)).ToArray();
        Check(ActionShortcut.Parse("Win+MouseLeft") == new ActionShortcut(8, 0, "mouseleft"), "Windows left click parses as desktop shortcut");
        Check(ActionShortcut.Parse("Win+MouseRight") == new ActionShortcut(8, 0, "mouseright"), "Windows right click parses as desktop shortcut");
        Check(Events(0x25, 0x5B).SequenceEqual(new (ushort, uint)[] { (0xA2, 0), (0x25, 1), (0x25, 3), (0xA2, 2) }), "Desktop left chord preserves held left Windows key for repeated clicks");
        Check(Events(0x27, 0x5C, 0xA3).SequenceEqual(new (ushort, uint)[] { (0x27, 1), (0x27, 3) }), "Desktop right chord preserves held right Windows and Control keys");
        Check(Events(0x27, 0x5B, 0xA4, 0xA1).SequenceEqual(new (ushort, uint)[] { (0xA4, 2), (0xA1, 2), (0xA2, 0), (0x27, 1), (0x27, 3), (0xA2, 2), (0xA4, 0), (0xA1, 0) }), "Desktop chord releases and restores unrelated physical modifiers");
        Check(Events(0x25).SequenceEqual(new (ushort, uint)[] { (0xA2, 0), (0x5B, 0), (0x25, 1), (0x25, 3), (0x5B, 2), (0xA2, 2) }), "Desktop chord supplies and releases missing modifiers");
    }
    private static void PreparedActionsChecks()
    {
        var context = new Context(); var module = new ContextMenuModule(); module.Initialize(context);
        var panel = (IModulePanel)module;
        Check(ContextMenuCatalog.Contexts.Contains("shortcut") && ContextMenuCatalog.Contexts.Contains("application") && ContextMenuCatalog.Contexts.Contains("virtual"), "Shortcut, application and virtual shell contexts are available");
        var inherited = new ContextMenuRule("x", Contexts: new() { ["file"] = false });
        Check(!ContextMenuCatalog.VisibleIn(inherited, "shortcut") && !ContextMenuCatalog.VisibleIn(inherited, "application"), "New file subtypes inherit old file preferences");
        Check(ContextMenuCatalog.VisibleIn(inherited with { Contexts = new() { ["file"] = false, ["shortcut"] = true } }, "shortcut"), "Shortcut visibility can override the old file preference");
        panel.HandlePanelCommand("save-options", JsonSerializer.SerializeToElement(new { cleanSeparators = false, showSeparators = false, unquotedCopyPath = false }, Json));
        var optionReload = new ContextMenuModule(); optionReload.Initialize(context);
        var options = JsonSerializer.SerializeToElement(optionReload.GetPanelState(), Json);
        Check(!options.GetProperty("cleanSeparators").GetBoolean() && !options.GetProperty("showSeparators").GetBoolean() && !options.GetProperty("unquotedCopyPath").GetBoolean(), "Separator and copy-path preferences persist after reload");
        panel.HandlePanelCommand("save-item", JsonSerializer.SerializeToElement(new { ids = new[] { "builtin:copy-path" }, rule = new ContextMenuRule("builtin:copy-path", SeparatorBefore: true, SeparatorAfter: true, Contexts: new() { ["shortcut"] = true, ["file"] = false }) }, Json));
        var lineReload = new ContextMenuModule(); lineReload.Initialize(context);
        var copyPath = JsonSerializer.SerializeToElement(lineReload.GetPanelState(), Json).GetProperty("entries").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "builtin:copy-path");
        Check(copyPath.GetProperty("separatorBefore").GetBoolean() && copyPath.GetProperty("separatorAfter").GetBoolean() && copyPath.GetProperty("contexts").GetProperty("shortcut").GetBoolean(), "Custom separator placement and shortcut overrides survive reload");
        var entries = JsonSerializer.SerializeToElement(panel.GetPanelState(), Json).GetProperty("entries");
        Check(entries.EnumerateArray().Count(e => e.GetProperty("id").GetString()!.StartsWith("prepared:") && !e.GetProperty("visible").GetBoolean()) == 2, "Prepared actions are available and disabled by default");
        var folderIcon = entries.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "prepared:folder-icon");
        Check(folderIcon.GetProperty("visible").GetBoolean() && folderIcon.GetProperty("contexts").EnumerateObject().All(p => p.Value.GetBoolean() == (p.Name == "folder")), "Folder icon action defaults to folders only");
        var shellLibrary = NativeLibrary.Load("shell32.dll");
        try { Check(NativeLibrary.TryGetExport(shellLibrary, "PickIconDlg", out _), "Windows exposes the native icon selection dialog"); }
        finally { NativeLibrary.Free(shellLibrary); }
        foreach (var id in new[] { "prepared:desktop-shortcut", "prepared:toggle-hidden" })
            panel.HandlePanelCommand("save", JsonSerializer.SerializeToElement(new { id, rule = new ContextMenuRule(id, Group: "Other") }, Json));
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinForge.PreparedActions", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        var source = System.IO.Path.Combine(root, "sample.txt"); System.IO.File.WriteAllText(source, "test");
        try
        {
            typeof(ContextMenuModule).GetProperty(nameof(ContextMenuModule.IsActive))!.SetValue(module, true);
            Reject(() => module.HandleCommand("--context-folder-icon", [source]));
            Reject(() => module.HandleCommand("--context-folder-icon", [root, root]));
            var originalAttributes = System.IO.File.GetAttributes(root);
            Check(!FolderIconPicker.Show(root, (IntPtr owner, System.Text.StringBuilder path, uint capacity, ref int index) => 0), "Cancelling the native icon picker returns without applying");
            Check(!System.IO.File.Exists(System.IO.Path.Combine(root, "desktop.ini")) && System.IO.File.GetAttributes(root) == originalAttributes, "Cancelling leaves folder settings and attributes untouched");
            Check(FolderIconPicker.Show(root, (IntPtr owner, System.Text.StringBuilder path, uint capacity, ref int index) => { index = 4; return 1; }), "Confirmed icon selection applies through Windows shell settings");
            var ini = System.IO.Path.Combine(root, "desktop.ini");
            Check(System.IO.File.Exists(ini) && System.IO.File.ReadAllText(ini).Contains("shell32.dll", StringComparison.OrdinalIgnoreCase), "Native folder customization persists the selected icon resource");
            Check(System.IO.File.GetAttributes(root).HasFlag(System.IO.FileAttributes.ReadOnly), "Folder is marked for Explorer customization");
            Check(!FolderIconPicker.Show(root, (IntPtr owner, System.Text.StringBuilder path, uint capacity, ref int index) => { Check(index == 4 && path.ToString().EndsWith("shell32.dll", StringComparison.OrdinalIgnoreCase), "Picker reopens with the current folder icon"); return 0; }), "Cancelling an existing customization preserves it");
            module.HandleCommand("--context-toggle-hidden", [source]);
            Check(System.IO.File.GetAttributes(source).HasFlag(System.IO.FileAttributes.Hidden), "Hide action changes the selected file attribute");
            module.HandleCommand("--context-toggle-hidden", [source]);
            Check(!System.IO.File.GetAttributes(source).HasFlag(System.IO.FileAttributes.Hidden), "Show action restores visibility");
            var desktop = System.IO.Path.Combine(root, "desktop"); System.IO.Directory.CreateDirectory(desktop);
            ContextMenuModule.CreateDesktopShortcut(source, desktop);
            ContextMenuModule.CreateDesktopShortcut(source, desktop);
            Check(System.IO.File.Exists(System.IO.Path.Combine(desktop, "sample.lnk")) && System.IO.File.Exists(System.IO.Path.Combine(desktop, "sample (2).lnk")), "Desktop action creates real shell links without overwriting existing shortcuts");
        }
        finally
        {
            typeof(ContextMenuModule).GetProperty(nameof(ContextMenuModule.IsActive))!.SetValue(module, false);
            var expected = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinForge.PreparedActions"));
            if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(root)) == expected)
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(root, "*", System.IO.SearchOption.AllDirectories)) System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
                System.IO.File.SetAttributes(root, System.IO.FileAttributes.Directory);
                System.IO.Directory.Delete(root, true);
            }
        }
    }

    private static void NativeRecordingChecks()
    {
        var keyboard = typeof(ShortcutRecorder).GetMethod("Keyboard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var mouse = typeof(ShortcutRecorder).GetMethod("Mouse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var expected in new[] { "Win+D", "Alt+MouseX1" })
        {
            var recorded = new List<string>();
            using var recorder = new ShortcutRecorder(recorded.Add, false);
            var pointer = Marshal.AllocHGlobal(128);
            try
            {
                IntPtr Key(uint key, int message)
                {
                    Marshal.StructureToPtr(new KBDLLHOOKSTRUCT { vkCode = key }, pointer, false);
                    return (IntPtr)keyboard.Invoke(recorder, [0, new IntPtr(message), pointer])!;
                }
                Check(Key(expected.StartsWith("Win") ? 0x5Bu : 0xA4u, 0x100) == new IntPtr(1), "Recorder consumes modifier before Windows sees it");
                if (expected.StartsWith("Win"))
                {
                    Check(Key(0x44, 0x100) == new IntPtr(1), "Win+D is consumed instead of reaching desktop");
                    Key(0x44, 0x100);
                    Check(Key(0x44, 0x101) == new IntPtr(1), "Recorded key release is consumed");
                }
                else
                {
                    Marshal.StructureToPtr(new MSLLHOOKSTRUCT { mouseData = 1u << 16 }, pointer, false);
                    Check((IntPtr)mouse.Invoke(recorder, [0, new IntPtr(0x20B), pointer])! == new IntPtr(1), "Side mouse input is consumed before Explorer navigation");
                }
                Check(recorded.SequenceEqual([expected]), "Native recording produces one complete chord: " + expected);
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
    }

    private static void ContextMenuChecks()
    {
        ContextMenuOwnershipChecks();
        Check(ContextMenuCatalog.Normalize("&Properties\tAlt+Enter") == "properties", "Menu labels normalize accelerators");
        Check(ContextMenuCatalog.BestName("as18w0fw0q8", null, "Readable extension", "Unknown") == "Readable extension", "Opaque handler keys use descriptive names");
        Check(ContextMenuCatalog.BestName("ignored", "@shell32.dll,-21770", null, "Unknown").Length > 0, "Indirect shell resources resolve to display names");
        Check(ContextMenuCatalog.IsOpaque("{00000000-0000-0000-0000-000000000000}"), "CLSID values are not presented as friendly names");
        Check(ContextMenuCatalog.IconPreview("shell32.dll,3")?.StartsWith("data:image/png;base64,") == true, "Existing shell resource icons render as PNG previews");
        Reject(() => ContextMenuCatalog.ValidateRules([new("x", Group: "invalid\nname")]));
        Reject(() => ContextMenuCatalog.ValidateRules([new("x"), new("x")]));
        var context = new Context(); var module = new ContextMenuModule(); module.Initialize(context);
        var panel = (IModulePanel)module;
        void Update(ContextMenuRule rule) => panel.HandlePanelCommand("save", JsonSerializer.SerializeToElement(new { id = rule.Id, rule }, Json));
        Update(new("builtin:properties", "Özellikler", Group: "Diğer"));
        Update(new("builtin:new-folder", "Yeni klasör", Group: "Diğer", Icon: "shell32.dll,3"));
        var state = JsonSerializer.SerializeToElement(panel.GetPanelState(), Json);
        Check(state.GetProperty("groups").EnumerateArray().Single().GetString() == "Diğer", "Properties and New Folder can share one group");
        Check(state.GetProperty("entries").EnumerateArray().Count(row => row.GetProperty("group").GetString() == "Diğer") == 2, "Both built-in entries appear in the panel group");
        Check(state.GetProperty("entries").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "builtin:new-folder").GetProperty("iconPreview").GetString()!.StartsWith("data:image/png"), "Custom icon is visible on existing entry");
        var reload = new ContextMenuModule(); reload.Initialize(context);
        Check(JsonSerializer.SerializeToElement(reload.GetPanelState(), Json).GetProperty("groups")[0].GetString() == "Diğer", "Menu groups persist across reload");
        Update(new("builtin:properties", "Dosya özellikleri", Visible: false, Group: "Diğer"));
        state = JsonSerializer.SerializeToElement(panel.GetPanelState(), Json);
        var properties = state.GetProperty("entries").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "builtin:properties");
        Check(!properties.GetProperty("visible").GetBoolean() && properties.GetProperty("name").GetString() == "Dosya özellikleri", "Visibility and readable title update automatically");
        panel.HandlePanelCommand("reset", JsonSerializer.SerializeToElement(new { id = "builtin:properties" }, Json));
        properties = JsonSerializer.SerializeToElement(panel.GetPanelState(), Json).GetProperty("entries").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "builtin:properties");
        Check(properties.GetProperty("visible").GetBoolean() && properties.GetProperty("group").GetString() == "", "Entry can return to original menu");
        panel.HandlePanelCommand("save-item", JsonSerializer.SerializeToElement(new { ids = new[] { "builtin:properties" }, rule = new ContextMenuRule("builtin:properties", Contexts: new() { ["desktop"] = false, ["folder"] = true }) }, Json));
        var scopedReload = new ContextMenuModule(); scopedReload.Initialize(context);
        properties = JsonSerializer.SerializeToElement(scopedReload.GetPanelState(), Json).GetProperty("entries").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "builtin:properties");
        Check(!properties.GetProperty("contexts").GetProperty("desktop").GetBoolean() && properties.GetProperty("contexts").GetProperty("folder").GetBoolean(), "Independent menu scopes persist across reload");
        panel.HandlePanelCommand("save-group", JsonSerializer.SerializeToElement(new { name = "Tools", ids = new[] { "builtin:properties", "builtin:new-folder" } }, Json));
        panel.HandlePanelCommand("save-group", JsonSerializer.SerializeToElement(new { name = "Utilities", previousName = "Tools", ids = new[] { "builtin:properties" } }, Json));
        state = JsonSerializer.SerializeToElement(panel.GetPanelState(), Json);
        Check(!state.GetProperty("groups").EnumerateArray().Any(g => g.GetString() == "Tools"), "Renaming a group removes its old name");
        Check(state.GetProperty("entries").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "builtin:new-folder").GetProperty("group").GetString() == "", "Deselected group members return to root");
        panel.HandlePanelCommand("delete-group", JsonSerializer.SerializeToElement(new { name = "Utilities" }, Json));
        Check(!JsonSerializer.SerializeToElement(panel.GetPanelState(), Json).GetProperty("groups").EnumerateArray().Any(g => g.GetString() == "Utilities"), "Deleting a group removes membership and its persisted name");
        panel.HandlePanelCommand("save-group", JsonSerializer.SerializeToElement(new { name = "Empty", ids = Array.Empty<string>() }, Json));
        var emptyReload = new ContextMenuModule(); emptyReload.Initialize(context);
        Check(JsonSerializer.SerializeToElement(emptyReload.GetPanelState(), Json).GetProperty("groups").EnumerateArray().Any(g => g.GetString() == "Empty"), "Empty groups persist before members are selected");
        Check(!module.IsActive, "Menu tests leave real Explorer configuration untouched");
    }
    private static void ContextMenuOwnershipChecks()
    {
        var root = @"Software\WinForge.Tests\ContextMenuOwnership\" + Guid.NewGuid().ToString("N");
        var flags = System.Reflection.BindingFlags.NonPublic;
        var apply = typeof(ContextMenuModule).GetMethod("ApplyCustomItems", flags | System.Reflection.BindingFlags.Instance)!;
        var remove = typeof(ContextMenuModule).GetMethod("RemoveCustomItems", flags | System.Reflection.BindingFlags.Static)!;
        var preserved = new[] { "WinForge.FileLocksmith", "WinForge.Peek", "WinForge.PowerRename", "WinForge.Prepared.desktop-shortcut", "OtherApp" };
        try
        {
            using var shell = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(root);
            foreach (var name in preserved.Append("WinForge.0").Append("WinForge.12"))
            {
                using var command = shell.CreateSubKey(name + @"\command");
                command.SetValue(null, "original-command");
            }
            apply.Invoke(new ContextMenuModule(), [root, "Test item|notepad.exe"]);
            Check(!shell.GetSubKeyNames().Contains("WinForge.12") && shell.GetSubKeyNames().Contains("WinForge.0"),
                "Custom menu apply replaces previous numbered items");
            remove.Invoke(null, [root]);
            Check(!shell.GetSubKeyNames().Contains("WinForge.0"), "Custom menu cleanup removes its numbered items");
            foreach (var name in preserved)
            {
                using var command = shell.OpenSubKey(name + @"\command");
                Check(command?.GetValue(null) as string == "original-command",
                    "Custom menu apply and cleanup preserve feature registration: " + name);
            }
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root, false); }
    }
    private static void TranslationChecks()
    {
        var context = new Context();
        var module = new CombinationTranslatorModule(); module.Initialize(context); module.Activate();
        var mapping = CombinationTranslatorModule.ParseMapping(new("example", "Example", true, "Ctrl+D", "translate", "Alt+W"));
        Check(mapping.FromKey == 0x44 && mapping.FromModifiers == 1 && mapping.ToKey == 0x57 && mapping.ToModifiers == 2, "Translator parses Ctrl+D to Alt+W with correct modifier flags");
        Save(module, new("example", "Example", true, "Ctrl+D", "translate", "Alt+W"));
        var engine = (KeyboardManagerModule)typeof(CombinationTranslatorModule).GetField("_engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(module)!;
        Check(engine.IsActive && context.HotkeyItems.Count == 0, "Translator uses the suppressing keyboard engine");
        Command(module, "capture-begin", new { });
        Check(!engine.IsActive, "Recording releases the translation hook");
        using (var captureHotkeys = new HotkeyService())
        using (captureHotkeys.Suspend()) Command(module, "capture-end", new { });
        Check(engine.IsActive, "Cancelling recording restores the translator before the outer capture scope resumes");
        var events = new List<(uint Key, bool Up)>();
        Native.LowLevelKeyboardProc observer = (code, message, pointer) =>
        {
            if (code >= 0)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(pointer);
                if ((info.flags & 0x10) != 0) { events.Add((info.vkCode, (info.flags & 0x80) != 0)); return new IntPtr(1); }
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, message, pointer);
        };
        var hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, observer, Native.GetModuleHandle(null), 0);
        try
        {
            var handler = typeof(KeyboardManagerModule).GetMethod("HandleKeyboard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<KBDLLHOOKSTRUCT>());
            IntPtr Input(uint key, bool up)
            {
                Marshal.StructureToPtr(new KBDLLHOOKSTRUCT { vkCode = key, flags = up ? 0x80u : 0 }, pointer, false);
                return (IntPtr)handler.Invoke(engine, [0, new IntPtr(up ? 0x101 : 0x100), pointer])!;
            }
            try
            {
                Input(0xA2, false);
                Check(Input(0x44, false) == new IntPtr(1), "Original Ctrl+D is consumed");
                Check(Input(0x44, true) == new IntPtr(1), "Original D release is consumed");
                Input(0xA2, true); Pump();
                Check(events.SequenceEqual(new (uint, bool)[] { (0xA2, true), (0xA4, false), (0x57, false), (0x57, true), (0xE8, false), (0xE8, true), (0xA4, true), (0xA2, false) }), "Native SendInput releases source Ctrl, sends Alt+W, masks the Alt menu and restores source modifier: " + string.Join(",", events));
                events.Clear();
                using (var hotkeys = new HotkeyService())
                using (hotkeys.Suspend()) { Input(0xA2, false); Input(0x44, false); Input(0x44, true); Input(0xA2, true); Pump(); }
                Check(events.Count == 0, "Shortcut capture suspends conversion");

                Save(module, new("example", "Desktop conversion", true, "Win+D", "translate", "Ctrl+Shift+OemOpenBrackets"));
                foreach (var win in new uint[] { 0x5B, 0x5C })
                {
                    events.Clear();
                    Input(win, false);
                    Check(Input(0x44, false) == new IntPtr(1), "Win+D source is consumed for either Windows key");
                    Check(Input(0x44, false) == new IntPtr(1), "Held source is consumed without retranslating");
                    Input(win, true);
                    Check(Input(0x44, false) == new IntPtr(1), "Source repeats remain consumed after Win is released");
                    Check(Input(0x44, true) == new IntPtr(1), "Source release remains consumed after Win is released");
                    Pump();
                    Check(events.SequenceEqual(new (uint, bool)[] {
                        (0xE8, false), (0xE8, true), (win, true),
                        (0xA2, false), (0xA0, false), (0xDB, false), (0xDB, true),
                        (0xA0, true), (0xA2, true), (win, false), (0xE8, false), (0xE8, true)
                    }), "Win+D sends exactly Ctrl+Shift+OemOpenBrackets once, with Start menu masking: " + string.Join(",", events));
                }
                Save(module, new("example", "Example", true, "Ctrl+D", "translate", "Alt+W"));
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        finally { ActionInput.Send([ActionInput.Key(0xA2, 2)]); Native.UnhookWindowsHookEx(hook); module.Deactivate(); GC.KeepAlive(observer); }
        var reload = new CombinationTranslatorModule(); reload.Initialize(context);
        Check(Panel(reload).GetProperty("rules")[0].GetProperty("value").GetString() == "Alt+W", "Translator rule survives reload");
        Reject(() => Save(reload, new("example", "Example", true, "Ctrl+D", "translate", "Win+L")));
        Reject(() => Save(reload, new("example", "Example", true, "Ctrl+D", "translate", "Ctrl+D")));
    }
    private static void DownloadsShortcutChecks()
    {
        var context = new Context();
        var module = new ProgramShortcutsModule(); module.Initialize(context);
        var rule = Panel(module).GetProperty("rules")[0].Deserialize<ShortcutActionRule>(Json)!;
        Check(rule.Enabled && rule.Shortcut == "MouseX2" && rule.Action == "downloads", "XButton2 defaults to opening Downloads");
        var launch = ShortcutActionModule.CreateLaunchInfo(rule);
        Check(launch.UseShellExecute && launch.FileName.EndsWith("explorer.exe") && launch.ArgumentList.Single() == "shell:Downloads", "Downloads uses Windows shell target to follow folder relocation");
        Save(module, rule with { Enabled = false });
        var reload = new ProgramShortcutsModule(); reload.Initialize(context);
        Check(!Panel(reload).GetProperty("rules")[0].GetProperty("enabled").GetBoolean(), "Disabled Downloads shortcut stays disabled after reload");
        Command(reload, "delete", new { id = rule.Id });
        var deleted = new ProgramShortcutsModule(); deleted.Initialize(context);
        Check(Panel(deleted).GetProperty("rules").GetArrayLength() == 0, "Deleted Downloads shortcut is not re-added");

        var existing = new ShortcutActionRule("custom", "Existing", true, "MouseXButton2", "launch", "notepad.exe");
        var migratedContext = new Context();
        migratedContext.Values["rules"] = JsonSerializer.Serialize(new[] { existing }, Json);
        var migrated = new ProgramShortcutsModule(); migrated.Initialize(migratedContext);
        var rules = Panel(migrated).GetProperty("rules").Deserialize<List<ShortcutActionRule>>(Json)!;
        Check(rules.Count == 2 && rules[0] == existing && !rules[1].Enabled, "Migration preserves an existing XButton2 binding and adds Downloads disabled");
        var emptyContext = new Context(); emptyContext.Values["rules"] = "[]";
        var empty = new ProgramShortcutsModule(); empty.Initialize(emptyContext);
        Check(Panel(empty).GetProperty("rules")[0].GetProperty("enabled").GetBoolean(), "Existing empty configuration gets enabled Downloads shortcut");
    }
    private static void Command(ShortcutActionModule m, string action, object data) => m.HandlePanelCommand(action, JsonSerializer.SerializeToElement(data, Json));
    private static JsonElement Panel(ShortcutActionModule m) => JsonSerializer.SerializeToElement(m.GetPanelState(), Json);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static void Reject(Action action) { try { action(); } catch { _checks++; return; } throw new Exception("Invalid operation accepted"); }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void NativeMediaShortcutCheck()
    {
        // Consume the generated media key in our hook so the test cannot change playback.
        var hits = 0;
        Native.LowLevelKeyboardProc callback = (code, message, pointer) =>
        {
            if (code >= 0 && Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(pointer).vkCode == 0xB3)
            {
                if (message.ToInt32() == 0x100) hits++;
                return new IntPtr(1);
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, message, pointer);
        };
        var hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, callback, Native.GetModuleHandle(null), 0);
        Check(hook != IntPtr.Zero, "Media observation hook installed");
        using var hotkeys = new HotkeyService();
        var module = new MediaShortcutsModule();
        var context = new Context { NativeHotkeys = hotkeys };
        try
        {
            module.Initialize(context); module.Activate();
            Save(module, new("play", "Native playback test", true, "F23", "play"));
            ActionInput.Chord(0x86);
            Pump();
            Check(hits == 1, "Real WM_HOTKEY dispatch executes configured media action exactly once: " + hits);
            Save(module, new("play", "Native playback test", true, "F24", "play"));
            ActionInput.Chord(0x86); Pump();
            Check(hits == 1, "Saving a new shortcut unregisters old native binding immediately");
            ActionInput.Chord(0x87); Pump();
            Check(hits == 2, "Saved replacement native shortcut executes media action");
            using (var suspended = hotkeys.Suspend())
            {
                ActionInput.Chord(0x87); Pump();
                Check(hits == 2, "Shortcut editing suspends existing native actions");
                using (hotkeys.Suspend())
                {
                    Save(module, new("play", "Native playback test", true, "F23", "play"));
                }
                ActionInput.Chord(0x86); Pump();
                Check(hits == 2, "Nested capture does not resume a pending edit");
            }
            ActionInput.Chord(0x87); Pump();
            Check(hits == 2, "Capture resume does not restore superseded shortcut");
            ActionInput.Chord(0x86); Pump();
            Check(hits == 3, "Capture resume registers most recently saved shortcut");
            Save(module, new("play", "Native playback test", true, "Alt+WheelUp", "play"));
            using (hotkeys.Suspend())
                Check(!ActionMouseBindings.ProcessInput(0x20A, new MSLLHOOKSTRUCT { mouseData = 120u << 16 }, 1), "Existing mouse shortcut passes through during capture");
            Check(!HotkeyService.IsCapturing, "Capture scope restores keyboard and mouse dispatch");
            ActionInput.Chord(0x86); Pump();
            Check(hits == 3, "Keyboard to mouse edit removes native keyboard binding");
            Check(ActionMouseBindings.ProcessInput(0x20A, new MSLLHOOKSTRUCT { mouseData = 120u << 16 }, 1), "Replacement mouse binding matches");
            Pump();
            Check(hits == 4, "Replacement mouse shortcut executes configured media action");
            Save(module, new("play", "Native playback test", true, "F24", "play"));
            Check(!ActionMouseBindings.ProcessInput(0x20A, new MSLLHOOKSTRUCT { mouseData = 120u << 16 }, 1), "Mouse to keyboard edit removes old hook binding");
            ActionInput.Chord(0x87); Pump();
            Check(hits == 5, "Mouse to keyboard replacement executes new native key");
            module.Deactivate();
            ActionInput.Chord(0x87); Pump();
            Check(hits == 5, "Deactivated native shortcut does not execute media action");
        }
        finally
        {
            module.Deactivate();
            Native.UnhookWindowsHookEx(hook);
            GC.KeepAlive(callback);
        }
    }
    private static void TextExpansionChecks()
    {
        var buffer = new TextExpansionBuffer();
        foreach (var c in ".gk") Check(buffer.Push(c) is null, "Expansion waits for space");
        Check(buffer.Push(' ') == ".gk", "Space completes exact abbreviation");
        foreach (var c in "x.gk") buffer.Push(c);
        Check(buffer.Push(' ') == "x.gk", "Abbreviations do not match inside another token");
        foreach (var c in ".gk") buffer.Push(c);
        buffer.Reset(); Check(buffer.Push(' ') == "", "Focus/navigation reset discards pending abbreviation");
        foreach (var c in new string('a', 65) + ".gk") buffer.Push(c);
        Check(buffer.Push(' ') is null, "Overlong input cannot match a suffix");
        foreach (var invalid in new[] { "", ".g k", ".gk\n", new string('x', 65) }) Reject(() => TextExpansionBinding.Validate(invalid));
        TextExpansionBinding.Validate(".gk"); TextExpansionBinding.Validate(".iş");
        var context = new Context(); var module = new TextShortcutsModule(); module.Initialize(context); module.Activate();
        try
        {
            Save(module, new("text", "Mail", true, ".gk", "text-expand", "mail@example.com"));
            Check(context.HotkeyItems.Count == 0, "Expansion installs hook without registering a keyboard shortcut");
            Check(!context.Values["rules"].Contains("mail@example.com") && Panel(module).GetProperty("rules")[0].GetProperty("value").GetString() == "mail@example.com", "Expansion text remains encrypted in settings and visible in panel");
            Command(module, "add", new { });
            var second = Panel(module).GetProperty("rules")[1].Deserialize<ShortcutActionRule>(Json)!;
            Reject(() => Save(module, second with { Enabled = true, Action = "text-expand", Shortcut = ".gk", Value = "duplicate" }));
            Save(module, second with { Enabled = true, Action = "text-expand", Shortcut = ".iş", Value = "Turkish" });
            Command(module, "capture-begin", new { }); Command(module, "capture-end", new { });
            var reload = new TextShortcutsModule(); module.Deactivate(); reload.Initialize(context); reload.Activate(); reload.Deactivate();
            Check(Panel(reload).GetProperty("rules")[0].GetProperty("action").GetString() == "text-expand", "Expansion configuration survives reload and capture lifecycle");
        }
        finally { module.Deactivate(); }
    }

    private sealed class Context : IModuleContext, IModuleSettings, IHotkeyService, ILogger
    {
        internal readonly Dictionary<string, string> Values = new();
        internal readonly List<(uint Modifiers, uint Key, Action Callback)> HotkeyItems = new();
        internal uint RejectKey;
        internal IHotkeyService? NativeHotkeys;
        public IModuleSettings Settings => this;
        public ILogger Log => this;
        public IHotkeyService Hotkeys => NativeHotkeys ?? this;
        public string Path => "";
        public void Notify(string message) { }
        public bool GetBool(string key, bool fallback = false) => Values.TryGetValue(key, out var v) ? bool.Parse(v) : fallback;
        public int GetInt(string key, int fallback = 0) => Values.TryGetValue(key, out var v) ? int.Parse(v) : fallback;
        public string GetString(string key, string fallback = "") => Values.GetValueOrDefault(key, fallback);
        public void Set(string key, object? value) => Values[key] = value?.ToString() ?? "";
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback)
        {
            if (virtualKey == RejectKey) throw new InvalidOperationException("Hotkey in use");
            var item = (modifiers, virtualKey, callback); HotkeyItems.Add(item); return new Lease(() => HotkeyItems.Remove(item));
        }
        private sealed class Lease(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}
