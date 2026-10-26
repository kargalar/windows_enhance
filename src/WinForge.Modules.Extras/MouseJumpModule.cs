using System.Windows;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class MouseJumpModule : ITweakModule
{
    private const uint VkJ = 0x4A;
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private readonly SettingDescriptor[] _settings;

    public MouseJumpModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open",
                Label = "Ekranlar arası fare taşıma",
                Kind = SettingKind.Action,
                Description = "Ekran önizlemesini açın, sonra taşımak istediğiniz noktaya tıklayın.",
                Execute = Open
            },
            new SettingDescriptor
            {
                Key = "hotkey-enabled",
                Label = "Win+Alt+J kısayolu etkin",
                Kind = SettingKind.Toggle,
                Default = true
            }
        ];
    }

    public string Id => "mouse-jump";
    public string Name => "Ekranlar Arası Fare Taşıma";
    public string Category => "Fare";
    public string Description => "Win+Alt+J ile ekran önizlemesini açıp fareyi tek tıklamayla başka bir ekrana taşıyın.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        try
        {
            RegisterHotkey();
        }
        catch
        {
            IsActive = false;
            throw;
        }
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
        _hotkey = context.Hotkeys.Register(HotkeyModifiers.Win | HotkeyModifiers.Alt, VkJ, Open);
    }

    private void Open()
    {
        try
        {
            var monitors = MouseJumpWindow.CaptureMonitors();
            if (monitors.Count == 0)
            {
                _context?.Notify("Etkin ekran bulunamadı.");
                return;
            }

            var window = new MouseJumpWindow(monitors);
            if (window.ShowDialog() != true || window.Destination is not { } point) return;
            if (!Native.SetCursorPos(point.X, point.Y))
                _context?.Notify("Fare konumu değiştirilemedi.");
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Ekranlar arası fare taşıma açılamadı.", exception);
            _context?.Notify("Ekranlar arası fare taşıma açılamadı: " + exception.Message);
        }
    }
}
