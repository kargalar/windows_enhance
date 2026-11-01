namespace WinForge.Modules.Extras;

public sealed class MouseModule : CombinedModule
{
    public MouseModule() : base(
        new("tweaks-enabled", "Fare ayarları", new MouseTweaksModule()),
        new("spotlight-enabled", "Fare işaretçisi", new MouseSpotlightModule())) { }

    public override string Id => "mouse";
    public override string Name => "Fare";
    public override string Category => "Fare";
    public override string Description => "Fare hızlandırmasını, kaydırma ve çift tık hızını, işaretçi vurgularını ve imleç artısını tek yerden yönetin.";
}
