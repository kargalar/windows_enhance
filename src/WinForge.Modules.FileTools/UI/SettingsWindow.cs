using System.Windows;
using Microsoft.Win32;
using System.Text.Json;
using WinForge.Core;
using WinForge.FileTools.Engine;
namespace WinForge.FileTools.UI;
public sealed partial class SettingsWindow:Window
{
    private readonly LibraryStore? appearanceStore;
    private readonly PreferencesStore store;private readonly Action changed;
    private readonly Func<Task> _update;
    private readonly IFeatureUiSurface _uiSurface;
    private readonly IFeatureUiService _featureUi;
    private string _section="General";
    private string? _selectedFormat;
    public SettingsWindow(Window owner,PreferencesStore store,Action changed,Func<Task> update,LibraryStore? appearanceStore=null,IFeatureUiService? featureUi=null)
    {
        this.appearanceStore=appearanceStore;_featureUi=featureUi??throw new InvalidOperationException("React UI is required for settings.");
        this.store=store;this.changed=changed;_update=update;Owner=owner;Theme.Attach(this);Title="Nashi settings";Width=850;Height=680;MinWidth=740;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};
        _uiSurface = _featureUi.CreateSurface("nashi-settings");
        _uiSurface.MessageReceived += OnUiMessage;
        Content = _uiSurface.Element;
        Closed += (_, _) => _uiSurface.Dispose();
        PushUiState();
    }
    internal void ShowError(string title,string message)=>_ = _featureUi.ShowMessageAsync(L10n.Text(title),L10n.Text(message));
    private void Save(Preferences value){try{store.Save(value);changed();PushUiState();}catch(Exception error)when(error is IOException or ArgumentException or UnauthorizedAccessException){ShowError("Settings could not be saved",error.Message);}}
    private void OnUiMessage(string command,JsonElement payload)
    {
        try
        {
            switch(command)
            {
                case "settings.close":Close();break;
                case "settings.section":
                    _section=payload.GetProperty("section").GetString()??"General";
                    break;
                case "settings.profile.select":
                    _selectedFormat=payload.GetProperty("format").GetString();
                    break;
                case "settings.toggle":UpdateToggle(payload);break;
                case "settings.pick":UpdatePick(payload);break;
                case "settings.scale":
                    if(payload.GetProperty("value").TryGetDouble(out var scale))Save(store.Value with{WheelScale=Math.Clamp(scale,.85,1.4)});
                    break;
                case "settings.folder":ChooseFolder(payload.GetProperty("folder").GetString()??"");break;
                case "settings.profile.save":SaveProfile(payload);break;
                case "settings.profile.reset":
                    if(payload.TryGetProperty("format",out var format))store.SetProfile(format.GetString()??"",new());
                    changed();PushUiState();break;
                case "settings.updates":_ = _update();break;
                case "settings.feedback":Feedback.Play();break;
                case "settings.source":MainWindow.OpenUrl("https://github.com/orkunisitmak/nashi-app");break;
            }
            PushUiState();
        }
        catch(Exception error){ShowError("Settings could not be saved",error.Message);}
    }

    private void UpdateToggle(JsonElement payload)
    {
        var key=payload.GetProperty("key").GetString();var value=payload.GetProperty("value").GetBoolean();
        switch(key)
        {
            case "Enabled":Save(store.Value with{Enabled=value});break;
            case "Feedback":Save(store.Value with{Feedback=value});break;
            case "Animations":Save(store.Value with{Animations=value});break;
            case "KeepInTray":Save(store.Value with{KeepInTray=value});break;
            case "RevealFinished":Save(store.Value with{RevealFinished=value});break;
            case "AutomaticUpdateChecks":Save(store.Value with{AutomaticUpdateChecks=value});break;
            case "WindowManagement" when appearanceStore is not null:appearanceStore.SetWindowManagement(value);changed();PushUiState();break;
        }
    }

    private void UpdatePick(JsonElement payload)
    {
        var key=payload.GetProperty("key").GetString();var value=payload.GetProperty("value").GetString()??"";
        switch(key)
        {
            case "Language":Save(store.Value with{Language=value=="Türkçe"?"tr":"en"});break;
            case "Appearance" when appearanceStore is not null:appearanceStore.SetAppearance(value);Theme.Apply(value);changed();PushUiState();break;
            case "WheelFinish":Save(store.Value with{WheelFinish=value});break;
            case "FloatBehavior":Save(store.Value with{FloatBehavior=value});break;
            case "ToolsModifier":Save(store.Value with{ToolsModifier=value});break;
            case "ConvertModifier":Save(store.Value with{ConvertModifier=value});break;
            case "ActionsModifier":Save(store.Value with{ActionsModifier=value});break;
            case "OutputMode":Save(store.Value with{OutputMode=value});break;
        }
    }

    private void ChooseFolder(string folder)
    {
        var current=folder=="output"?store.Value.OutputFolder:store.Value.DownloadFolder;
        var picker=new OpenFolderDialog{Title=folder=="output"?"Save new copies here":"Save downloaded media here",InitialDirectory=Directory.Exists(current)?current:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
        if(picker.ShowDialog(this)!=true)return;
        if(folder=="output")Save(store.Value with{OutputFolder=picker.FolderName,OutputMode="folder"});
        else Save(store.Value with{DownloadFolder=picker.FolderName});
    }

    private void SaveProfile(JsonElement payload)
    {
        var format=payload.GetProperty("format").GetString()??throw new InvalidDataException("Choose an output format.");
        var profile=payload.GetProperty("profile").Deserialize<OutputProfile>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Export defaults are invalid.");
        store.SetProfile(format,profile);changed();PushUiState();
    }

    private void PushUiState()
    {
        var formats=FeatureCatalog.Conversions.Values.SelectMany(value=>value).Distinct().Order().ToArray();
        var compatible=FeatureCatalog.Conversions.ToDictionary(pair=>pair.Key,pair=>pair.Value);
        _selectedFormat ??= formats.FirstOrDefault()??"png";
        _uiSurface.SetState(new
        {
            section=_section,
            language=store.Value.Language,
            appearance=appearanceStore?.Appearance??"system",
            enabled=store.Value.Enabled,feedback=store.Value.Feedback,animations=store.Value.Animations,keepInTray=store.Value.KeepInTray,revealFinished=store.Value.RevealFinished,
            windowManagement=appearanceStore?.WindowManagementEnabled??false,wheelFinish=store.Value.WheelFinish,wheelScale=store.Value.WheelScale,
            toolsModifier=store.Value.ToolsModifier,convertModifier=store.Value.ConvertModifier,actionsModifier=store.Value.ActionsModifier,floatBehavior=store.Value.FloatBehavior,
            outputMode=store.Value.OutputMode,outputFolder=store.Value.OutputFolder,downloadFolder=store.Value.DownloadFolder,automaticUpdateChecks=store.Value.AutomaticUpdateChecks,
            formats,compatible,profiles=store.Value.Formats,selectedFormat=_selectedFormat,about="Local by design · MIT source · WinForge"
        });
    }
}
