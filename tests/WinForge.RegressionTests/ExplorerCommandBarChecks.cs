using System.Diagnostics;
using System.Reflection;
using System.IO;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class ExplorerCommandBarChecks
{
    internal static void Run(Action<bool, string> check, bool live)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var type = assembly.GetType("WinForge.Modules.Extras.ExplorerCommandBarModule")!;
        var module = (ITweakModule)Activator.CreateInstance(type)!;
        check(module.Category == "Gezgin" && module.Settings.Single().Default is true, "Explorer command bar has a discoverable visibility control");
        var method = type.GetMethod("Rules", BindingFlags.NonPublic | BindingFlags.Static)!;
        var rules = (IReadOnlyList<ShellStyleRule>)method.Invoke(null, [true])!;
        check(rules.Count == 3 && rules.Count(rule => rule.Properties.GetValueOrDefault("Visibility") == "Collapsed") == 2,
            "Both modern command bar variants collapse while the navigation bar spans the reclaimed row");
        check(((IReadOnlyList<ShellStyleRule>)method.Invoke(null, [false])!).Count == 0, "Showing the command bar releases all layout overrides");
        if (!live) return;
        var getProcesses = type.GetMethod("ExplorerProcesses", BindingFlags.NonPublic | BindingFlags.Static)!;
        var processes = (HashSet<uint>)getProcesses.Invoke(null, null)!;
        if (processes.Count == 0) throw new InvalidOperationException("Open a File Explorer window before running the live check.");
        var native = Environment.GetEnvironmentVariable("WINFORGE_NATIVE_DIRECTORY") ?? Path.Combine(AppContext.BaseDirectory, "native");
        var root = Native.FindWindow("CabinetWClass", null);
        Native.GetWindowThreadProcessId(root, out var processId);
        var tab = Native.FindWindowEx(root, 0, "ShellTabWindowClass", null);
        check(tab != 0 && processes.Contains(processId), "Live check targets an existing Explorer file-view host");
        Native.GetWindowRect(tab, out var originalBounds);
        using var session = ShellDiagnosticsSession.ConnectAsync(processId, native, winUi: true).GetAwaiter().GetResult();
        bool IsBar(ShellVisualNode node) => node.Type is "FileExplorerExtensions.CommandBarControl" or "FileExplorerExtensions.CommandBarControl_Wave1";
        Wait(() => session.ReadVisualTree().Any(IsBar), session, "Command bar enumeration");
        var originals = session.ReadVisualTree().Where(IsBar).ToDictionary(node => node.Handle, node => node.Visibility);
        try
        {
            session.Apply(true, rules);
            Wait(() => session.ReadVisualTree().Where(IsBar).All(node => node.Visibility is "Collapsed" or "1"), session, "Command bar collapse");
            check(session.Status.Errors == 0, "The real Explorer command bar collapses without native errors");
            Wait(() => Native.GetWindowRect(tab, out var bounds) && bounds.Top < originalBounds.Top && bounds.Bottom == originalBounds.Bottom,
                session, "File view reclaims the command bar space");
            check(true, "The native file-view host expands upward without moving its bottom edge");
            if (Environment.GetEnvironmentVariable("WINFORGE_EXPLORER_VISUAL_CHECK") == "1") Thread.Sleep(30000);
        }
        finally { session.Apply(true, []); }
        Wait(() => originals.All(original => session.ReadVisualTree().Any(node => node.Handle == original.Key && node.Visibility == original.Value)), session, "Command bar restoration");
        check(session.Status.Errors == 0, "Removing the feature restores the original Explorer visibility");
        Wait(() => Native.GetWindowRect(tab, out var bounds) && bounds.Top == originalBounds.Top && bounds.Bottom == originalBounds.Bottom,
            session, "Native Explorer layout restoration");
        check(true, "Disabling the feature restores the exact file-view bounds");
    }
    private static void Wait(Func<bool> predicate, ShellDiagnosticsSession session, string operation)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (session.Status.Errors > 0) throw new InvalidOperationException(session.Status.LastErrorMessage + " " + session.Status.LastError);
            if (predicate()) return;
            Thread.Sleep(50);
        }
        throw new TimeoutException(operation);
    }
}
