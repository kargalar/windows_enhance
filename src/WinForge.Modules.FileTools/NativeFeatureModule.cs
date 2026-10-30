using System.Windows;
using System.Windows.Input;
using WinForge.Core;

namespace WinForge.FileTools;

public abstract class NativeFeatureModule : ITweakModule, IQuickActionProvider
{
    private IDisposable? _hotkey;
    private Window? _window;
    protected IModuleContext Context { get; private set; } = null!;
    protected abstract uint ShortcutKey { get; }
    protected abstract Window CreateWindow();
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public string Category => "Verimlilik";
    public virtual string? Warning => null;
    public bool IsActive { get; private set; }
    public virtual IReadOnlyList<SettingDescriptor> Settings =>
    [
        new() { Key = "open", Label = "Araç panelini aç", Kind = SettingKind.Action, Execute = Open },
        new() { Key = "hotkey-enabled", Label = "Açma kısayolu etkin", Kind = SettingKind.Toggle, Default = true },
        new() { Key = "hotkey", Label = "Açma kısayolu", Kind = SettingKind.Text, Default = $"Win+Alt+{(char)ShortcutKey}", Description = "Örnek: Ctrl+Alt+T" }
    ];

    public void Initialize(IModuleContext context) => Context = context;
    public virtual void Activate()
    {
        if (IsActive) return;
        RegisterHotkey();
        IsActive = true;
    }

    public virtual void Deactivate()
    {
        if (_window is not null)
        {
            if (_window is IManagedFeatureWindow managed) managed.CloseForModule();
            else _window.Close();
            if (_window is not null)
                throw new InvalidOperationException("Devam eden işlemi tamamlayın veya iptal edin, ardından modülü kapatın.");
        }
        _hotkey?.Dispose();
        _hotkey = null;
        IsActive = false;
    }

    public virtual void Apply()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        if (IsActive) RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        if (Context.Settings.GetBool("hotkey-enabled", true))
        {
            var (modifiers, key) = FeatureShortcut.Parse(Context.Settings.GetString("hotkey", $"Win+Alt+{(char)ShortcutKey}"));
            _hotkey = Context.Hotkeys.Register(modifiers, key, OnShortcut);
        }
    }

    protected virtual void OnShortcut() => Open();
    protected Window? CurrentWindow => _window;

    protected void Open()
    {
        if (!IsActive) { Context.Notify("Önce modülü etkinleştirin."); return; }
        try
        {
            var window = EnsureWindow();
            if (window is IFeatureWindow featureWindow) featureWindow.BeforeShow();
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }
        catch (Exception error)
        {
            Context.Log.Error($"{Name} açılamadı.", error);
            Context.Notify(error.Message);
        }
    }

    protected Window EnsureWindow()
    {
        if (_window is not null) return _window;
        _window = CreateWindow();
        _window.Closed += (_, _) => _window = null;
        return _window;
    }

    public IEnumerable<QuickAction> GetQuickActions(string query)
    {
        if (IsActive) yield return new QuickAction(Id + ".open", Name, Description, Open);
    }
}

internal interface IFeatureWindow { void BeforeShow(); }
internal interface IManagedFeatureWindow { void CloseForModule(); }

internal static class FeatureShortcut
{
    internal static (uint Modifiers, uint Key) Parse(string value, bool requireModifier = true)
    {
        var pieces = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        uint modifiers = 0; Key? key = null;
        foreach (var piece in pieces)
        {
            var modifier = piece.ToLowerInvariant() switch { "ctrl" or "control" => HotkeyModifiers.Control, "alt" => HotkeyModifiers.Alt, "shift" => HotkeyModifiers.Shift, "win" or "windows" => HotkeyModifiers.Win, _ => 0u };
            if (modifier != 0) { if ((modifiers & modifier) != 0) throw new ArgumentException("Kısayolda aynı değiştirici iki kez kullanılamaz."); modifiers |= modifier; continue; }
            if (key is not null || !Enum.TryParse<Key>(piece.Length == 1 && char.IsDigit(piece[0]) ? "D" + piece : piece, true, out var parsed) || !Enum.IsDefined(parsed) || parsed is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                throw new ArgumentException("Kısayolu Ctrl+Alt+T gibi yazın.");
            key = parsed;
        }
        if (requireModifier && modifiers == 0 || key is null || KeyInterop.VirtualKeyFromKey(key.Value) == 0) throw new ArgumentException("Kısayolda bir tuş ve gerekiyorsa değiştirici kullanın.");
        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(key.Value));
    }
}

public sealed class NashiModule : NativeFeatureModule
{
    private readonly Func<UI.MainWindow>? _windowFactory;
    public NashiModule() { }
    internal NashiModule(Func<UI.MainWindow> windowFactory) => _windowFactory = windowFactory;
    public override string Id => "nashi";
    public override string Name => "Nashi · Dosya Araçları";
    public override string Description => "Win+Alt+N: görsel, PDF, belge, arşiv ve medya dönüştürme; kırpma, işaretleme, QR, web indirme, sürükleme çarkı ve dosya kütüphanesi.";
    public override string? Warning => "Medya, Office ve metadata işlemleri için paneldeki araç kurulumunu tamamlayın. Özgün dosyalar korunur.";
    protected override uint ShortcutKey => 'N';
    protected override Window CreateWindow() => (_windowFactory?.Invoke() ?? new UI.MainWindow(offerToolSetup: false)).UseModuleLifetime();
    public override void Activate()
    {
        if (IsActive) return;
        base.Activate();
        try
        {
            var window = EnsureWindow();
            window.ShowActivated = false; window.Opacity = 0;
            window.Show(); window.Hide();
            window.Opacity = 1; window.ShowActivated = true;
        }
        catch { base.Deactivate(); throw; }
    }
    public override void Deactivate()
    {
        var window = CurrentWindow as UI.MainWindow;
        base.Deactivate();
        window?.CloseFloatingShelves();
        UI.Feedback.Stop(); UI.PdfRasterDocument.Shutdown();
    }
}
