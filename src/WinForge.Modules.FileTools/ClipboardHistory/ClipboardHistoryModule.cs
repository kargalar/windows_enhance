using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;
using WinForge.History;
using Localization = WinForge.Core.Localization;

namespace WinForge.FileTools.ClipboardHistory;

public sealed class ClipboardHistoryModule : ITweakModule, IAsyncTweakModule, IQuickActionProvider
{
    private const int DefaultRetentionDays = 7;
    private const string RetentionKey = "retention-days";
    private readonly string _databasePath;
    private IModuleContext _context = null!;
    private Dispatcher _dispatcher = null!;
    private ClipboardHistoryStore? _store;
    private ClipboardHistoryMonitor? _monitor;
    private DispatcherTimer? _retention;
    private IDisposable? _hotkey;
    private IFeatureUiWindow? _window;
    private Task _writes = Task.CompletedTask;
    private int _days = DefaultRetentionDays;
    private string _query = "", _kind = "all", _error = "";
    private int _session, _request, _page = 1;
    private bool _copying, _hasNext, _loadingMore;
    private IntPtr _previousWindow;
    private List<ClipboardHistoryItem> _items = [];

    public ClipboardHistoryModule() : this(Path.Combine(AppData.DirectoryPath, "ClipboardHistory", "history.db")) { }

    internal ClipboardHistoryModule(string databasePath) => _databasePath = databasePath;

