using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace WinForge.Modules.NetSpeed;

public sealed class CodexQuotaModule : ITweakModule, IModulePanel
{
    private IModuleContext? _context;
    private QuotaIndicator? _indicator;
    public string Id => "codex-quota";
    public string Name => "Codex Limitleri";
    public string Category => "Taskbar";
    public string Description => "AI Limit Status eklentisindeki Codex hesaplarının kalan limitlerini görev çubuğunda taşınabilir hesap taşları olarak gösterir.";
    public string? Warning => null;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[] { new SettingDescriptor { Key = "position-left", Label = "Soldan uzaklık (px)", Kind = SettingKind.Number, Default = 0, Min = 0, Max = 5000, Description = "Göstergeyi fareyle sürükleyebilir veya konumunu burada belirleyebilirsiniz." } };
    public event Action? PanelStateChanged;
    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() { _indicator = new QuotaIndicator(ReadAccounts, _context!); IsActive = true; }
    public void Deactivate() { IsActive = false; _indicator?.Dispose(); _indicator = null; }
    public void Apply() => _indicator?.Refresh();
    private static string StorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai_limit_accounts", "accounts.json");
    private sealed record Account(string Id, string Email, string Alias, bool Visible, string Status, string Error, WindowInfo[] Windows);
    private sealed record WindowInfo(string Label, double Remaining, long? ResetsAt, string ResetLabel);
    private Account[] ReadAccounts()
    {
        try
        {
            using var stream = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.GetProperty("accounts").EnumerateArray()
                .Where(a => Get(a, "provider") == "codex" && Get(a, "id") is not null).Select(a => {
                    var id = Get(a, "id")!;
                    var windows = a.TryGetProperty("windows", out var ws) && ws.ValueKind == JsonValueKind.Array
                        ? ws.EnumerateArray().Where(w => w.TryGetProperty("remainingPercent", out var p) && p.ValueKind == JsonValueKind.Number)
                            .Select(w => new WindowInfo(Get(w, "label") ?? "Limit", w.GetProperty("remainingPercent").GetDouble(),
                                w.TryGetProperty("resetsAt", out var at) && at.TryGetInt64(out var ms) ? ms : null, Get(w, "resetLabel") ?? "")).ToArray()
                        : Array.Empty<WindowInfo>();
                    return new Account(id, Get(a, "email") ?? "Hesap", _context!.Settings.GetString("alias-" + id, ""),
                        _context.Settings.GetBool("visible-" + id, true), Get(a, "status") ?? "", Get(a, "error") ?? "", windows);
                }).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { return []; }
    }
    private static string? Get(JsonElement element, string name) => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public object GetPanelState() => new { kind = "codex-quota", accounts = ReadAccounts(), updatedAt = File.Exists(StorePath) ? File.GetLastWriteTime(StorePath).ToString("dd.MM.yyyy HH:mm") : "" };
    public void HandlePanelCommand(string action, JsonElement payload)
    {
        if (action == "refresh") { PanelStateChanged?.Invoke(); _indicator?.Refresh(); return; }
        var id = payload.GetProperty("id").GetString() ?? "";
        if (!ReadAccounts().Any(a => a.Id == id)) throw new InvalidOperationException("Hesap bulunamadı; listeyi yenileyin.");
        if (action == "visibility") _context!.Settings.Set("visible-" + id, payload.GetProperty("visible").GetBoolean());
        else if (action == "alias") _context!.Settings.Set("alias-" + id, (payload.GetProperty("alias").GetString() ?? "").Trim()[..Math.Min((payload.GetProperty("alias").GetString() ?? "").Trim().Length, 80)]);
        else throw new InvalidOperationException("Bilinmeyen işlem.");
        _indicator?.Refresh(); PanelStateChanged?.Invoke();
    }

    private sealed class QuotaIndicator : IDisposable
    {
        private readonly Func<Account[]> _read;
        private readonly IModuleContext _context;
        private ShellDiagnosticsBroker.Lease? _lease;
        private uint _processId;
        private string _lastRules = "";
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly Window _popup;
        private readonly StackPanel _details = new();
        private Account[] _accounts = [];
        private ShellVisualNode[] _tiles = [];
        private int _shownIndex = -1;
        private DateTime _lastRefresh;
        private ShellVisualNode[] _clocks = [];
        private bool _dragging;
        private bool _mouseWasDown;
        private int _dragStart;
        private int _offsetStart;
        private DateTime _lastTree;
        public QuotaIndicator(Func<Account[]> read, IModuleContext context)
        {
            _read = read;
            _context = context;
            var surface = new Border { Background = Brush("#F5181818"), BorderBrush = Brush("#FF444444"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = _details };
            _popup = new Window { Width = 380, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Background = Brushes.Transparent, Content = surface };
            _timer.Tick += (_, _) => {
                CheckPointer();
                if (DateTime.UtcNow - _lastRefresh >= TimeSpan.FromSeconds(10)) Refresh();
            };
            _timer.Start(); Refresh();
        }
        private void CheckPointer()
        {
            var point = System.Windows.Forms.Control.MousePosition;
            var down = (Native.GetAsyncKeyState(1) & 0x8000) != 0;
            if (_lease is not null && DateTime.UtcNow - _lastTree > TimeSpan.FromSeconds(1))
            {
                _lastTree = DateTime.UtcNow;
                try {
                    var nodes = _lease.Session.ReadVisualTree();
                    _clocks = nodes.Where(n => n.Name == "WinForgeQuotaText").ToArray();
                    _tiles = nodes.Where(n => n.Name.StartsWith("WinForgeQuotaTile", StringComparison.Ordinal)).ToArray();
                } catch { _clocks = []; _tiles = []; }
            }
            var over = _clocks.Any(clock => clock.ScreenWidth > 0 && clock.ScreenHeight > 0 && point.X >= clock.ScreenLeft && point.X < clock.ScreenLeft + clock.ScreenWidth && point.Y >= clock.ScreenTop && point.Y < clock.ScreenTop + clock.ScreenHeight);
            if (down && !_mouseWasDown && over)
            {
                _dragging = true; _dragStart = point.X; _offsetStart = _context.Settings.GetInt("position-left", 0); _popup.Hide();
            }
            if (_dragging && down)
            {
                var taskbar = Native.FindWindow("Shell_TrayWnd", null);
                var scale = taskbar == IntPtr.Zero ? 1 : Native.GetDpiForWindow(taskbar) / 96.0;
                if (scale <= 0) scale = 1;
                var offset = Math.Clamp(_offsetStart + (int)((point.X - _dragStart) / scale), 0, 5000);
                if (offset != _context.Settings.GetInt("position-left", 0)) { _context.Settings.Set("position-left", offset); Refresh(); }
            }
            if (!down) _dragging = false;
            _mouseWasDown = down;
            var hoveredTile = over && !_dragging && !down
                ? _tiles.FirstOrDefault(n => point.X >= n.ScreenLeft && point.X < n.ScreenLeft + n.ScreenWidth && point.Y >= n.ScreenTop && point.Y < n.ScreenTop + n.ScreenHeight)
                : null;
            if (hoveredTile is { } tile)
            {
                var index = !int.TryParse(tile.Name["WinForgeQuotaTile".Length..], out var parsed) ? -1 : parsed;
                if (index >= 0 && index < _accounts.Length)
                {
                    if (_shownIndex != index) { _shownIndex = index; BuildDetails(_accounts[index]); }
                    var screen = System.Windows.Forms.Screen.FromPoint(point);
                    var scale = VisualTreeHelper.GetDpi(_popup).DpiScaleX;
                    var content = (FrameworkElement)_popup.Content;
                    content.Measure(new System.Windows.Size(_popup.Width, double.PositiveInfinity));
                    var height = content.DesiredSize.Height;
                    _popup.Left = Math.Max(screen.WorkingArea.Left / scale, Math.Min(tile.ScreenLeft / scale, screen.WorkingArea.Right / scale - _popup.Width));
                    var above = (tile.ScreenTop / scale) - height;
                    _popup.Top = above >= screen.WorkingArea.Top / scale ? above : (tile.ScreenTop + tile.ScreenHeight) / scale;
                    if (!_popup.IsVisible) _popup.Show();
                }
            }
            else if (_popup.IsVisible && !_popup.IsMouseOver) _popup.Hide();
        }
        private void ApplyTaskbar(string text)
        {
            try
            {
                var taskbar = Native.FindWindow("Shell_TrayWnd", null);
                if (taskbar == IntPtr.Zero) return;
                Native.GetWindowThreadProcessId(taskbar, out var processId);
                if (_lease is null || _processId != processId || !_lease.Session.IsConnected)
                {
                    _lease?.Dispose();
                    _lease = ShellDiagnosticsBroker.Acquire(processId, Path.Combine(AppContext.BaseDirectory, "native"));
                    _processId = processId; _lastRules = "";
                }
                var rules = new[] { new ShellStyleRule("Taskbar.TaskbarFrame", new Dictionary<string,string> {
                    ["QuotaText"] = text, ["QuotaOffset"] = Math.Clamp(_context.Settings.GetInt("position-left", 0), 0, 5000).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }) };
                var serialized = JsonSerializer.Serialize(rules);
                if (serialized != _lastRules) { _lease.Apply(rules); _lastRules = serialized; }
            }
            catch (Exception e) { _context.Log.Error("Codex görev çubuğu metni uygulanamadı.", e); _lease?.Dispose(); _lease = null; _lastRules = ""; }
        }
        private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        private static string Initials(Account account)
        {
            var name = string.IsNullOrWhiteSpace(account.Alias) ? account.Email.Split('@')[0] : account.Alias;
            var words = name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
            var result = words.Length > 1 ? string.Concat(words[0][0], words[1][0]) : name.Length > 1 ? name[..2] : name;
            return string.IsNullOrWhiteSpace(result) ? "?" : result.ToUpperInvariant();
        }
        private static WindowInfo? FiveHour(Account account) => account.Status is "error" or "expired" ? null : account.Windows.FirstOrDefault(w => string.Equals(w.Label.Trim(), "5h", StringComparison.OrdinalIgnoreCase));
        private static string ResetTime(WindowInfo? window)
        {
            if (window?.ResetsAt is long ms) try { return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm"); } catch (ArgumentOutOfRangeException) { }
            return string.IsNullOrWhiteSpace(window?.ResetLabel) ? "—" : window.ResetLabel;
        }
        private static string Countdown(WindowInfo? window)
        {
            if (window?.ResetsAt is not long ms) return "—";
            try {
                var left = DateTimeOffset.FromUnixTimeMilliseconds(ms) - DateTimeOffset.UtcNow;
                if (left <= TimeSpan.Zero) return "Yenileniyor";
                return left.TotalDays >= 1 ? $"{(int)left.TotalDays}g {left.Hours}sa" : $"{(int)left.TotalHours}sa {left.Minutes}dk";
            } catch (ArgumentOutOfRangeException) { return "—"; }
        }
        private static string Accent(WindowInfo? window) => "#FFAAAAAA";
        private static TextBlock Label(string text, double size, string color, FontWeight weight) => new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis };
        private void BuildDetails(Account account)
        {
            _details.Children.Clear();
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 14) };
            var badge = new Border { Background = Brush("#FF3B3B3B"), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 8, 11, 8), Margin = new Thickness(0, 0, 12, 0), Child = Label(Initials(account), 18, "#FFFFFFFF", FontWeights.SemiBold) };
            DockPanel.SetDock(badge, Dock.Left); header.Children.Add(badge);
            var title = new StackPanel(); title.Children.Add(Label("Codex kullanım limitleri", 18, "#FFFFFFFF", FontWeights.SemiBold));
            title.Children.Add(Label(string.IsNullOrWhiteSpace(account.Alias) ? account.Email : account.Alias + " · " + account.Email, 12, "#FFFFFFFF", FontWeights.Normal));
            header.Children.Add(title); _details.Children.Add(header);
            if (account.Status is "error" or "expired") _details.Children.Add(Label("Güncellenemedi: " + account.Error, 13, "#FFFFFFFF", FontWeights.Normal));
            else if (account.Windows.Length == 0) _details.Children.Add(Label("Limit bilgisi bulunamadı.", 13, "#FFFFFFFF", FontWeights.Normal));
            else foreach (var window in account.Windows)
            {
                _details.Children.Add(new Border { Height = 1, Background = Brush("#FF383838"), Margin = new Thickness(0, 0, 0, 12) });
                var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var description = new StackPanel(); description.Children.Add(Label(window.Label, 14, "#FFFFFFFF", FontWeights.SemiBold));
                description.Children.Add(Label($"Kalan %{Math.Clamp(window.Remaining, 0, 100):0}", 12, "#FFFFFFFF", FontWeights.Normal)); row.Children.Add(description);
                var progress = new System.Windows.Controls.ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(window.Remaining, 0, 100), Height = 8, Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(Accent(window)), Background = Brush("#FF3B3B3B"), BorderThickness = new Thickness(0) };
                Grid.SetColumn(progress, 1); row.Children.Add(progress);
                var reset = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                reset.Children.Add(Label(Countdown(window), 13, "#FFFFFFFF", FontWeights.SemiBold));
                var resetText = window.ResetLabel;
                if (window.ResetsAt is long ms) try { resetText = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("dd.MM HH:mm"); } catch (ArgumentOutOfRangeException) { }
                if (window.Remaining > 0) reset.Children.Add(Label(resetText, 11, "#FFFFFFFF", FontWeights.Normal));
                Grid.SetColumn(reset, 2); row.Children.Add(reset); _details.Children.Add(row);
            }
        }
        public void Refresh()
        {
            _lastRefresh = DateTime.UtcNow;
            _accounts = _read().Where(a => a.Visible).ToArray();
            var items = _accounts.Select(account => {
                var window = FiveHour(account);
                var exhausted = window is not null && window.Remaining <= 0;
                return new { initials = Initials(account), percent = exhausted ? "" : window is null ? "—" : $"%{Math.Clamp(window.Remaining, 0, 100):0}", countdown = "◷ " + (exhausted ? Countdown(window) : ResetTime(window)), color = Accent(window) };
            }).ToArray();
            ApplyTaskbar(JsonSerializer.Serialize(items));
            if (_shownIndex >= 0 && _shownIndex < _accounts.Length) BuildDetails(_accounts[_shownIndex]);
            else { _shownIndex = -1; _popup.Hide(); }
        }
        public void Dispose() { _timer.Stop(); _popup.Close(); _lease?.Dispose(); _lease = null; }

    }
}

