using System.IO;
using System.Text.Json;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class CombinedModuleChecks
{
    internal static void Run(string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "combined-modules.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Enabled = new[] { "start-tweaks", "search-tweaks", "mouse-spotlight", "mouse-jump", "peek-preview" },
            Values = new Dictionary<string, Dictionary<string, string>>
            {
                ["start-appearance"] = new() { ["width"] = "650", ["hide-labels"] = "true" },
                ["start-tweaks"] = new() { ["recent"] = "false" },
                ["search-tweaks"] = new() { ["web"] = "false" },
                ["mouse-tweaks"] = new() { ["scroll-lines"] = "7" },
                ["mouse-spotlight"] = new() { ["click-color-left"] = "Mor" },
                ["peek-preview"] = new() { ["hotkey-enabled"] = "true" },
                ["mouse-jump"] = new() { ["shortcut"] = "Win+Alt+J" }
            }
        }));
        var store = new SettingsStore(path);
        check(store.Enabled.SetEquals(["start-menu", "mouse"]), "Legacy enabled modules migrate to the two combined modules");
        check(!store.Enabled.Contains("peek-preview") && !store.Values.ContainsKey("peek-preview"), "Removed file preview settings are discarded during migration");
        var start = store.For("start-menu");
        var mouse = store.For("mouse");
        check(start["appearance-enabled"] == "false" && start["cleanup-enabled"] == "true" && start["search-enabled"] == "true",
            "Start migration preserves the three independent enabled states");
        check(start["width"] == "650" && start["hide-labels"] == "true" && start["recent"] == "false" && start["web"] == "false",
            "Start appearance, cleanup and search values survive migration");
        check(mouse["tweaks-enabled"] == "false" && mouse["spotlight-enabled"] == "true" && mouse["scroll-lines"] == "7" && mouse["click-color-left"] == "Mor",
            "Mouse settings and independent enabled states survive migration");
        check(!store.Values.ContainsKey("mouse-jump") && !store.Values.ContainsKey("start-tweaks"), "Retired settings are removed");
        store.Save();
        var reloaded = new SettingsStore(path);
        check(reloaded.Enabled.SetEquals(store.Enabled) && reloaded.For("start-menu")["width"] == "650",
            "Combined migration is stable after save and reload");
        reloaded.Enabled.Remove("mouse");
        reloaded.For("mouse")["scroll-lines"] = "4";
        reloaded.Enabled.Add("mouse-tweaks");
        reloaded.For("mouse-tweaks")["scroll-lines"] = "9";
        reloaded.Save();
        reloaded = new SettingsStore(path);
        check(!reloaded.Enabled.Contains("mouse") && reloaded.For("mouse")["scroll-lines"] == "4",
            "Repeated migration preserves newer combined settings and disabled state");

        var logger = new FileLogger(Path.Combine(directory, "combined-modules.log"));
        var host = new ModuleHost(reloaded, logger, new NoHotkeys(), Path.Combine(AppContext.BaseDirectory, "modules"), _ => { });
        host.Load();
        check(host.Modules.All(module => module.Id != "square-corners" && module.Id != "dynamic-island"),
            "Removed window corners and Dynamic Island modules are absent from discovery");
        check(host.Modules.Count(module => module.Id == "start-menu") == 1 && host.Modules.Count(module => module.Id == "mouse") == 1,
            "Discovery exposes one Start Menu module and one Mouse module");
        check(!host.Modules.Any(module => new[] { "start-appearance", "start-tweaks", "search-tweaks", "mouse-tweaks", "mouse-spotlight", "mouse-jump", "peek-preview" }.Contains(module.Id)),
            "Component modules and removed mouse jump are absent from discovery");
        foreach (var id in new[] { "start-menu", "mouse" })
        {
            var module = host.Modules.Single(module => module.Id == id);
            check(module.Settings.Select(setting => setting.Key).Distinct().Count() == module.Settings.Count,
                id + " exposes all settings without key collisions");
            check(module.Settings.All(setting => !string.IsNullOrWhiteSpace(setting.Section)), id + " organizes settings into named sections");
            foreach (var setting in module.Settings.Where(setting => setting.Key.EndsWith("-enabled") && !setting.Key.StartsWith("crosshairs-")))
                host.SettingsOf(module).Set(setting.Key, false);
            host.SetEnabled(module, true);
            check(module.IsActive && host.ErrorOf(module) is null, id + " activates with feature groups independently disabled");
            host.Apply(module);
            host.SetEnabled(module, false);
            check(!module.IsActive && host.ErrorOf(module) is null, id + " applies and disables cleanly");
        }
        var startModule = host.Modules.Single(module => module.Id == "start-menu");
        var placement = startModule.Settings.Single(setting => setting.Key == "cursor-monitor-enabled");
        check(placement.Kind == SettingKind.Toggle && placement.Default is false && placement.Section == "Konum",
            "Start cursor-monitor placement is an independent opt-in setting");
        check(JsonSerializer.SerializeToElement(((IModulePanel)startModule).GetPanelState()).GetProperty("kind").GetString() == "start-sections",
            "Combined Start Menu retains its interactive section panel");
        StartMenuPlacementChecks.Run(check);
    }

    private sealed class NoHotkeys : IHotkeyService
    {
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback) => throw new InvalidOperationException("Disabled features must not register hotkeys.");
    }
}
