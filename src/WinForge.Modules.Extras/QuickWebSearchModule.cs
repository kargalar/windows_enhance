using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class QuickWebSearchModule : ITweakModule
{
    private IModuleContext _context = null!;
    private IDisposable? _binding;
    private Window? _window;
    public string Id => "quick-web-search";
    public string Name => "Hızlı Web Araması";
    public string Category => "Verimlilik";
    public string Description => "Kısayolla kompakt arama panelini açın ve Google'da arayın.";
    public string? Warning => null;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } = [new()
    {
        Key = "shortcut", Label = "Arama kısayolu", Kind = SettingKind.Text, Default = "CapsLock",
        Description = "Caps Lock kısayola atandığında normal kilit işlevi devre dışı kalır."
    }];
    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() { IsActive = true; try { Bind(); } catch { IsActive = false; throw; } }
    public void Deactivate() { IsActive = false; _binding?.Dispose(); _binding = null; _window?.Close(); _window = null; }
    public void Apply() { if (!IsActive) return; _binding?.Dispose(); _binding = null; Bind(); }
    private void Bind()
    {
        var key = AutomationCommands.ParseShortcut(_context.Settings.GetString("shortcut", "CapsLock"));
        _binding = _context.Hotkeys.Register(key.Modifiers, (uint)key.Key, Show);
    }
    private void Show()
    {
        if (_window is not null) { _window.Activate(); return; }
        var input = new TextBox { FontSize = 17, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Foreground = Brushes.White, CaretBrush = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0), Margin = new Thickness(48, 0, 64, 0) };
        var placeholder = new TextBlock { Text = "Web'de ara...", FontSize = 17,
            Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(48, 0, 64, 0), IsHitTestVisible = false };
        input.TextChanged += (_, _) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var content = new Grid();
        content.Children.Add(new TextBlock { Text = "⌕", FontSize = 27, Foreground = new SolidColorBrush(Color.FromRgb(155, 155, 155)),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(17, 0, 0, 2), IsHitTestVisible = false });
        content.Children.Add(input);
        content.Children.Add(placeholder);
        content.Children.Add(new TextBlock { Text = "ENTER ↵", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(125, 125, 125)),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 16, 0), IsHitTestVisible = false });
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(12), Child = content };
        var area = CursorPanelPlacement.WorkArea;
        var window = new Window { Width = Math.Min(520, area.Width - 24), Height = 60,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Topmost = true, AllowsTransparency = true, Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.Manual, Content = border };
        window.Left = area.Left + (area.Width - window.Width) / 2;
        window.Top = area.Bottom - window.Height - 24;
        _window = window;
        var activated = false;
        var closing = false;
        void ClosePanel()
        {
            if (!closing) window.Close();
        }
        window.Closing += (_, _) => { closing = true; if (_window == window) _window = null; };
        window.Closed += (_, _) => { if (_window == window) _window = null; };
        window.Activated += (_, _) => { if (!closing) { activated = true; input.Focus(); Keyboard.Focus(input); } };
        window.Deactivated += (_, _) => { if (activated && !closing) ClosePanel(); };
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!closing) FocusWindow(window, input, () => closing);
        }));
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; ClosePanel(); }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var text = input.Text.Trim();
                if (text.Length == 0) return;
                try { Process.Start(new ProcessStartInfo(SelectionSearch.BuildUrl(text)) { UseShellExecute = true }); ClosePanel(); }
                catch (Exception ex) { _context.Notify("Arama açılamadı: " + ex.Message); }
            }
        };
        window.Show();
    }
    private static void FocusWindow(Window window, TextBox input, Func<bool> isClosing)
    {
        if (!window.IsVisible) return;
        var foreground = Native.GetForegroundWindow();
        var foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);
            if (isClosing()) return;
            window.Activate();
            if (isClosing()) return;
            input.Focus();
            if (!isClosing()) Keyboard.Focus(input);
        }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
    }

    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

}
