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

        var isTr = WinForge.Core.Localization.IsTurkish(Appearance.Language);
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(isTr ? "WinForge'u aç" : "Open WinForge", null, (_, _) => _show());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(isTr ? "Çıkış" : "Exit", null, (_, _) => _exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "WinForge",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => _show();
    }

    public void UpdateLanguage()
    {
        if (_notifyIcon?.ContextMenuStrip is { } menu)
        {
            var isTr = WinForge.Core.Localization.IsTurkish(Appearance.Language);
            if (menu.Items.Count > 0) menu.Items[0].Text = isTr ? "WinForge'u aç" : "Open WinForge";
            if (menu.Items.Count > 2) menu.Items[2].Text = isTr ? "Çıkış" : "Exit";
        }
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
