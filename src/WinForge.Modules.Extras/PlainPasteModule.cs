using System.Runtime.InteropServices;
using System.Windows;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class PlainPasteModule : ITweakModule
{
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyEventKeyUp = 0x0002;

    private IModuleContext? _context;
    private IDisposable? _hotkey;

    public string Id => "plain-paste";
    public string Name => "Düz Metin Yapıştır";
    public string Category => "Verimlilik";
    public string Description => "Win+Ctrl+Alt+V ile panodaki metni biçimlendirmesini temizleyip yapıştırır.";
    public string? Warning => "Yönetici olarak çalışan uygulamalarda sentetik tuşlar Windows tarafından engellenebilir.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "hotkey-enabled",
            Label = "Win+Ctrl+Alt+V kısayolu etkin",
            Kind = SettingKind.Toggle,
            Default = true
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

        _hotkey = context.Hotkeys.Register(
            HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.Alt, VkV, Paste);
        context.Log.Info("Düz metin yapıştırma kısayolu kaydedildi (Win+Ctrl+Alt+V).");
    }

    private void Paste()
    {
        var context = _context;
        if (context is null) return;

        string? text = null;
        try
        {
            if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch
        {
            // Clipboard can be locked by another process for a moment.
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            context.Notify("Panoda metin yok");
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            context.Notify("Panoya yazılamadı");
            return;
        }

        context.Notify("Düz metin yapıştırılıyor");

        // Let the hotkey modifiers go before synthesizing Ctrl+V.
        Task.Delay(160).ContinueWith(_ => SendCtrlV(), TaskScheduler.Default);
    }

    private static void SendCtrlV()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
