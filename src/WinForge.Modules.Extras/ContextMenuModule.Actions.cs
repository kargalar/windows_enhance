using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinForge.Modules.Extras;

public sealed partial class ContextMenuModule
{
    private void ApplyPreparedActions(IReadOnlyList<ContextMenuRule> rules)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("WinForge yolu bulunamadı.");
        foreach (var (root, scope) in new[] {
            (@"Software\Classes\*\shell", "file"), (@"Software\Classes\Directory\shell", "folder"),
            (@"Software\Classes\lnkfile\shell", "shortcut"), (@"Software\Classes\InternetShortcut\shell", "shortcut"),
            (@"Software\Classes\exefile\shell", "application") })
        foreach (var action in new[] { "desktop-shortcut", "toggle-hidden" })
        {
            var path = root + @"\WinForge.Prepared." + action;
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
            var rule = rules.FirstOrDefault(r => r.Id == "prepared:" + action);
            if (rule is null || !(scope == "file"
                ? new[] { "file", "shortcut", "application" }.Any(context => ContextMenuCatalog.VisibleIn(rule, context))
                : ContextMenuCatalog.VisibleIn(rule, scope))) continue;
            var entry = _entries.First(e => e.Id == rule.Id);
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("", entry.Name);
            key.SetValue("Icon", rule.Icon.Length > 0 ? rule.Icon : entry.Icon);
            key.SetValue("MultiSelectModel", "Single");
            using var command = key.CreateSubKey("command");
            command.SetValue("", "\"" + executable + "\" --context-" + action + " \"%1\"");
        }
    }

    public bool HandleCommand(string command, IReadOnlyList<string> arguments)
    {
        if (command is not ("--context-desktop-shortcut" or "--context-toggle-hidden")) return false;
        if (!IsActive) throw new InvalidOperationException("Sağ tık menüsü modülü kapalı.");
        var action = command["--context-".Length..];
        var rule = _rules.FirstOrDefault(r => r.Id == "prepared:" + action);
        if (rule is null || !(rule.Contexts?.Values.Any(v => v) ?? rule.Visible))
            throw new InvalidOperationException("Bu menü eylemi kapalı.");
        foreach (var argument in arguments.Take(100))
        {
            var path = Path.GetFullPath(argument);
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Seçili öğe bulunamadı.", path);
            if (action == "toggle-hidden")
                File.SetAttributes(path, File.GetAttributes(path) ^ FileAttributes.Hidden);
            else CreateDesktopShortcut(path);
        }
        Reg.Broadcast("ShellState");
        return true;
    }

    internal static void CreateDesktopShortcut(string path, string? desktopDirectory = null)
    {
        var desktop = desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var baseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        var name = Directory.Exists(path) ? baseName : Path.GetFileNameWithoutExtension(baseName);
        if (name.Length == 0) name = baseName;
        var temporary = Path.Combine(desktop, Guid.NewGuid().ToString("N") + ".lnk");
        object? shell = null, link = null;
        try
        {
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) File.Copy(path, temporary, false);
            else
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                dynamic shortcut = ((dynamic)shell!).CreateShortcut(temporary);
                link = shortcut;
                shortcut.TargetPath = path;
                shortcut.WorkingDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                shortcut.Save();
            }
            for (var index = 1; ; index++)
            {
                var destination = Path.Combine(desktop, name + (index == 1 ? "" : " (" + index + ")") + ".lnk");
                try { File.Move(temporary, destination, false); break; }
                catch (IOException) when (File.Exists(destination)) { }
            }
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
