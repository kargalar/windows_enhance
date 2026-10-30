using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Text.Json;
using WinForge.Core;
using WinForge.FileTools.Engine;
namespace WinForge.FileTools.UI;

/// Reads only the three configured modifiers and primary mouse button. It never
/// records keystrokes or polls the clipboard. Actual files/links arrive through
/// OLE drag/drop after the user chooses a wheel segment.
public sealed class GlobalDragController:IDisposable
{
    [StructLayout(LayoutKind.Sequential)]internal struct NativePoint{public int X,Y;}
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr window,int id);
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct GuiThreadInfo{public int Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public NativeRect CaretRect;}
    [DllImport("user32.dll")]private static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
    /// True while the foreground thread is in a window move/size loop (title-bar drag).
    /// Those drags belong to window management and never carry files.
    private static bool InMoveSize(){var info=new GuiThreadInfo{Size=Marshal.SizeOf<GuiThreadInfo>()};return GetGUIThreadInfo(0,ref info)&&(info.Flags&0x2)!=0;}
    [DllImport("oleacc.dll")]private static extern int AccessibleObjectFromPoint(NativePoint point,[MarshalAs(UnmanagedType.Interface)]out object accessible,[MarshalAs(UnmanagedType.Struct)]out object child);
    private readonly Window owner;private readonly PreferencesStore preferences;private readonly Action<string,IDataObject,string?> dropped;private readonly Action link;private readonly IFeatureUiService? featureUi;private readonly System.Windows.Threading.DispatcherTimer timer;
    private NativePoint start;private bool down,opened;private Task<string?>? hint;private GlobalDropWheel? wheel;private HwndSource? source;private IntPtr handle;private bool? shortcutEnabled;
    internal GlobalDropWheel? ActiveWheel=>wheel;
    public bool LinkShortcutRegistered{get;private set;}
    private bool OwnerAvailable=>DesktopGeometry.IsInteractive(owner);
    public GlobalDragController(Window owner,PreferencesStore preferences,Action<string,IDataObject,string?> dropped,Action link,IFeatureUiService? featureUi=null)
    {
        this.owner=owner;this.preferences=preferences;this.dropped=dropped;this.link=link;this.featureUi=featureUi;timer=new(){Interval=TimeSpan.FromMilliseconds(40)};timer.Tick+=(_,_)=>Poll();
        if(new WindowInteropHelper(owner).Handle!=IntPtr.Zero)Register();else owner.SourceInitialized+=(_,_)=>Register();timer.Start();
    }
    private void Register(){handle=new WindowInteropHelper(owner).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(Hook);UpdateShortcut();}
    private void UpdateShortcut(){if(handle==IntPtr.Zero)return;var enabled=preferences.Value.Enabled;if(shortcutEnabled==enabled)return;shortcutEnabled=enabled;if(LinkShortcutRegistered)UnregisterHotKey(handle,7701);LinkShortcutRegistered=enabled&&RegisterHotKey(handle,7701,0x4000|0x2|0x4,0x44);}
    private IntPtr Hook(IntPtr window,int message,IntPtr w,IntPtr l,ref bool handled){if(message==0x0312&&w.ToInt32()==7701){if(preferences.Value.Enabled&&OwnerAvailable)link();handled=true;}return IntPtr.Zero;}
    private static bool Held(int key)=>(GetAsyncKeyState(key)&0x8000)!=0;
    private string Chord(){var parts=new List<string>();if(Held(0x11))parts.Add("Ctrl");if(Held(0x10))parts.Add("Shift");if(Held(0x12))parts.Add("Alt");return string.Join('+',parts);}
    private void Poll()
    {
        UpdateShortcut();if(!preferences.Value.Enabled||!OwnerAvailable){wheel?.Close();wheel=null;down=false;opened=false;hint=null;return;}var held=PrimaryMouseButton.IsPressed;GetCursorPos(out var point);
        if(!held){if(down){down=false;opened=false;hint=null;if(wheel is {IsVisible:true}){var old=wheel;var dismiss=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};dismiss.Tick+=(_,_)=>{dismiss.Stop();if(old.IsVisible)old.Close();};dismiss.Start();}wheel=null;}return;}
        if(!down){down=true;start=point;opened=false;hint=null;return;}if(opened||Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)<10)return;
        hint??=Task.Run(()=>LinkAt(start));var chord=Chord();var p=preferences.Value;var kind=chord==p.ToolsModifier?"tools":chord==p.ConvertModifier?"convert":chord==p.ActionsModifier?"actions":null;if(kind is null)return;opened=true;if(InMoveSize())return;
        var sourceHint=hint;wheel=new GlobalDropWheel(kind,p.WheelScale,(data,id)=>dropped(id,data,sourceHint?.IsCompletedSuccessfully==true?sourceHint.Result:null),()=>sourceHint?.IsCompletedSuccessfully==true?sourceHint.Result:null,featureUi);wheel.Show();DesktopGeometry.PlaceWheel(wheel,point.X,point.Y,WheelPresentation.Diameter*p.WheelScale);
    }
    private static string? LinkAt(NativePoint point)
    {
        try
        {
            var element=AutomationElement.FromPoint(new Point(point.X,point.Y));for(var n=0;n<7&&element is not null;n++,element=TreeWalker.ControlViewWalker.GetParent(element))
            {
                if(element.Current.IsPassword||element.Current.ControlType==ControlType.Edit)return null;
                if(element.TryGetCurrentPattern(ValuePattern.Pattern,out var value)&&WebMediaPayload.Parse(((ValuePattern)value).Current.Value) is {} result)return result.Url;
            }
            if(AccessibleObjectFromPoint(point,out var accessible,out var child)>=0)
            {
                try{for(var n=0;n<4;n++){var value=accessible.GetType().InvokeMember("accValue",System.Reflection.BindingFlags.GetProperty,null,accessible,[child])?.ToString();if(WebMediaPayload.Parse(value) is {} link)return link.Url;var parent=accessible.GetType().InvokeMember("accParent",System.Reflection.BindingFlags.GetProperty,null,accessible,null);if(parent is null)break;Marshal.FinalReleaseComObject(accessible);accessible=parent;child=0;}}finally{if(Marshal.IsComObject(accessible))Marshal.FinalReleaseComObject(accessible);}
            }
        }catch(ElementNotAvailableException){}catch(InvalidOperationException){}catch(COMException){}catch(System.Reflection.TargetInvocationException){}catch(MissingMethodException){}return null;
    }
    public void Dispose(){timer.Stop();wheel?.Close();if(handle!=IntPtr.Zero)UnregisterHotKey(handle,7701);source?.RemoveHook(Hook);}
}

