using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinForge.Modules.Capture;

internal static class CaptureUi
{
    public static void Theme(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "BgBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        window.FontFamily = new FontFamily("Segoe UI Variable Text");
        window.FontSize = 14;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(10, 6, 10, 6) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "GhostButton");
        button.Click += (_, _) => action();
        return button;
    }
}
