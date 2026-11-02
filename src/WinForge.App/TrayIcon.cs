using System.Drawing;
using System.Windows.Forms;

namespace WinForge.App;

public sealed class TrayIcon : IDisposable
{
    private readonly Action _show;
    private readonly Action _exit;
    private NotifyIcon? _notifyIcon;
    private Icon? _icon;

    public TrayIcon(Action show, Action exit)
    {
        _show = show;
        _exit = exit;
    }

    public void Initialize()
    {
        using var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/WinForge;component/Assets/WinForge.ico")).Stream;
        _icon = new Icon(resource, SystemInformation.SmallIconSize);

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("WinForge'u aç", null, (_, _) => _show());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => _exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "WinForge",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => _show();
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
    }
}
