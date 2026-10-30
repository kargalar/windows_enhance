using System.Windows;
using WinForge.FileTools;

namespace WinForge.FileTools.UI;

public sealed partial class MainWindow : IManagedFeatureWindow
{
    private bool _closingForModule;
    internal bool DesktopIntegrationActive => globalDrag is not null;
    internal void CloseFloatingShelves() { foreach (var shelf in shelves.ToArray()) shelf.Close(); }

    internal MainWindow UseModuleLifetime()
    {
        Closing += (_, args) =>
        {
            if (_closingForModule || args.Cancel) return;
            args.Cancel = true;
            Hide();
        };
        return this;
    }

    void IManagedFeatureWindow.CloseForModule()
    {
        if (OwnedWindows.Cast<Window>().Any(window => window.IsVisible))
            throw new InvalidOperationException("Açık dosya düzenleyicisini veya iletişim penceresini kapatın, ardından modülü kapatın.");
        _closingForModule = true;
        try { Close(); }
        finally { _closingForModule = false; }
    }
}
