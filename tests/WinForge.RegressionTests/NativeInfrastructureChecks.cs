using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using WinForge.Core;
using WinForge.Modules.WindowControl;

namespace WinForge.RegressionTests;

internal static class NativeInfrastructureChecks
{
    internal static void Run(string directory, Action<bool, string> check, bool smoke)
    {
        TestMigration(directory, check);
        TestGeometry(check);
        TestAudio(check);
        var module = new WindowControlModule();
        var context = new Context(directory);
        module.Initialize(context);
        context.Settings.Set("LMB", "Unknown legacy action");
        context.Settings.Set("SnapThreshold", 900);
        context.Settings.Set("SnapGap", -70);
        var options = module.ReadOptions();
        check(options.Actions["LMB"] == WindowAction.Move && options.MagnetDistance == 100 && options.Gap == -70,
            "Native window engine rejects unknown actions and clamps corrupt settings");
        check(!module.IsActive && module.GetType().Assembly.GetManifestResourceNames().Length == 0,
            "Window module initializes without installing or embedding an external engine");
        if (!smoke) return;

        try
        {
            module.Activate();
            check(module.IsActive, "Native window engine installs its Windows mouse hook");
            module.Apply();
        }
        finally { module.Deactivate(); }
        module.Deactivate();
        check(!module.IsActive, "Native window engine deactivation is idempotent and releases the hook");
        using (var management = new WindowsManagement(@"root\cimv2"))
        {
            var processes = management.Query($"SELECT ProcessId, CommandLine FROM Win32_Process WHERE ProcessId = {Environment.ProcessId}", "ProcessId", "CommandLine");
            check(processes.Count == 1 && Convert.ToInt32(processes[0]["ProcessId"]) == Environment.ProcessId &&
                processes[0]["CommandLine"] is string, "Own WMI connection reads live process arguments without a package");
        }
        using var capture = new WasapiLoopbackCapture();
        capture.Start();
        capture.Dispose();
        check(true, "Own WASAPI engine starts against the default output and releases its stream");
    }