internal sealed class GlobalDropWheel : Window
{
    private const double HiddenOpacity = .02;
    private readonly string mode;
    private readonly Action<IDataObject, string> chosen;
    private readonly Func<string?> hint;
    private readonly IFeatureUiSurface uiSurface;
    private string selection = "";
    private int selectedIndex = -1;
    private IReadOnlyList<ToolChoice> choices = [];
    private int entered;
    private int over;
    private int dropCount;

    internal bool Revealed { get; private set; }
    internal string State => $"entered={entered}, over={over}, dropped={dropCount}, choices={choices.Count}, selection={selection}, revealed={Revealed}";

    public GlobalDropWheel(string mode, double scale, Action<IDataObject, string> chosen, Func<string?> hint,
        IFeatureUiService? featureUi = null)
    {
        this.mode = mode;
        this.chosen = chosen;
        this.hint = hint;
        Title = "Nashi · " + mode;
        Width = Height = WheelPresentation.Diameter * scale;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;

        uiSurface = featureUi?.CreateSurface("nashi-wheel")
            ?? throw new InvalidOperationException("Dosya bırakma tekerleği için React arayüzü gerekli.");
        Content = uiSurface.Element;
        uiSurface.Element.IsHitTestVisible = false;
        Closed += (_, _) => uiSurface.Dispose();
        Opacity = HiddenOpacity;
        PushState();

        // Reveal only after a supported file or link reaches the native OLE drop target.
        DragEnter += (_, e) => { entered++; Track(e); if (choices.Count > 0) Reveal(); };
        DragOver += (_, e) => { over++; Track(e); if (choices.Count > 0) Reveal(); };
        Drop += (_, e) =>
        {
            dropCount++;
            Track(e);
            var id = selection;
            Close();
            if (id.Length > 0) chosen(e.Data, id);
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }

    private void Reveal()
    {
        if (Revealed) return;
        Revealed = true;
        PushState();
        WheelPresentation.Animate(this, HiddenOpacity);
        if (Opacity < 1 && !HasAnimatedProperties) Opacity = 1;
    }

    private void Track(DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        var updated = Choices(files, DragPayload.Parse(e.Data, hint()));
        // Keep the native drop target stable while the browser surface is refreshed.
        if (!updated.SequenceEqual(choices))
        {
            choices = updated;
            selection = "";
            selectedIndex = -1;
            PushState();
        }

        var point = e.GetPosition(this);
        var factor = 540 / Math.Max(ActualWidth, 1);
        point = new Point(point.X * factor, point.Y * factor);
        var delta = point - new Point(270, 270);
        var distance = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (distance < 108 || distance > 250 || choices.Count == 0)
        {
            selection = "";
            Highlight(-1);
        }
        else
        {
            var angle = (Math.Atan2(delta.Y, delta.X) * 180 / Math.PI -
                WheelPresentation.StartAngle(choices.Count) + 360 + 180d / choices.Count) % 360;
            var index = (int)(angle / (360d / choices.Count));
            var id = choices[index].Id;
            if (id != selection)
            {
                selection = id;
                Feedback.Play();
            }
            Highlight(index);
        }

        e.Effects = selection.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private IReadOnlyList<ToolChoice> Choices(string[] files, WebMediaPayload? web)
    {
        if (files.Length > 0 && web is null)
        {
            if (mode == "convert")
                return FeatureCatalog.Formats(files).Select(format =>
                    new ToolChoice("convert:" + format, format.ToUpperInvariant(), "↗")).ToArray();
            if (mode == "actions")
                return
                [
                    new("share", "Share", "◉"), new("float", "Float", "▣"),
                    new("copy", "Copy", "▱"), new("move", "Move", "↗"),
                    new("copyPath", "Copy Path", "↪"), new("terminal", "Terminal", ">_")
                ];
            return FeatureCatalog.Tools(files).Select(tool =>
                new ToolChoice(tool, FeatureCatalog.ToolNames[tool], MainWindow.ToolSymbol(tool))).ToArray();
        }

        if (web is null) return [];
        return web.Kind switch
        {
            "youtube" =>
            [
                new("download:video", "Download Video", "↓"),
                new("download:thumbnail", "Download Thumbnail", "▣"),
                new("download:audio", "Download Audio", "♫")
            ],
            "instagramPhotos" => [new("download:photos", "Download Images", "▣")],
            "instagramReel" =>
            [
                new("download:video", "Download Video", "↓"),
                new("download:audio", "Download Audio", "♫")
            ],
            "image" => [new("download:image", "Download Image", "↓")],
            _ =>
            [
                new("download:video", "Download Video", "↓"),
                new("download:audio", "Download Audio", "♫")
            ]
        };
    }

    private void Highlight(int index)
    {
        if (selectedIndex == index) return;
        selectedIndex = index;
        PushState();
    }

    private void PushState()
    {
        uiSurface.SetState(new
        {
            externalDrag = true,
            finish = WheelPresentation.Preferences.WheelFinish,
            selected = selectedIndex,
            center = choices.Count == 0 ? "Keep dragging" : mode == "convert" ? "Convert" : mode == "actions" ? "File actions" : "Tools",
            subtitle = choices.Count == 0 ? "Drop a file or link" : "Release to choose",
            startAngle = WheelPresentation.StartAngle(Math.Max(1, choices.Count)),
            choices = choices.Select(choice => new { choice.Id, choice.Title, choice.Symbol }).ToArray()
        });
    }
}
