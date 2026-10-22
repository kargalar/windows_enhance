using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ClassicContextMenuModule : ITweakModule
{
    private const string ContextKey =
        @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    private IModuleContext? _context;
    private bool _existed;
    private string? _original;

    public string Id => "classic-context-menu";
    public string Name => "Klasik Sağ Tık Menüsü";
    public string Category => "Gezgin";
    public string Description => "Windows 11'in kısa sağ tık menüsü yerine Windows 10'un tam menüsünü kullanır.";
    public string? Warning => "Değişiklik Explorer yeniden başlatılınca görünür. Kapatınca önceki ayar geri gelir.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings => Array.Empty<SettingDescriptor>();

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _existed = Reg.KeyExists(ContextKey);
        _original = _existed ? Reg.ReadValue(ContextKey, "") as string : null;

        Reg.WriteString(ContextKey, "", "");
        IsActive = true;
        Reg.Broadcast("ShellState");

        _context?.Notify("Klasik sağ tık menüsü açık · Explorer'ı yeniden başlatın");
        _context?.Log.Info("Klasik sağ tık menüsü etkin.");
    }

    public void Deactivate()
    {
        if (_existed) Reg.WriteString(ContextKey, "", _original ?? "");
        else Reg.DeleteTree(ContextKey);

        IsActive = false;
        Reg.Broadcast("ShellState");
        _context?.Notify("Klasik sağ tık menüsü kapatıldı · Explorer'ı yeniden başlatın");
    }

    public void Apply()
    {
    }
}
