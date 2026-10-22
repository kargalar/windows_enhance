using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal static class Reg
{
    public static object? ReadValue(string key, string name)
    {
        using var sub = Registry.CurrentUser.OpenSubKey(key);
        return sub?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public static int? ReadDword(string key, string name) => ReadValue(key, name) as int?;

    public static bool KeyExists(string key)
    {
        using var sub = Registry.CurrentUser.OpenSubKey(key);
        return sub is not null;
    }

    public static void WriteDword(string key, string name, int value)
    {
        using var sub = Registry.CurrentUser.CreateSubKey(key);
        sub?.SetValue(name, value, RegistryValueKind.DWord);
    }

    public static void WriteString(string key, string name, string value)
    {
        using var sub = Registry.CurrentUser.CreateSubKey(key);
        sub?.SetValue(name, value, RegistryValueKind.String);
    }

    public static void DeleteValue(string key, string name)
    {
        using var sub = Registry.CurrentUser.OpenSubKey(key, writable: true);
        sub?.DeleteValue(name, throwOnMissingValue: false);
    }

    public static void DeleteTree(string key) => Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);

    public static void Broadcast(params string[] areas)
    {
        foreach (var area in areas)
        {
            Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE, IntPtr.Zero, area,
                Native.SMTO_ABORTIFHUNG, 1000, out _);
        }
    }
}

internal sealed class RegSnapshot
{
    private readonly string _key;
    private readonly string _name;
    private readonly object? _value;

    public RegSnapshot(string key, string name)
    {
        _key = key;
        _name = name;
        _value = Reg.ReadValue(key, name);
    }

    public void Restore()
    {
        switch (_value)
        {
            case int number:
                Reg.WriteDword(_key, _name, number);
                break;
            case string text:
                Reg.WriteString(_key, _name, text);
                break;
            default:
                Reg.DeleteValue(_key, _name);
                break;
        }
    }
}
