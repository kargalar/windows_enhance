using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class AwakeModule : ITweakModule
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    private static readonly string[] DurationOptions = { "Sonsuz", "30 dakika", "1 saat", "2 saat", "4 saat" };

    private IModuleContext? _context;
    private DispatcherTimer? _timer;

    public string Id => "awake";
    public string Name => "Uyanık Kal";
    public string Category => "Sistem";
    public string Description => "Modül açıkken bilgisayarın uykuya geçmesini engeller; ekranı da açık tutabilir.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "keep-display",
            Label = "Ekranı da açık tut",
            Kind = SettingKind.Toggle,
            Default = false,
            Description = "Kapalıyken yalnızca sistem uykusu engellenir; ekran normal süre sonunda kapanabilir."
        },
        new SettingDescriptor
        {
            Key = "duration",
            Label = "Süre",
            Kind = SettingKind.Choice,
            Options = DurationOptions,
            Default = "Sonsuz",
            Description = "Süre dolunca uyku engeli kalkar, modül açık kalır."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        Apply();
        _context?.Log.Info("Uyanık Kal etkin.");
    }

    public void Deactivate()
    {
        IsActive = false;
        _timer?.Stop();
        _timer = null;
        SetThreadExecutionState(EsContinuous);
    }

    public void Apply()
    {
        if (!IsActive) return;

        var settings = _context!.Settings;
        var flags = EsContinuous | EsSystemRequired;
        if (settings.GetBool("keep-display")) flags |= EsDisplayRequired;
        SetThreadExecutionState(flags);

        _timer?.Stop();
        _timer = null;

        var minutes = DurationMinutes(settings.GetString("duration", DurationOptions[0]));
        if (minutes <= 0) return;

        // Execution state belongs to the calling thread; expiry must run on
        // the same dispatcher as activation, rather than on a thread-pool thread.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
        _timer.Tick += (_, _) => Expire();
        _timer.Start();
    }

    private void Expire()
    {
        _timer?.Stop();
        _timer = null;
        if (!IsActive) return;
        SetThreadExecutionState(EsContinuous);
        _context?.Notify("Uyanık Kal süresi doldu");
        _context?.Log.Info("Uyanık Kal süresi doldu.");
    }

    private static int DurationMinutes(string value) => value switch
    {
        "30 dakika" => 30,
        "1 saat" => 60,
        "2 saat" => 120,
        "4 saat" => 240,
        _ => 0
    };

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
