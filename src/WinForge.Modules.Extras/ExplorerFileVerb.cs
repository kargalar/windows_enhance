using System.IO;
using Microsoft.Win32;

namespace WinForge.Modules.Extras;

internal sealed class ExplorerFileVerb(string verbName, string label, string argumentTemplate, bool multiSelect = false)
{
    private const string ShellRoot = @"Software\Classes\*\shell";
    private string? _registeredCommand;

    public bool Install()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            !Path.GetFileName(executable).Equals("WinForge.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        var keyPath = $@"{ShellRoot}\{verbName}";
        var command = $"\"{executable}\" {argumentTemplate}";
        using (var existing = Registry.CurrentUser.OpenSubKey(keyPath))
        {
            if (existing is not null)
            {
                using var existingCommandKey = existing.OpenSubKey("command");
                if (!string.Equals(existingCommandKey?.GetValue(null) as string, command, StringComparison.Ordinal))
                    return false;
            }
        }

        using var verb = Registry.CurrentUser.CreateSubKey(keyPath)
            ?? throw new IOException("Gezgin komut menüsü kaydı oluşturulamadı.");
        verb.SetValue(null, label, RegistryValueKind.String);
        verb.SetValue("Icon", executable, RegistryValueKind.String);
        if (multiSelect) verb.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);
        using var commandKey = verb.CreateSubKey("command")
            ?? throw new IOException("Gezgin komut satırı kaydı oluşturulamadı.");
        commandKey.SetValue(null, command, RegistryValueKind.String);
        _registeredCommand = command;
        return true;
    }

    public void Uninstall()
    {
        if (_registeredCommand is null) return;

        var keyPath = $@"{ShellRoot}\{verbName}";
        using (var verb = Registry.CurrentUser.OpenSubKey(keyPath))
        using (var commandKey = verb?.OpenSubKey("command"))
        {
            var currentCommand = commandKey?.GetValue(null) as string;
            if (!string.Equals(currentCommand, _registeredCommand, StringComparison.Ordinal))
            {
                _registeredCommand = null;
                return;
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        _registeredCommand = null;
    }
}
