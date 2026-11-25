using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForge.ClipboardLine;
using WinForge.Core;
using WinForge.FileTools.ClipboardHistory;
using WinForge.History;
using Clipboard = System.Windows.Clipboard;

internal static class ClipboardHistoryChecks
{
    internal static void Run(Action<bool, string> check) => RunSta(() => RunOnSta(check));

    internal static void RunShelfStartup(Action<bool, string> check) => RunSta(() =>
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WinForge.FileToolsTests;component/TestTheme.xaml", UriKind.Relative) });
        var directory = Directory.CreateTempSubdirectory("WinForge-ClipboardShelf-");
        try
        {
            var context = new Context(Path.Combine(directory.FullName, "shelf"));
            var library = new ClipboardLibrary(directory.FullName);
            const string fixture = "Clipboard shelf first render";
            library.AddText(fixture);
            using var capture = new ClipboardCapture(library, context);
            var shelf = new ClipboardShelfWindow(library, capture, context);
            try
            {
                var state = context.ShelfSurface.State;
                check(!state.GetProperty("libraryMode").GetBoolean() &&
                    state.GetProperty("rows")[1].GetProperty("items")[0].GetProperty("description").GetString() == fixture,
                    "The shelf sends existing text to the initialized UI on its first render.");
            }
            finally { shelf.Close(); }
            check(context.ShelfSurface.Disposed, "Closing the shelf releases its UI surface.");
        }
        finally
        {
            app.Shutdown();
            if (directory.Parent?.FullName == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
                directory.Delete(recursive: true);
        }
    });

