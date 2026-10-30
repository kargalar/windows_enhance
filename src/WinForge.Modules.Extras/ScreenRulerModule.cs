using System.Windows;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ScreenRulerModule : ITweakModule
{
    private static readonly string[] Units = { "Piksel", "Santimetre", "İnç" };

    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private bool _busy;

    public string Id => "screen-ruler";
    public string Name => "Ekran Cetveli";
    public string Category => "Verimlilik";
    public string Description => "Ctrl+Alt+M ile ekranda bir alan seç, piksel ölçüsünü panoya kopyala.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "hotkey-enabled",
            Label = "Ctrl+Alt+M kısayolu etkin",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "unit",
            Label = "Ölçü birimi",
            Kind = SettingKind.Choice,
            Options = Units,
            Default = Units[0]
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        RegisterHotkey();
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
    }

    public void Apply()
    {
        if (!IsActive) return;

        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");
        if (!context.Settings.GetBool("hotkey-enabled", true)) return;

        _hotkey = context.Hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4D /* M */, Measure);
        context.Log.Info("Ekran cetveli kısayolu kaydedildi (Ctrl+Alt+M).");
    }

    private async void Measure()
    {
        var context = _context;
        if (context is null || _busy) return;

        _busy = true;
        try
        {
            var featureUi = (context as IFeatureUiContext)?.FeatureUi
                ?? throw new InvalidOperationException("React arayüz altyapısı kullanılamıyor.");
            using var overlay = new RulerOverlay(featureUi, context.Settings.GetString("unit", Units[0]));
            if (await overlay.Completion is not { } result) return;

            if (!TryCopy(result))
            {
                context.Notify(result);
                return;
            }

            context.Notify($"{result} · panoya kopyalandı");
        }
        catch (Exception ex)
        {
            context.Log.Error("Ekran cetveli açılamadı.", ex);
            context.Notify("Ekran cetveli açılamadı");
        }
        finally
        {
            _busy = false;
        }
    }

    private static bool TryCopy(string text)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }
}
