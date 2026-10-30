using System.Text.Json;
using System.Windows;
using WinForge.Core;

namespace WinForge.ClipboardLine;

internal sealed class ClipboardPromptWindow : Window
{
    private readonly IFeatureUiSurface uiSurface;
    private readonly string title;
    private string value;

    public string Value => value;

    public ClipboardPromptWindow(Window owner, string title, string value, IFeatureUiService? featureUi)
    {
        this.title = title;
        this.value = value;
        Owner = owner;
        Title = title;
        Width = 520;
        Height = 250;
        MinWidth = 420;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        uiSurface = featureUi?.CreateSurface("clipboard-prompt")
            ?? throw new InvalidOperationException("Pano metin penceresi için React arayüzü gerekli.");
        uiSurface.MessageReceived += OnUiMessage;
        Content = uiSurface.Element;
        Closed += (_, _) => uiSurface.Dispose();
        PushState();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "clipboard-prompt.change":
                value = payload.GetProperty("value").GetString() ?? "";
                PushState();
                break;
            case "clipboard-prompt.save": DialogResult = true; break;
            case "clipboard-prompt.cancel": DialogResult = false; break;
        }
    }

    private void PushState() => uiSurface.SetState(new { title, value });
}
