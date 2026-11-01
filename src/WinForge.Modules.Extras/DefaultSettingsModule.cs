using System.Diagnostics;
using System.Text.Json;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public abstract class DefaultSettingsModule : ITweakModule
{
    protected IModuleContext Context { get; private set; } = null!;
    protected IFeatureUiWindow? Window { get; private set; }
    protected string Status { get; set; } = "Hazır";
    public abstract string Id { get; }
    public abstract string Name { get; }
    public string Category => "Sistem";
    public abstract string Description { get; }
    public abstract string? Warning { get; }
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    protected DefaultSettingsModule()
    {
        Settings = [new SettingDescriptor
        {
            Key = "open", Label = "Yönetim panelini aç", Kind = SettingKind.Action, Execute = Open
        }];
    }

    public void Initialize(IModuleContext context) => Context = context;
    public void Activate() => IsActive = true;
    public void Deactivate()
    {
        IsActive = false;
        Window?.Close();
        Window = null;
    }
    public void Apply() { }

    private void Open()
    {
        try
        {
            if (Window is null)
            {
                var ui = (Context as IFeatureUiContext)?.FeatureUi
                    ?? throw new InvalidOperationException("React arayüzü kullanılamıyor.");
                Window = ui.Open(Id, Id, Name, new FeatureUiWindowOptions(980, 700, ShowInTaskbar: true, Resizable: true));
                Window.MessageReceived += HandleMessage;
                Window.ClosedByHost += () => Window = null;
            }
            Refresh();
            PushState();
            Window.Show();
        }
        catch (Exception exception)
        {
            Context.Log.Error(Name + " açılamadı.", exception);
            Context.Notify(Name + ": " + exception.Message);
        }
    }

    private void HandleMessage(string command, JsonElement payload)
    {
        try
        {
            if (command == "defaults.close") { Window?.Close(); Window = null; return; }
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
