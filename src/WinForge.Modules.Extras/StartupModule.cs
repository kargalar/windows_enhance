using System.IO;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class StartupModule : ITweakModule
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinForge";

    private IModuleContext? _context;

    public string Id => "startup";
    public string Name => "Başlangıçta Aç";
    public string Category => "Sistem";
    public string Description => "Windows oturum açtığında WinForge'u arka planda (tepside) başlatır.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = Array.Empty<SettingDescriptor>();

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        Reg.WriteString(RunKey, ValueName, $"\"{ExecutablePath()}\" --startup");
        IsActive = true;
        _context?.Log.Info("Başlangıçta açma etkin.");
    }

    public void Deactivate()
    {
        Reg.DeleteValue(RunKey, ValueName);
        IsActive = false;
    }

    public void Apply()
    {
    }

    private static string ExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path) ||
            !Path.GetFileName(path).Equals("WinForge.exe", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(AppContext.BaseDirectory, "WinForge.exe");
        return path;
    }
}