    private static void RunSta(Action run)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { run(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Clipboard UI integration failed.", failure);
    }

    private static void RunOnSta(Action<bool, string> check)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var directory = Directory.CreateTempSubdirectory("WinForge-ClipboardHistory-");
        var path = Path.Combine(directory.FullName, "history.db");
        var previous = Clipboard.GetDataObject();
        var context = new Context(path);
        var module = new ClipboardHistoryModule(path);
        module.Initialize(context);
        var store = new ClipboardHistoryStore(path);
        store.Initialize();
        store.Add(DateTimeOffset.UtcNow.AddDays(-8).ToUnixTimeMilliseconds(), "expired fixture", null);
        uint lastOwned = 0;
        try
        {
            module.Activate();
            check(context.Hotkey == (HotkeyModifiers.Win, (uint)'V'), "The module claims Win+V.");
            const string text = "  İstanbul 📋\r\nBol boşluk  ";
            Clipboard.SetText(text);
            lastOwned = FocusedTextOutput.GetClipboardSequenceNumber();
            PumpUntil(() => store.List("istanbul", "text", 0, 0, "tr").Count == 1);
            check(store.List("istanbul", "text", 0, 0, "tr").Single().Text == text, "Copied text is searchable with Turkish casing and exact whitespace.");

            var privateData = new DataObject();
            using var marker = new MemoryStream([1]);
            privateData.SetText("private fixture");
            privateData.SetData(ClipboardHistoryMonitor.ExcludeFormat, marker, false);
            ClipboardAccess.SetData(privateData);
            lastOwned = FocusedTextOutput.GetClipboardSequenceNumber();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(180));
            check(store.List("private fixture", "all", 0, 0, "en").Count == 0, "Private clipboard data stays outside history.");

            var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255, 255, 0, 0, 128 }, 8);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream();
            encoder.Save(png);
            var imageData = new DataObject();
            using var imageBytes = new MemoryStream(png.ToArray());
            imageData.SetData(ClipboardHistoryMonitor.PngFormat, imageBytes, false);
            ClipboardAccess.SetData(imageData);
            lastOwned = FocusedTextOutput.GetClipboardSequenceNumber();
            PumpUntil(() => store.List("", "image", 0, 0, "en").Count == 1);
            var image = store.List("", "image", 0, 0, "en").Single();
            check(image is { Width: 2, Height: 1, Thumbnail.Length: > 0 }, "PNG-only clipboard images get a thumbnail.");

            context.Open!();
            PumpUntil(() => context.Window.State.GetProperty("items").GetArrayLength() == 3);
            context.Window.Send("clipboard-history.copy", new { id = image.Id });
            PumpUntil(() => !context.Window.IsVisible);
            lastOwned = FocusedTextOutput.GetClipboardSequenceNumber();
            var restored = Clipboard.GetDataObject();
            check(Clipboard.ContainsImage() && restored?.GetDataPresent(ClipboardHistoryMonitor.PngFormat) == true,
                "Selecting a stored image restores Windows bitmap and PNG formats and closes the panel.");
            check(restored?.GetData(ClipboardHistoryMonitor.PngFormat) is MemoryStream actual && actual.ToArray().SequenceEqual(png.ToArray()),
                "Re-copying an image preserves the original transparent PNG.");

            context.Open!();
            PumpUntil(() => context.Window.State.GetProperty("items").GetArrayLength() == 3);
            var textId = store.List("istanbul", "text", 0, 0, "tr").Single().Id;
            context.Window.Send("clipboard-history.copy", new { id = textId });
            PumpUntil(() => !context.Window.IsVisible);
            lastOwned = FocusedTextOutput.GetClipboardSequenceNumber();
            check(Clipboard.GetText() == text, "Selecting a stored text restores the text exactly and closes the panel.");
            PumpUntil(() => false, TimeSpan.FromMilliseconds(150));
            check(store.List("", "all", 0, 0, "en").Count == 3, "Restoring an entry does not re-record it.");

            context.RetentionDays = 7;
            module.Apply();
            PumpUntil(() => store.List("", "all", 0, 0, "en").Count == 2);
            module.Deactivate();
            module.Activate();
            context.Open!();
            PumpUntil(() => context.Window.State.GetProperty("items").GetArrayLength() == 2);
            check(store.List("", "all", 0, 0, "en").Count == 2, "Changing retention deletes old data and recent entries survive restarting the module.");

            Clipboard.SetImage(bitmap);
            lastOwned = FocusedTextOutput.GetClipboardSequenceNumber();
            PumpUntil(() => store.List("", "image", 0, 0, "en").Count == 2);
            check(store.List("", "image", 0, 0, "en")[0] is { Width: 2, Height: 1 }, "Standard bitmap clipboard images are captured as well.");

            context.Window.Send("clipboard-history.close", new { });
            PumpUntil(() => !context.Window.IsVisible);

            for (var index = 1; index <= 28; index++)
                store.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + index, $"Page item {index}", null);

            context.Open!();
            PumpUntil(() => context.Window.State.GetProperty("items").GetArrayLength() == ClipboardHistoryStore.PageSize);
            check(context.Window.State.GetProperty("page").GetInt32() == 1 &&
                context.Window.State.GetProperty("hasNext").GetBoolean(),
                "Initial clipboard history load returns first page with hasNext=true.");

            context.Window.Send("clipboard-history.load-more", new { page = 1 });
            PumpUntil(() => context.Window.State.GetProperty("page").GetInt32() == 2 &&
                !context.Window.State.GetProperty("loadingMore").GetBoolean());
            var loadedItems = context.Window.State.GetProperty("items").EnumerateArray().ToList();
            var loadedIds = loadedItems.Select(item => item.GetProperty("id").GetInt64()).ToList();
            check(loadedItems.Count == 31 &&
                !context.Window.State.GetProperty("hasNext").GetBoolean() &&
                loadedIds.Distinct().Count() == loadedIds.Count &&
                loadedIds.Zip(loadedIds.Skip(1), (a, b) => a > b).All(ok => ok),
                "Infinite scroll load-more appends unique descending items for page 2.");

            context.Window.Send("clipboard-history.load-more", new { page = 1 });
            PumpUntil(() => false, TimeSpan.FromMilliseconds(120));
            check(context.Window.State.GetProperty("items").GetArrayLength() == 31,
                "Stale load-more for an earlier page does not duplicate items.");

            var page2Id = loadedIds[^1];
            context.Window.Send("clipboard-history.copy", new { id = page2Id });
            PumpUntil(() => !context.Window.IsVisible);
            lastOwned = FocusedTextOutput.GetClipboardSequenceNumber();
            check(Clipboard.GetText() == text, "Selecting an entry from page 2 restores it and closes the panel.");

            context.Open!();
            PumpUntil(() => context.Window.State.GetProperty("items").GetArrayLength() == ClipboardHistoryStore.PageSize);
            context.Window.Send("clipboard-history.load-more", new { page = 1 });
            PumpUntil(() => context.Window.State.GetProperty("page").GetInt32() == 2);
            context.Window.Send("clipboard-history.filter", new { query = "Page item", kind = "text" });
            PumpUntil(() => context.Window.State.GetProperty("query").GetString() == "Page item" &&
                context.Window.State.GetProperty("page").GetInt32() == 1);
            check(context.Window.State.GetProperty("items").GetArrayLength() == ClipboardHistoryStore.PageSize &&
                context.Window.State.GetProperty("hasNext").GetBoolean(),
                "Filtering resets infinite scroll pagination cleanly back to page 1.");
        }
        catch (Exception error) when (File.Exists(path + ".log"))
        {
            throw new InvalidOperationException(File.ReadAllText(path + ".log"), error);
        }
        finally
        {
            module.Deactivate();
            if (lastOwned == FocusedTextOutput.GetClipboardSequenceNumber())
            {
                if (previous is null) Clipboard.Clear();
                else Clipboard.SetDataObject(previous, true);
            }
            File.Delete(path);
            File.Delete(path + ".log");
            directory.Delete();
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan? delay = null)
    {
        var frame = new DispatcherFrame();
        var until = DateTime.UtcNow + (delay ?? TimeSpan.FromSeconds(5));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= until) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (delay is null && !condition()) throw new TimeoutException("Clipboard history update did not arrive.");
    }

    private sealed class Context(string path) : IModuleContext, IModuleSettings, IFeatureUiContext, IFeatureUiService, IHotkeyService
    {
        public IModuleSettings Settings => this;
        public ILogger Log { get; } = new FileLogger(path + ".log");
        public IHotkeyService Hotkeys => this;
        public IFeatureUiService FeatureUi => this;
        public int RetentionDays { get; set; }
        public (uint Modifiers, uint Key) Hotkey { get; private set; }
        public Action? Open { get; private set; }
        public Panel Window { get; } = new();
        public Surface ShelfSurface { get; } = new();
        public void Notify(string message) => throw new InvalidOperationException(message);
        public int GetInt(string key, int fallback) => RetentionDays;
        public bool GetBool(string key, bool fallback) => fallback;
        public string GetString(string key, string fallback) => fallback;
        public void Set(string key, object? value) => throw new NotSupportedException();
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback)
        {
            Hotkey = (modifiers, virtualKey);
            Open = callback;
            return new MemoryStream();
        }
        IFeatureUiWindow IFeatureUiService.Open(string key, string view, string title, FeatureUiWindowOptions options) => Window;
        public IFeatureUiSurface CreateSurface(string view) => view == "clipboard-shelf" ? ShelfSurface : throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel) => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel) => throw new NotSupportedException();
    }

    internal sealed class Surface : IFeatureUiAssetSurface
    {
        public FrameworkElement Element { get; } = new System.Windows.Controls.Border();
        public JsonElement State { get; private set; }
        public bool Disposed { get; private set; }
        public event Action<string, JsonElement>? MessageReceived;
        internal void Send(string command, object payload) => MessageReceived?.Invoke(command, JsonSerializer.SerializeToElement(payload));
        public void SetState(object state) => State = JsonSerializer.SerializeToElement(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        public Uri MapLocalAsset(string filePath) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }

    private sealed class Panel : IFeatureUiWindow
    {
        public bool IsVisible { get; private set; }
        public JsonElement State { get; private set; }
        public event Action<string, JsonElement>? MessageReceived;
        public event Action? ClosedByHost;
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close()
        {
            Hide();
            ClosedByHost?.Invoke();
            MessageReceived = null;
            ClosedByHost = null;
        }
        public void Dispose() => Close();
        public void SetBounds(double left, double top, double width, double height) { }
        public void SetState(object state) => State = JsonSerializer.SerializeToElement(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        public void Send(string command, object payload) => MessageReceived?.Invoke(command, JsonSerializer.SerializeToElement(payload));
    }
}
