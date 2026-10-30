using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using WinForge.Core;
using WinForge.FileTools.Engine;
namespace WinForge.FileTools.UI;
public sealed class MetadataEditor:Window
{
    private readonly CheckBox removeAll=new(){Content="Remove all metadata",Margin=new(4,14,4,14)};
    private readonly Dictionary<string,TextBox> fields=[];private readonly Dictionary<string,string> original=[];
    private readonly Dictionary<string,string> _labels=[];
    private readonly List<(string Label,string Value)> _details=[];
    private readonly IFeatureUiSurface _uiSurface;
    private readonly bool _media;
    private bool _removeAll;
    public bool RemoveAll => _removeAll;
    public Dictionary<string, string> Updates => _updates.Where(pair => pair.Value != original.GetValueOrDefault(pair.Key, ""))
        .ToDictionary(pair => pair.Key, pair => pair.Value);
    private Dictionary<string,string> _updates=[];
    public MetadataEditor(Window owner,IReadOnlyDictionary<string,string> metadata,bool media=false,IFeatureUiService? featureUi=null)
    {
        Owner=owner;_media=media;Theme.Attach(this);Title="Metadata · Nashi";Width=820;Height=700;MinWidth=640;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(26)};Content=root;var header=new StackPanel();header.Children.Add(L10n.Label(new TextBlock{Text="Metadata",FontSize=28,FontWeight=FontWeights.Bold}));header.Children.Add(new TextBlock{Text=L10n.Text(media?"Edit fields or remove metadata. Encoded audio and video stay untouched.":"Edit fields or remove metadata. Encoded image pixels stay untouched."),Margin=new(0,8,0,0),TextWrapping=TextWrapping.Wrap});header.Children.Add(removeAll);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new DockPanel{LastChildFill=false,Margin=new(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);var save=MainWindow.Button("Save new copy ↗",()=>DialogResult=true);save.Background=Theme.Primary;save.Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43,54,28));DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);var cancel=MainWindow.Button("Cancel",()=>DialogResult=false);DockPanel.SetDock(cancel,Dock.Left);footer.Children.Add(cancel);var stack=new StackPanel();root.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var source=metadata.GetValueOrDefault("SourceFile","")??"";var prefix=FeatureCatalog.Extension(source)=="pdf"?"PDF:":FeatureCatalog.Extension(source)=="mp3"?"ID3:":FeatureCatalog.IsVideo(source)||FeatureCatalog.IsAudio(source)?"QuickTime:":"";
        IEnumerable<string> editable=MetadataService.EditableTags.Where(k=>prefix.Length>0?k.StartsWith(prefix,StringComparison.Ordinal):k.StartsWith("EXIF:",StringComparison.Ordinal)||k.StartsWith("XMP:",StringComparison.Ordinal));
        if(media)editable=MediaMetadata.Editable.Concat(metadata.Keys.Where(k=>k.StartsWith("stream:",StringComparison.Ordinal)||k.StartsWith("chapter:",StringComparison.Ordinal)).Where(k=>k.EndsWith(":title",StringComparison.Ordinal)||k.EndsWith(":language",StringComparison.Ordinal)||k.EndsWith(":handler_name",StringComparison.Ordinal)||k.EndsWith(":comment",StringComparison.Ordinal))).Distinct();
        foreach(var key in editable){var value=metadata.GetValueOrDefault(key,"");original[key]=value;var label=media?MediaMetadata.Label(key):key;_labels[key]=label;stack.Children.Add(new TextBlock{Text=label,Margin=new(4,12,4,0)});var field=new TextBox{Text=value};fields[key]=field;stack.Children.Add(field);}
        var details=new StackPanel();foreach(var pair in metadata.Where(p=>!fields.ContainsKey(p.Key))){var label=media?MediaMetadata.Label(pair.Key):pair.Key;_details.Add((label,pair.Value));details.Children.Add(new TextBlock{Text=label+"  ·  "+pair.Value,TextWrapping=TextWrapping.Wrap,Margin=new(4,5,4,5)});}stack.Children.Add(new Expander{Header="All metadata",Content=details,Margin=new(4,18,4,12)});removeAll.Checked+=(_,_)=>{foreach(var field in fields.Values)field.IsEnabled=false;};removeAll.Unchecked+=(_,_)=>{foreach(var field in fields.Values)field.IsEnabled=true;};PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};
        _uiSurface = featureUi?.CreateSurface("nashi-metadata-editor")
            ?? throw new InvalidOperationException("Meta veri düzenleyicisi için React arayüzü gerekli.");
        _uiSurface.MessageReceived += OnUiMessage;
        Content = _uiSurface.Element;
        Closed += (_, _) => _uiSurface.Dispose();
        PushState();
    }

    private void OnUiMessage(string command,JsonElement payload)
    {
        switch(command)
        {
            case "metadata.update":
                if(payload.TryGetProperty("key",out var key)&&key.GetString() is { } fieldKey&&payload.TryGetProperty("value",out var value))_updates[fieldKey]=value.GetString()??"";
                break;
            case "metadata.remove-all":_removeAll=payload.GetProperty("value").GetBoolean();break;
            case "metadata.save":DialogResult=true;break;
            case "metadata.cancel":DialogResult=false;break;
        }
        PushState();
    }

    private void PushState()
    {
        var editable=fields.Keys.Select(key=>new{key,label=_labels[key],value=_updates.GetValueOrDefault(key,original[key])}).ToArray();
        _uiSurface.SetState(new{media=_media,removeAll=_removeAll,fields=editable,details=_details.Select(item=>new{label=item.Label,value=item.Value}).ToArray(),canSave=true});
    }
}
