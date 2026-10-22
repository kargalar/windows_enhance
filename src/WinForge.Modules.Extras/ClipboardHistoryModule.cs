using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ClipboardHistoryModule : ITweakModule
{
    private const string ClipboardKey = @"Software\Microsoft\Clipboard";

    private IModuleContext? _context;
    private RegSnapshot? _original;
    private RegSnapshot? _syncOriginal;

    public string Id => "clipboard-history";
    public string Name => "Pano Geçmişi";
    public string Category => "Sistem";
    public string Description => "Windows pano geçmişini açar; Win+V ile son kopyalananları kullanabilirsin.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "sync",
            Label = "Cihazlar arası panoyu eşitle",
            Kind = SettingKind.Toggle,
            Default = false,
            Description = "Pano içeriğini Microsoft hesabınla diğer cihazlara gönderir."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _original = new RegSnapshot(ClipboardKey, "EnableClipboardHistory");
        _syncOriginal = new RegSnapshot(ClipboardKey, "CloudClipboardAutomaticUpload");
        Reg.WriteDword(ClipboardKey, "EnableClipboardHistory", 1);

        IsActive = true;
        Apply();
        _context?.Notify("Pano geçmişi açık · Win+V");
        _context?.Log.Info("Pano geçmişi etkin.");
    }

    public void Deactivate()
    {
        _original?.Restore();
        _syncOriginal?.Restore();
        _original = null;
        _syncOriginal = null;

        IsActive = false;
        _context?.Notify("Pano geçmişi kapatıldı");
    }

    public void Apply()
    {
        if (!IsActive) return;

        Reg.WriteDword(ClipboardKey, "CloudClipboardAutomaticUpload",
            _context!.Settings.GetBool("sync") ? 1 : 0);
    }
}
