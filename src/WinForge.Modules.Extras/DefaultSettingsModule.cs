using System.Diagnostics;
using System.Text.Json;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public abstract class DefaultSettingsModule : ITweakModule, IModulePanel
{
    protected IModuleContext Context { get; private set; } = null!;
    protected string Status { get; set; } = "Hazır";
    public abstract string Id { get; }
    public abstract string Name { get; }
    public string Category => "Sistem";
    public abstract string Description { get; }
    public abstract string? Warning { get; }
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    private object? _panelState;
    public event Action? PanelStateChanged;
    protected DefaultSettingsModule() { Settings = []; }
    public void Initialize(IModuleContext context) { Context = context; Refresh(); PushState(); }
    public object GetPanelState() => new { kind = Id, state = _panelState };
    public void HandlePanelCommand(string action, JsonElement payload) => HandleMessage(action, payload);
    protected void PublishState(object state)
    {
        _panelState = state;
        PanelStateChanged?.Invoke();
    }
    public void Activate() => IsActive = true;
    public void Deactivate()
    {
        IsActive = false;
    }
    public void Apply() { }

    private void HandleMessage(string command, JsonElement payload)
    {
        try
        {
            if (command == "defaults.reload") { Refresh(); Status = "Güncel Windows ayarları okundu."; }
            else OnMessage(command, payload);
        }
        catch (Exception exception)
        {
            Status = "İşlem tamamlanamadı: " + exception.Message;
            Context.Log.Error(Name, exception);
        }
        PushState();
    }

    protected static string Text(JsonElement payload, string key) =>
        payload.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    protected static void OpenSettings(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    protected abstract void Refresh();
    protected abstract void PushState();
    protected abstract void OnMessage(string command, JsonElement payload);
}
