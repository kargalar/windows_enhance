using System.Windows;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class PlainPasteModule : ITweakModule
{
    private const byte VkV = 0x56;

    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private CancellationTokenSource? _pendingPaste;

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
        CancelPendingPaste();
        _hotkey?.Dispose();
        _hotkey = null;
    }

    public void Apply()
    {
        if (!IsActive) return;
        CancelPendingPaste();

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

    private void Paste() => _ = PasteAsync();

    private async Task PasteAsync()
    {
        var context = _context;
        if (context is null || !IsActive) return;
        var target = Native.GetForegroundWindow();
        CancelPendingPaste();
        var cancellation = new CancellationTokenSource();
        _pendingPaste = cancellation;

        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            if (string.IsNullOrEmpty(text))
            {
                context.Notify("Panoda metin yok");
                return;
            }

            var completed = await PasteOperation.ExecuteAsync(target, () =>
            {
                Clipboard.SetText(text);
                KeyboardInputSender.PasteShortcut();
            }, cancellation.Token);
            context.Notify(completed ? "Düz metin yapıştırılıyor" : "Yapıştırma iptal edildi.");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            context.Log.Error("Düz metin yapıştırılamadı.", exception);
            context.Notify("Yapıştırılamadı: " + exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_pendingPaste, cancellation)) _pendingPaste = null;
            cancellation.Dispose();
        }
    }

    private void CancelPendingPaste()
    {
        var pending = Interlocked.Exchange(ref _pendingPaste, null);
        pending?.Cancel();
    }
}
