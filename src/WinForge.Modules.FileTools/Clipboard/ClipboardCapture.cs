using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;
using WinForge.FileTools.UI;
using NativeClipboard = System.Windows.Clipboard;

using WinForge.FileTools;
namespace WinForge.ClipboardLine;

internal sealed class ClipboardCapture : IDisposable
{
    internal const string OwnCopyFormat = "WinForge.ClipboardLine.OwnCopy";
    private const int ClipboardUpdateMessage = 0x031D;
    private readonly ClipboardLibrary _library;
    private readonly IModuleContext _context;
    private readonly HwndSource _source;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _retention;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, byte> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    internal PasteTargetTracker PasteTargets { get; private set; } = null!;
    private readonly HashSet<(Guid Id, string Content)> _indexed = [];
    private readonly HashSet<(Guid Id, string Content)> _linkAttempts = [];
    private CancellationTokenSource? _linkCancellation;
    private uint _lastSequence;
    private bool _disposed;
    private bool _indexing;
    private bool _enriching;
    private int _attempts;

    internal ClipboardCapture(ClipboardLibrary library, IModuleContext context)
    {
        _library = library; _context = context;
        _source = new HwndSource(new HwndSourceParameters("WinForge ClipboardLine") { ParentWindow = new IntPtr(-3) });
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => ReadClipboard();
        _retention = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _retention.Tick += (_, _) => { try { _library.Expire(); } catch (Exception error) { _context.Log.Error("Pano saklama süresi uygulanamadı.", error); } };
        try
        {
            PasteTargets = new PasteTargetTracker();
            _source.AddHook(WindowProcedure);
            if (!AddClipboardFormatListener(_source.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _lastSequence = GetClipboardSequenceNumber();
            _retention.Start(); _library.Changed += LibraryChanged;
            ClipboardFileExport.RemoveExpiredDragFiles(_library.DirectoryPath, DateTimeOffset.UtcNow, _context.Log);
            RefreshWatchers(); _ = IndexImagesAsync(); _ = EnrichLinksAsync();
        }
        catch
        {
            _library.Changed -= LibraryChanged; _timer.Stop(); _retention.Stop();
            foreach (var watcher in _watchers) watcher.Dispose();
            RemoveClipboardFormatListener(_source.Handle); _source.RemoveHook(WindowProcedure); _source.Dispose();
            PasteTargets?.Dispose(); _lifetime.Dispose();
            throw;
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == ClipboardUpdateMessage && !_disposed) { _attempts = 0; _timer.Stop(); _timer.Start(); }
        return IntPtr.Zero;
    }

    private void ReadClipboard()
    {
        _timer.Stop(); if (_disposed) return;
        var sequence = GetClipboardSequenceNumber(); if (sequence == _lastSequence) return;
        try
        {
            var data = NativeClipboard.GetDataObject();
            if (data is null || IsPrivate(data)) { _lastSequence = sequence; return; }
            if (_library.Preferences.CaptureImages && data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                if (GetClipboardSequenceNumber() != sequence) { _timer.Start(); return; }
                ImportBitmap(bitmap);
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && text.Length > 0 && text.Length <= ClipboardLibrary.MaximumTextLength)
            {
                if (GetClipboardSequenceNumber() != sequence) { _timer.Start(); return; }
                if (ClipboardLibrary.IsStandaloneLink(text) ? _library.Preferences.CaptureLinks : _library.Preferences.CaptureText)
                    _library.AddText(text);
            }
            _lastSequence = sequence;
        }
        catch (ExternalException) { if (++_attempts < 5) _timer.Start(); }
        catch (Exception error) { _lastSequence = sequence; _context.Log.Error("Pano içeriği alınamadı.", error); }
    }

    internal static bool IsPrivate(IDataObject data)
    {
        if (data.GetDataPresent(OwnCopyFormat) || data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("org.nspasteboard.ConcealedType") || data.GetDataPresent("com.agilebits.onepassword")) return true;
        foreach (var name in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (!data.GetDataPresent(name)) continue;
            var value = data.GetData(name);
            if (value is MemoryStream stream) { var bytes = stream.ToArray(); if (bytes.Length < 4 || BitConverter.ToInt32(bytes) == 0) return true; }
            else if (value is byte[] bytes) { if (bytes.Length < 4 || BitConverter.ToInt32(bytes) == 0) return true; }
            else if (value is not int number || number == 0) return true;
        }
        return false;
    }

    internal ClipItem ImportBitmap(BitmapSource bitmap, string title = "Pano görseli")
    {
        if (bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1 || (long)bitmap.PixelWidth * bitmap.PixelHeight > 100_000_000) throw new InvalidDataException("Görsel 100 megapiksel sınırını aşıyor.");
        var temporary = Path.Combine(_library.DirectoryPath, Guid.NewGuid().ToString("N") + ".png");
        try { ImageEngine.Save(bitmap, temporary, "png"); return _library.AddMedia(temporary, title); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal async void ImportFile(string path)
    {
        if (_disposed) return;
        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".mp4" or ".mov")
            {
                await RecordingTools.InspectAsync(path, _lifetime.Token);
                if (!_disposed) _library.AddMedia(path);
            }
            else if (WinForge.FileTools.Engine.FeatureCatalog.IsImage(path)) ImportBitmap(ImageEngine.Load(path), Path.GetFileNameWithoutExtension(path));
            else if (extension is ".txt" or ".md" && new FileInfo(path).Length <= ClipboardLibrary.MaximumTextLength) _library.AddText(File.ReadAllText(path));
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error) { _context.Log.Error("Pano kütüphanesine dosya alınamadı.", error); _context.Notify(error.Message); }
    }

    private void LibraryChanged(object? sender, EventArgs e)
    {
        if (!_library.Preferences.FetchLinkMetadata) _linkCancellation?.Cancel();
        _ = IndexImagesAsync(); _ = EnrichLinksAsync();
    }

    internal bool RefreshLinkMetadata(Guid id)
    {
        if (_disposed || !_library.Preferences.FetchLinkMetadata) return false;
        var item = _library.Items.SingleOrDefault(candidate => candidate.Id == id && candidate.Kind == ClipKind.Link && candidate.TrashedAt is null);
        if (item is null) return false;
        _linkAttempts.Remove((item.Id, item.Content));
        if (!_library.MarkLinkMetadataPending(item.Id)) return false;
        _ = EnrichLinksAsync();
        return true;
    }

    private async Task EnrichLinksAsync()
    {
        if (_enriching || _disposed || !_library.Preferences.FetchLinkMetadata) return;
        _enriching = true;
        try
        {
            while (!_disposed && _library.Preferences.FetchLinkMetadata)
            {
                var item = _library.Items.FirstOrDefault(item => item.Kind == ClipKind.Link && item.TrashedAt is null && !item.LinkEnriched && !_linkAttempts.Contains((item.Id, item.Content)));
                if (item is null) break;
                _linkAttempts.Add((item.Id, item.Content));
                if (!Uri.TryCreate(item.Content, UriKind.Absolute, out var uri) || !LinkMetadata.IsSafeUrl(uri)) continue;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _linkCancellation = cancellation;
                string? thumbnailFile = null;
                try
                {
                    var details = await LinkMetadata.ReadAsync(uri, cancellation.Token);
                    if (details.Thumbnail is not null)
                    {
                        try
                        {
                            var bytes = await LinkMetadata.ReadThumbnailAsync(details.Thumbnail, cancellation.Token);
                            using var stream = new MemoryStream(bytes);
                            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                            var bitmap = decoder.Frames[0];
                            if (bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1 || (long)bitmap.PixelWidth * bitmap.PixelHeight > 20_000_000) throw new InvalidDataException("Bağlantı görseli çok büyük.");
                            var scale = Math.Min(1, 1024d / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
                            BitmapSource preview = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
                            thumbnailFile = Path.Combine(_library.DirectoryPath, Guid.NewGuid().ToString("N") + ".png");
                            ImageEngine.Save(preview, thumbnailFile, "png");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error) { _context.Log.Info("Bağlantı küçük resmi alınamadı: " + error.GetType().Name); }
                    }
                    if (!_disposed && _library.Preferences.FetchLinkMetadata && !cancellation.IsCancellationRequested)
                        _library.SetLinkMetadata(item.Id, item.Content, details, thumbnailFile);
                }
                catch (OperationCanceledException) { if (!_disposed && !_library.Preferences.FetchLinkMetadata) _linkAttempts.Remove((item.Id, item.Content)); }
                catch (Exception error) { _context.Log.Info("Bağlantı bilgisi alınamadı: " + error.GetType().Name); }
                finally { if (thumbnailFile is not null && File.Exists(thumbnailFile)) File.Delete(thumbnailFile); if (ReferenceEquals(_linkCancellation, cancellation)) _linkCancellation = null; }
            }
        }
        finally { _enriching = false; }
    }

    private async Task IndexImagesAsync()
    {
        if (_indexing || _disposed || !_library.Preferences.IndexImages) return;
        _indexing = true;
        try
        {
            while (!_disposed && _library.Preferences.IndexImages)
            {
                var item = _library.Items.FirstOrDefault(item => item.Kind == ClipKind.Image && item.TrashedAt is null && item.OcrText.Length == 0 && !_indexed.Contains((item.Id, item.Content)));
                if (item is null) break;
                _indexed.Add((item.Id, item.Content)); var generation = item.Content;
                try
                {
                    var text = await LocalOcr.RecognizeAsync(_library.MediaPath(item), _library.DirectoryPath, _lifetime.Token);
                    if (!_disposed && _library.Items.Any(current => current.Id == item.Id && current.Content == generation && current.TrashedAt is null))
                        _library.Update([item.Id], current => current with { OcrText = text }, undo: false, touch: false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
                catch (Exception error) { _context.Log.Info("Pano görseli OCR ile indekslenemedi: " + error.Message); }
            }
        }
        finally { _indexing = false; }
    }

    internal void RefreshWatchers()
    {
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        foreach (var folder in _library.Preferences.WatchFolders.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size };
            watcher.Created += (_, args) => QueueFile(args.FullPath);
            watcher.Renamed += (_, args) => QueueFile(args.FullPath);
            watcher.Error += (_, args) => _context.Log.Error("Pano klasör izlemesi kesildi.", args.GetException());
            watcher.EnableRaisingEvents = true; _watchers.Add(watcher);
        }
    }

    private async void QueueFile(string path)
    {
        if (_disposed || !_pendingFiles.TryAdd(path, 0)) return;
        try
        {
            var token = _lifetime.Token;
            if (await WaitForStableFileAsync(path, TimeSpan.FromMilliseconds(500), 60, token))
                await _source.Dispatcher.InvokeAsync(() => { if (!_disposed) ImportFile(path); });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { _context.Log.Error("İzlenen pano dosyası alınamadı.", error); }
        finally { _pendingFiles.TryRemove(path, out _); }
    }

    internal static async Task<bool> WaitForStableFileAsync(string path, TimeSpan interval, int attempts, CancellationToken token)
    {
        long previousLength = -1; DateTime previousWrite = default; var stable = 0;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await Task.Delay(interval, token).ConfigureAwait(false);
            try
            {
                using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var length = file.Length; var written = File.GetLastWriteTimeUtc(path);
                stable = length > 0 && length == previousLength && written == previousWrite ? stable + 1 : 0;
                if (stable >= 2) return true;
                previousLength = length; previousWrite = written;
            }
            catch (IOException) { previousLength = -1; stable = 0; }
            catch (UnauthorizedAccessException) { return false; }
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _timer.Stop(); _retention.Stop();
        _library.Changed -= LibraryChanged;
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        RemoveClipboardFormatListener(_source.Handle); _source.RemoveHook(WindowProcedure); _source.Dispose();
        PasteTargets.Dispose();
        _lifetime.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}

public sealed class ClipboardLineModule : NativeFeatureModule
{
    private ClipboardLibrary? _library;
    private ClipboardCapture? _capture;
    private readonly Func<IModuleContext, (ClipboardLibrary Library, ClipboardCapture Capture)>? _servicesFactory;
    private readonly List<IDisposable> _extraHotkeys = [];
    public ClipboardLineModule() { }
    internal ClipboardLineModule(Func<IModuleContext, (ClipboardLibrary Library, ClipboardCapture Capture)> servicesFactory) => _servicesFactory = servicesFactory;
    public override string Id => "clipboardline";
    public override string Name => "ClipBoardLine · Pano Rafları";
    public override string Description => "Win+Alt+B: görsel, metin ve bağlantı rafları; yerel kütüphane, klasörler, etiketler, OCR araması, görsel düzenleme ve geri alınabilir pano geçmişi.";
    protected override uint ShortcutKey => 'B';
    public override IReadOnlyList<SettingDescriptor> Settings => base.Settings.Concat(new[]
    {
        new SettingDescriptor { Key = "search-hotkey", Label = "Pano aramasını aç", Kind = SettingKind.Text, Default = "Ctrl+Alt+S", Description = "Kapatmak için boş bırakın." },
        new SettingDescriptor { Key = "screenshot-hotkey", Label = "Ekran görüntüsü al", Kind = SettingKind.Text, Default = "Ctrl+Alt+A", Description = "Kapatmak için boş bırakın." },
        new SettingDescriptor { Key = "library-hotkey", Label = "Pano kütüphanesini aç", Kind = SettingKind.Text, Default = "", Description = "İsteğe bağlı; örnek: Ctrl+Alt+L." },
        new SettingDescriptor { Key = "latest-image-hotkey", Label = "Son görseli kopyala", Kind = SettingKind.Text, Default = "", Description = "İsteğe bağlı; örnek: Ctrl+Alt+C." }
    }).ToArray();
    public override void Activate()
    {
        if (IsActive) return;
        try
        {
            var services = _servicesFactory?.Invoke(Context) ?? CreateServices(Context);
            _library = services.Library; _capture = services.Capture;
            base.Activate(); RegisterExtraHotkeys();
        }
        catch { _capture?.Dispose(); _capture = null; throw; }
    }
    public override void Deactivate() { base.Deactivate(); ClearExtraHotkeys(); _capture?.Dispose(); _capture = null; _library = null; }
    public override void Apply() { base.Apply(); if (IsActive) RegisterExtraHotkeys(); }
    protected override void OnShortcut()
    {
        if (CurrentWindow is { IsVisible: true } window) window.Hide();
        else Open();
    }
    protected override Window CreateWindow() => new ClipboardShelfWindow(_library!, _capture!, Context);

    private static (ClipboardLibrary Library, ClipboardCapture Capture) CreateServices(IModuleContext context)
    {
        var library = new ClipboardLibrary();
        return (library, new ClipboardCapture(library, context));
    }

    private void RegisterExtraHotkeys()
    {
        ClearExtraHotkeys();
        Register("search-hotkey", "Ctrl+Alt+S", window => window.FocusSearch());
        Register("screenshot-hotkey", "Ctrl+Alt+A", window => window.TakeScreenshot());
        Register("library-hotkey", "", window => window.ShowLibrary());
        Register("latest-image-hotkey", "", window => window.CopyLatestImage(), show: false);
    }

    private void Register(string setting, string fallback, Action<ClipboardShelfWindow> action, bool show = true)
    {
        var value = Context.Settings.GetString(setting, fallback).Trim(); if (value.Length == 0) return;
        try
        {
            var (modifiers, key) = FeatureShortcut.Parse(value);
            _extraHotkeys.Add(Context.Hotkeys.Register(modifiers, key, () =>
            {
                if (!IsActive) return;
                if (show) Open();
                action((ClipboardShelfWindow)EnsureWindow());
            }));
        }
        catch (Exception error) { Context.Log.Error("Pano kısayolu kaydedilemedi: " + setting, error); Context.Notify($"{value}: {error.Message}"); }
    }

    private void ClearExtraHotkeys() { foreach (var registration in _extraHotkeys) registration.Dispose(); _extraHotkeys.Clear(); }
}
