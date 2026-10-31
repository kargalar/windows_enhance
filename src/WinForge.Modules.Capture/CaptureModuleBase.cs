using System.Windows;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

public abstract class CaptureModuleBase : ITweakModule, IQuickActionProvider
{
    protected IModuleContext Context { get; private set; } = null!;
    private Window? window;
    public abstract string Id { get; }
    public abstract string Name { get; }
    public string Category => "Verimlilik";
    public abstract string Description { get; }
    public string? Warning => null;
    public bool IsActive { get; protected set; }
    protected abstract string Kind { get; }
    protected HistoryStore Store => new(PortableHistoryMigration.DirectoryFor(AppData.DirectoryPath,
        Context.Settings.GetString(PortableHistoryMigration.StorageIdKey, "")));
    protected virtual IEnumerable<SettingDescriptor> ModuleSettings => [];
    public IReadOnlyList<SettingDescriptor> Settings => new[]
    {
        new SettingDescriptor { Key = "open", Label = "Geçmişi aç", Kind = SettingKind.Action, Execute = Open },
        new SettingDescriptor { Key = "import", Label = "Eski kayıtları içe aktar", Kind = SettingKind.Action,
            Execute = () => new ImportWindow(Store, Kind, Context, (Context as IFeatureUiContext)?.FeatureUi).Show() }
    }.Where(setting => Kind != "capture" || setting.Key != "import").Concat(ModuleSettings).ToArray();
    public void Initialize(IModuleContext context) => Context = context;
    public virtual void Activate() => IsActive = true;
    public virtual void Deactivate() { IsActive = false; window?.Close(); window = null; }
    public virtual void Apply() { }
    protected virtual Window CreateWindow() => new HistoryWindow(Store, Kind, Name,
        (Context as IFeatureUiContext)?.FeatureUi);
    public void Open()
    {
        try
        {
            if (window is null) { window = CreateWindow(); window.Closed += (_, _) => window = null; }
            window.Show(); window.Activate();
        }
        catch (Exception error) { Context.Log.Error(Name, error); Context.Notify(error.Message); }
    }
    public virtual IEnumerable<QuickAction> GetQuickActions(string query) =>
        [new(Id + ".open", Name, Name + " geçmiş kayıt", Open)];
    protected void Report(Exception error) { Context.Log.Error(Name, error); Context.Notify(error.Message); }
}
