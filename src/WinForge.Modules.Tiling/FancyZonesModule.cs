namespace WinForge.Modules.Tiling;

/// <summary>Independent FancyZones-style drag-to-zone placement feature.</summary>
public sealed class FancyZonesModule : TilingModule
{
    public FancyZonesModule() : base(1) { }

    public override string Id => "fancy-zones";
    public override string Name => "FancyZones · Bölgeli Pencere Taşıma";
    public override string Description => "Pencereleri sürükleyerek ekrandaki bölgelere yerleştirin ve kenarlara yaklaşarak alanları birleştirin.";
    public override string? Warning => null;
}
