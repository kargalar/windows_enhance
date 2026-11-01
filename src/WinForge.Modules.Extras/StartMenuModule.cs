using System.Text.Json;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class StartMenuModule : CombinedModule, IModulePanel
{
    private readonly StartAppearanceModule _appearance;
    public StartMenuModule() : this(new StartAppearanceModule()) { }
    private StartMenuModule(StartAppearanceModule appearance) : base(
        new("cursor-monitor-enabled", "Konum", new StartMenuPlacementModule(), false, "Farenin bulunduğu monitörde aç"),
        new("appearance-enabled", "Tasarım", appearance),
        new("cleanup-enabled", "Başlat temizliği", new StartTweaksModule()),
        new("search-enabled", "Arama temizliği", new SearchTweaksModule())) => _appearance = appearance;

    public override string Id => "start-menu";
    public override string Name => "Başlat Menüsü";
    public override string Category => "Başlat";
    public override string Description => "Başlat menüsünün görünümünü, önerilerini ve Windows arama seçeneklerini tek yerden yönetin.";
    public object GetPanelState() => _appearance.GetPanelState();
    public void HandlePanelCommand(string action, JsonElement payload) => _appearance.HandlePanelCommand(action, payload);
    public event Action? PanelStateChanged
    {
        add => _appearance.PanelStateChanged += value;
        remove => _appearance.PanelStateChanged -= value;
    }
}
