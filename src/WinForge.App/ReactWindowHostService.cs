using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinForge.Core;

namespace WinForge.App;

internal sealed class ReactWindowHostService : IFeatureUiService, IDisposable
{
    private const string HostName = "appassets.winforge";
    private readonly Dictionary<string, ReactFeatureWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _uiDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    private bool _disposed;

    public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Pencere anahtarı boş olamaz.", nameof(key));
        if (string.IsNullOrWhiteSpace(view)) throw new ArgumentException("Arayüz görünümü boş olamaz.", nameof(view));
        ObjectDisposedException.ThrowIf(_disposed, this);

        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
            return dispatcher.Invoke(() => Open(key, view, title, options));

        if (_windows.TryGetValue(key, out var existing) && existing.IsOpen)
        {
            existing.Show();
            return existing;
        }

        var window = new ReactFeatureWindow(key, view, title, options, _uiDirectory, HostName, _jsonOptions);
        window.Closed += OnWindowClosed;
        _windows[key] = window;
        window.Show();
        return window;
    }

    public IFeatureUiSurface CreateSurface(string view)
    {
        if (string.IsNullOrWhiteSpace(view)) throw new ArgumentException("Arayüz görünümü boş olamaz.", nameof(view));
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ReactFeatureSurface(view, _uiDirectory, HostName, _jsonOptions);
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        if (sender is not ReactFeatureWindow window) return;
        window.Closed -= OnWindowClosed;
        if (_windows.TryGetValue(window.Key, out var current) && ReferenceEquals(current, window))
            _windows.Remove(window.Key);
        window.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear();
    }

    private sealed class ReactFeatureWindow : Window, IFeatureUiWindow
    {
        private const int GwlExStyle = -20;
        private const long WsExTransparent = 0x00000020;
        private const long WsExToolWindow = 0x00000080;
        private const long WsExNoActivate = 0x08000000;
        private readonly string _view;
        private readonly string _uiDirectory;
        private readonly string _hostName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly WebView2 _webView;
        private readonly bool _resizable;
        private object? _state;
        private bool _disposed;

        public ReactFeatureWindow(string key, string view, string title, FeatureUiWindowOptions options,
            string uiDirectory, string hostName, JsonSerializerOptions jsonOptions)
        {
            Key = key;
            _view = view;
            _uiDirectory = uiDirectory;
            _hostName = hostName;
            _jsonOptions = jsonOptions;
            _resizable = options.Resizable;
            Title = title;
            Width = options.Width;
            Height = options.Height;
            MinWidth = options.Width;
            MinHeight = options.Height;
            MaxWidth = options.Width;
            MaxHeight = options.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = options.Resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
            ShowInTaskbar = options.ShowInTaskbar;
            ShowActivated = options.ActivateOnShow;
            Topmost = options.Topmost;
            AllowsTransparency = options.Transparent;
            Background = options.Transparent ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(16, 16, 16));
            WindowStartupLocation = WindowStartupLocation.Manual;
            IsHitTestVisible = !options.ClickThrough;

            _webView = new WebView2
            {
                DefaultBackgroundColor = options.Transparent
                    ? System.Drawing.Color.FromArgb(0, 0, 0, 0)
                    : System.Drawing.Color.FromArgb(255, 16, 16, 16)
            };
            Content = _webView;
            SourceInitialized += (_, _) => ApplyWindowStyles(options);
            Loaded += OnLoaded;
        }

        public string Key { get; }
        public bool IsOpen => !_disposed;
        public new bool IsVisible => !_disposed && (Dispatcher.CheckAccess() ? base.IsVisible : Dispatcher.Invoke(() => base.IsVisible));
        public event Action<string, JsonElement>? MessageReceived;

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
                var core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.SetVirtualHostNameToFolderMapping(_hostName, _uiDirectory,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += (_, navigation) =>
                {
                    if (!navigation.Uri.StartsWith($"https://{_hostName}/", StringComparison.OrdinalIgnoreCase))
                        navigation.Cancel = true;
                };
                core.NewWindowRequested += (_, request) => request.Handled = true;
                core.WebMessageReceived += OnWebMessageReceived;
                core.Navigate($"https://{_hostName}/index.html?view={Uri.EscapeDataString(_view)}");
            }
            catch (Exception exception)
            {
                App.Instance.Logger?.Error($"React penceresi başlatılamadı: {_view}", exception);
                Close();
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                using var document = JsonDocument.Parse(args.WebMessageAsJson);
                var root = document.RootElement;
                var command = root.GetProperty("command").GetString();
                if (command == "feature.ready")
                {
                    PostState();
                    return;
                }

                var payload = root.TryGetProperty("payload", out var value) ? value.Clone() : default;
                if (!string.IsNullOrWhiteSpace(command)) MessageReceived?.Invoke(command, payload);
            }
            catch (Exception exception)
            {
                App.Instance.Logger?.Error($"React penceresi komutu işlenemedi: {_view}", exception);
            }
        }

        public new void Show()
        {
            if (_disposed) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Show);
                return;
            }
            if (!IsVisible) base.Show();
            if (ShowActivated) Activate();
        }

        public new void Hide()
        {
            if (_disposed) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Hide);
                return;
            }
            if (IsVisible) base.Hide();
        }

        public new void Close()
        {
            if (_disposed) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Close);
                return;
            }
            base.Close();
        }

        public void SetBounds(double left, double top, double width, double height)
        {
            if (_disposed) return;
            Dispatcher.Invoke(() =>
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                if (!_resizable)
                {
                    MinWidth = MaxWidth = width;
                    MinHeight = MaxHeight = height;
                }
            });
        }

        public void SetState(object state)
        {
            if (_disposed) return;
            _state = state;
            if (_webView.CoreWebView2 is null) return;
            Dispatcher.Invoke(PostState);
        }

        private void PostState()
        {
            if (_webView.CoreWebView2 is null || _state is null) return;
            var stateJson = JsonSerializer.Serialize(_state, _jsonOptions);
            _webView.CoreWebView2.PostWebMessageAsJson($"{{\"type\":\"feature.state\",\"payload\":{stateJson}}}");
        }

        private void ApplyWindowStyles(FeatureUiWindowOptions options)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExToolWindow;
            if (!options.ActivateOnShow) style |= WsExNoActivate;
            if (options.ClickThrough) style |= WsExTransparent;
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            MessageReceived = null;
            _webView.Dispose();
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    }

    private sealed class ReactFeatureSurface : IFeatureUiSurface
    {
        private readonly string _view;
        private readonly string _uiDirectory;
        private readonly string _hostName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly WebView2 _webView;
        private object? _state;
        private bool _ready;
        private bool _disposed;

        public ReactFeatureSurface(string view, string uiDirectory, string hostName, JsonSerializerOptions jsonOptions)
        {
            _view = view;
            _uiDirectory = uiDirectory;
            _hostName = hostName;
            _jsonOptions = jsonOptions;
            _webView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0) };
            _webView.Loaded += OnLoaded;
        }

        public FrameworkElement Element => _webView;
        public event Action<string, JsonElement>? MessageReceived;

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
                var core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.SetVirtualHostNameToFolderMapping(_hostName, _uiDirectory,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += (_, navigation) =>
                {
                    if (!navigation.Uri.StartsWith($"https://{_hostName}/", StringComparison.OrdinalIgnoreCase))
                        navigation.Cancel = true;
                };
                core.NewWindowRequested += (_, request) => request.Handled = true;
                core.WebMessageReceived += OnWebMessageReceived;
                core.Navigate($"https://{_hostName}/index.html?view={Uri.EscapeDataString(_view)}");
            }
            catch (Exception exception)
            {
                App.Instance.Logger?.Error($"React yüzeyi başlatılamadı: {_view}", exception);
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                using var document = JsonDocument.Parse(args.WebMessageAsJson);
                var root = document.RootElement;
                var command = root.GetProperty("command").GetString();
                if (command == "feature.ready")
                {
                    _ready = true;
                    PostState();
                    return;
                }
                var payload = root.TryGetProperty("payload", out var value) ? value.Clone() : default;
                if (!string.IsNullOrWhiteSpace(command)) MessageReceived?.Invoke(command, payload);
            }
            catch (Exception exception)
            {
                App.Instance.Logger?.Error($"React yüzeyi komutu işlenemedi: {_view}", exception);
            }
        }

        public void SetState(object state)
        {
            if (_disposed) return;
            _state = state;
            if (!_ready || _webView.CoreWebView2 is null) return;
            var dispatcher = _webView.Dispatcher;
            if (dispatcher.CheckAccess()) PostState();
            else dispatcher.BeginInvoke(PostState);
        }

        private void PostState()
        {
            if (_disposed || !_ready || _webView.CoreWebView2 is null || _state is null) return;
            var stateJson = JsonSerializer.Serialize(_state, _jsonOptions);
            _webView.CoreWebView2.PostWebMessageAsJson($"{{\"type\":\"feature.state\",\"payload\":{stateJson}}}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            MessageReceived = null;
            _webView.Dispose();
        }
    }
}
