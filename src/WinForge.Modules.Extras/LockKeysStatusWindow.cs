using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal static class LockKeysStatusWindow
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private static IFeatureUiService? _featureUi;
    private static IFeatureUiWindow? _instance;

    static LockKeysStatusWindow()
    {
        Timer.Tick += (_, _) =>
        {
            Timer.Stop();
            _instance?.Close();
            _instance = null;
        };
    }

    public static void Configure(IFeatureUiService? featureUi) => _featureUi = featureUi;

    public static void Show(string key, bool enabled)
    {
        if (_featureUi is null) return;
        var bounds = CursorPanelPlacement.WorkArea;
        _instance = _featureUi.Open("lock-keys-status", "lock-keys-status", "Kilit tuşu durumu",
            new FeatureUiWindowOptions(230, 76, Topmost: true, ActivateOnShow: false, Transparent: true));
        _instance.SetBounds(bounds.Left + (bounds.Width - 230) / 2, bounds.Top + 48, 230, 76);
        _instance.SetState(new { key, enabled });
        Timer.Stop();
        Timer.Start();
    }
}