    private static void TestMigration(string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "legacy-engine.json");
        File.WriteAllText(path, """
            {"Enabled":["altsnap","windhawk-taskbar","always-on-top"],"Values":{
              "altsnap":{"LMB":"Taşı","Hotkeys":"Sol Alt","SnapThreshold":"37","__native-LMB":"Taşı"},
              "window-control":{"SnapThreshold":"18"},"windhawk-taskbar":{"styler-theme":"DockLike"},
              "always-on-top":{"border-enabled":"true"}}}
            """);
        var store = new SettingsStore(path);
        check(store.Enabled.SetEquals(["window-control", "always-on-top"]) && store.For("window-control")["LMB"] == "Taşı" &&
            store.For("window-control")["SnapThreshold"] == "18" && !store.Values.ContainsKey("altsnap") &&
            !store.Values.ContainsKey("windhawk-taskbar") && !store.For("window-control").ContainsKey("__native-LMB"),
            "Legacy engine settings migrate without overwriting current preferences or enabling retired shell mods");
        store.Save();
        var reloaded = new SettingsStore(path);
        check(reloaded.Enabled.SetEquals(store.Enabled) && reloaded.For("always-on-top")["border-enabled"] == "true",
            "Engine migration survives save/reload and preserves unrelated module settings");
    }

    private static void TestGeometry(Action<bool, string> check)
    {
        var original = new WindowBounds(-1800, 100, 800, 600);
        var start = new POINT(-1500, 300);
        check(WindowGeometry.Drag(original, start, new POINT(-1600, 350), ResizeEdges.None, 200, 100) == new WindowBounds(-1900, 150, 800, 600),
            "Native dragging preserves size and supports monitors with negative coordinates");
        check(WindowGeometry.Drag(original, start, new POINT(500, 2000), ResizeEdges.Left | ResizeEdges.Top, 200, 100) ==
            new WindowBounds(-1200, 600, 200, 100), "Resizing the leading edges enforces app minimums without moving the opposite corner");
        check(WindowGeometry.Drag(original, start, new POINT(500, 2000), ResizeEdges.Right | ResizeEdges.Bottom, 200, 100, 1000, 700) ==
            new WindowBounds(-1800, 100, 1000, 700), "Native resizing respects application maximum tracking dimensions");
        check(WindowGeometry.RestoreUnderPointer(new(-1920, 0, 1920, 1080), new(0, 0, 800, 600), new(-960, 16)) ==
            new WindowBounds(-1360, 0, 800, 600), "Dragging maximized windows restores their normal size under the pointer");
        var work = new WindowBounds(-1920, 0, 1920, 1040);
        check(WindowGeometry.EdgeTarget(work, new POINT(-1919, 1), 8, 50, 50, 4) == new WindowBounds(-1916, 4, 952, 512) &&
            WindowGeometry.EdgeTarget(work, new POINT(-1, 500), 8, 50, 50, 0) == new WindowBounds(-960, 0, 960, 1040) &&
            WindowGeometry.EdgeTarget(work, new POINT(-1000, 500), 8, 50, 50, 0) is null,
            "Screen snapping uses work areas, custom gaps, side halves and corner quarters");
        var moving = new WindowBounds(100, 100, 200, 200);
        var next = new WindowBounds(310, 100, 200, 200);
        check(WindowGeometry.Magnet(moving, [next], 20, 4, false).X == 106 &&
            WindowGeometry.Magnet(moving, [new WindowBounds(310, 500, 200, 200)], 20, 4, false) == moving &&
            WindowGeometry.Magnet(moving, [next], 0, 4, false) == moving,
            "Window magnets honor spacing and ignore non-overlapping or disabled targets");
        check(WindowGeometry.IsExcluded("mstsc.exe,game*.exe", "*Belge*|Editor*", "game64.exe", "", "") &&
            WindowGeometry.IsExcluded("", "*Belge*|Editor*", "edit.exe", "Yeni Belge", "EditorWindow") &&
            !WindowGeometry.IsExcluded("mstsc.exe", "*Belge*|Editor*", "edit.exe", "Yeni Belge", "OtherWindow"),
            "Process and title/class exclusions support case-insensitive wildcards and combined matching");
    }

    private static void TestAudio(Action<bool, string> check)
    {
        Span<float> output = stackalloc float[2];
        var pcm16 = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(pcm16, short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(pcm16.AsSpan(2), short.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(pcm16.AsSpan(4), 16384);
        BinaryPrimitives.WriteInt16LittleEndian(pcm16.AsSpan(6), 16384);
        WasapiLoopbackCapture.DecodeMono(pcm16, output, new(48000, 2, 16, 4, false), false);
        check(Math.Abs(output[0]) < 0.0001 && output[1] == 0.5f, "PCM16 conversion downmixes stereo and preserves signed samples");
        WasapiLoopbackCapture.DecodeMono([0, 0, 0x80, 0xff, 0xff, 0x7f], output, new(48000, 1, 24, 3, false), false);
        check(output[0] == -1 && output[1] > 0.9999f, "PCM24 conversion sign-extends full-scale samples");
        var floats = new[] { float.NaN, float.PositiveInfinity, 0.25f, 0.75f };
        WasapiLoopbackCapture.DecodeMono(MemoryMarshal.AsBytes(floats.AsSpan()), output, new(44100, 2, 32, 8, true), false);
        check(output[0] == 0 && output[1] == 0.5f, "Float audio conversion rejects non-finite values and downmixes channels");
        WasapiLoopbackCapture.DecodeMono([], output, new(48000, 2, 32, 8, true), true);
        check(output[0] == 0 && output[1] == 0, "WASAPI silence never dereferences a null packet");
        var rejected = false;
        try { WasapiLoopbackCapture.DecodeMono([0], output, new(48000, 2, 16, 4, false), false); }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "Truncated audio packets fail before sample access");
        var sine = Enumerable.Range(0, 1024).Select(index => (float)(Math.Sin(2 * Math.PI * 1000 * index / 48000) * 0.1)).ToArray();
        var complete = new AudioSpectrumAnalyzer();
        var chunked = new AudioSpectrumAnalyzer();
        complete.Push(sine, 48000);
        for (var index = 0; index < sine.Length; index += 128) chunked.Push(sine.AsSpan(index, 128), 48000);
        var expected = complete.GetLevels();
        var actual = chunked.GetLevels();
        check(expected.Max() > 0.1 && expected.SequenceEqual(actual), "Spectrum analysis accumulates small WASAPI packets without losing frequency data");
        chunked.Push(sine.AsSpan(0, 512), 44100);
        check(chunked.GetLevels().All(value => value == 0), "Changing endpoint sample rate discards partial frames and stale spectrum levels");
    }

    private sealed class Context(string directory) : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(directory, "native-engine.log"));
        public IHotkeyService Hotkeys { get; } = new FakeHotkeys();
        public void Notify(string message) { }
    }
}
