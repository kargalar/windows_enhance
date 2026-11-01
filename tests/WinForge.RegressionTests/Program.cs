using System.Reflection;
using System.Numerics;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForge.App;
using ForgeApplication = WinForge.App.App;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class Program
{
    private static readonly string TestDirectory = Path.Combine(Path.GetTempPath(), "WinForge.Tests", Guid.NewGuid().ToString("N"));
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--notification-activation")) return 0;
        if (args.Length == 1 && args[0] == "--window-fixture") return WindowControlParityChecks.RunFixture();
        if (args.Length is 2 or 3 && args[0] == "--autocorrect-turkish-tweets-benchmark")
        {
            var maximumRows = args.Length == 3 && args[2].StartsWith("--max-rows=", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(args[2]["--max-rows=".Length..], System.Globalization.CultureInfo.InvariantCulture)
                : int.MaxValue;
            var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
            AutoCorrectCorpusChecks.RunTurkishTweetCorrections(args[1], extras, Check, maximumRows);
            Console.WriteLine($"PASS: {_passed} TurkishTweets benchmark checks.");
            return 0;
        }
        if (args.Length == 2 && args[0] == "--autocorrect-turkish-tweets-nextword-benchmark")
        {
            var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
            AutoCorrectCorpusChecks.RunTurkishTweetNextWords(args[1], extras, Check);
            Console.WriteLine($"PASS: {_passed} TurkishTweets next-word benchmark checks.");
            return 0;
        }
        if (args.Contains("--software-rendering")) RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        Directory.CreateDirectory(TestDirectory);
        var app = new RegressionApplication();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/WinForge;component/Theme.xaml", UriKind.Relative)
        });
        app.CheckRunner = () => RunChecks(app, args);
        try { return app.Run(); }
        finally
        {
            var cleanupPath = Path.GetFullPath(TestDirectory);
            var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinForge.Tests")) + Path.DirectorySeparatorChar;
            if (cleanupPath.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(cleanupPath))
                Directory.Delete(cleanupPath, true);
        }
    }

    private static int RunChecks(RegressionApplication app, string[] args)
    {
        try
        {
            if (args.Contains("--module-fixes-only"))
            {
                ModuleFixChecks.Run(TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} module fix checks.");
                return 0;
            }
            if (args.Contains("--brightness-wheel-only"))
            {
                BrightnessWheelChecks.Run(Check);
                Console.WriteLine($"PASS: {_passed} brightness wheel checks.");
                return 0;
            }
            if (args.Contains("--start-placement-only"))
            {
                StartMenuPlacementChecks.Run(Check);
                Console.WriteLine($"PASS: {_passed} Start placement checks.");
                return 0;
            }
            if (args.Contains("--combined-modules-only"))
            {
                CombinedModuleChecks.Run(TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} combined module checks.");
                return 0;
            }
            if (args.Contains("--context-menu-ui-only"))
            {
                ContextMenuPanelChecks.Run(app, TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} context menu WebView2 checks.");
                return 0;
            }
            if (args.Contains("--panel-placement-only"))
            {
                PanelPlacementChecks.Run(Check);
                Console.WriteLine($"PASS: {_passed} panel placement and automatic settings checks.");
                return 0;
            }
            if (args.Contains("--volume-overlay-only"))
            {
                VolumeOverlayChecks.Run(Check);
                Console.WriteLine($"PASS: {_passed} volume overlay checks.");
                return 0;
            }
            if (args.Contains("--volume-keys-only"))
            {
                VolumeKeyChecks.Run(Check);
                Console.WriteLine($"PASS: {_passed} volume key checks.");
                return 0;
            }
            if (args.Contains("--startup-only"))
            {
                StartupResponsivenessChecks.Run(Check);
                Console.WriteLine($"PASS: {_passed} startup responsiveness checks.");
                return 0;
            }
            if (args.Contains("--net-speed-only"))
            {
                var host = CreateHost(Path.Combine(AppContext.BaseDirectory, "modules"), "net-speed");
                host.Load();
                var netSpeedModule = host.Modules.Single(module => module.Id == "net-speed");
                var netSpeedLayout = netSpeedModule.Settings.Single(setting => setting.Key == "layout");
                Check(netSpeedLayout.Kind == SettingKind.Choice &&
                      netSpeedLayout.Options.Contains("Yan yana") && netSpeedLayout.Options.Contains("Alt alta"),
                    "Network speed indicator offers horizontal and vertical layouts");
                host.StopAll();
                Console.WriteLine($"PASS: {_passed} network speed checks.");
                return 0;
            }
            if (args.Contains("--autocorrect-only"))
            {
                var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
                FeatureVerificationChecks.TestWordCorrectionEngine(extras, Check);
                Console.WriteLine($"PASS: {_passed} autocorrect checks.");
                return 0;
            }
            var autocorrectBenchmarkIndex = Array.IndexOf(args, "--autocorrect-benchmark");
            if (autocorrectBenchmarkIndex >= 0)
            {
                var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
                if (autocorrectBenchmarkIndex + 1 >= args.Length) throw new ArgumentException("--autocorrect-benchmark requires a held-out corpus file.");
                var languageTag = autocorrectBenchmarkIndex + 2 < args.Length ? args[autocorrectBenchmarkIndex + 2] : "tr-TR";
                AutoCorrectCorpusChecks.Run(args[autocorrectBenchmarkIndex + 1], languageTag, extras, Check);
                Console.WriteLine($"PASS: {_passed} held-out autocorrect benchmark checks.");
                return 0;
            }
            var turkishTweetsBenchmarkIndex = Array.IndexOf(args, "--autocorrect-turkish-tweets-benchmark");
            if (turkishTweetsBenchmarkIndex >= 0)
            {
                var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
                if (turkishTweetsBenchmarkIndex + 1 >= args.Length)
                    throw new ArgumentException("--autocorrect-turkish-tweets-benchmark requires the TurkishTweets dataset file.");
                AutoCorrectCorpusChecks.RunTurkishTweetCorrections(args[turkishTweetsBenchmarkIndex + 1], extras, Check);
                Console.WriteLine($"PASS: {_passed} TurkishTweets benchmark checks.");
                return 0;
            }
            var turkishTweetNextWordBenchmarkIndex = Array.IndexOf(args, "--autocorrect-turkish-tweets-nextword-benchmark");
            if (turkishTweetNextWordBenchmarkIndex >= 0)
            {
                var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
                if (turkishTweetNextWordBenchmarkIndex + 1 >= args.Length)
                    throw new ArgumentException("--autocorrect-turkish-tweets-nextword-benchmark requires the TurkishTweets dataset file.");
                AutoCorrectCorpusChecks.RunTurkishTweetNextWords(args[turkishTweetNextWordBenchmarkIndex + 1], extras, Check);
                Console.WriteLine($"PASS: {_passed} TurkishTweets next-word benchmark checks.");
                return 0;
            }
            var annotatedBenchmarkIndex = Array.IndexOf(args, "--autocorrect-annotated-benchmark");
            if (annotatedBenchmarkIndex >= 0)
            {
                var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
                if (annotatedBenchmarkIndex + 1 >= args.Length) throw new ArgumentException("--autocorrect-annotated-benchmark requires an annotation TSV file.");
                var languageTag = annotatedBenchmarkIndex + 2 < args.Length ? args[annotatedBenchmarkIndex + 2] : "en-US";
                var heldOutOnly = args.Skip(annotatedBenchmarkIndex + 2).Contains("--heldout", StringComparer.OrdinalIgnoreCase);
                AutoCorrectCorpusChecks.RunAnnotatedErrors(args[annotatedBenchmarkIndex + 1], languageTag, extras, Check, heldOutOnly);
                Console.WriteLine($"PASS: {_passed} annotated spelling benchmark checks.");
                return 0;
            }
            var nextWordBenchmarkIndex = Array.IndexOf(args, "--autocorrect-nextword-benchmark");
            if (nextWordBenchmarkIndex >= 0)
            {
                var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
                if (nextWordBenchmarkIndex + 1 >= args.Length) throw new ArgumentException("--autocorrect-nextword-benchmark requires a held-out corpus file.");
                var languageTag = nextWordBenchmarkIndex + 2 < args.Length ? args[nextWordBenchmarkIndex + 2] : "tr-TR";
                AutoCorrectCorpusChecks.RunNextWord(args[nextWordBenchmarkIndex + 1], languageTag, extras, Check);
                Console.WriteLine($"PASS: {_passed} held-out next-word benchmark checks.");
                return 0;
            }
            if (args.Contains("--tiling-only"))
            {
                var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Tiling.dll"));
                TilingInteractionChecks.Run(assembly.GetType("WinForge.Modules.Tiling.TilingModule")!, Check);
                Console.WriteLine($"PASS: {_passed} tiling interaction checks.");
                return 0;
            }
            if (args.Contains("--interface-only"))
            {
                TestSettings();
                TestLifecycle();
                TestInterface(app);
                Console.WriteLine($"PASS: {_passed} interface checks.");
                return 0;
            }
            if (args.Contains("--audio-live"))
            {
                AudioLiveChecks.Run(TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} live audio checks.");
                return 0;
            }
            if (args.Contains("--notification-live"))
            {
                NotificationLiveChecks.Run(TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} live notification checks.");
                return 0;
            }
            if (args.Contains("--browser-live"))
            {
                LiveBrowserChecks.Run(TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} actual browser checks.");
                return 0;
            }
            if (args.Contains("--popup-only"))
            {
                PopupEngineChecks.Run(Check);
                Console.WriteLine($"PASS: {_passed} own popup engine checks.");
                return 0;
            }
            if (args.Contains("--shell-live"))
            {
                var nativeDirectory = Environment.GetEnvironmentVariable("WINFORGE_NATIVE_DIRECTORY") ?? Path.Combine(AppContext.BaseDirectory, "native");
                ShellEngineChecks.VerifyTaskbar(nativeDirectory, Check);
                Console.WriteLine($"PASS: {_passed} live taskbar checks.");
                return 0;
            }
            if (args.Contains("--taskbar-only"))
            {
                TaskbarFeatureChecks.Run(Check, nativeSmoke: false);
                TaskbarCustomizationChecks.Run(TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} taskbar checks.");
                return 0;
            }
            if (args.Contains("--taskbar-panel-only"))
            {
                TaskbarPanelChecks.Run(app, TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} taskbar panel checks.");
                return 0;
            }
            if (args.Contains("--taskbar-background-live"))
            {
                TaskbarCustomizationChecks.RunBackgroundLive(Check);
                Console.WriteLine($"PASS: {_passed} live taskbar background checks.");
                return 0;
            }
            if (args.Contains("--taskbar-customization-live"))
            {
                TaskbarCustomizationChecks.RunLive(Check);
                Console.WriteLine($"PASS: {_passed} taskbar customization live checks.");
                return 0;
            }
            if (args.Contains("--taskbar-height-live"))
            {
                TaskbarCustomizationChecks.RunHeightLive(Check);
                Console.WriteLine($"PASS: {_passed} taskbar height geometry checks.");
                return 0;
            }
            if (args.Contains("--taskbar-width-live"))
            {
                TaskbarCustomizationChecks.RunWidthLive(Check);
                Console.WriteLine($"PASS: {_passed} taskbar width geometry checks.");
                return 0;
            }
            if (args.Contains("--tray-live"))
            {
                TrayIconsLiveChecks.Run(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Taskbar.dll"), TestDirectory, Check);
                Console.WriteLine($"PASS: {_passed} live notification-area checks.");
                return 0;
            }
            if (args.Contains("--display-brightness-only"))
            {
                DisplayBrightnessChecks.Run(Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll")), Check);
                Console.WriteLine($"PASS: {_passed} display brightness checks.");
                return 0;
            }
            if (args.Contains("--display-readonly"))
            {
                DisplayBrightnessChecks.ReadConnected(Check);
                Console.WriteLine($"PASS: {_passed} connected display checks.");
                return 0;
            }
            if (args.Contains("--shell-only"))
            {
                var nativeDirectory = Environment.GetEnvironmentVariable("WINFORGE_NATIVE_DIRECTORY") ?? Path.Combine(AppContext.BaseDirectory, "native");
                ShellEngineChecks.Run(nativeDirectory, Check);
                Console.WriteLine($"PASS: {_passed} native shell checks.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--shell-probe")
            {
                var nativeDirectory = Environment.GetEnvironmentVariable("WINFORGE_NATIVE_DIRECTORY") ?? Path.Combine(AppContext.BaseDirectory, "native");
                ShellEngineChecks.Probe(uint.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), nativeDirectory);
                return 0;
            }
            if (args.Contains("--start-appearance-live"))
            {
                ShellEngineChecks.VerifyStartAppearance(Check);
                Console.WriteLine($"PASS: {_passed} live Start appearance checks.");
                return 0;
            }
            if (args.Contains("--feature-only"))
            {
                FeatureVerificationChecks.Run(TestDirectory, Check, args.Contains("--native-smoke"));
                Console.WriteLine($"PASS: {_passed} feature verification checks.");
                return 0;
            }
            if (args.Contains("--window-only"))
            {
                WindowControlParityChecks.Run(Check);
                Console.WriteLine($"PASS: {_passed} window engine checks.");
                return 0;
            }
            TestSettings();
            TestLifecycle();
            StartupResponsivenessChecks.Run(Check);
            NativeInfrastructureChecks.Run(TestDirectory, Check, args.Contains("--native-smoke"));
            WindowControlParityChecks.Run(Check);
            FeatureVerificationChecks.Run(TestDirectory, Check, args.Contains("--native-smoke"));
            if (args.Contains("--native-smoke")) PopupEngineChecks.Run(Check);
            TestInterface(app);
            Console.WriteLine($"PASS: {_passed} regression checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            SetProperty(app, nameof(ForgeApplication.IsExiting), true);
            foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
        }
    }

    // Dispatcher pumping must not start the real application or its single-instance shutdown.
    private sealed class RegressionApplication : ForgeApplication
    {
        internal Func<int>? CheckRunner { get; set; }
        protected override void OnStartup(StartupEventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
        {
            Shutdown(CheckRunner?.Invoke() ?? 1);
        }));
    }

    private static void TestSettings()
    {
        var path = Path.Combine(TestDirectory, "settings.json");
        var store = new SettingsStore(path);
        store.Enabled.Add("EXAMPLE");
        store.For("example")["Value"] = "42";
        store.Save();
        var loaded = new SettingsStore(path);
        Check(loaded.Enabled.Contains("example"), "Enabled module IDs survive reload, ignoring case");
        Check(loaded.For("EXAMPLE")["value"] == "42", "Settings survive reload, ignoring case");
        File.WriteAllText(path, "{broken");
        Check(new SettingsStore(path).Values.Count == 0, "Corrupt JSON does not prevent startup");
        File.WriteAllText(path, "{\"Enabled\":null,\"Values\":{\"broken\":null,\"valid\":{\"key\":\"value\"}}}");
        Check(new SettingsStore(path).For("valid")["key"] == "value", "Null JSON sections do not discard valid settings");
        File.WriteAllText(path, "{\"Values\":{\"valid\":{\"key\":null}}}");
        Check(new SettingsStore(path).For("valid")["key"] == "", "Null setting values normalize to empty text");
    }

    private static ModuleHost CreateHost(string folder, string name)
    {
        var store = new SettingsStore(Path.Combine(TestDirectory, name + ".json"));
        return new ModuleHost(store, new FileLogger(Path.Combine(TestDirectory, "test.log")), new FakeHotkeys(), folder, _ => { });
    }

    private static void TestLifecycle()
    {
        var folder = Path.Combine(TestDirectory, "fixtures");
        Directory.CreateDirectory(folder);
        File.Copy(typeof(Program).Assembly.Location, Path.Combine(folder, "WinForge.Modules.Fixture.dll"));
        var host = CreateHost(folder, "lifecycle");
        host.Load();
        Check(host.Modules.Count == 1, "Module discovery loads a valid plugin");
        var module = (FixtureModule)host.Modules.Single();
        var settings = host.SettingsOf(module);
        settings.Set("invalid-bool", "invalid");
        Check(settings.GetBool("invalid-bool", true), "Invalid boolean uses its fallback");
        settings.Set("invalid-bool", "no");
        Check(!settings.GetBool("invalid-bool", true), "Legacy false boolean is supported");
        settings.Set("number", "invalid");
        Check(settings.GetInt("number", 17) == 17, "Invalid integer uses its fallback");
        host.Apply(module);
        Check(module.ApplyCount == 0, "Inactive modules do not apply settings");
        host.SetEnabled(module, true);
        Check(module.IsActive && new SettingsStore(Path.Combine(TestDirectory, "lifecycle.json")).Enabled.Contains(module.Id), "Activation persists enabled state");
        host.Apply(module);
        Check(module.ApplyCount == 1, "Active modules apply settings");
        module.FailApply = true;
        host.Apply(module);
        Check(host.ErrorOf(module) is not null, "Apply failures are reported");
        module.FailApply = false;
        host.Apply(module);
        Check(host.ErrorOf(module) is null, "Successful apply clears the error");
        module.FailDeactivate = true;
        ExpectFailure(() => host.SetEnabled(module, false));
        Check(module.IsActive && module.DeactivateCount == 1, "Failed deactivation is not repeated or falsely shown as disabled");
        module.FailDeactivate = false;
        host.SetEnabled(module, false);
        Check(!module.IsActive && !new SettingsStore(Path.Combine(TestDirectory, "lifecycle.json")).Enabled.Contains(module.Id), "Deactivation persists disabled state");
        module.FailActivate = true;
        ExpectFailure(() => host.SetEnabled(module, true));
        Check(!module.ResourceAllocated && host.ErrorOf(module) is not null, "Failed activation releases resources before IsActive is set");
        module.FailActivate = false;
        host.SetEnabled(module, true);
        host.StopAll();
        module.FailActivate = true;
        host.StartEnabled();
        Check(!module.ResourceAllocated && host.ErrorOf(module) is not null, "Startup activation failure also releases resources");
        host.Load();
        Check(host.ErrorOf(host.Modules.Single()) is null, "Reload clears stale errors");
    }

    private static void TestInterface(ForgeApplication app)
    {
        var host = CreateHost(Path.Combine(AppContext.BaseDirectory, "modules"), "ui");
        host.Load();
        Check(host.Modules.Count >= 25 && host.Modules.Any(module => module.Id == "window-control"), "All shipped modules including the native window engine load and initialize");
        Check(host.Modules.Select(module => module.Id).Distinct().Count() == host.Modules.Count, "Module IDs are unique");
        Check(host.Modules.All(module => module.Id is not ("workspaces" or "awake" or "advanced-paste" or "hosts-file-editor" or "environment-variables" or "clipboard-history" or "plain-paste" or "launcher" or "shortcut-guide")), "Removed modules are absent from the shipped module catalog");
        var tiling = host.Modules.Single(module => module.Id == "auto-tiling");
        TilingInteractionChecks.Run(tiling.GetType(), Check);
        var fancyZones = host.Modules.Single(module => module.Id == "fancy-zones");
        Check(tiling.Name == "Tam Ekran Pencere Yönetimi" &&
              tiling.Settings.Any(setting => setting.Key == "layout") &&
              tiling.Settings.All(setting => !setting.Key.StartsWith("zone-", StringComparison.Ordinal)),
            "Fullscreen tiling is a separate feature with only its own settings");
        Check(fancyZones.Settings.Any(setting => setting.Key == "zone-layout") &&
              fancyZones.Settings.All(setting => setting.Key.StartsWith("zone-", StringComparison.Ordinal) || setting.Key == "gap"),
            "FancyZones is a separate feature with only its own settings");
        var zoneActivationSetting = fancyZones.Settings.Single(setting => setting.Key == "zone-activation");
        Check(zoneActivationSetting.Options.Contains("Shift tuşuyla etkinleştir") &&
              zoneActivationSetting.Options.Contains("Sürüklerken otomatik göster"),
            "FancyZones mode supports both Shift activation and automatic zone display while dragging");
        Check(fancyZones.Settings.Any(setting => setting.Key == "zone-window-switching" && setting.Kind == SettingKind.Toggle && setting.Default is true),
            "FancyZones exposes independently configurable same-zone window switching");
        Check(fancyZones.Settings.Any(setting => setting.Key == "zone-keyboard-move-enabled" && setting.Kind == SettingKind.Toggle && setting.Default is true),
            "FancyZones exposes direct keyboard movement of the active window between adjacent zones");
        Check(fancyZones.Settings.Any(setting => setting.Key == "zone-show-numbers" && setting.Kind == SettingKind.Toggle) &&
              fancyZones.Settings.Any(setting => setting.Key == "zone-active-color" && setting.Kind == SettingKind.Palette && setting.Options.Length >= 8) &&
              fancyZones.Settings.Any(setting => setting.Key == "zone-inactive-color" && setting.Kind == SettingKind.Palette && setting.Options.Length >= 8) &&
              fancyZones.Settings.Any(setting => setting.Key == "zone-border-thickness" && setting.Kind == SettingKind.Number) &&
              fancyZones.Settings.Any(setting => setting.Key == "zone-merge-distance" && setting.Kind == SettingKind.Number),
            "FancyZones exposes configurable preview labels, colors, border thickness, and merge distance");
        Check(fancyZones.Settings.Any(setting => setting.Key == "zone-auto-place-new-windows" && setting.Kind == SettingKind.Toggle && setting.Default is true),
            "FancyZones automatically places newly opened windows into the least occupied zone by default");
        var zoneContext = (IModuleContext)tiling.GetType().GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fancyZones)!;
        var zoneHotkeys = (FakeHotkeys)zoneContext.Hotkeys;
        var previousHotkeyCount = zoneHotkeys.RegisteredHotkeys.Count;
        host.SetEnabled(fancyZones, true);
        Check(zoneHotkeys.RegisteredHotkeys.Count == previousHotkeyCount + 6 &&
              zoneHotkeys.RegisteredHotkeys.Where(entry => (entry.Modifiers & HotkeyModifiers.Control) != 0 &&
                                                              (entry.Modifiers & HotkeyModifiers.Alt) != 0)
                  .Select(entry => entry.VirtualKey).Order().SequenceEqual(new uint[] { 0x25, 0x26, 0x27, 0x28 }),
            "FancyZones registers four Win+Ctrl+Alt+Arrow zone move actions alongside same-zone window switching");
        host.SetEnabled(fancyZones, false);
        Check(zoneHotkeys.RegisteredHotkeys.Count == previousHotkeyCount,
            "FancyZones releases keyboard zone movement shortcuts when disabled");
        var zoneLayoutSetting = fancyZones.Settings.Single(setting => setting.Key == "zone-layout");
        Check(zoneLayoutSetting.Options.Contains("Özel ızgara") &&
              fancyZones.Settings.Single(setting => setting.Key == "zone-columns") is { Min: 1, Max: 6, Default: 3 } &&
              fancyZones.Settings.Single(setting => setting.Key == "zone-rows") is { Min: 1, Max: 4, Default: 2 },
            "FancyZones mode exposes a configurable custom grid with bounded rows and columns");
        var monitorGridSetting = fancyZones.Settings.Single(setting => setting.Key == "zone-monitor-grid-profiles");
        var parseMonitorProfiles = tiling.GetType().GetMethod("ParseMonitorGridProfiles", BindingFlags.Static | BindingFlags.NonPublic)!;
        var monitorProfiles = (Dictionary<string, (int Columns, int Rows)>)parseMonitorProfiles.Invoke(null,
            ["DISPLAY1=3x2;DISPLAY2=2x4;DISPLAY1=99x0;bad/name=5x2;DISPLAY3=abc"])!;
        var tilingType = tiling.GetType();
        tilingType.GetField("_defaultZoneLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tiling, 0);
        tilingType.GetField("_defaultZoneColumns", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tiling, 3);
        tilingType.GetField("_defaultZoneRows", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tiling, 2);
        tilingType.GetField("_monitorGridProfiles", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tiling, monitorProfiles);
        var applyMonitorProfile = tilingType.GetMethod("ApplyMonitorGridProfile", BindingFlags.Instance | BindingFlags.NonPublic)!;
        applyMonitorProfile.Invoke(tiling, ["DISPLAY2"]);
        var selectedMonitorGrid = ((int)tilingType.GetField("_zoneLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tiling)!,
            (int)tilingType.GetField("_zoneColumns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tiling)!,
            (int)tilingType.GetField("_zoneRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tiling)!);
        applyMonitorProfile.Invoke(tiling, ["DISPLAY3"]);
        var fallbackMonitorGrid = ((int)tilingType.GetField("_zoneLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tiling)!,
            (int)tilingType.GetField("_zoneColumns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tiling)!,
            (int)tilingType.GetField("_zoneRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tiling)!);
        Check(monitorGridSetting.Kind == SettingKind.Text && monitorProfiles.Count == 2 &&
              monitorProfiles["display1"] == (6, 1) && monitorProfiles["DISPLAY2"] == (2, 4) &&
              selectedMonitorGrid == (3, 2, 4) && fallbackMonitorGrid == (0, 3, 2),
            "FancyZones accepts per-monitor grids, clamps dimensions, ignores malformed entries and resolves device IDs case-insensitively");
        var createGridZones = tiling.GetType().GetMethod("CreateGridZones", BindingFlags.Static | BindingFlags.NonPublic)!;
        var customGrid = (IReadOnlyList<RECT>)createGridZones.Invoke(null,
            new object[] { new RECT { Left = 10, Top = 20, Right = 1010, Bottom = 620 }, 3, 2, 8 })!;
        Check(customGrid.Count == 6 && customGrid.All(zone => zone.Left >= 10 && zone.Top >= 20 && zone.Right <= 1010 && zone.Bottom <= 620) &&
              customGrid[0].Right + 8 == customGrid[1].Left && customGrid[0].Bottom + 8 == customGrid[3].Top,
            "FancyZones custom grid creates correctly spaced zones contained in the active monitor work area");
        var fourGrid = (IReadOnlyList<RECT>)createGridZones.Invoke(null,
            new object[] { new RECT { Left = 0, Top = 0, Right = 1000, Bottom = 600 }, 2, 2, 8 })!;
        var findZonesNearDividers = tilingType.GetMethod("FindZonesNearDividers", BindingFlags.Static | BindingFlags.NonPublic)!;
        var verticalMerge = (HashSet<int>)findZonesNearDividers.Invoke(null, [fourGrid, 2, 2, new POINT { X = 500, Y = 150 }, 24])!;
        var centerMerge = (HashSet<int>)findZonesNearDividers.Invoke(null, [fourGrid, 2, 2, new POINT { X = 500, Y = 300 }, 24])!;
        Check(verticalMerge.SetEquals([0, 1]) && centerMerge.SetEquals([0, 1, 2, 3]),
            "FancyZones selects adjacent areas near a divider and all four areas near a four-way grid intersection");
        var findLeastOccupiedZone = tilingType.GetMethod("FindLeastOccupiedZone", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((int)findLeastOccupiedZone.Invoke(null, [new[] { 2, 0, 1 }])! == 1 &&
              (int)findLeastOccupiedZone.Invoke(null, [Array.Empty<int>()])! == -1,
            "FancyZones chooses the first least occupied area and handles an empty grid");
        var areAllZonesOccupied = tilingType.GetMethod("AreAllZonesOccupied", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((bool)areAllZonesOccupied.Invoke(null, [new[] { 1, 2, 1 }])! &&
              !(bool)areAllZonesOccupied.Invoke(null, [new[] { 1, 0, 1 }])! &&
              !(bool)areAllZonesOccupied.Invoke(null, [Array.Empty<int>()])!,
            "FancyZones detects when every zone is occupied to maximize overflow windows");
        var maximumGrid = (IReadOnlyList<RECT>)createGridZones.Invoke(null,
            new object[] { new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }, 99, 99, 99 })!;
        Check(maximumGrid.Count == 24 && maximumGrid.All(zone => zone.Left >= 0 && zone.Top >= 0 && zone.Right <= 1920 && zone.Bottom <= 1080),
            "FancyZones custom grid clamps excessive rows, columns and gaps to safe monitor bounds");
        var findAdjacentZone = tiling.GetType().GetMethod("FindAdjacentZone", BindingFlags.Static | BindingFlags.NonPublic)!;
        var testZones = new[]
        {
            new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 },
            new RECT { Left = 110, Top = 0, Right = 210, Bottom = 100 },
            new RECT { Left = 0, Top = 110, Right = 100, Bottom = 210 },
            new RECT { Left = 110, Top = 110, Right = 210, Bottom = 210 }
        };
        Check((int)findAdjacentZone.Invoke(null, new object[] { testZones, 0, 4 })! == 1 &&
              (int)findAdjacentZone.Invoke(null, new object[] { testZones, 0, 8 })! == 2 &&
              (int)findAdjacentZone.Invoke(null, new object[] { testZones, 2, 4 })! == 3 &&
              (int)findAdjacentZone.Invoke(null, new object[] { testZones, 1, 4 })! == 1,
            "FancyZones arrow navigation selects the nearest zone in the requested direction and stays at edges");
        var findAdjacentZoneForWindow = tilingType.GetMethod("FindAdjacentZoneForWindow", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((int)findAdjacentZoneForWindow.Invoke(null, new object[] { testZones, testZones[0], 4 })! == 1 &&
              (int)findAdjacentZoneForWindow.Invoke(null, new object[]
              {
                  testZones, new RECT { Left = 205, Top = 20, Right = 250, Bottom = 70 }, 8
              })! == 3,
            "FancyZones keyboard move resolves a window's current or nearest zone before moving in the requested direction");
        var tryGetSpanBounds = tiling.GetType().GetMethod("TryGetSpanBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
        var horizontalSpanArguments = new object[] { testZones, new HashSet<int> { 0, 1 }, 10, default(RECT) };
        Check((bool)tryGetSpanBounds.Invoke(null, horizontalSpanArguments)! &&
              (RECT)horizontalSpanArguments[3] is { Left: 0, Top: 0, Right: 210, Bottom: 100 },
            "FancyZones combines adjacent zones into a single rectangular window area including their gutter");
        var tryExtendZoneSelection = tiling.GetType().GetMethod("TryExtendZoneSelection", BindingFlags.Static | BindingFlags.NonPublic)!;
        var selectionToExtend = new HashSet<int> { 0, 1 };
        Check((bool)tryExtendZoneSelection.Invoke(null, new object[] { testZones, selectionToExtend, 2, 10 })! &&
              selectionToExtend.SetEquals(new[] { 0, 1, 2, 3 }),
            "FancyZones extends a multi-zone span by the complete adjacent row to preserve rectangular geometry");
        var diagonalSpanArguments = new object[] { testZones, new HashSet<int> { 0, 3 }, 10, default(RECT) };
        var diagonalSelection = new HashSet<int> { 0 };
        Check(!(bool)tryGetSpanBounds.Invoke(null, diagonalSpanArguments)! &&
              !(bool)tryExtendZoneSelection.Invoke(null, new object[] { testZones, diagonalSelection, 3, 10 })! &&
              diagonalSelection.SetEquals(new[] { 0 }),
            "FancyZones rejects diagonal gaps and leaves a selection unchanged when it cannot form a rectangle");
        var findBestZone = tilingType.GetMethod("FindBestZone", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((int)findBestZone.Invoke(null, new object[] { testZones, testZones[0] })! == 0 &&
              (int)findBestZone.Invoke(null, new object[] { testZones, new RECT { Left = 50, Top = 0, Right = 160, Bottom = 100 } })! == -1 &&
              (int)findBestZone.Invoke(null, new object[] { testZones, default(RECT) })! == -1,
            "FancyZones maps only windows substantially contained by one zone and rejects multi-zone or invalid bounds");
        var findCycleWindowIndex = tilingType.GetMethod("FindCycleWindowIndex", BindingFlags.Static | BindingFlags.NonPublic)!;
        var updateWindowCycleOrder = tilingType.GetMethod("UpdateWindowCycleOrder", BindingFlags.Static | BindingFlags.NonPublic)!;
        var zoneWindows = new List<IntPtr> { new(1), new(2), new(3) };
        updateWindowCycleOrder.Invoke(null, new object[] { zoneWindows, new List<IntPtr> { new(2), new(1), new(3), new(4) } });
        updateWindowCycleOrder.Invoke(null, new object[] { zoneWindows, new List<IntPtr> { new(2), new(3), new(4) } });
        Check(zoneWindows.SequenceEqual(new[] { new IntPtr(2), new IntPtr(3), new IntPtr(4) }) &&
              (int)findCycleWindowIndex.Invoke(null, new object[] { zoneWindows, new IntPtr(2), 1 })! == 1 &&
              (int)findCycleWindowIndex.Invoke(null, new object[] { zoneWindows, new IntPtr(2), -1 })! == 2 &&
              (int)findCycleWindowIndex.Invoke(null, new object[] { zoneWindows, new IntPtr(9), 1 })! == -1,
            "FancyZones cycles through persistent window order, wraps in both directions, and skips stale handles");
        var themeSwitch = host.Modules.Single(module => module.Id == "theme-switch");
        var lightSwitch = themeSwitch.Settings.Single(setting => setting.Key == "schedule-enabled");
        Check(lightSwitch.Kind == SettingKind.Toggle && Equals(lightSwitch.Default, false) &&
              Equals(themeSwitch.Settings.Single(setting => setting.Key == "light-time").Default, "07:00") &&
              Equals(themeSwitch.Settings.Single(setting => setting.Key == "dark-time").Default, "19:00"),
            "Light Switch exposes an opt-in schedule with clear local-time defaults");
        var themeType = themeSwitch.GetType();
        var isLightPeriod = themeType.GetMethod("IsLightPeriod", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((bool)isLightPeriod.Invoke(null, new object[] { new TimeOnly(7, 0), new TimeOnly(7, 0), new TimeOnly(19, 0) })! &&
              !(bool)isLightPeriod.Invoke(null, new object[] { new TimeOnly(19, 0), new TimeOnly(7, 0), new TimeOnly(19, 0) })! &&
              (bool)isLightPeriod.Invoke(null, new object[] { new TimeOnly(23, 0), new TimeOnly(20, 0), new TimeOnly(6, 0) })! &&
              !(bool)isLightPeriod.Invoke(null, new object[] { new TimeOnly(12, 0), new TimeOnly(20, 0), new TimeOnly(6, 0) })!,
            "Light Switch calculates normal and overnight theme periods with inclusive start times");
        Check((bool)isLightPeriod.Invoke(null, new object[] { new TimeOnly(23, 0), new TimeOnly(8, 0), new TimeOnly(8, 0) })!,
            "Light Switch handles equal schedule times consistently");
        var parseScheduleTime = themeType.GetMethod("ParseScheduleTime", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((TimeOnly?)parseScheduleTime.Invoke(null, new object[] { "06:45" }) == new TimeOnly(6, 45) &&
              parseScheduleTime.Invoke(null, new object[] { "25:61" }) is null,
            "Light Switch accepts strict HH:mm values and rejects invalid schedule text");
        Check(host.Modules.All(module => module.Id != "windhawk-taskbar"), "Removed external shell integration is not discovered");
        var displayBrightness = host.Modules.Single(module => module.Id == "display-brightness");
        BrightnessWheelChecks.Run(Check);
        Check(displayBrightness.Settings.Single(setting => setting.Key == "open-panel").Kind == SettingKind.Action &&
              displayBrightness.Settings.Single(setting => setting.Key == "brightness-schedule-enabled").Default is false &&
              displayBrightness.Settings.Single(setting => setting.Key == "day-start-time").Default as string == "07:00" &&
              displayBrightness.Settings.Single(setting => setting.Key == "day-brightness").Default is 75 &&
              displayBrightness.Settings.Single(setting => setting.Key == "night-start-time").Default as string == "20:00" &&
              displayBrightness.Settings.Single(setting => setting.Key == "night-brightness").Default is 35 &&
              displayBrightness.Settings.Single(setting => setting.Key == "brightness-hotkeys-enabled").Default is false &&
              displayBrightness.Settings.Single(setting => setting.Key == "brightness-hotkey-step").Default is 5 &&
              displayBrightness.Settings.Single(setting => setting.Key == "brightness-dim-hotkey").Default as string == "Ctrl+Alt+F11" &&
              displayBrightness.Settings.Single(setting => setting.Key == "brightness-brighten-hotkey").Default as string == "Ctrl+Alt+F12",
            "Power Display exposes its panel, opt-in day/night schedule and cursor-monitor brightness hotkeys");
        var parseBrightnessHotkey = displayBrightness.GetType().GetMethod("ParseBrightnessHotkey", BindingFlags.Static | BindingFlags.NonPublic)!;
        var customBrightnessHotkey = ((uint Modifiers, uint VirtualKey, int WheelDirection))parseBrightnessHotkey.Invoke(null, ["Shift+Alt+F8"])!;
        var wheelBrightnessHotkey = ((uint Modifiers, uint VirtualKey, int WheelDirection))parseBrightnessHotkey.Invoke(null, ["Ctrl+Alt+MouseWheelUp"])!;
        Check(customBrightnessHotkey == (HotkeyModifiers.Shift | HotkeyModifiers.Alt, 0x77u, 0) &&
              wheelBrightnessHotkey == (HotkeyModifiers.Control | HotkeyModifiers.Alt, 0u, 1),
            "Power Display parses configurable keyboard and mouse-wheel brightness shortcuts");
        var displayModuleContext = (IModuleContext)displayBrightness.GetType()
            .GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(displayBrightness)!;
        var fakeHotkeys = (FakeHotkeys)displayModuleContext.Hotkeys;
        var hotkeysBeforeBrightness = fakeHotkeys.RegisteredHotkeys.Count;
        host.SettingsOf(displayBrightness).Set("brightness-dim-hotkey", "Ctrl+Alt+PageDown");
        host.SettingsOf(displayBrightness).Set("brightness-brighten-hotkey", "Ctrl+Alt+PageUp");
        host.SettingsOf(displayBrightness).Set("brightness-hotkeys-enabled", true);
        host.SetEnabled(displayBrightness, true);
        var brightnessHotkeys = fakeHotkeys.RegisteredHotkeys.Skip(hotkeysBeforeBrightness).ToArray();
        Check(brightnessHotkeys.Select(hotkey => (hotkey.Modifiers, hotkey.VirtualKey)).ToHashSet().SetEquals(new[]
              {
                  (HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x21u),
                  (HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x22u)
              }),
            "Power Display registers configured brightness hotkeys only when cursor-monitor hotkeys are enabled");
        host.SetEnabled(displayBrightness, false);
        Check(fakeHotkeys.RegisteredHotkeys.Count == hotkeysBeforeBrightness,
            "Power Display releases both brightness hotkeys when the module is disabled");
        host.SettingsOf(displayBrightness).Set("brightness-hotkeys-enabled", false);
        var capabilitiesParserType = displayBrightness.GetType().Assembly.GetType("WinForge.Modules.Extras.MonitorCapabilitiesParser")!;
        var parseCapabilities = capabilitiesParserType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parsedVcp = (System.Collections.IDictionary)parseCapabilities.Invoke(null,
            new object[] { "(prot(monitor)model(Test) vcp(10 12 14(04 05 0B) 60(0F 11 12) 62))" })!;
        var classifyVcp = capabilitiesParserType.GetMethod("IsContinuousFeature", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check(parsedVcp.Contains((byte)0x10) && parsedVcp.Contains((byte)0x12) && parsedVcp.Contains((byte)0x62) &&
              ((IReadOnlyList<uint>)parsedVcp[(byte)0x14]!).SequenceEqual(new uint[] { 0x04, 0x05, 0x0B }) &&
              ((IReadOnlyList<uint>)parsedVcp[(byte)0x60]!).SequenceEqual(new uint[] { 0x0F, 0x11, 0x12 }) &&
              parsedVcp.Count == 5 &&
              !(bool)classifyVcp.Invoke(null, new object[] { parsedVcp[(byte)0x60]! })! &&
              (bool)classifyVcp.Invoke(null, new object[] { Array.Empty<uint>() })!,
            "Power Display parses supported VCP values and correctly distinguishes discrete from continuous controls");
        var internalPanelService = displayBrightness.GetType().Assembly
            .GetType("WinForge.Modules.Extras.InternalPanelBrightnessService")!;
        var discoveredPanels = (IEnumerable<object>)internalPanelService
            .GetMethod("Discover", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var isInternalPanelId = internalPanelService
            .GetMethod("IsInternalId", BindingFlags.Static | BindingFlags.NonPublic)!;
        var mapPanelPercent = internalPanelService
            .GetMethod("MapPercentToLevel", BindingFlags.Static | BindingFlags.NonPublic)!;
        var mapPanelLevel = internalPanelService
            .GetMethod("MapLevelToPercent", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check(discoveredPanels.All(panel =>
                  (string)panel.GetType().GetProperty("Id")!.GetValue(panel)! is var id &&
                  (bool)isInternalPanelId.Invoke(null, [id])! &&
                  (byte)panel.GetType().GetProperty("Current")!.GetValue(panel)! <= 100 &&
                  ((IReadOnlyList<byte>)panel.GetType().GetProperty("Levels")!.GetValue(panel)!).Count > 0),
            "Power Display discovers supported internal WMI panels with valid brightness ranges");
        var discretePanelLevels = new byte[] { 5, 30, 85 };
        Check((byte)mapPanelPercent.Invoke(null, [50, discretePanelLevels])! == 30 &&
              (int)mapPanelLevel.Invoke(null, [(byte)85, discretePanelLevels])! == 100 &&
              (byte)mapPanelPercent.Invoke(null, [-10, discretePanelLevels])! == 5 &&
              (byte)mapPanelPercent.Invoke(null, [110, discretePanelLevels])! == 85,
            "Power Display maps percentages to discrete internal-panel brightness levels and clamps both ends");
        var mediaFlyout = host.Modules.Single(module => module.Id == "media-flyout");
        Check(mediaFlyout.Settings.Any(setting => setting.Key == "open-flyout" && setting.Kind == SettingKind.Action) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "hotkey-enabled" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "volume-hotkey-enabled" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "up-next-enabled" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "flyout-pinned" && setting.Kind == SettingKind.Toggle && setting.Default is false) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "flyout-auto-dismiss-seconds" && setting.Kind == SettingKind.Number && setting.Default is 3) &&
               mediaFlyout.Settings.Any(setting => setting.Key == "taskbar-media-widget-enabled" && setting.Kind == SettingKind.Toggle && setting.Default is false) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "taskbar-widget-scroll-mode" && setting.Kind == SettingKind.Choice && setting.Options.Contains("master-volume") && setting.Options.Contains("player-volume") && setting.Options.Contains("off")) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "taskbar-visualizer-enabled" && setting.Kind == SettingKind.Toggle && setting.Default is false) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "taskbar-visualizer-position" && setting.Options.Contains("center") && setting.Options.Contains("near-widget")) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "taskbar-visualizer-hide-when-silent" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "media-app-filter-mode" && setting.Kind == SettingKind.Choice && setting.Options.Contains("allowlist") && setting.Options.Contains("blocklist")) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "media-app-filter-list" && setting.Kind == SettingKind.Text) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "lock-keys-flyout-enabled" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "caps-lock-flyout-enabled" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "num-lock-flyout-enabled" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "scroll-lock-flyout-enabled" && setting.Default is true) &&
               mediaFlyout.Settings.Any(setting => setting.Key == "hardware-keys-flyout-enabled" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "external-volume-flyout-enabled" && setting.Default is true) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "flyout-position" && setting.Kind == SettingKind.Choice && setting.Options.Length == 4) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "flyout-monitor" && setting.Options.Contains("cursor")) &&
              !mediaFlyout.Settings.Any(setting => setting.Key == "flyout-theme") &&
              mediaFlyout.Settings.Any(setting => setting.Key == "flyout-animation" && setting.Options.Contains("fade")) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "flyout-dismiss-seconds" && setting.Kind == SettingKind.Number && setting.Min == 0 && setting.Max == 60) &&
              mediaFlyout.Settings.Any(setting => setting.Key == "flyout-fullscreen-suppressed" && setting.Kind == SettingKind.Toggle) &&
              (mediaFlyout.Warning ?? "").Contains("albüm kapağı", StringComparison.OrdinalIgnoreCase),
            "Fluent Media Flyout exposes media controls, configurable placement, dismissal and fullscreen behavior");
        Check(!VolumeService.IsWinForgeChange(new VolumeService.EndpointVolumeChange(50, false, Guid.Empty)) &&
              !VolumeService.IsWinForgeChange(new VolumeService.EndpointVolumeChange(50, false, Guid.NewGuid())),
            "External endpoint volume events are distinguished from WinForge-originated changes");
        try
        {
            using var endpointWatcher = VolumeService.WatchDefaultEndpoint(_ => { });
            Check(true, "Core Audio default endpoint volume notifications register and unregister cleanly");
        }
        catch (COMException exception) when (exception.HResult is unchecked((int)0x88890004) or unchecked((int)0x80070490))
        {
            Check(true, "Core Audio endpoint notification smoke test skipped because no default output device is available");
        }
        var mediaWindowType = mediaFlyout.GetType().Assembly.GetType("WinForge.Modules.Extras.MediaFlyoutWindow")!;
        VolumeKeyChecks.Run(Check);
        var keyHookType = mediaFlyout.GetType().Assembly.GetType("WinForge.Modules.Extras.LockKeysFlyoutHook")!;
        var isMediaFlyoutKey = keyHookType.GetMethod("IsMediaFlyoutKey", BindingFlags.Static | BindingFlags.NonPublic)!;
        var getEnabledLockKeyLabel = keyHookType.GetMethod("GetEnabledLockKeyLabel", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((bool)isMediaFlyoutKey.Invoke(null, [0xAD])! && (bool)isMediaFlyoutKey.Invoke(null, [0xB3])! &&
              !(bool)isMediaFlyoutKey.Invoke(null, [0xB4])!,
            "Fluent Flyout recognizes volume and media hardware keys without claiming unrelated keys");
        Check((string?)getEnabledLockKeyLabel.Invoke(null, [0x14, true, false, false]) == "Caps Lock" &&
              getEnabledLockKeyLabel.Invoke(null, [0x90, true, false, true]) is null &&
              (string?)getEnabledLockKeyLabel.Invoke(null, [0x91, false, false, true]) == "Scroll Lock",
            "Fluent Flyout independently filters Caps, Num and Scroll Lock indicators");
        var parseMediaAppFilterList = mediaWindowType.GetMethod("ParseMediaAppFilterList", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parsedMediaApps = (HashSet<string>)parseMediaAppFilterList.Invoke(null, ["C:\\Apps\\Spotify.exe; msedge, 'MusicPlayer'\r\nspotify"])!;
        var isMediaAppAllowed = mediaWindowType.GetMethod("IsMediaAppAllowed", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check(parsedMediaApps.SetEquals(["spotify", "msedge", "MusicPlayer"]) &&
              (bool)isMediaAppAllowed.Invoke(null, ["Spotify!App", "allowlist", parsedMediaApps])! &&
              !(bool)isMediaAppAllowed.Invoke(null, ["Spotify!App", "blocklist", parsedMediaApps])! &&
              (bool)isMediaAppAllowed.Invoke(null, ["VLC", "blocklist", parsedMediaApps])! &&
              !(bool)isMediaAppAllowed.Invoke(null, ["VLC", "allowlist", new HashSet<string>(StringComparer.OrdinalIgnoreCase)])!,
            "Fluent Flyout parses app filter lists and applies allowlist/blocklist rules consistently");
        var mediaWindow = (Window)Activator.CreateInstance(mediaWindowType, nonPublic: true)!;
        mediaWindow.Show();
        Pump(mediaWindow);
        Check(Descendants<Slider>(mediaWindow).Any(), "Media Flyout renders its volume slider");
        Check(Descendants<Slider>(mediaWindow).Any(slider => slider.Name == "MediaTimelineSlider") &&
              Descendants<ComboBox>(mediaWindow).Any(comboBox => comboBox.Name == "MediaSessionSelector"),
            "Media Flyout renders playback seek and multi-session selection controls");
        Check(Descendants<Button>(mediaWindow).Any(button => (button.ToolTip?.ToString() ?? "").Contains("oturum", StringComparison.OrdinalIgnoreCase)),
            "Media Flyout can persistently pin or unpin a selected media session");
        Check(Descendants<Image>(mediaWindow).Any(image => image.Name == "MediaArtwork") &&
              Descendants<Button>(mediaWindow).Any(button => button.ToolTip?.ToString() == "Karıştırmayı değiştir") &&
              Descendants<Button>(mediaWindow).Any(button => button.ToolTip?.ToString() == "Tekrar modunu değiştir"),
            "Media Flyout renders bounded album artwork and shuffle/repeat controls");
        Check(Descendants<Button>(mediaWindow).Any(button => button.ToolTip?.ToString() == "Uygulama ses karıştırıcısını aç/kapat"),
            "Media Flyout offers an expandable per-application audio mixer");
        Check(Descendants<Button>(mediaWindow).Any(button => button.ToolTip?.ToString() == "Windows Ses Ayarları"),
            "Media Flyout offers a documented Windows sound-device selection route");
        var audioSessions = AudioSessionService.GetActiveSessions();
        Check(audioSessions.All(session => !string.IsNullOrWhiteSpace(session.InstanceId) &&
                                           session.ProcessId > 0 && session.Volume is >= 0 and <= 100 &&
                                           !string.IsNullOrWhiteSpace(session.Application)),
            "Per-application mixer enumerates valid active Windows audio sessions without changing levels");
        Check(Descendants<Button>(mediaWindow).Count() >= 3, "Media Flyout renders media transport controls");
        Check(!Descendants<TextBlock>(mediaWindow).Any(block => block.Text.Contains("Caps Lock", StringComparison.Ordinal)),
            "Media Flyout keeps lock-key status outside the media panel");
        Check(!Descendants<TextBlock>(mediaWindow).Any(block => block.Name == "UpNextNotification"),
            "Media Flyout omits the redundant next-track notification");
        Check(Descendants<TextBlock>(mediaWindow).Any(block => block.Name == "MediaSessionInfo" && !string.IsNullOrWhiteSpace(block.Text)),
            "Media Flyout renders current-session metadata or a graceful unavailable state");
        Check(!Descendants<Button>(mediaWindow).Any(button => button.Name == "PinFlyoutButton" ||
              string.Equals(button.Content?.ToString(), "Oturumu sabitle", StringComparison.Ordinal)),
            "Media Flyout omits session and window pin buttons");
        var setWindowPinned = mediaWindowType.GetMethod("SetWindowPinned", BindingFlags.Instance | BindingFlags.Public)!;
        var isWindowPinnedProperty = mediaWindowType.GetProperty("IsWindowPinned", BindingFlags.Instance | BindingFlags.Public)!;
        var dismissTimerField = mediaWindowType.GetField("_dismissTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dismissTimer = (System.Windows.Threading.DispatcherTimer)dismissTimerField.GetValue(mediaWindow)!;
        setWindowPinned.Invoke(mediaWindow, [true]);
        Check((bool)isWindowPinnedProperty.GetValue(mediaWindow)! && !dismissTimer.IsEnabled,
            "Pinning the Media Flyout window keeps it open and stops the auto-dismiss timer");
        setWindowPinned.Invoke(mediaWindow, [false]);
        Check(!(bool)isWindowPinnedProperty.GetValue(mediaWindow)! && dismissTimer.IsEnabled,
            "Unpinning the Media Flyout window re-arms the auto-dismiss timer");
        mediaWindow.Close();
        var taskbarWidgetType = mediaFlyout.GetType().Assembly.GetType("WinForge.Modules.Extras.TaskbarMediaWidgetWindow")!;
        var taskbarWidget = (Window)Activator.CreateInstance(taskbarWidgetType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [new Action(() => { })], culture: null)!;
        taskbarWidget.Show();
        Pump(taskbarWidget);
        Check(taskbarWidget.Topmost && taskbarWidget.ShowInTaskbar == false &&
              Descendants<Button>(taskbarWidget).Count(button => button.ToolTip is not null) == 3 &&
              Descendants<TextBlock>(taskbarWidget).Any(block => block.Text.Contains("Medya oturumu", StringComparison.Ordinal)),
            "Fluent Flyout renders its optional compact taskbar media widget with transport controls");
        var placementCalculator = taskbarWidgetType.GetMethod("CalculatePlacement", BindingFlags.Static | BindingFlags.NonPublic)!;
        var topPlacement = (Rect)placementCalculator.Invoke(null,
            [new Rect(0, 40, 1920, 1040), new Rect(0, 0, 1920, 40), 1u, 340d, 54d])!;
        var bottomPlacement = (Rect)placementCalculator.Invoke(null,
            [new Rect(0, 0, 1920, 1040), new Rect(0, 1040, 1920, 40), 3u, 340d, 54d])!;
        var leftPlacement = (Rect)placementCalculator.Invoke(null,
            [new Rect(40, 0, 1880, 1080), new Rect(0, 0, 40, 1080), 0u, 340d, 54d])!;
        var rightPlacement = (Rect)placementCalculator.Invoke(null,
            [new Rect(0, 0, 1880, 1080), new Rect(1880, 0, 40, 1080), 2u, 340d, 54d])!;
        Check(topPlacement.Top >= 50 && bottomPlacement.Bottom <= 1030 &&
              leftPlacement.Left >= 50 && rightPlacement.Right <= 1870 &&
              topPlacement.Right <= 1920 && bottomPlacement.Right <= 1920,
            "Fluent Flyout mini player adapts its placement to top, bottom, left and right taskbars");
        Check(taskbarWidget.ContextMenu?.Items.Count == 3,
            "Taskbar media widget provides quick media, sound settings and player actions");
        var configureWidget = taskbarWidgetType.GetMethod("ConfigureMediaFilter")!;
        string? savedWidgetSession = null;
        configureWidget.Invoke(taskbarWidget, ["all", "", "master-volume", "", (Action<string?>)(appId => savedWidgetSession = appId)]);
        Check((string?)taskbarWidgetType.GetField("_scrollMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(taskbarWidget) == "master-volume",
            "Taskbar media widget accepts the configured master-volume scroll mode");
        configureWidget.Invoke(taskbarWidget, ["all", "", "player-volume", "", (Action<string?>)(appId => savedWidgetSession = appId)]);
        Check((string?)taskbarWidgetType.GetField("_scrollMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(taskbarWidget) == "player-volume",
            "Taskbar media widget accepts the configured player-volume scroll mode");
        var playerSessionMatcher = taskbarWidgetType.GetMethod("IsAudioSessionForMediaApp", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((bool)playerSessionMatcher.Invoke(null, ["Spotify.exe", "spotify"])! &&
              !(bool)playerSessionMatcher.Invoke(null, ["Spotify.exe", "chrome"])! &&
              !(bool)playerSessionMatcher.Invoke(null, ["", "spotify"])!,
            "Taskbar media widget matches player audio sessions by normalized app identity only");
        configureWidget.Invoke(taskbarWidget, ["all", "", "master-volume", "", (Action<string?>)(appId => savedWidgetSession = appId)]);
        taskbarWidgetType.GetMethod("SetPinnedSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(taskbarWidget, ["Spotify"]);
        Check(savedWidgetSession == "Spotify" &&
              (string?)taskbarWidgetType.GetField("_pinnedSessionId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(taskbarWidget) == "Spotify",
            "Taskbar media widget can persistently pin a selected media session");
        taskbarWidgetType.GetMethod("SetPinnedSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(taskbarWidget, [null]);
        Check(savedWidgetSession is null,
            "Taskbar media widget can return session selection to automatic tracking");
        taskbarWidget.Close();
        var sineSamples = new Complex[1024];
        for (var index = 0; index < sineSamples.Length; index++)
            sineSamples[index] = new Complex(Math.Sin(2 * Math.PI * 10 * index / sineSamples.Length), 0);
        AudioSpectrumAnalyzer.Transform(sineSamples);
        var strongestBin = Enumerable.Range(1, sineSamples.Length / 2 - 1)
            .MaxBy(index => sineSamples[index].Magnitude);
        Check(strongestBin == 10,
            "Taskbar audio visualizer spectrum analysis identifies the input frequency bin");
        var visualizerType = mediaFlyout.GetType().Assembly.GetType("WinForge.Modules.Extras.TaskbarAudioVisualizerWindow")!;
        var visualizer = (Window)Activator.CreateInstance(visualizerType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: ["center", false, false, true], culture: null)!;
        visualizer.Show();
        Pump(visualizer);
        Check(visualizer.ShowInTaskbar == false && Descendants<System.Windows.Shapes.Rectangle>(visualizer).Count() == 24,
            "Taskbar audio visualizer renders 24 non-activating spectrum bars without a taskbar button");
        var captureStarted = (bool)visualizerType.GetField("_captureStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visualizer)!;
        Check(captureStarted || (visualizer.ToolTip?.ToString() ?? "").Contains("başlatılamadı", StringComparison.OrdinalIgnoreCase),
            captureStarted
                ? "Taskbar audio visualizer starts WASAPI loopback capture on this device"
                : "Taskbar audio visualizer reports unavailable output gracefully");
        visualizer.Close();
        var autoCorrectModule = host.Modules.Single(module => module.Id == "auto-correct");
        var autoCorrectType = autoCorrectModule.GetType();
        var parseExcludedProcesses = autoCorrectType.GetMethod("ParseExcludedProcesses", BindingFlags.Static | BindingFlags.NonPublic)!;
        var excludedProcesses = (HashSet<string>)parseExcludedProcesses.Invoke(null, ["C:\\Games\\game.exe; Discord; discord.exe;; 'terminal'"])!;
        Check(excludedProcesses.SetEquals(["game", "Discord", "terminal"]),
            "AutoCorrect normalizes and deduplicates excluded executable names");
        var cropModule = host.Modules.Single(module => module.Id == "crop-and-lock");
        Check(cropModule.Settings.Any(setting => setting.Key == "start-crop" && setting.Kind == SettingKind.Action) &&
              cropModule.Settings.Any(setting => setting.Key == "hotkey-enabled" && setting.Default is true),
            "Crop And Lock exposes selection action and default Win+Ctrl+Shift+T shortcut");
        var cropAssembly = cropModule.GetType().Assembly;
        var cropMathType = cropAssembly.GetType("WinForge.Modules.Extras.CropSelectionMath")!;
        var toWindowRect = cropMathType.GetMethod("ToWindowClientRect", BindingFlags.Static | BindingFlags.Public)!;
        var cropScreenRectType = cropAssembly.GetType("WinForge.Modules.Extras.CropScreenRect")!;
        var screenSelection = Activator.CreateInstance(cropScreenRectType, 120, 150, 260, 290)!;
        var windowBounds = new RECT { Left = 100, Top = 100, Right = 400, Bottom = 400 };
        var sourceSelection = toWindowRect.Invoke(null, new object[] { screenSelection, windowBounds });
        Check(sourceSelection is not null &&
              (int)sourceSelection.GetType().GetField("Left")!.GetValue(sourceSelection)! == 20 &&
              (int)sourceSelection.GetType().GetField("Top")!.GetValue(sourceSelection)! == 50 &&
              toWindowRect.Invoke(null, new object[] { Activator.CreateInstance(cropScreenRectType, 90, 120, 200, 220)!, windowBounds }) is null,
            "Crop And Lock maps selected screen coordinates to valid source-window coordinates and rejects outside selections");
        var brightnessType = displayBrightness.GetType().Assembly.GetType("WinForge.Modules.Extras.BrightnessPercent")!;
        var toMonitorValue = brightnessType.GetMethod("ToMonitorValue", BindingFlags.Static | BindingFlags.NonPublic)!;
        var fromMonitorValue = brightnessType.GetMethod("FromMonitorValue", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((uint)toMonitorValue.Invoke(null, new object[] { 50, 20u, 80u })! == 50u &&
              (uint)toMonitorValue.Invoke(null, new object[] { -10, 20u, 80u })! == 20u &&
              (uint)toMonitorValue.Invoke(null, new object[] { 130, 20u, 80u })! == 80u &&
              (int)fromMonitorValue.Invoke(null, new object[] { 50u, 20u, 80u })! == 50 &&
              (int)fromMonitorValue.Invoke(null, new object[] { 100u, 100u, 100u })! == 0,
            "Display Brightness maps percentages to monitor ranges and clamps invalid input");
        var scheduleType = displayBrightness.GetType().Assembly.GetType("WinForge.Modules.Extras.DisplayBrightnessSchedule")!;
        var getScheduledBrightness = scheduleType.GetMethod("TryGetTarget", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] nightBeforeStart = { new DateTime(2026, 5, 1, 6, 59, 0), "07:00", 75, "20:00", 35, null };
        object?[] dayAtStart = { new DateTime(2026, 5, 1, 7, 0, 0), "07:00", 75, "20:00", 35, null };
        object?[] nightAtStart = { new DateTime(2026, 5, 1, 20, 0, 0), "07:00", 75, "20:00", 35, null };
        Check((bool)getScheduledBrightness.Invoke(null, nightBeforeStart)! && Equals(nightBeforeStart[5], 35) &&
              (bool)getScheduledBrightness.Invoke(null, dayAtStart)! && Equals(dayAtStart[5], 75) &&
              (bool)getScheduledBrightness.Invoke(null, nightAtStart)! && Equals(nightAtStart[5], 35),
            "Power Display schedule switches brightness at the configured local day and night start times");
        object?[] overnightTarget = { new DateTime(2026, 5, 1, 23, 0, 0), "08:00", 75, "22:00", 35, null };
        object?[] overnightDayTarget = { new DateTime(2026, 5, 1, 9, 0, 0), "08:00", 75, "22:00", 35, null };
        object?[] invalidSchedule = { new DateTime(2026, 5, 1, 9, 0, 0), "7:00", 75, "20:00", 35, null };
        object?[] equalTimes = { new DateTime(2026, 5, 1, 23, 0, 0), "07:00", 140, "07:00", 35, null };
        Check((bool)getScheduledBrightness.Invoke(null, overnightTarget)! && Equals(overnightTarget[5], 35) &&
              (bool)getScheduledBrightness.Invoke(null, overnightDayTarget)! && Equals(overnightDayTarget[5], 75) &&
              !(bool)getScheduledBrightness.Invoke(null, invalidSchedule)! &&
              (bool)getScheduledBrightness.Invoke(null, equalTimes)! && Equals(equalTimes[5], 100),
            "Power Display schedule handles overnight periods, clamps levels, resolves equal times and rejects malformed local times");
        var profilesType = displayBrightness.GetType().Assembly.GetType("WinForge.Modules.Extras.DisplaySettingsProfiles")!;
        var monitorModelType = displayBrightness.GetType().Assembly.GetType("WinForge.Modules.Extras.DisplayBrightnessMonitor")!;
        var fakeMonitor = Activator.CreateInstance(monitorModelType, new object?[]
            { "TESTDISPLAY:0", "Test Monitor", 0u, 100u, 50u, true, null, null, null, null, null, false })!;
        var monitorCollectionType = typeof(List<>).MakeGenericType(monitorModelType);
        var fakeMonitors = (System.Collections.IList)Activator.CreateInstance(monitorCollectionType)!;
        fakeMonitors.Add(fakeMonitor);
        var secondDisplayMonitor = Activator.CreateInstance(monitorModelType, new object?[]
            { "\\\\.\\DISPLAY2:0", "Second Monitor", 0u, 100u, 40u, true, null, null, null, null, null, false })!;
        var mirroredPhysicalMonitor = Activator.CreateInstance(monitorModelType, new object?[]
            { "TESTDISPLAY:1", "Test Monitor 2", 0u, 100u, 45u, true, null, null, null, null, null, false })!;
        var findMonitorsForDevice = displayBrightness.GetType().Assembly.GetType("WinForge.Modules.Extras.DisplayBrightnessSession")!
            .GetMethod("FindMonitorsForDevice", BindingFlags.Static | BindingFlags.NonPublic)!;
        var testPhysicalMonitors = Array.CreateInstance(monitorModelType, 3);
        testPhysicalMonitors.SetValue(fakeMonitor, 0);
        testPhysicalMonitors.SetValue(secondDisplayMonitor, 1);
        testPhysicalMonitors.SetValue(mirroredPhysicalMonitor, 2);
        var matchingDisplayMonitors = ((System.Collections.IEnumerable)findMonitorsForDevice.Invoke(null,
            new object[] { testPhysicalMonitors, "testdisplay" })!).Cast<object>().ToArray();
        Check(matchingDisplayMonitors.Length == 2 && matchingDisplayMonitors.All(monitor =>
                  ((string)monitor.GetType().GetProperty("Id")!.GetValue(monitor)!).StartsWith("TESTDISPLAY:", StringComparison.OrdinalIgnoreCase)) &&
              !((System.Collections.IEnumerable)findMonitorsForDevice.Invoke(null,
                  new object[] { testPhysicalMonitors, "unknown-display" })!).Cast<object>().Any(),
            "Power Display resolves every physical monitor belonging to one cursor-selected Windows display");
        var captureProfile = profilesType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!;
        var fakeProfile = captureProfile.Invoke(null, new object[] { "Test profile", fakeMonitors })!;
        var fakeProfileMonitor = ((System.Collections.IEnumerable)fakeProfile.GetType().GetProperty("Monitors")!.GetValue(fakeProfile)!)
            .Cast<object>().Single();
        Check((int)fakeProfileMonitor.GetType().GetProperty("Brightness")!.GetValue(fakeProfileMonitor)! == 50 &&
              (string)fakeProfile.GetType().GetProperty("Name")!.GetValue(fakeProfile)! == "Test profile",
            "Power Display profiles capture normalized per-monitor settings with a user name");
        var fakeProfileArray = Array.CreateInstance(fakeProfile.GetType(), 1);
        fakeProfileArray.SetValue(fakeProfile, 0);
        var serializedProfile = System.Text.Json.JsonSerializer.Serialize(fakeProfileArray, fakeProfileArray.GetType());
        var loadProfiles = profilesType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
        var loadedProfiles = (System.Collections.IList)loadProfiles.Invoke(null, new object[] { serializedProfile })!;
        Check(loadedProfiles.Count == 1, "Power Display profile JSON reload returns the saved profile");
        Check((string)loadedProfiles[0]!.GetType().GetProperty("Name")!.GetValue(loadedProfiles[0])! == "Test profile",
            "Power Display profile reload preserves its name");
        Check(((System.Collections.IList)loadProfiles.Invoke(null, new object[] { "corrupt profile json" })!).Count == 0,
            "Power Display ignores malformed profile JSON");
        var brightnessWindowType = displayBrightness.GetType().Assembly.GetType("WinForge.Modules.Extras.DisplayBrightnessWindow")!;
        host.SettingsOf(displayBrightness).Set("profiles", serializedProfile);
        host.SetEnabled(displayBrightness, true);
        displayBrightness.Settings.Single(setting => setting.Key == "open-panel").Execute!();
        var brightnessWindow = app.Windows.Cast<Window>().Single(window => window.GetType() == brightnessWindowType);
        var discoveryStatus = (TextBlock)brightnessWindowType.GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(brightnessWindow)!;
        PumpUntil(brightnessWindow, () => !discoveryStatus.Text.Contains("yetenekleri okunuyor", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(30));
        Check(Descendants<CheckBox>(brightnessWindow).Any() &&
              Descendants<TextBlock>(brightnessWindow).Any(block => block.Text.Length > 0) &&
              Descendants<Button>(brightnessWindow).Any(button => button.Content?.ToString() == "Profili kaydet") &&
              Descendants<Button>(brightnessWindow).Any(button => button.Content?.ToString() == "Uygula") &&
              Descendants<Button>(brightnessWindow).Any(button => button.Content?.ToString() == "Profili sil"),
            "Power Display loads asynchronously and renders profile save, apply and delete controls");
        var profileSelector = Descendants<ComboBox>(brightnessWindow).Single(comboBox => comboBox.Name == "DisplayProfileSelector");
        Check(profileSelector.Items.Contains("Test profile"), "Power Display reloads persisted monitor profiles into its selector");
        Descendants<Button>(brightnessWindow).Single(button => button.Content?.ToString() == "Uygula").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(discoveryStatus.Text.Contains("1 ekran bulunamadı", StringComparison.Ordinal),
            "Power Display skips a profile monitor that is not attached and reports it without sending a command");
        Descendants<Button>(brightnessWindow).Single(button => button.Content?.ToString() == "Profili sil").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(profileSelector.Items.Count == 0 &&
              host.SettingsOf(displayBrightness).GetString("profiles") == "[]",
            "Power Display deletes a selected profile and persists the updated profile list");
        var liveDisplaySession = brightnessWindowType.GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(brightnessWindow)!;
        var discoveredDisplays = liveDisplaySession is null ? Array.Empty<object>() :
            ((System.Collections.IEnumerable)liveDisplaySession.GetType().GetProperty("Monitors")!.GetValue(liveDisplaySession)!)
                .Cast<object>().ToArray();
        var expectedDisplaySliders = discoveredDisplays.Sum(monitor =>
        {
            var type = monitor.GetType();
            var brightness = (bool)type.GetProperty("SupportsBrightness")!.GetValue(monitor)! || type.GetProperty("VcpBrightness")!.GetValue(monitor) is not null;
            var contrast = type.GetProperty("Contrast")!.GetValue(monitor) is not null;
            var volume = type.GetProperty("Volume")!.GetValue(monitor) is not null;
            return (brightness ? 1 : 0) + (contrast ? 1 : 0) + (volume ? 1 : 0);
        });
        var expectedDisplaySelectors = discoveredDisplays.Sum(monitor =>
            (monitor.GetType().GetProperty("InputSource")!.GetValue(monitor) is not null ? 1 : 0) +
            (monitor.GetType().GetProperty("ColorPreset")!.GetValue(monitor) is not null ? 1 : 0)) + 1;
        Check(Descendants<Slider>(brightnessWindow).Count() == expectedDisplaySliders &&
              Descendants<ComboBox>(brightnessWindow).Count() == expectedDisplaySelectors,
            "Power Display UI exposes exactly the controls discovered from connected monitor capabilities");
        brightnessWindow.Close();
        host.SetEnabled(displayBrightness, false);
        var mouseSpotlight = host.Modules.Single(module => module.Id == "mouse");
        var crosshairSettings = mouseSpotlight.Settings.Where(setting => setting.Key.StartsWith("crosshairs-", StringComparison.Ordinal)).ToArray();
        Check(crosshairSettings.Length == 7 &&
              crosshairSettings.Single(setting => setting.Key == "crosshairs-color").Options.SequenceEqual(new[] { "Beyaz", "Sarı", "Camgöbeği" }) &&
              crosshairSettings.Single(setting => setting.Key == "crosshairs-fixed-length").Default is 0,
            "Mouse crosshairs expose persistent startup, appearance and fixed-length settings");
        var clickSettings = mouseSpotlight.Settings.ToDictionary(setting => setting.Key);
        Check(clickSettings["click-buttons"].Default?.ToString() == "left-right" &&
              clickSettings["click-size"].Min == 24 && clickSettings["click-size"].Max == 180 &&
              clickSettings["click-duration"].Min == 100 && clickSettings["click-duration"].Max == 2000 &&
              new[] { "click-color-left", "click-color-right", "click-color-middle", "click-color-x1", "click-color-x2" }
                  .All(key => clickSettings[key].Options.Contains("Beyaz")),
            "Mouse highlighter exposes button scope, ring size, duration and independent colors for five buttons");
        var xButtonNumber = typeof(GlobalMouseHook).GetMethod("GetXButtonNumber", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((int)xButtonNumber.Invoke(null, new object[] { 0x00010000u })! == 1 &&
              (int)xButtonNumber.Invoke(null, new object[] { 0x00020000u })! == 2 &&
              (int)xButtonNumber.Invoke(null, new object[] { 0x00030000u })! == 0,
            "Mouse hook recognizes XBUTTON1/XBUTTON2 and ignores invalid side-button identifiers");
        var crosshairType = mouseSpotlight.GetType().Assembly.GetType("WinForge.Modules.Extras.CrosshairOverlay")!;
        var crosshairOverlay = (Window)Activator.CreateInstance(crosshairType, nonPublic: true)!;
        crosshairType.GetMethod("Start")!.Invoke(crosshairOverlay,
            new object[] { Colors.Yellow, 75, 20, 5, 2, 0 });
        Pump(crosshairOverlay);
        Check(crosshairOverlay.IsVisible && Descendants<System.Windows.Shapes.Line>(crosshairOverlay).Any(),
            "Mouse crosshair overlay renders visible lines and follows the active monitor");
        crosshairType.GetMethod("Stop")!.Invoke(crosshairOverlay, null);
        crosshairOverlay.Close();
        Check(!host.Modules.Any(module => module.Id == "mouse-jump"), "Removed mouse jump is not discoverable");
        Check(host.Modules.All(module => module.Id != "peek-preview"), "Removed file preview module is absent from discovery");
        var commandModules = host.Modules.OfType<IAppCommandModule>().Select(module => module.GetType().Name).ToHashSet(StringComparer.Ordinal);
        Check(commandModules.SetEquals(new[] { "PowerRenameModule", "FileLocksmithModule" }),
            "Explorer actions expose only the intended file commands to the single-instance broker");
        var commandRequest = typeof(ForgeApplication).GetMethod("IsCommandRequest", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((bool)commandRequest.Invoke(null, new object[] { new[] { "--power-rename", "sample.txt" } })! &&
              !(bool)commandRequest.Invoke(null, new object[] { new[] { "--peek", "sample.txt" } })! &&
              !(bool)commandRequest.Invoke(null, new object[] { new[] { "--run", "anything" } })!,
            "Single-instance broker accepts only the registered file command verbs");
        Check(!host.Modules.Any(module => module.Id == "keyboard-manager"), "Retired Keyboard Manager is absent from module discovery");
        var keyboardManager = (ITweakModule)Activator.CreateInstance(
            Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"))
                .GetType("WinForge.Modules.Extras.KeyboardManagerModule")!)!;
        var editorType = keyboardManager.GetType().Assembly.GetType("WinForge.Modules.Extras.KeyboardMappingEditor")!;
        var parseShortcuts = editorType.GetMethod("ParseShortcutMappings", BindingFlags.NonPublic | BindingFlags.Static)!;
        var validShortcuts = (System.Collections.ICollection)parseShortcuts.Invoke(null, new object[] { "Ctrl+C = Ctrl+Insert" })!;
        Check(validShortcuts.Count == 1, "Keyboard Manager parses shortcut remaps");
        Check(InvocationThrowsFormatException(parseShortcuts, "Win+L = Ctrl+L"), "Keyboard Manager rejects the reserved Windows lock shortcut");
        var parseAppMappings = editorType.GetMethod("ParseAppSpecificMappings", BindingFlags.NonPublic | BindingFlags.Static)!;
        var appMappings = (System.Collections.IList)parseAppMappings.Invoke(null, new object[]
        {
            "msedge.exe: Ctrl+Shift+X = Ctrl+Shift+Y\nnotepad.exe: A = B"
        })!;
        Check(appMappings.Count == 2 &&
              (string)appMappings[0]!.GetType().GetProperty("ProcessName")!.GetValue(appMappings[0])! == "msedge" &&
              (bool)appMappings[0]!.GetType().GetProperty("IsShortcut")!.GetValue(appMappings[0])! &&
              (string)appMappings[1]!.GetType().GetProperty("ProcessName")!.GetValue(appMappings[1])! == "notepad" &&
              !(bool)appMappings[1]!.GetType().GetProperty("IsShortcut")!.GetValue(appMappings[1])!,
            "Keyboard Manager parses process-scoped key and shortcut mappings");
        Check(InvocationThrowsFormatException(parseAppMappings, "C:\\Apps\\msedge.exe: A = B"),
            "Keyboard Manager rejects application paths where a process name is required");
        var parseTextMappings = editorType.GetMethod("ParseTextMappings", BindingFlags.NonPublic | BindingFlags.Static)!;
        var textMappings = (System.Collections.IList)parseTextMappings.Invoke(null, new object[]
        {
            "Ctrl+Alt+T = Meeting notes Ω = Ready\nH = Hello!"
        })!;
        Check(textMappings.Count == 2 &&
              (string)textMappings[0]!.GetType().GetProperty("Text")!.GetValue(textMappings[0])! == "Meeting notes Ω = Ready" &&
              (int)textMappings[0]!.GetType().GetProperty("FromKey")!.GetValue(textMappings[0])! == 0x54 &&
              (uint)textMappings[1]!.GetType().GetProperty("FromModifiers")!.GetValue(textMappings[1])! == 0 &&
              (string)textMappings[1]!.GetType().GetProperty("Text")!.GetValue(textMappings[1])! == "Hello!",
            "Keyboard Manager parses key- and shortcut-triggered Unicode text snippets");
        Check(InvocationThrowsFormatException(parseTextMappings, "Win+L = Locked") &&
              InvocationThrowsFormatException(parseTextMappings, "Ctrl+Alt+T = " + new string('x', 2049)),
            "Keyboard Manager rejects reserved shortcut and oversized text snippet triggers");
        var parseShortcutActions = editorType.GetMethod("ParseShortcutActions", BindingFlags.NonPublic | BindingFlags.Static)!;
        var shortcutActions = (System.Collections.IList)parseShortcutActions.Invoke(null, new object[]
        {
            "Ctrl+Alt+N = app|\"%WINDIR%\\System32\\notepad.exe\"|--new-window|\"%TEMP%\"|elevated|show|hidden\nCtrl+Alt+W = uri|https://example.com/path?q=1"
        })!;
        Check(shortcutActions.Count == 2 &&
              (string)shortcutActions[0]!.GetType().GetProperty("ActionType")!.GetValue(shortcutActions[0])! == "app" &&
              (string)shortcutActions[0]!.GetType().GetProperty("Arguments")!.GetValue(shortcutActions[0])! == "--new-window" &&
              (string)shortcutActions[0]!.GetType().GetProperty("WorkingDirectory")!.GetValue(shortcutActions[0])! == "%TEMP%" &&
              (string)shortcutActions[0]!.GetType().GetProperty("Elevation")!.GetValue(shortcutActions[0])! == "elevated" &&
              (string)shortcutActions[0]!.GetType().GetProperty("IfRunning")!.GetValue(shortcutActions[0])! == "show" &&
              (string)shortcutActions[0]!.GetType().GetProperty("Visibility")!.GetValue(shortcutActions[0])! == "hidden" &&
              (string)shortcutActions[1]!.GetType().GetProperty("Target")!.GetValue(shortcutActions[1])! == "https://example.com/path?q=1",
            "Keyboard Manager parses app launch arguments, directory, elevation, running policy, visibility and URI targets");
        var chordActions = (System.Collections.IList)parseShortcutActions.Invoke(null, new object[]
        {
            "Ctrl+K, Ctrl+C = uri|https://example.com/copy\nAlt+Space, Alt+F = app|notepad.exe"
        })!;
        var firstChordSequence = (System.Collections.IList)chordActions[0]!.GetType().GetProperty("Sequence")!.GetValue(chordActions[0])!;
        Check(chordActions.Count == 2 && firstChordSequence.Count == 2 &&
              (int)firstChordSequence[0]!.GetType().GetProperty("Key")!.GetValue(firstChordSequence[0])! == 0x4B &&
              (int)firstChordSequence[1]!.GetType().GetProperty("Key")!.GetValue(firstChordSequence[1])! == 0x43,
            "Keyboard Manager parses two-step shortcut sequences while retaining legacy single-chord actions");
        var keyboardManagerType = keyboardManager.GetType();
        ((System.Collections.IList)keyboardManagerType.GetField("_shortcutActions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(keyboardManager)!).Add(chordActions[0]);
        var pressedKeyboardKeys = (HashSet<int>)keyboardManagerType.GetField("_pressedKeys", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(keyboardManager)!;
        var tryGetShortcutAction = keyboardManagerType.GetMethod("TryGetShortcutAction", BindingFlags.NonPublic | BindingFlags.Instance)!;
        pressedKeyboardKeys.Add(0xA2);
        object?[] firstChordResult = { 0x4B, null };
        var firstChordTriggered = (bool)tryGetShortcutAction.Invoke(keyboardManager, firstChordResult)!;
        pressedKeyboardKeys.Remove(0xA2);
        pressedKeyboardKeys.Add(0xA2);
        object?[] secondChordResult = { 0x43, null };
        var sequenceTriggered = (bool)tryGetShortcutAction.Invoke(keyboardManager, secondChordResult)!;
        pressedKeyboardKeys.Clear();
        Check(!firstChordTriggered && sequenceTriggered && ReferenceEquals(secondChordResult[1], chordActions[0]),
            "Keyboard Manager waits for a matching second chord and triggers only after the full sequence");
        ((System.Collections.IList)keyboardManagerType.GetField("_shortcutActions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(keyboardManager)!).Clear();
        Check(InvocationThrowsFormatException(parseShortcutActions, "Ctrl+Alt+W = uri|javascript:alert(1)") &&
              InvocationThrowsFormatException(parseShortcutActions, "Ctrl+K = uri|https://example.com/short\nCtrl+K, Ctrl+C = uri|https://example.com/long") &&
              InvocationThrowsFormatException(parseShortcutActions, "Ctrl+K, Win+L = uri|https://example.com") &&
              InvocationThrowsFormatException(parseShortcutActions, "Ctrl+K, Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z = uri|https://example.com") &&
              InvocationThrowsFormatException(parseShortcutActions, "Win+L = uri|https://example.com") &&
              InvocationThrowsFormatException(parseShortcutActions, "Ctrl+Alt+N = app|notepad.exe|||bad-level") &&
              InvocationThrowsFormatException(parseShortcutActions, "Ctrl+Alt+N = app|notepad.exe||||normal|bad-policy"),
            "Keyboard Manager rejects unsafe URI schemes, reserved triggers and invalid app launch policies");
        var powerRename = host.Modules.Single(module => module.Id == "power-rename");
        var renameTransaction = powerRename.GetType().Assembly.GetType("WinForge.Modules.Extras.RenameTransaction")!;
        var renameApply = renameTransaction.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!;
        var renameFolder = Path.Combine(TestDirectory, "power-rename");
        Directory.CreateDirectory(renameFolder);
        var first = Path.Combine(renameFolder, "first.txt");
        var second = Path.Combine(renameFolder, "second.txt");
        File.WriteAllText(first, "first-content");
        File.WriteAllText(second, "second-content");
        var operationType = powerRename.GetType().Assembly.GetType("WinForge.Modules.Extras.RenameOperation")!;
        var renameUndoStore = powerRename.GetType().Assembly.GetType("WinForge.Modules.Extras.RenameUndoStore")!;
        var renameUndoPath = Path.Combine(renameFolder, "undo.json");
        var saveUndo = renameUndoStore.GetMethod("Save", BindingFlags.Public | BindingFlags.Static)!;
        var loadUndo = renameUndoStore.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;
        var clearUndo = renameUndoStore.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static)!;
        var canUndo = renameUndoStore.GetMethod("CanUndo", BindingFlags.Public | BindingFlags.Static)!;
        var persistedOperation = CreateRenameOperation(first, second);
        var persistedOperations = Array.CreateInstance(operationType, 1);
        persistedOperations.SetValue(persistedOperation, 0);
        saveUndo.Invoke(null, new object[] { persistedOperations, renameUndoPath });
        var loadedUndo = (System.Collections.IList)loadUndo.Invoke(null, new object[] { renameUndoPath })!;
        Check(loadedUndo.Count == 1 &&
              (string)operationType.GetProperty("Source")!.GetValue(loadedUndo[0])! == first &&
              (string)operationType.GetProperty("Target")!.GetValue(loadedUndo[0])! == second,
            "PowerRename serializes and reloads its undo operation from the durable local record");
        Check(!(bool)canUndo.Invoke(null, new object[] { persistedOperations })!,
            "PowerRename refuses stale undo records when the rename destination does not match the filesystem");
        clearUndo.Invoke(null, new object[] { renameUndoPath });
        File.WriteAllText(renameUndoPath, "{invalid json");
        Check(((System.Collections.IList)loadUndo.Invoke(null, new object[] { renameUndoPath })!).Count == 0,
            "PowerRename ignores a corrupt undo record without blocking its window");
        clearUndo.Invoke(null, new object[] { renameUndoPath });
        File.WriteAllText(first, "first-content");
        object CreateRenameOperation(string source, string target) => Activator.CreateInstance(operationType, source, target)!;
        var swap = Array.CreateInstance(operationType, 2);
        swap.SetValue(CreateRenameOperation(first, second), 0);
        swap.SetValue(CreateRenameOperation(second, first), 1);
        renameApply.Invoke(null, new object[] { swap });
        Check(File.ReadAllText(first) == "second-content" && File.ReadAllText(second) == "first-content", "PowerRename safely supports selected-file name swaps");
        var undo = Array.CreateInstance(operationType, 2);
        undo.SetValue(CreateRenameOperation(second, first), 0);
        undo.SetValue(CreateRenameOperation(first, second), 1);
        renameApply.Invoke(null, new object[] { undo });
        Check(File.ReadAllText(first) == "first-content" && File.ReadAllText(second) == "second-content", "PowerRename transaction can be reversed without data loss");
        var occupied = Path.Combine(renameFolder, "occupied.txt");
        File.WriteAllText(occupied, "occupied-content");
        var conflict = Array.CreateInstance(operationType, 1);
        conflict.SetValue(CreateRenameOperation(first, occupied), 0);
        Check(InvocationThrowsIOException(renameApply, conflict) && File.ReadAllText(first) == "first-content" && File.ReadAllText(occupied) == "occupied-content", "PowerRename rejects existing target files without changing sources");
        host.SetEnabled(powerRename, true);
        Check(powerRename.IsActive, "PowerRename activates cleanly");
        host.SetEnabled(powerRename, false);
        Check(!powerRename.IsActive, "PowerRename deactivates cleanly");
        var renameWindowType = powerRename.GetType().Assembly.GetType("WinForge.Modules.Extras.PowerRenameWindow")!;
        var renameWindow = (Window)Activator.CreateInstance(renameWindowType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
            args: new object?[] { null, renameUndoPath }, culture: null)!;
        renameWindow.Show();
        Pump(renameWindow);
        Check(Descendants<DataGrid>(renameWindow).Single().Columns.Count == 3, "PowerRename preview window creates its file, target and status columns");
        var previewFolder = Path.Combine(renameFolder, "preview");
        Directory.CreateDirectory(previewFolder);
        var previewSource = Path.Combine(previewFolder, "photo-17.jpg");
        var previewTarget = Path.Combine(previewFolder, "image-17.jpg");
        File.WriteAllText(previewSource, "image-content");
        var previewType = powerRename.GetType().Assembly.GetType("WinForge.Modules.Extras.RenamePreview")!;
        var preview = Activator.CreateInstance(previewType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: new object[] { previewSource }, culture: null)!;
        var selectedWindow = (Window)Activator.CreateInstance(renameWindowType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
            args: new object[] { new[] { previewSource } }, culture: null)!;
        selectedWindow.Show();
        Pump(selectedWindow);
        Check(Descendants<DataGrid>(selectedWindow).Single().Items.Count == 1,
            "PowerRename opens with files passed from Explorer selection");
        selectedWindow.Close();
        ((System.Collections.IList)renameWindowType.GetField("_previews", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renameWindow)!).Add(preview);
        ((TextBox)renameWindowType.GetField("_find", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renameWindow)!).Text = "photo-(?<number>\\d+)";
        ((TextBox)renameWindowType.GetField("_replace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renameWindow)!).Text = "image-${number}";
        ((CheckBox)renameWindowType.GetField("_regex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renameWindow)!).IsChecked = true;
        Invoke(renameWindow, "RefreshPreview");
        Check((string)previewType.GetProperty("NewName")!.GetValue(preview)! == "image-17.jpg" &&
              (string)previewType.GetProperty("Status")!.GetValue(preview)! == "Hazır", "PowerRename previews regex replacements before touching files");
        Click((Button)renameWindowType.GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renameWindow)!);
        Check(File.Exists(previewTarget) && File.ReadAllText(previewTarget) == "image-content", "PowerRename applies the reviewed preview while preserving file contents");
        var storedRename = (System.Collections.IList)loadUndo.Invoke(null, new object[] { renameUndoPath })!;
        Check(storedRename.Count == 1 && (string)operationType.GetProperty("Source")!.GetValue(storedRename[0])! == previewSource,
            "PowerRename writes successful batch changes to the durable undo record");
        var reopenedRenameWindow = (Window)Activator.CreateInstance(renameWindowType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
            args: new object?[] { null, renameUndoPath }, culture: null)!;
        reopenedRenameWindow.Show();
        Pump(reopenedRenameWindow);
        Check(((Button)renameWindowType.GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopenedRenameWindow)!).IsEnabled,
            "PowerRename restores the undo action after its window is reopened");
        reopenedRenameWindow.Close();
        Click((Button)renameWindowType.GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renameWindow)!);
        Check(File.Exists(previewSource) && File.ReadAllText(previewSource) == "image-content", "PowerRename UI undo restores the original filename and contents");
        Check(!File.Exists(renameUndoPath), "PowerRename removes the durable undo record after a successful undo");
        renameWindow.Close();
        var imageProcessor = powerRename.GetType().Assembly.GetType("WinForge.Modules.Extras.ImageResizeProcessor")!;
        var calculateSize = imageProcessor.GetMethod("CalculateSize", BindingFlags.Public | BindingFlags.Static)!;
        var scaledSize = calculateSize.Invoke(null, new object[] { 4000, 2000, 1280 })!;
        Check((int)scaledSize.GetType().GetField("Item1")!.GetValue(scaledSize)! == 1280 &&
              (int)scaledSize.GetType().GetField("Item2")!.GetValue(scaledSize)! == 640, "Image Resizer keeps aspect ratio for preset dimensions");
        var untouchedSize = calculateSize.Invoke(null, new object[] { 640, 480, 854 })!;
        Check((int)untouchedSize.GetType().GetField("Item1")!.GetValue(untouchedSize)! == 640 &&
              (int)untouchedSize.GetType().GetField("Item2")!.GetValue(untouchedSize)! == 480, "Image Resizer presets do not enlarge smaller images");
        var imageSource = Path.Combine(renameFolder, "sample.png");
        var resizedPng = Path.Combine(renameFolder, "sample-small.png");
        var convertedJpeg = Path.Combine(renameFolder, "sample-copy.jpg");
        CreateTestImage(imageSource, 40, 20);
        var resize = imageProcessor.GetMethod("Resize", BindingFlags.Public | BindingFlags.Static)!;
        resize.Invoke(null, new object[] { imageSource, resizedPng, 20, "PNG", 90 });
        var readSize = imageProcessor.GetMethod("ReadSize", BindingFlags.Public | BindingFlags.Static)!;
        var resizedDimensions = readSize.Invoke(null, new object[] { resizedPng })!;
        var sourceDimensions = readSize.Invoke(null, new object[] { imageSource })!;
        Check((int)resizedDimensions.GetType().GetField("Item1")!.GetValue(resizedDimensions)! == 20 &&
              (int)resizedDimensions.GetType().GetField("Item2")!.GetValue(resizedDimensions)! == 10 &&
              (int)sourceDimensions.GetType().GetField("Item1")!.GetValue(sourceDimensions)! == 40,
            "Image Resizer writes a correctly scaled PNG and preserves the source");
        resize.Invoke(null, new object[] { imageSource, convertedJpeg, 0, "JPEG", 85 });
        var jpegDimensions = readSize.Invoke(null, new object[] { convertedJpeg })!;
        Check((int)jpegDimensions.GetType().GetField("Item1")!.GetValue(jpegDimensions)! == 40 && File.ReadAllBytes(convertedJpeg)[0] == 0xFF,
            "Image Resizer creates a valid JPEG at the requested quality setting");
        var metadataJpeg = Path.Combine(renameFolder, "metadata-source.jpg");
        var metadataOutput = Path.Combine(renameFolder, "metadata-output.jpg");
        CreateJpegWithMetadata(imageSource, metadataJpeg, "/app1/ifd/{ushort=270}", "WinForge EXIF regression");
        var resizeWithOptions = imageProcessor.GetMethod("ResizeWithOptions", BindingFlags.Public | BindingFlags.Static)!;
        resizeWithOptions.Invoke(null, new object[] { metadataJpeg, metadataOutput, 0, "Kaynak biçimi", 85, true, CancellationToken.None });
        using (var metadataStream = File.OpenRead(metadataOutput))
        {
            var metadataFrame = BitmapDecoder.Create(metadataStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var outputMetadata = metadataFrame.Metadata as BitmapMetadata;
            Check(outputMetadata?.GetQuery("/app1/ifd/{ushort=270}") as string == "WinForge EXIF regression",
                "Image Resizer preserves EXIF metadata when the output codec matches the source");
        }
        var strippedMetadataOutput = Path.Combine(renameFolder, "metadata-stripped.jpg");
        resizeWithOptions.Invoke(null, new object[] { metadataJpeg, strippedMetadataOutput, 0, "Kaynak biçimi", 85, false, CancellationToken.None });
        using (var strippedStream = File.OpenRead(strippedMetadataOutput))
        {
            var strippedFrame = BitmapDecoder.Create(strippedStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Check((strippedFrame.Metadata as BitmapMetadata)?.ContainsQuery("/app1/ifd/{ushort=270}") != true,
                "Image Resizer omits EXIF metadata when the preservation option is disabled");
        }
        var cancelledOutput = Path.Combine(renameFolder, "cancelled-output.png");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var cancelledBeforeOutput = false;
            try { resizeWithOptions.Invoke(null, new object[] { imageSource, cancelledOutput, 20, "PNG", 90, true, cancelled.Token }); }
            catch (TargetInvocationException exception) when (exception.InnerException is OperationCanceledException)
            {
                cancelledBeforeOutput = true;
            }
            Check(cancelledBeforeOutput && !File.Exists(cancelledOutput) &&
                  !Directory.EnumerateFiles(renameFolder, "cancelled-output.png.*.winforge.tmp").Any(),
                "Image Resizer honors cancellation before encoding without leaving partial outputs");
        }
        File.WriteAllText(Path.Combine(renameFolder, "occupied-small.png"), "keep-me");
        Check(InvocationThrowsIOException(resize, imageSource, Path.Combine(renameFolder, "occupied-small.png"), 20, "PNG", 90) &&
              File.ReadAllText(Path.Combine(renameFolder, "occupied-small.png")) == "keep-me",
            "Image Resizer does not overwrite an existing output");
        var imageResizer = host.Modules.Single(module => module.Id == "image-resizer");
        host.SetEnabled(imageResizer, true);
        Check(imageResizer.IsActive, "Image Resizer activates cleanly");
        host.SetEnabled(imageResizer, false);
        Check(!imageResizer.IsActive, "Image Resizer deactivates cleanly");
        var imageWindowType = imageResizer.GetType().Assembly.GetType("WinForge.Modules.Extras.ImageResizerWindow")!;
        var imageWindow = (Window)Activator.CreateInstance(imageWindowType, nonPublic: true)!;
        imageWindow.Show();
        Pump(imageWindow);
        Check(Descendants<DataGrid>(imageWindow).Single().Columns.Count == 5 &&
              Descendants<CheckBox>(imageWindow).Any(check => check.Content?.ToString()?.Contains("metadata", StringComparison.OrdinalIgnoreCase) == true) &&
              Descendants<ProgressBar>(imageWindow).Any() && Descendants<Button>(imageWindow).Any(button => button.Content?.ToString() == "İptal"),
            "Image Resizer preview window exposes metadata preservation, batch progress and cancellation");
        imageWindow.Close();
        var fileLocksmith = host.Modules.Single(module => module.Id == "file-locksmith");
        var restartManagerType = fileLocksmith.GetType().Assembly.GetType("WinForge.Modules.Extras.RestartManagerQuery")!;
        var findProcesses = restartManagerType.GetMethod("FindProcesses", BindingFlags.Public | BindingFlags.Static)!;
        var lockedTestFile = Path.Combine(renameFolder, "locked-test.txt");
        File.WriteAllText(lockedTestFile, "lock-test");
        IReadOnlyCollection<object> processRows;
        using (File.Open(lockedTestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            processRows = ((System.Collections.IEnumerable)findProcesses.Invoke(null, new object[] { lockedTestFile })!).Cast<object>().ToArray();
        Check(processRows.Any(item => (int)(uint)item.GetType().GetProperty("ProcessId")!.GetValue(item)! == Environment.ProcessId),
            "File Locksmith identifies this process while it holds the selected file open");
        var describeStatus = restartManagerType.GetMethod("DescribeStatus", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((string)describeStatus.Invoke(null, new object[] { 1u })! == "çalışıyor" &&
              ((string)describeStatus.Invoke(null, new object[] { 2u })!).Contains("Restart Manager tarafından durduruldu"),
            "File Locksmith translates Restart Manager status flags correctly");
        var currentLock = processRows.Single(item => (int)(uint)item.GetType().GetProperty("ProcessId")!.GetValue(item)! == Environment.ProcessId);
        Check(!(bool)currentLock.GetType().GetProperty("CanTerminate")!.GetValue(currentLock)!, "File Locksmith never offers to terminate WinForge itself");
        Check(File.Exists(lockedTestFile) && Process.GetCurrentProcess().HasExited == false, "File Locksmith scan leaves inspected files and processes untouched");
        host.SetEnabled(fileLocksmith, true);
        Check(fileLocksmith.IsActive, "File Locksmith activates cleanly");
        host.SetEnabled(fileLocksmith, false);
        Check(!fileLocksmith.IsActive, "File Locksmith deactivates cleanly");
        var locksmithWindowType = fileLocksmith.GetType().Assembly.GetType("WinForge.Modules.Extras.FileLocksmithWindow")!;
        var locksmithWindow = (Window)Activator.CreateInstance(locksmithWindowType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
            args: new object[] { lockedTestFile }, culture: null)!;
        locksmithWindow.Show();
        Pump(locksmithWindow);
        Check(Descendants<DataGrid>(locksmithWindow).Single().Columns.Count == 4 &&
              Descendants<TextBox>(locksmithWindow).Single().Text == lockedTestFile,
            "File Locksmith opens from the selected Explorer file and displays process details");
        locksmithWindow.Close();
        foreach (var module in host.Modules)
        {
            Check(module.Settings.Select(setting => setting.Key).Distinct().Count() == module.Settings.Count, $"Unique settings: {module.Name}");
            foreach (var setting in module.Settings)
            {
                if (setting.Kind == SettingKind.Number)
                    Check(Convert.ToInt32(setting.Default) >= setting.Min && Convert.ToInt32(setting.Default) <= setting.Max, $"Numeric default within range: {module.Id}/{setting.Key}");
                if (setting.Kind == SettingKind.Choice)
                    Check(setting.Options.Contains(setting.Default as string), $"Choice default exists: {module.Id}/{setting.Key}");
            }
        }
        SetProperty(app, nameof(ForgeApplication.Host), host);
        var window = new MainWindow(Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(AppData.DirectoryPath, "WebView2")));
        window.Show();
        Pump(window);
        var list = Find<ListBox>(window, "ModuleList");
        var search = Find<TextBox>(window, "SearchBox");
        var categories = Find<ListBox>(window, "CategoryList");
        Check(list.Items.Count == host.Modules.Count && list.SelectedItem is ModuleVm, "Initial view selects a module");
        foreach (var category in categories.Items.Cast<string>().ToArray())
        {
            categories.SelectedItem = category;
            Check(list.Items.Count > 0 && list.SelectedItem is ModuleVm selected && (category == "Tümü" || selected.Category == category), $"Category updates selection: {category}");
        }
        categories.SelectedIndex = 0;
        search.Text = "   TASKBAR   ";
        Check(list.Items.Count > 0 && list.Items.Cast<ModuleVm>().All(module => module.Category == "Taskbar"), "Search matches category and trims whitespace");
        search.Text = "no-such-module-123456";
        Check(list.Items.Count == 0 && list.SelectedItem is null && Find<StackPanel>(window, "EmptyState").Visibility == Visibility.Visible, "Empty results clear stale details and show recovery");
        Click(Descendants<Button>(Find<StackPanel>(window, "EmptyState")).Single());
        Check(search.Text == "" && categories.SelectedIndex == 0 && list.Items.Count == host.Modules.Count, "Reset filters restores the module list");
        var activeOnly = Find<CheckBox>(window, "ActiveOnlyToggle");
        activeOnly.IsChecked = true;
        Click(activeOnly);
        Check(list.Items.Count == 0, "Active filter hides inactive modules");
        activeOnly.IsChecked = false;
        Click(activeOnly);
        foreach (var vm in list.Items.Cast<ModuleVm>().ToArray())
        {
            list.SelectedItem = vm;
            Pump(window);
            Check(Find<StackPanel>(window, "DetailPanel").Children.Count >= 4, $"Settings render: {vm.Name}");
        }
        list.SelectedItem = list.Items.Cast<ModuleVm>().Single(module => module.Module.Id == "window-control");
        Pump(window);
        var windowControlDetail = Find<StackPanel>(window, "DetailPanel");
        var leftButtonAction = Descendants<ComboBox>(windowControlDetail).Single(combo =>
            System.Windows.Automation.AutomationProperties.GetAutomationId(combo) == "window-control.LMB");
        Check(leftButtonAction.Items.Contains("Taşı") && leftButtonAction.Items.Contains("Boyutlandır"),
            "Native window actions render configurable move and resize choices");
        leftButtonAction.SelectedItem = "Ortala";
        Check(host.SettingsOf(host.Modules.Single(module => module.Id == "window-control")).GetString("LMB") == "Ortala",
            "Native window action changes persist through the application settings panel");
        Capture(window, "window-control.png");
        list.SelectedItem = list.Items.Cast<ModuleVm>().First(module => module.Module.Id == "net-speed");
        Pump(window);
        var detailScroll = Find<ScrollViewer>(window, "DetailScrollViewer");
        detailScroll.ScrollToEnd();
        Pump(window);
        Check(detailScroll.VerticalOffset > 0, "Detail settings scroll to their final options");
        list.SelectedItem = list.Items[0];
        Pump(window);
        Check(detailScroll.VerticalOffset == 0, "Selecting another module resets detail scrolling");
        list.SelectedItem = list.Items.Cast<ModuleVm>().First(module => module.Module.Id == "net-speed");
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Pump(window);
        Check(list.ActualWidth >= 360 && Find<StackPanel>(window, "DetailPanel").ActualWidth > 300, "Minimum window size keeps both columns usable");
        foreach (var control in Descendants<CheckBox>(list))
        {
            var origin = control.TranslatePoint(new Point(), list);
            Check(origin.X + control.ActualWidth <= list.ActualWidth, $"Module switch remains visible at minimum width ({origin.X}, {list.ActualWidth})");
        }
        Capture(window, "compact.png");
        window.Width = 1180;
        window.Height = 820;
        Click(Find<Button>(window, "AppearanceButton"));
        Check(Find<TextBlock>(window, "PageTitle").Text == "Görünüm", "Appearance opens from the toolbar");
        var originalTone = Appearance.ToneName;
        var originalAccent = Appearance.AccentName;
        foreach (var tone in Appearance.Tones)
        {
            SetProperty(null, nameof(Appearance.ToneName), tone.Name, typeof(Appearance));
            Invoke(window, "ApplyAppearance");
            Invoke(window, "RenderDetail");
            Pump(window);
            Check(((SolidColorBrush)app.Resources["TextBrush"]).Color != ((SolidColorBrush)app.Resources["CardBrush"]).Color, $"Theme resources update: {tone.Name}");
            if (tone.Name == "Açık") Capture(window, "light.png");
            foreach (var accent in Appearance.Accents)
            {
                SetProperty(null, nameof(Appearance.AccentName), accent.Name, typeof(Appearance));
                Appearance.Apply();
                Check(Contrast(app, "MutedBrush", "CardBrush") >= 4.5 && Contrast(app, "AccentBrush", "CardBrush") >= 4.5,
                    $"Readable text contrast: {tone.Name}/{accent.Name}");
                Check(Contrast(app, "SwitchThumbBrush", "SwitchTrackBrush") >= 3,
                    $"Visible switch contrast: {tone.Name}/{accent.Name}");
            }
        }
        SetProperty(null, nameof(Appearance.ToneName), originalTone, typeof(Appearance));
        SetProperty(null, nameof(Appearance.AccentName), originalAccent, typeof(Appearance));
        Invoke(window, "ApplyAppearance");
        search.Text = "İnternet";
        Pump(window);
        Check(Find<TextBlock>(window, "PageTitle").Text != "Görünüm" && list.SelectedItem is ModuleVm, "Search leaves appearance and selects the matching module");
        Click(Find<Button>(window, "ClearSearchButton"));
        Pump(window);
        Check(search.Text == "" && list.Items.Count == host.Modules.Count, "Clear-search button restores results");
        var selectedContainer = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListBoxItem;
        Check(selectedContainer is not null && selectedContainer.TranslatePoint(new Point(), list).Y >= 0
            && selectedContainer.TranslatePoint(new Point(), list).Y + selectedContainer.ActualHeight <= list.ActualHeight,
            "Selected module remains in view after clearing search");
        Capture(window, "main.png");
        TestToggleUi(app);
    }

    private static void TestToggleUi(ForgeApplication app)
    {
        var host = CreateHost(Path.Combine(TestDirectory, "fixtures"), "ui-toggle");
        host.Load();
        SetProperty(app, nameof(ForgeApplication.Host), host);
        var window = new MainWindow(Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(AppData.DirectoryPath, "WebView2")));
        window.Show();
        Pump(window);
        var list = Find<ListBox>(window, "ModuleList");
        var toggle = Descendants<CheckBox>(Find<StackPanel>(window, "DetailPanel")).First();
        toggle.IsChecked = true;
        Click(toggle);
        Check(host.Modules.Single().IsActive && ((ModuleVm)list.SelectedItem).IsActive, "Detail switch updates module and list");
        var detail = Find<StackPanel>(window, "DetailPanel");
        var settings = host.SettingsOf(host.Modules.Single());
        var settingToggle = Descendants<CheckBox>(detail).Last();
        settingToggle.IsChecked = true;
        Click(settingToggle);
        Check(settings.GetBool("toggle"), "Toggle setting saves its value");
        Descendants<Slider>(detail).Single().Value = 7;
        PumpFor(window, TimeSpan.FromMilliseconds(220));
        Check(settings.GetInt("number") == 7, "Numeric setting saves its value");
        var text = Descendants<TextBox>(detail).Single();
        text.Text = "  saved text  ";
        text.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(settings.GetString("text") == "saved text", "Text setting trims and saves on focus loss");
        Descendants<RadioButton>(detail).Last().IsChecked = true;
        Check(settings.GetString("choice") == "Two", "Choice setting saves its value");
        var labeledChoice = Descendants<ComboBox>(detail).Single();
        labeledChoice.SelectedValue = "21996";
        Pump(window);
        Check(settings.GetString("labeled-choice") == "21996" && labeledChoice.Text == "SunValley (Legacy)",
            "Labeled choice settings display friendly names while persisting the underlying value");
        var activeOnly = Find<CheckBox>(window, "ActiveOnlyToggle");
        activeOnly.IsChecked = true;
        Click(activeOnly);
        Pump(window);
        toggle = Descendants<CheckBox>(list).Single();
        toggle.IsChecked = false;
        Click(toggle);
        Check(!host.Modules.Single().IsActive && list.Items.Count == 0 && list.SelectedItem is null, "List switch updates active filter and removes stale selection");
    }

    private static T Find<T>(Window window, string name) where T : FrameworkElement => (T)window.FindName(name);

    private static double Contrast(ForgeApplication app, string foreground, string background)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var first = Luminance(((SolidColorBrush)app.Resources[foreground]).Color);
        var second = Luminance(((SolidColorBrush)app.Resources[background]).Color);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static void SetProperty(object? target, string name, object value, Type? type = null)
    {
        var property = (type ?? target!.GetType()).GetProperty(name)!;
        property.DeclaringType!.GetProperty(name)!.SetValue(target, value);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Pump(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static bool PumpUntil(Window window, Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            Pump(window);
            if (condition()) return true;
            PumpFor(window, TimeSpan.FromMilliseconds(100));
        }
        Pump(window);
        return condition();
    }

    private static void PumpFor(Window window, TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Pump(window);
    }

    private static void PumpTask(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            timer.Tick += (_, _) =>
            {
                if (!task.IsCompleted) return;
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private static void CreateMinimalPdf(string path)
    {
        var content = "q\n0.15 0.45 0.85 rg\n20 20 160 160 re f\nQ\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream"
        };
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("%PDF-1.4\n");
        writer.Flush();
        var offsets = new List<long>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(stream.Position);
            writer.Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            writer.Flush();
        }
        var xrefOffset = stream.Position;
        writer.Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) writer.Write($"{offset:D10} 00000 n \n");
        writer.Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        writer.Flush();
        File.WriteAllBytes(path, stream.ToArray());
    }

    private static void Capture(Window window, string name)
    {
        Pump(window);
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, name));
        encoder.Save(file);
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected lifecycle failure did not occur.");
    }

    private static bool InvocationThrowsFormatException(MethodInfo method, string value)
    {
        try
        {
            method.Invoke(null, new object[] { value });
            return false;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is FormatException)
        {
            return true;
        }
    }

    private static bool InvocationThrowsIOException(MethodInfo method, object argument)
    {
        try
        {
            method.Invoke(null, new[] { argument });
            return false;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is IOException)
        {
            return true;
        }
    }

    private static bool InvocationThrowsIOException(MethodInfo method, params object[] arguments)
    {
        try
        {
            method.Invoke(null, arguments);
            return false;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is IOException)
        {
            return true;
        }
    }

    private static void CreateTestImage(string path, int width, int height)
    {
        var drawing = new System.Windows.Media.DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void CreateJpegWithMetadata(string sourcePath, string targetPath, string query, string value)
    {
        using var input = File.OpenRead(sourcePath);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery(query, value);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0], null, metadata, null));
        using var output = File.Create(targetPath);
        encoder.Save(output);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }
}

public sealed class FixtureModule : ITweakModule
{
    public string Id => "fixture";
    public string Name => "Test modülü";
    public string Category => "Test";
    public string Description => "Yan etkisiz arayüz ve yaşam döngüsü testi.";
    public string? Warning => null;
    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [
        new() { Key = "toggle", Label = "Test toggle", Default = false },
        new() { Key = "number", Label = "Test number", Kind = SettingKind.Number, Min = 0, Max = 10, Default = 3 },
        new() { Key = "text", Label = "Test text", Kind = SettingKind.Text, Default = "" },
        new() { Key = "choice", Label = "Test choice", Kind = SettingKind.Choice, Options = ["One", "Two"], Default = "One" },
        new()
        {
            Key = "labeled-choice", Label = "Test labeled choice", Kind = SettingKind.Choice,
            Options = ["original", "21996", "Option3", "Option4", "Option5", "Option6", "Option7"],
            OptionLabels = new Dictionary<string, string> { ["original"] = "Keep existing", ["21996"] = "SunValley (Legacy)" },
            Default = "original"
        }
    ];
    public bool IsActive { get; private set; }
    public bool ResourceAllocated { get; private set; }
    public bool FailActivate { get; set; }
    public bool FailDeactivate { get; set; }
    public bool FailApply { get; set; }
    public int ApplyCount { get; private set; }
    public int DeactivateCount { get; private set; }
    public void Initialize(IModuleContext context) { }
    public void Activate()
    {
        ResourceAllocated = true;
        if (FailActivate) throw new InvalidOperationException("Activation failed");
        IsActive = true;
    }
    public void Deactivate()
    {
        DeactivateCount++;
        if (FailDeactivate) throw new InvalidOperationException("Deactivation failed");
        ResourceAllocated = false;
        IsActive = false;
    }
    public void Apply()
    {
        if (FailApply) throw new InvalidOperationException("Apply failed");
        ApplyCount++;
    }
}

internal sealed class FakeHotkeys : IHotkeyService
{
    public List<(uint Modifiers, uint VirtualKey, Action Callback)> RegisteredHotkeys { get; } = new();

    public IDisposable Register(uint modifiers, uint virtualKey, Action callback)
    {
        var hotkey = (modifiers, virtualKey, callback);
        RegisteredHotkeys.Add(hotkey);
        return new Registration(RegisteredHotkeys, hotkey);
    }

    private sealed class Registration(List<(uint Modifiers, uint VirtualKey, Action Callback)> hotkeys,
        (uint Modifiers, uint VirtualKey, Action Callback) hotkey) : IDisposable
    {
        public void Dispose() => hotkeys.Remove(hotkey);
    }
}

