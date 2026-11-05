using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Controls the Num Lock state Windows initializes for the current user at sign-in.</summary>
public sealed class NumLockStartupModule : ITweakModule
{
    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string ValueName = "InitialKeyboardIndicators";
    private const string On = "2";
    private const string Off = "0";

    private static readonly string[] States = [On, Off];
    private IModuleContext? _context;
    private RegSnapshot? _originalValue;

    public string Id => "numlock-startup";
    public string Name => "Num Lock başlangıç durumu";
    public string Category => "Sistem";
    public string Description => "Windows oturum açarken Num Lock'un açık mı kapalı mı olacağını belirler.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [
        new SettingDescriptor
        {
            Key = "state",
            Label = "Başlangıçta Num Lock",
            Kind = SettingKind.Choice,
            Default = On,
            Options = States,
            OptionLabels = new Dictionary<string, string> { [On] = "Açık", [Off] = "Kapalı" },
            Description = "Değişiklik sonraki Windows oturum açılışında uygulanır."
        }
    ];

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _originalValue = new RegSnapshot(KeyboardKey, ValueName);
        IsActive = true;
        Apply();
    }

    public void Deactivate()
    {
        IsActive = false;
        _originalValue?.Restore();
        _originalValue = null;
    }

    public void Apply()
    {
        if (_context is null) return;
        var state = _context.Settings.GetString("state", On);
        if (state is not (On or Off)) state = On;
        Reg.WriteString(KeyboardKey, ValueName, state);
    }
}
