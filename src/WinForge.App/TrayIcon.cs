using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using WinForge.Core;

namespace WinForge.App;

public sealed class TrayIcon : IDisposable
{
    private readonly Action _show;
    private readonly Action _exit;
    private NotifyIcon? _notifyIcon;
    private Icon? _icon;
    private IntPtr _iconHandle;

    public TrayIcon(Action show, Action exit)
    {
        _show = show;
        _exit = exit;
    }

    public void Initialize()
    {
        _icon = BuildIcon(out _iconHandle);

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

    private static Icon BuildIcon(out IntPtr handle)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using var path = RoundedRect(new RectangleF(1, 1, 30, 30), 8);
            using var background = new SolidBrush(Color.FromArgb(76, 194, 255));
            graphics.FillPath(background, path);

            using var font = new Font("Segoe UI", 17f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var foreground = new SolidBrush(Color.FromArgb(6, 19, 28));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("W", font, foreground, new RectangleF(1, 1, 30, 30), format);
        }

        handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
        if (_iconHandle != IntPtr.Zero)
        {
            Native.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }
}
