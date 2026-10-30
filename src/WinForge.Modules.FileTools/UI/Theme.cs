using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace WinForge.FileTools.UI;

internal static class Theme
{
    internal static bool Dark{get;private set;}
    internal static ResourceDictionary Resources { get; } = new() { Source = new Uri("/WinForge.Modules.FileTools;component/UI/Resources.xaml", UriKind.Relative) };
    internal static Brush Primary=>new SolidColorBrush(Color.FromRgb(76,194,255));
    internal static Brush Brush(string name)=>(Brush)Resources[name];
    internal static void Apply(string appearance)
    {
        Dark=appearance=="dark"||(appearance=="system"&&Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value&&value==0);
        foreach(var (key,light,dark) in new[]{("Paper","#F5F8FC","#0B0E14"),("Ink","#1B293B","#EDF2FA"),("Surface","#FFFFFF","#151A24"),("Inset","#EAF0F7","#0D1219"),("Muted","#52647A","#8C98AB"),("Line","#CAD6E3","#232C3A"),("Sage","#DCEFFB","#17202B"),("Sand","#D5EDF9","#12303F")})
            Resources[key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Dark?dark:light));
        foreach(Window window in Application.Current.Windows)
            if (window.GetType().Namespace == typeof(Theme).Namespace) ApplyTitleBar(window);
    }
    internal static void Attach(Window window)
    {
        window.Resources.MergedDictionaries.Add(Resources);
        window.SetResourceReference(Window.BackgroundProperty,"Paper");window.SetResourceReference(Window.ForegroundProperty,"Ink");
        window.Loaded+=(_,_)=>{if(window.WindowStyle!=WindowStyle.None)DesktopGeometry.FitToWorkArea(window);L10n.Bind(window,Window.TitleProperty,window.Title);L10n.Attach(window);};
        window.SourceInitialized+=(_,_)=>ApplyTitleBar(window);
    }
    internal static void Entrance(UIElement element)
    {
        if(!WheelPresentation.Preferences.Animations||!SystemParameters.ClientAreaAnimation)return;
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
        var offset=new TranslateTransform();element.RenderTransform=offset;
        offset.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(8,0,TimeSpan.FromMilliseconds(180)){DecelerationRatio=.8});
    }
    private static void ApplyTitleBar(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;var dark=Dark?1:0;_ = DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));
    }
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
