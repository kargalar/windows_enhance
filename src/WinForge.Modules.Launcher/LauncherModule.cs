using System.IO;
using WinForge.Core;

namespace WinForge.Modules.Launcher;

public sealed class LauncherModule : ITweakModule
{
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private LauncherWindow? _window;

    public string Id => "launcher";
    public string Name => "Hızlı Başlatıcı";
    public string Category => "Verimlilik";
    public string Description => "Başlat menüsündeki uygulamaları klavyeyle arayın ve anında açın.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "hotkey-enabled",
            Label = "Win+Alt+Space kısayolu etkin",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Kısayol, uygulama arama penceresini açar."
        },
        new SettingDescriptor
        {
            Key = "index-roots",
            Label = "Ek arama klasörleri",
            Kind = SettingKind.Text,
            Default = "",
            Description = "Varsayılan klasörlere ek olarak taranacak tam yolları noktalı virgülle ayırın. Ortam değişkenleri (%USERPROFILE% gibi) genişletilir."
        },
        new SettingDescriptor
        {
            Key = "index-extensions",
            Label = "Dosya türü filtresi",
            Kind = SettingKind.Text,
            Default = "",
            Description = "İndekslenecek dosya uzantılarını virgülle yazın (ör. pdf, docx, xlsx). Boş bırakılırsa tüm dosyalar; klasörler ve Başlat menüsü kısayolları her zaman listelenir."
        },
        new SettingDescriptor
        {
            Key = "content-search-enabled",
            Label = "Dosya içeriklerinde ara",
            Kind = SettingKind.Toggle,
            Default = false,
            Description = "Yalnızca ek arama klasörleri ve varsayılan kullanıcı klasörlerinde; desteklenen metin dosyalarının ilk 128 KB bölümünde arar. İçerikler diske kaydedilmez."
        },
        new SettingDescriptor
        {
            Key = "recent-items-enabled",
            Label = "Son kullanılan öğeleri göster",
            Kind = SettingKind.Toggle,
            Default = false,
            Description = "Windows Son Kullanılanlar klasöründeki en yeni 20 dosya kısayolunu başlatıcıda gösterir."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        var quickActions = (_context as IQuickActionContext)?.QuickActions;
        _window = new LauncherWindow(Launch, CopyResult, GetAdditionalRoots(), GetAllowedExtensions(),
            IsContentSearchEnabled(), AreRecentItemsEnabled(), quickActions,
            (_context as IFeatureUiContext)?.FeatureUi);
        RegisterHotkey();
        IsActive = true;
        _context?.Log.Info("Hızlı Başlatıcı etkin.");
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
        _window?.Close();
        _window = null;
    }

    public void Apply()
    {
        if (!IsActive) return;

        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
        _window?.ApplyIndexOptions(GetAdditionalRoots(), GetAllowedExtensions(), IsContentSearchEnabled(), AreRecentItemsEnabled());
    }

    private void RegisterHotkey()
    {
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");
        if (!context.Settings.GetBool("hotkey-enabled", true)) return;

        _hotkey = context.Hotkeys.Register(
            HotkeyModifiers.Win | HotkeyModifiers.Alt,
            0x20,
            () => _window?.Open());
    }

    internal string[] GetAdditionalRoots() => ParseIndexRoots(_context?.Settings.GetString("index-roots") ?? "");

    internal IReadOnlySet<string> GetAllowedExtensions() => ParseIndexExtensions(_context?.Settings.GetString("index-extensions") ?? "");
    internal bool IsContentSearchEnabled() => _context?.Settings.GetBool("content-search-enabled", false) == true;
    internal bool AreRecentItemsEnabled() => _context?.Settings.GetBool("recent-items-enabled", false) == true;

    internal static string[] ParseIndexRoots(string value) => value
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(TryNormalizeIndexRoot)
        .Where(root => root is not null)
        .Select(root => root!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string? TryNormalizeIndexRoot(string value)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            if (expanded.Length == 0) return null;
            var fullPath = Path.GetFullPath(expanded);
            var volumeRoot = Path.GetPathRoot(fullPath);
            return volumeRoot is not null && fullPath.Length > volumeRoot.Length
                ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fullPath;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    internal static IReadOnlySet<string> ParseIndexExtensions(string value)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "*") return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalizedToken = token.StartsWith("*.", StringComparison.Ordinal) ? token[1..] : token;
            var extension = normalizedToken.StartsWith('.') ? normalizedToken : "." + normalizedToken;
            var segments = extension[1..].Split('.');
            if (segments.Length > 0 && segments.All(segment => segment.Length > 0 &&
                    segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
                extensions.Add(extension);
        }
        return extensions;
    }

    private void Launch(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _context?.Log.Error($"Başlatıcı öğesi açılamadı: {path}", exception);
            _context?.Notify("Uygulama açılamadı: " + exception.Message);
        }
    }

    private void CopyResult(string result)
    {
        try
        {
            System.Windows.Clipboard.SetText(result);
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Hesap makinesi sonucu panoya kopyalanamadı.", exception);
            _context?.Notify("Sonuç panoya kopyalanamadı");
        }
    }
}