    public string Id => "clipboard-history";
    public string Name => "Pano Geçmişi";
    public string Category => "Verimlilik";
    public string Description => "Win+V ile kopyalanan metin ve görsellere erişin. Geçmiş bu cihazda saklanır.";
    public string? Warning => null;
    public bool IsActive => _monitor is not null;
    public IReadOnlyList<SettingDescriptor> Settings =>
    [
        new() { Key = "open", Label = "Pano geçmişini aç", Kind = SettingKind.Action, Execute = Open },
        new()
        {
            Key = RetentionKey, Label = "Otomatik silme süresi (gün)", Kind = SettingKind.Number,
            Default = DefaultRetentionDays, Min = 0, Max = 3650,
            Description = "Bu süreden eski öğeler otomatik silinir. Süresiz saklamak için 0 girin."
        }
    ];

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        var store = new ClipboardHistoryStore(_databasePath);
        store.Initialize();
        Start(store);
    }

    public async Task ActivateAsync()
    {
        if (IsActive) return;
        var store = new ClipboardHistoryStore(_databasePath);
        await Task.Run(store.Initialize);
        Start(store);
    }

    private void Start(ClipboardHistoryStore store)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _store = store;
        try
        {
            _monitor = new ClipboardHistoryMonitor(_context.Log, Record);
            _hotkey = _context.Hotkeys.Register(HotkeyModifiers.Win, 'V', Toggle);
            _retention = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _retention.Tick += OnRetention;
            _retention.Start();
            Apply();
        }
        catch
        {
            _ = Stop();
            throw;
        }
    }

    public void Deactivate() => Stop().GetAwaiter().GetResult();
    public Task DeactivateAsync() => Stop();

    private Task Stop()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        _monitor?.Dispose();
        _monitor = null;
        if (_retention is not null)
        {
            _retention.Stop();
            _retention.Tick -= OnRetention;
            _retention = null;
        }
        ++_session;
        _window?.Close();
        _window = null;
        _store = null;
        _items = [];
        _page = 1;
        _hasNext = false;
        _loadingMore = false;
        return _writes;
    }

    public void Apply()
    {
        _days = Math.Clamp(_context.Settings.GetInt(RetentionKey, DefaultRetentionDays), 0, 3650);
        Expire();
        if (_window?.IsVisible == true) _ = RefreshAsync();
    }

    public Task ApplyAsync() { Apply(); return _writes; }

    private long Cutoff => _days == 0 ? 0 : DateTimeOffset.UtcNow.AddDays(-_days).ToUnixTimeMilliseconds();

    private void OnRetention(object? sender, EventArgs args) => Expire();

    private void Expire()
    {
        var cutoff = Cutoff;
        if (cutoff != 0)
            QueueWrite(store => store.Expire(cutoff) > 0, "Pano geçmişinin saklama süresi uygulanamadı.");
    }

    private void Record(ClipboardHistorySnapshot snapshot) => QueueWrite(store =>
    {
        ClipboardHistoryImageData? image = null;
        try { image = ClipboardHistoryImage.Prepare(snapshot); }
        catch (Exception error) { _context.Log.Error("Pano görseli işlenemedi.", error); }
        if (snapshot.Text is null && image is null) return false;
        store.Add(snapshot.CopiedAt, snapshot.Text, image);
        return true;
    }, "Pano geçmişi kaydedilemedi.");

    private void QueueWrite(Func<ClipboardHistoryStore, bool> operation, string failure)
    {
        if (_store is not { } store) return;
        _writes = WriteAsync(_writes, store, operation, failure);
    }

    private async Task WriteAsync(Task previous, ClipboardHistoryStore store, Func<ClipboardHistoryStore, bool> operation, string failure)
    {
        try
        {
            await previous.ConfigureAwait(false);
            if (!await Task.Run(() => operation(store)).ConfigureAwait(false) || _dispatcher.HasShutdownStarted) return;
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_store, store) && _window?.IsVisible == true && _page == 1 && !_loadingMore) _ = RefreshAsync();
            });
        }
        catch (Exception error) { _context.Log.Error(failure, error); }
    }

    private void Toggle()
    {
        if (_window?.IsVisible == true) Dismiss();
        else Open();
    }

    private void Open()
    {
        if (!IsActive)
        {
            _context.Notify(Message("Önce Pano Geçmişi modülünü etkinleştirin.", "Enable the Clipboard History module first."));
            return;
        }
        try
        {
            if (_window?.IsVisible == true) return;
            var ui = (_context as IFeatureUiContext)?.FeatureUi ?? throw new InvalidOperationException("WinForge UI is unavailable.");
            if (_window is null)
            {
                var window = ui.Open(Id, Id, Localization.Get(Name),
                    new FeatureUiWindowOptions(440, 560, Topmost: true, ShowOnOpen: false, DismissOnDeactivate: true));
                window.MessageReceived += HandleMessage;
                window.ClosedByHost += () => { if (ReferenceEquals(_window, window)) _window = null; };
                _window = window;
            }

            _previousWindow = Native.GetForegroundWindow();
            var area = CursorPanelPlacement.WorkArea;
            var width = Math.Min(440, area.Width - 24);
            var height = Math.Min(560, area.Height - 24);
            _window.SetBounds(area.Right - width - 12, area.Bottom - height - 12, width, height);
            ++_session;
            _query = "";
            _kind = "all";
            _error = "";
            _page = 1;
            _hasNext = false;
            _loadingMore = false;
            _items = [];
            _copying = false;
            Publish(loading: true);
            _window.Show();
            _ = RefreshAsync();
        }
        catch (Exception error)
        {
            _context.Log.Error("Pano geçmişi açılamadı.", error);
            _context.Notify(Message("Pano geçmişi açılamadı.", "Clipboard history could not open."));
        }
    }

    private void Dismiss()
    {
        ++_session;
        _window?.Hide();
        if (Native.IsWindow(_previousWindow)) Native.SetForegroundWindow(_previousWindow);
    }

    private void HandleMessage(string command, JsonElement payload)
    {
        if (_window?.IsVisible != true) return;
        switch (command)
        {
            case "clipboard-history.close":
                Dismiss();
                break;
            case "clipboard-history.filter":
                _query = (payload.GetProperty("query").GetString() ?? "").Trim();
                var kind = payload.GetProperty("kind").GetString();
                _kind = kind is "text" or "image" ? kind : "all";
                _page = 1;
                _hasNext = false;
                _loadingMore = false;
                _error = "";
                _ = RefreshAsync();
                break;
            case "clipboard-history.load-more":
                if (_loadingMore || !_hasNext || _items.Count == 0) return;
                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("page", out var pageElement) &&
                    pageElement.TryGetInt32(out var requestedPage) && requestedPage != _page) return;
                _error = "";
                _ = LoadNextPageAsync();
                break;
            case "clipboard-history.copy":
                var id = payload.GetProperty("id").GetInt64();
                if (_items.Any(item => item.Id == id)) _ = CopyAsync(id);
                break;
        }
    }

    private async Task RefreshAsync()
    {
        if (_store is not { } store || _window?.IsVisible != true) return;
        var request = ++_request;
        var session = _session;
        _loadingMore = false;
        var (query, kind, cutoff, language) = (_query, _kind, Cutoff, Localization.CurrentLanguage);
        try
        {
            var fetched = await Task.Run(() => store.List(query, kind, 0, cutoff, language));
            if (request != _request || !IsVisible(session)) return;
            _hasNext = fetched.Count > ClipboardHistoryStore.PageSize;
            _items = [.. fetched.Take(ClipboardHistoryStore.PageSize)];
            _page = 1;
            Publish();
        }
        catch (Exception error)
        {
            _context.Log.Error("Pano geçmişi yüklenemedi.", error);
            if (request != _request || !IsVisible(session)) return;
            _items = [];
            _hasNext = false;
            _page = 1;
            _loadingMore = false;
            _error = Message("Pano geçmişi yüklenemedi.", "Clipboard history could not load.");
            Publish();
        }
    }

    private async Task LoadNextPageAsync()
    {
        if (_store is not { } store || _window?.IsVisible != true || _loadingMore || !_hasNext || _items.Count == 0) return;
        var request = _request;
        var session = _session;
        var beforeId = _items[^1].Id;
        var (query, kind, cutoff, language) = (_query, _kind, Cutoff, Localization.CurrentLanguage);
        _loadingMore = true;
        Publish(loadingMore: true);
        try
        {
            var fetched = await Task.Run(() => store.List(query, kind, 0, cutoff, language, beforeId));
            if (request != _request || !IsVisible(session)) return;
            _loadingMore = false;
            _hasNext = fetched.Count > ClipboardHistoryStore.PageSize;
            var existingIds = _items.Select(item => item.Id).ToHashSet();
            var appended = 0;
            foreach (var item in fetched.Take(ClipboardHistoryStore.PageSize))
            {
                if (!existingIds.Add(item.Id)) continue;
                _items.Add(item);
                appended++;
            }
            if (appended > 0) _page++;
            Publish();
        }
        catch (Exception error)
        {
            _context.Log.Error("Pano geçmişi devamı yüklenemedi.", error);
            if (request != _request || !IsVisible(session)) return;
            _loadingMore = false;
            _error = Message("Daha fazla öğe yüklenemedi.", "Could not load more items.");
            Publish();
        }
    }

    private async Task CopyAsync(long id)
    {
        if (_copying || _store is not { } store) return;
        _copying = true;
        var session = _session;
        var cutoff = Cutoff;
        try
        {
            var content = await Task.Run(() => store.Get(id, cutoff));
            if (!IsVisible(session)) return;
            if (content is null)
            {
                _error = Message("Bu öğenin saklama süresi doldu.", "This item has expired.");
                _ = RefreshAsync();
                return;
            }

            var data = new DataObject();
            using var marker = new MemoryStream([1], writable: false);
            using var pngStream = content.Png is null ? null : new MemoryStream(content.Png, writable: false);
            if (content.Text is { } text) data.SetText(text, TextDataFormat.UnicodeText);
            if (content.Png is { } png)
            {
                var image = await Task.Run(() => ClipboardHistoryImage.Decode(png));
                if (!IsVisible(session)) return;
                data.SetImage(image);
                data.SetData(ClipboardHistoryMonitor.PngFormat, pngStream, autoConvert: false);
            }
            data.SetData(ClipboardHistoryMonitor.ExcludeFormat, marker, autoConvert: false);
            for (var attempt = 0; IsVisible(session); attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(data, copy: true);
                    Dismiss();
                    return;
                }
                catch (ExternalException error) when (error.HResult == ClipboardHistoryMonitor.ClipboardBusy && attempt < 5)
                {
                    await Task.Delay(45);
                }
            }
        }
        catch (Exception error)
        {
            _context.Log.Error("Geçmişteki öğe panoya kopyalanamadı.", error);
            if (!IsVisible(session)) return;
            _error = Message("Panoya kopyalanamadı. Tekrar deneyin.", "Could not copy to the clipboard. Try again.");
            Publish();
        }
        finally { if (session == _session) _copying = false; }
    }

    private bool IsVisible(int session) => _session == session && _window?.IsVisible == true;

    private void Publish(bool loading = false, bool loadingMore = false)
    {
        var background = Application.Current?.TryFindResource("BgBrush") as SolidColorBrush;
        _window?.SetState(new
        {
            session = _session, language = Localization.CurrentLanguage, appearance = background?.Color.R > 127 ? "light" : "dark",
            query = _query, kind = _kind, loading, loadingMore, error = _error,
            page = _page,
            hasNext = _hasNext,
            items = _items.Select(item => new
            {
                item.Id, item.CopiedAt, item.Text, item.Width, item.Height,
                thumbnail = item.Thumbnail is { } image ? "data:image/png;base64," + Convert.ToBase64String(image) : null
            }).ToArray()
        });
    }

    private static string Message(string tr, string en) => Localization.IsTurkish() ? tr : en;

    public IEnumerable<QuickAction> GetQuickActions(string query)
    {
        if (IsActive) yield return new QuickAction(Id + ".open", Localization.Get(Name), "clipboard pano geçmişi Win+V", Open);
    }
}
