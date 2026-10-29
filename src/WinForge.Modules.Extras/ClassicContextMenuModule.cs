using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ClassicContextMenuModule : ITweakModule
{
    private const string ClsidKey =
        @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string InprocKey =
        @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    private IModuleContext? _context;
    private bool _shouldRestartOnNextChange;

    public string Id => "classic-context-menu";
    public string Name => "Klasik Sağ Tık Menüsü";
    public string Category => "Gezgin";
    public string Description => "Windows 11'in kısa sağ tık menüsü yerine Windows 10'un tam klasik menüsünü kullanır.";
    public string? Warning => "Değişikliklerin etkili olması için Windows Gezgini'nin yeniden başlatılması gerekir.";
    public bool IsActive { get; private set; }
    public bool IsPersistent => true;

    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public ClassicContextMenuModule()
    {
        Settings = new SettingDescriptor[]
        {
            new()
            {
                Key = "auto-restart",
                Label = "Değişiklikte Gezgini otomatik yeniden başlat",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Ayar açıldığında veya kapatıldığında değişikliğin hemen geçerli olması için Gezgin'i otomatik yeniden başlatır."
            },
            new()
            {
                Key = "restart-explorer",
                Label = "Windows Gezgini'ni Şimdi Yeniden Başlat",
                Kind = SettingKind.Action,
                Description = "Klasik veya modern menü görünmüyorsa Windows Gezgini'ni (explorer.exe) yeniden başlatır.",
                Execute = () =>
                {
                    RestartExplorer();
                    _context?.Notify("Windows Gezgini yeniden başlatıldı.");
                }
            }
        };
    }

    public void Initialize(IModuleContext context)
    {
        _context = context;
        _shouldRestartOnNextChange = false;
        IsActive = IsClassicConfigured();
    }

    public void Activate()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(InprocKey))
            {
                key?.SetValue("", "", RegistryValueKind.String);
                key?.Flush();
            }

            IsActive = true;
            Reg.Broadcast("ShellState");

            if (_shouldRestartOnNextChange && ShouldAutoRestart())
            {
                RestartExplorer();
                _context?.Notify("Klasik sağ tık menüsü açıldı · Windows Gezgini yeniden başlatıldı.");
            }
            else
            {
                _context?.Notify("Klasik sağ tık menüsü açık · Değişiklik için Gezgin'i yeniden başlatın.");
            }

            _context?.Log.Info("Klasik sağ tık menüsü etkinleştirildi.");
        }
        finally
        {
            _shouldRestartOnNextChange = true;
        }
    }

    public void Deactivate()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ClsidKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Klasik sağ tık menüsü anahtarı silinemedi.", ex);
        }

        IsActive = false;
        Reg.Broadcast("ShellState");

        if (ShouldAutoRestart())
        {
            RestartExplorer();
            _context?.Notify("Klasik sağ tık menüsü kapatıldı · Windows Gezgini yeniden başlatıldı.");
        }
        else
        {
            _context?.Notify("Klasik sağ tık menüsü kapatıldı · Değişiklik için Gezgin'i yeniden başlatın.");
        }

        _context?.Log.Info("Klasik sağ tık menüsü devre dışı bırakıldı.");
    }

    public void Apply()
    {
    }

    private bool ShouldAutoRestart() => _context?.Settings.GetBool("auto-restart", true) ?? true;

    private static bool IsClassicConfigured()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InprocKey);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void RestartExplorer()
    {
        try
        {
            var processes = Process.GetProcessesByName("explorer");
            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        Task.Delay(1000).ContinueWith(_ =>
        {
            try
            {
                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    var explorerPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        "explorer.exe");
                    Process.Start(new ProcessStartInfo(explorerPath) { UseShellExecute = true });
                }
            }
            catch
            {
            }
        });
    }
}
