using WinForge.Core;

internal static class BenchmarkMachineInfo
{
    internal static object Read()
    {
        object hardware;
        try
        {
            using var management = new WindowsManagement(@"root\cimv2");
            var processors = management.Query("SELECT Name FROM Win32_Processor", "Name");
            var memory = management.Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "TotalPhysicalMemory");
            hardware = new { processors = processors.Select(row => Convert.ToString(row["Name"])).ToArray(),
                physicalMemoryBytes = Convert.ToUInt64(memory.First()["TotalPhysicalMemory"]) };
        }
        catch (Exception error) { hardware = new { error = error.Message }; }
        var displays = DesktopDisplays.Enumerate().Select(display => new
        {
            display.DeviceName, display.IsPrimary,
            bounds = new { display.Bounds.Left, display.Bounds.Top, display.Bounds.Right, display.Bounds.Bottom }
        }).ToArray();
        return new { windows = Environment.OSVersion.VersionString, runtime = Environment.Version.ToString(),
            logicalProcessors = Environment.ProcessorCount, hardware, displays };
    }
}
