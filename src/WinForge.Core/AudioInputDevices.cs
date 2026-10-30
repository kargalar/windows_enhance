using Microsoft.Win32;

namespace WinForge.Core;

public sealed record AudioInputDevice(string Id, string Name);
public static class AudioInputDevices
{
    public static IReadOnlyList<AudioInputDevice> Enumerate()
    {
        var result = new List<AudioInputDevice> { new("", "Varsayılan mikrofon") };
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture");
        if (root is null) return result;
        foreach (var name in root.GetSubKeyNames())
        {
            using var device = root.OpenSubKey(name);
            if (device?.GetValue("DeviceState") is not int state || state != 1) continue;
            using var properties = device.OpenSubKey("Properties");
            var label = properties?.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},14") as string ?? name;
            result.Add(new("{0.0.1.00000000}." + name, label));
        }
        return result;
    }
}
