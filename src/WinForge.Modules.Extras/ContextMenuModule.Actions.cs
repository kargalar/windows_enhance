using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinForge.Modules.Extras;

public sealed partial class ContextMenuModule
{
    internal void ApplyPreparedActions(IReadOnlyList<ContextMenuRule> rules, string classesRoot = @"Software\Classes")
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("WinForge yolu bulunamadı.");
        foreach (var (root, scope) in new[] {
            (@"*\shell", "file"), (@"Directory\shell", "folder"),
            (@"lnkfile\shell", "shortcut"), (@"InternetShortcut\shell", "shortcut"),
            (@"exefile\shell", "application"), (@"Directory\Background\shell", "background"),
            (@"DesktopBackground\Shell", "desktop"), (@"Drive\shell", "drive") })
        foreach (var action in new[] { "desktop-shortcut", "toggle-hidden", "folder-icon", "hidden-items", "power-mode" })
        {
            var path = classesRoot + @"\" + root + @"\WinForge.Prepared." + action;
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
            if (action == "folder-icon" && scope != "folder") continue;
            if (action is "desktop-shortcut" or "toggle-hidden" && scope is "background" or "desktop" or "drive") continue;
            var rule = rules.FirstOrDefault(r => r.Id == "prepared:" + action);
            if (rule is null || !(scope == "file"
                ? new[] { "file", "shortcut", "application" }.Any(context => ContextMenuCatalog.VisibleIn(rule, context))
                : ContextMenuCatalog.VisibleIn(rule, scope))) continue;
            var entry = _entries.First(e => e.Id == rule.Id);
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("", entry.Name);
            key.SetValue("Icon", rule.Icon.Length > 0 ? rule.Icon : entry.Icon);
            key.SetValue("MultiSelectModel", "Single");
            if (action == "power-mode")
            {
                key.SetValue("MUIVerb", entry.Name);
                key.SetValue("SubCommands", "");
                foreach (var (mode, label) in ContextMenuQuickSettings.PowerModes)
                {
                    using var child = key.CreateSubKey(@"shell\" + mode);
                    child.SetValue("MUIVerb", label);
                    using var modeCommand = child.CreateSubKey("command");
                    modeCommand.SetValue("", "\"" + executable + "\" --context-power-mode " + mode);
                }
                continue;
            }
            using var command = key.CreateSubKey("command");
            command.SetValue("", "\"" + executable + "\" --context-" + action + (action == "hidden-items" ? " toggle" : " \"%1\""));
        }
    }

    public bool HandleCommand(string command, IReadOnlyList<string> arguments)
    {
        if (command is not ("--context-desktop-shortcut" or "--context-toggle-hidden" or "--context-folder-icon" or "--context-hidden-items" or "--context-power-mode")) return false;
        if (!IsActive) throw new InvalidOperationException("Sağ tık menüsü modülü kapalı.");
        var action = command["--context-".Length..];
        var rule = _rules.FirstOrDefault(r => r.Id == "prepared:" + action);
        if (rule is null || !(rule.Contexts?.Values.Any(v => v) ?? rule.Visible))
            throw new InvalidOperationException("Bu menü eylemi kapalı.");
        if (action == "hidden-items")
        {
            if (arguments.Count != 1 || arguments[0] != "toggle") throw new ArgumentException("Geçersiz gizli öğeler komutu.");
            ContextMenuQuickSettings.ToggleHiddenItems();
            return true;
        }
        if (action == "power-mode")
        {
            if (arguments.Count != 1) throw new ArgumentException("Bir güç modu seçin.");
            ContextMenuQuickSettings.SetPowerMode(arguments[0]);
            _context?.Notify("Güç modu: " + ContextMenuQuickSettings.PowerModes.Single(mode => mode.Id == arguments[0]).Label);
            return true;
        }
        if (action == "folder-icon")
        {
            if (!ContextMenuCatalog.VisibleIn(rule, "folder")) throw new InvalidOperationException("Bu menü eylemi klasörler için kapalı.");
            if (arguments.Count != 1) throw new ArgumentException("Simge değiştirmek için tek bir klasör seçin.");
            FolderIconPicker.Show(arguments[0]);
            return true;
        }
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
