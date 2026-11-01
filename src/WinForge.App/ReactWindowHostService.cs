using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
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
    private readonly Task<CoreWebView2Environment> _webViewEnvironment;
    private bool _disposed;

    internal Task<CoreWebView2Environment> WebViewEnvironment => _webViewEnvironment;

    public ReactWindowHostService()
    {
        var userDataDirectory = Path.Combine(WinForge.Core.AppData.DirectoryPath, "WebView2");
        Directory.CreateDirectory(userDataDirectory);
        _webViewEnvironment = CoreWebView2Environment.CreateAsync(null, userDataDirectory);
    }

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
            if (options.ShowOnOpen) existing.Show();
            return existing;
        }

        var window = new ReactFeatureWindow(key, view, title, options, _uiDirectory, HostName, _jsonOptions, _webViewEnvironment);
        window.Closed += OnWindowClosed;
        _windows[key] = window;
        if (options.ShowOnOpen) window.Show();
        return window;
    }

    public IFeatureUiSurface CreateSurface(string view)
    {
        if (string.IsNullOrWhiteSpace(view)) throw new ArgumentException("Arayüz görünümü boş olamaz.", nameof(view));
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ReactFeatureSurface(view, _uiDirectory, HostName, _jsonOptions, _webViewEnvironment);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel") =>
        ShowDialogAsync(title, message, confirmLabel, cancelLabel, isConfirmation: true);

    public async Task ShowMessageAsync(string title, string message, string closeLabel = "Close") =>
        _ = await ShowDialogAsync(title, message, closeLabel, "", isConfirmation: false);

    private Task<bool> ShowDialogAsync(string title, string message, string confirmLabel, string cancelLabel, bool isConfirmation)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = Open($"app-dialog-{Guid.NewGuid():N}", "app-dialog", title,
            new FeatureUiWindowOptions(520, 280, Topmost: true));

        void Complete(bool confirmed)
        {
            window.MessageReceived -= OnMessage;
            window.ClosedByHost -= OnClosed;
            window.Close();
            completion.TrySetResult(confirmed);
        }

        void OnMessage(string command, JsonElement payload)
        {
            if (command != "dialog.resolve") return;
            Complete(payload.TryGetProperty("confirmed", out var value) && value.ValueKind == JsonValueKind.True);
        }

        void OnClosed()
        {
            window.MessageReceived -= OnMessage;
            window.ClosedByHost -= OnClosed;
            completion.TrySetResult(false);
        }

        window.MessageReceived += OnMessage;
        window.ClosedByHost += OnClosed;
        window.SetState(new { title, message, confirmLabel, cancelLabel, isConfirmation });
        return completion.Task;
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
        private readonly Task<CoreWebView2Environment> _webViewEnvironment;
        private readonly WebView2 _webView;
        private readonly bool _resizable;
        private bool _centerAtCursor;
        private object? _state;
        private bool _disposed;

        public ReactFeatureWindow(string key, string view, string title, FeatureUiWindowOptions options,
            string uiDirectory, string hostName, JsonSerializerOptions jsonOptions,
            Task<CoreWebView2Environment> webViewEnvironment)
        {
            Key = key;
            _view = view;
            _uiDirectory = uiDirectory;
            _hostName = hostName;
            _jsonOptions = jsonOptions;
            _webViewEnvironment = webViewEnvironment;
            _resizable = options.Resizable;
            _centerAtCursor = options.Left is null && options.Top is null;
            Title = title;
            Width = options.Width;
            Height = options.Height;
            MinWidth = options.Resizable ? Math.Min(options.Width, 480) : options.Width;
            MinHeight = options.Resizable ? Math.Min(options.Height, 320) : options.Height;
            MaxWidth = options.Resizable ? double.PositiveInfinity : options.Width;
            MaxHeight = options.Resizable ? double.PositiveInfinity : options.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = options.Resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
            ShowInTaskbar = options.ShowInTaskbar;
            ShowActivated = options.ActivateOnShow;
            Topmost = options.Topmost;
            AllowsTransparency = options.Transparent;
            // A fully transparent layered WPF host passes mouse input through pixels
            // painted only by the child WebView HWND. Keep interactive hosts hit-testable.
            Background = options.Transparent
                ? options.ClickThrough ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
                : (Brush)Application.Current.FindResource("BgBrush");
            WindowStartupLocation = options.Transparent ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen;
            if (options.Left is not null || options.Top is not null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = options.Left ?? 0;
                Top = options.Top ?? 0;
            }
            IsHitTestVisible = !options.ClickThrough;

            _webView = new WebView2
            {
                DefaultBackgroundColor = options.Transparent
                    ? System.Drawing.Color.FromArgb(0, 0, 0, 0)
                    : System.Drawing.Color.FromArgb(255, 9, 10, 12)
            };
            Content = _webView;
            SourceInitialized += (_, _) => ApplyWindowStyles(options);
            Loaded += OnLoaded;
            Closed += (_, _) => ClosedByHost?.Invoke();
        }

        public string Key { get; }
        public bool IsOpen => !_disposed;
        public new bool IsVisible => !_disposed && (Dispatcher.CheckAccess() ? base.IsVisible : Dispatcher.Invoke(() => base.IsVisible));
        public event Action<string, JsonElement>? MessageReceived;
        public event Action? ClosedByHost;

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            // Hide/Show raises Loaded again; keep the existing controller and page.
            Loaded -= OnLoaded;
            try
            {
                await _webView.EnsureCoreWebView2Async(await _webViewEnvironment);
                if (_disposed) return;
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
            catch (COMException) when (_disposed)
            {
                // Closing a transient window can cancel WebView2 controller creation.
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
                if (command == "feature.drag")
                {
                    try { DragMove(); }
                    catch (InvalidOperationException) { }
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
            if (!IsVisible)
            {
                if (_centerAtCursor) CursorPanelPlacement.Center(this);
                base.Show();
            }
            if (ShowActivated) Activate();
            if (Topmost)
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
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
                // Explicit placement also applies to subsequent Hide/Show cycles.
                _centerAtCursor = false;
                WindowStartupLocation = WindowStartupLocation.Manual;
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
            Dispatcher.Invoke(() =>
            {
                if (_disposed) return;
                _state = state;
                PostState();
            });
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
            ClosedByHost = null;
            _webView.Dispose();
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    }

    private sealed class ReactFeatureSurface : IFeatureUiAssetSurface
    {
        private readonly string _view;
        private readonly string _uiDirectory;
        private readonly string _hostName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Task<CoreWebView2Environment> _webViewEnvironment;
        private readonly WebView2 _webView;
        private readonly Dictionary<string,string> _assetHosts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,Uri> _mappedAssets = new(StringComparer.OrdinalIgnoreCase);
        private object? _state;
        private bool _ready;
        private bool _disposed;

        public ReactFeatureSurface(string view, string uiDirectory, string hostName, JsonSerializerOptions jsonOptions,
            Task<CoreWebView2Environment> webViewEnvironment)
        {
            _view = view;
            _uiDirectory = uiDirectory;
            _hostName = hostName;
            _jsonOptions = jsonOptions;
            _webViewEnvironment = webViewEnvironment;
            _webView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0) };
            _webView.Loaded += OnLoaded;
        }

        public FrameworkElement Element => _webView;
        public event Action<string, JsonElement>? MessageReceived;

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            try
            {
                await _webView.EnsureCoreWebView2Async(await _webViewEnvironment);
                var core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.SetVirtualHostNameToFolderMapping(_hostName, _uiDirectory,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                foreach(var mapping in _assetHosts)
                    core.SetVirtualHostNameToFolderMapping(mapping.Key,mapping.Value,CoreWebView2HostResourceAccessKind.Allow);
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
            _webView.Dispatcher.Invoke(() =>
            {
                if (_disposed) return;
                _state = state;
                PostState();
            });
        }

        public Uri MapLocalAsset(string filePath)
        {
            var fullPath=Path.GetFullPath(filePath);
            if(!File.Exists(fullPath))throw new FileNotFoundException("Preview file was not found.",fullPath);
            if(_mappedAssets.TryGetValue(fullPath,out var existing))return existing;
            var directory=Path.GetDirectoryName(fullPath)??throw new InvalidOperationException("Preview file has no parent directory.");
            var hostName="asset-"+Guid.NewGuid().ToString("N")+".winforge";
            _assetHosts.Add(hostName,directory);
            if(_webView.CoreWebView2 is { } core)
            {
                if(_webView.Dispatcher.CheckAccess())core.SetVirtualHostNameToFolderMapping(hostName,directory,CoreWebView2HostResourceAccessKind.Allow);
                else _webView.Dispatcher.Invoke(()=>core.SetVirtualHostNameToFolderMapping(hostName,directory,CoreWebView2HostResourceAccessKind.Allow));
            }
            var uri=new Uri($"https://{hostName}/{Uri.EscapeDataString(Path.GetFileName(fullPath))}");
            _mappedAssets[fullPath]=uri;
            return uri;
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
