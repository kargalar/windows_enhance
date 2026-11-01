using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace WinForge.RegressionTests;

internal static class DisplayBrightnessChecks
{
    internal static void Run(Assembly extras, Action<bool, string> check)
    {
        var sessionType = extras.GetType("WinForge.Modules.Extras.DisplayBrightnessSession")!;
        var monitorType = extras.GetType("WinForge.Modules.Extras.DisplayBrightnessMonitor")!;
        var physicalType = sessionType.GetNestedType("PhysicalMonitor", BindingFlags.NonPublic)!;
        var panelType = extras.GetType("WinForge.Modules.Extras.InternalPanelBrightness")!;
        object Monitor(string id, uint current) => monitorType.GetConstructors().Single().Invoke(
            [id, id, 0u, 100u, current, true, null, null, null, null, null, false]);
        object Physical(object info, uint current) => physicalType.GetConstructors().Single().Invoke(
            [info, IntPtr.Zero, 0u, 100u, current, null, null, null, null, null]);
        var second = Physical(Monitor(@"\\.\DISPLAY8:0", 63), 63);
        var first = Physical(Monitor(@"\\.\DISPLAY2:0", 41), 41);
        var physicalList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(physicalType))!;
        physicalList.Add(second);
        physicalList.Add(first);
        var panels = Array.CreateInstance(panelType, 1);
        panels.SetValue(panelType.GetConstructors().Single().Invoke(["internal:fixture", "Internal fixture", (byte)40, new byte[] { 20, 40, 100 }]), 0);
        var session = sessionType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke([physicalList, panels]);
        var monitors = sessionType.GetProperty("Monitors")!.GetValue(session)!;
        var snapshots = ((IEnumerable)monitors).Cast<object>().ToArray();
        string Id(object monitor) => (string)monitorType.GetProperty("Id")!.GetValue(monitor)!;
        check(Id(snapshots[0]) == @"\\.\DISPLAY8:0" && Id(snapshots[1]) == @"\\.\DISPLAY2:0",
            "Live monitor snapshots preserve device-scoped physical IDs instead of replacing them with enumeration indices");
        var find = sessionType.GetMethod("FindMonitorsForDevice", BindingFlags.Static | BindingFlags.NonPublic)!;
        var matching = ((IEnumerable)find.Invoke(null, [monitors, @"\\.\DISPLAY2"])!).Cast<object>().ToArray();
        check(matching.Length == 1 && Id(matching[0]) == @"\\.\DISPLAY2:0",
            "Cursor brightness selection resolves the actual snapshot ID for exactly the intended display device");
        var lookup = sessionType.GetMethod("FindPhysicalMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        check(ReferenceEquals(lookup.Invoke(session, [@"\\.\DISPLAY2:0"]), first) &&
              lookup.Invoke(session, ["1"]) is null && lookup.Invoke(session, [@"\\.\DISPLAY9:0"]) is null,
            "Hardware setters resolve stable device IDs after enumeration order changes and reject unrelated or obsolete numeric IDs");
        var internalPanel = snapshots.Single(monitor => Id(monitor).StartsWith("internal:", StringComparison.Ordinal));
        check((uint)monitorType.GetProperty("Minimum")!.GetValue(internalPanel)! == 20 &&
              (uint)monitorType.GetProperty("Maximum")!.GetValue(internalPanel)! == 100 &&
              (int)monitorType.GetProperty("Percent")!.GetValue(internalPanel)! == 25,
            "Internal-panel readback uses its actual supported brightness-level range consistently with percentage writes");
        var read = sessionType.GetMethod("GetVCPFeatureAndVCPFeatureReply", BindingFlags.Static | BindingFlags.NonPublic)!;
        check(read.GetParameters()[2].ParameterType == typeof(uint).MakeByRefType(),
            "Native DDC/CI code-type output reserves the full 32-bit MC_VCP_CODE_TYPE enum storage");
        var apply = sessionType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "ApplyBrightnessToMonitors");
        var requests = new List<(string Id, int Percent)>();
        Func<string, int, int?> setter = (id, percent) => { requests.Add((id, percent)); return percent; };
        object?[] InvokeArgs(int? percent, int delta, Func<string, int, int?> write) => [monitors, percent, delta, write];
        var outcome = ((int Supported, int Applied, int Failed, int? Brightness))apply.Invoke(null, InvokeArgs(null, -5, setter))!;
        check(outcome == (3, 3, 0, 38) && requests.Select(item => item.Percent).Distinct().Single() == 38 &&
              requests.Select(item => item.Id).Distinct().Count() == 3,
            "Shared brightness applies one target to every external and internal display with different starting levels");
        requests.Clear();
        outcome = ((int, int, int, int?))apply.Invoke(null, InvokeArgs(120, 0, setter))!;
        check(outcome == (3, 3, 0, 100) && requests.All(item => item.Percent == 100),
            "Overlay brightness clamps and applies the absolute value to every monitor");
        outcome = ((int, int, int, int?))apply.Invoke(null, InvokeArgs(45, 0,
            (id, percent) => id == @"\\.\DISPLAY8:0" ? throw new InvalidOperationException("fixture") : percent))!;
        check(outcome == (3, 2, 1, 45),
            "A failed monitor does not stop siblings and the displayed success count excludes failures");
        outcome = ((int, int, int, int?))apply.Invoke(null, InvokeArgs(45, 0, (_, _) => null))!;
        check(outcome == (3, 0, 3, null), "All failed writes report zero applied monitors and no successful brightness");
        // Fixture handles are zero and were never acquired from Windows; no physical resources need disposal.
    }

    internal static void ReadConnected(Action<bool, string> check)
    {
        var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var type = extras.GetType("WinForge.Modules.Extras.DisplayBrightnessSession")!;
        using var session = (IDisposable)type.GetMethod("Discover")!.Invoke(null, null)!;
        var monitors = ((IEnumerable)type.GetProperty("Monitors")!.GetValue(session)!).Cast<object>().ToArray();
        Console.WriteLine("Read-only connected display data (no hardware controls were written):");
        Console.WriteLine(JsonSerializer.Serialize(monitors, new JsonSerializerOptions { WriteIndented = true }));
        check(monitors.Length > 0, "Own Windows APIs return at least one connected monitor with successful native control readback");
        foreach (var monitor in monitors)
        {
            var model = monitor.GetType();
            var id = (string)model.GetProperty("Id")!.GetValue(monitor)!;
            if ((bool)model.GetProperty("SupportsBrightness")!.GetValue(monitor)!)
            {
                var minimum = (uint)model.GetProperty("Minimum")!.GetValue(monitor)!;
                var maximum = (uint)model.GetProperty("Maximum")!.GetValue(monitor)!;
                var current = (uint)model.GetProperty("Current")!.GetValue(monitor)!;
                check(maximum > minimum && current >= minimum && current <= maximum,
                    $"Connected display {id} returns a real brightness value inside its native supported range");
            }
        }
    }
}
