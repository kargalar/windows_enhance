using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinForge.App;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class ModuleFixChecks
{
    internal static void Run(string directory, Action<bool, string> check)
    {
        var settingsPath = Path.Combine(directory, "retired-keyboard.json");
        File.WriteAllText(settingsPath, """{"Enabled":["keyboard-manager","combination-translator"],"Values":{"keyboard-manager":{"hook-enabled":"true"}}}""");
        var settings = new SettingsStore(settingsPath);
        check(!settings.Enabled.Contains("keyboard-manager") && !settings.Values.ContainsKey("keyboard-manager") &&
            settings.Enabled.Contains("combination-translator"), "Retired keyboard settings do not disable the combination translator");

        var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var ruleType = extras.GetType("WinForge.Modules.Extras.ShortcutActionRule")!;
        var step = extras.GetType("WinForge.Modules.Extras.ShortcutActionModule")!.GetMethod("VolumeStep", BindingFlags.NonPublic | BindingFlags.Static)!;
        object Rule(string action, string value) => JsonSerializer.Deserialize(JsonSerializer.Serialize(new
            { Id = "test", Name = "Test", Enabled = false, Shortcut = "F24", Action = action, Value = value, Arguments = "" }), ruleType)!;
        check((int)step.Invoke(null, [Rule("volume-up", "17")])! == 17 &&
            (int)step.Invoke(null, [Rule("volume-down", "9")])! == 9,
            "Increase and decrease shortcuts accept independent volume amounts");
        check((int)step.Invoke(null, [Rule("volume-up", "")])! == 2 &&
            (int)step.Invoke(null, [Rule("volume-down-5", "")])! == 5,
            "Existing volume shortcuts preserve their original amounts");
        foreach (var value in new[] { "0", "101", "oops" })
        {
            var rejected = false;
            try { step.Invoke(null, [Rule("volume-down", value)]); }
            catch (TargetInvocationException error) when (error.InnerException is FormatException) { rejected = true; }
            check(rejected, "Invalid volume amount is rejected: " + value);
        }

        var selector = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.TextGrabber.dll"))
            .GetType("WinForge.Modules.TextGrabber.SelectionOverlay")!;
        var window = (Window)Activator.CreateInstance(selector, [null])!;
        try
        {
            window.Show();
            check(window.Content is System.Windows.Controls.Canvas, "Text selection is ready without a WebView or feature UI service");
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            check(!window.IsVisible && selector.GetProperty("Result")!.GetValue(window) is null,
                "Escape closes text selection without depending on web focus");
        }
        finally { window.Close(); }

        var serviceType = typeof(OsdWindow).Assembly.GetType("WinForge.App.ReactWindowHostService")!;
        using var lifetime = (IDisposable)Activator.CreateInstance(serviceType, nonPublic: true)!;
        var ui = (IFeatureUiService)lifetime;
        var panel = ui.Open("interactive-hit-test", "system-osd", "Interactive overlay test", new(240, 60,
            Topmost: true, ActivateOnShow: false, Transparent: true));
        check(((Window)panel).Background is SolidColorBrush { Color.A: > 0 } && ((Window)panel).IsHitTestVisible,
            "Interactive transparent overlays have native hit-test pixels for WebView slider and button input");
        panel.Close();
    }

}
