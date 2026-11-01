using System.IO;
using System.Text.Json;
using WinForge.Core;
using WinForge.Modules.Extras;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge.DefaultSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            TestPaths(root);
            TestAssociations();
            TestPanels(root);
            Console.WriteLine($"PASS: {_checks} default settings checks; no real Windows settings changed.");
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinForge.DefaultSettingsTests"));
            if (Path.GetDirectoryName(resolved) == expectedParent) Directory.Delete(resolved, recursive: true);
        }
    }

    private static void TestPaths(string root)
    {
        var paths = new FakePaths(root);
        var settings = new MemorySettings();
        var service = new KnownFolderPathService(paths, settings);
        var original = service.Read("documents");
        var first = Directory.CreateDirectory(Path.Combine(root, "new-documents")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "other-documents")).FullName;
        File.WriteAllText(Path.Combine(original, "keep.txt"), "original content");

        service.Apply("documents", first);
        Check(service.Read("documents") == first && service.Original("documents") == original, "Redirect saves original path.");
        service.Apply("documents", second);
        Check(service.Original("documents") == original, "Repeated changes preserve first recovery path.");
        service.Restore("documents");
        Check(service.Read("documents") == original && File.ReadAllText(Path.Combine(original, "keep.txt")) == "original content", "Restore retains original files.");
        var writes = paths.Writes;
        service.Apply("documents", original);
        Check(paths.Writes == writes, "Unchanged path does not write Windows settings.");
        Throws(() => service.Apply("documents", service.Read("downloads")), "Reject same target as another known folder.");
        var nested = Directory.CreateDirectory(Path.Combine(service.Read("downloads"), "nested")).FullName;
        Throws(() => service.Apply("documents", nested), "Reject nested known folders.");
        Throws(() => service.Apply("documents", root), "Reject parent of another known folder.");
        Throws(() => service.Apply("documents", Path.Combine(root, "missing")), "Reject missing destination without creating it.");
        Throws(() => service.Apply("unknown", first), "Reject unknown folder IDs.");
        Throws(() => service.Restore("music"), "Reject restore without backup.");
        foreach (var invalid in new[] { "", "relative", @"C:relative", @"C:\", @"\\?\C:\data", @"D:\a:b", @"D:\*", @"D:\folder.", @"D:\folder \child" })
            Throws(() => KnownFolderPathService.Normalize(invalid), "Reject invalid folder: " + invalid);
        Check(KnownFolderPathService.Normalize(first + Path.DirectorySeparatorChar) == first, "Normalize trailing separator.");
        paths.FailWrites = true;
        Throws(() => service.Apply("music", first), "Propagate Windows write failure.");
        Check(service.Original("music") == service.Read("music"), "Failed write retains recovery information and current location.");
    }

    private static void TestAssociations()
    {
        Check(FileAssociationService.NormalizeExtension(" PDF ") == ".pdf", "Normalize extension for search and clipboard.");
        foreach (var invalid in new[] { "", ".", "a.exe /run", ".pdf?x", @".a\b", ".a.b", ".💥" })
            Throws(() => FileAssociationService.NormalizeExtension(invalid), "Reject malformed extension: " + invalid);
        Check(FileAssociationService.ReadExtensions().Length > 0, "Read registered Windows extensions without changing them.");
        Check(FileAssociationService.Read(".txt").Extension == ".txt", "Read association through native Windows query.");
        var realPaths = new WindowsKnownFolderPaths();
        Check(KnownFolderPathService.Folders.All(folder => Path.IsPathFullyQualified(realPaths.Read(folder.Id))), "Read all six real known folder paths without writing them.");
    }

    private static void TestPanels(string root)
    {
        var context = new TestContext(root);
        foreach (var module in new DefaultSettingsModule[] { new DefaultPathsModule(), new DefaultAppsModule() })
        {
            module.Initialize(context);
            module.Activate();
            Check(module.Settings.Count == 0 && context.Ui.Last is null, module.Id + " uses inline settings without a separate window.");
            var panel = (IModulePanel)module;
            JsonElement State() => JsonSerializer.SerializeToElement(panel.GetPanelState(), new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("state");
            void Send(string action, object data) => panel.HandlePanelCommand(action, JsonSerializer.SerializeToElement(data));
            Check(State().GetProperty("rows").GetArrayLength() > 0, module.Id + " exposes populated inline panel.");
            if (module.Id == "default-paths")
            {
                Send("paths.edit", new { key = "documents", path = root });
                Send("paths.apply", new { key = "documents" });
                Check(State().GetProperty("pending").ValueKind == JsonValueKind.Object, "Path edit requires explicit confirmation before Windows write.");
                Send("paths.cancel", new { });
                Check(State().GetProperty("pending").ValueKind == JsonValueKind.Null, "Cancel clears pending change.");
            }
            else
            {
                Send("apps.filter", new { query = "winforgetestextension" });
                Check(State().GetProperty("rows")[0].GetProperty("extension").GetString() == ".winforgetestextension", "Unregistered extension can be searched.");
            }
            module.Deactivate();
            Check(!module.IsActive && context.Ui.Last is null, module.Id + " closes without opening a window.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (Exception exception) when (exception is ArgumentException or IOException or FormatException or InvalidOperationException)
        { _checks++; return; }
        throw new InvalidOperationException(message);
    }

    private sealed class FakePaths : IKnownFolderPaths
    {
        private readonly Dictionary<Guid, string> _values;
        public int Writes { get; private set; }
        public bool FailWrites { get; set; }
        public FakePaths(string root) => _values = KnownFolderPathService.Folders.ToDictionary(folder => folder.Id,
            folder => Directory.CreateDirectory(Path.Combine(root, folder.Key)).FullName);
        public string Read(Guid id) => _values[id];
        public void Write(Guid id, string path)
        {
            if (FailWrites) throw new IOException("Simulated Windows failure.");
            _values[id] = path;
            Writes++;
        }
    }

    private sealed class MemorySettings : IModuleSettings
    {
        private readonly Dictionary<string, object?> _values = new();
        public bool GetBool(string key, bool fallback = false) => fallback;
        public int GetInt(string key, int fallback = 0) => fallback;
        public string GetString(string key, string fallback = "") => _values.GetValueOrDefault(key) as string ?? fallback;
        public void Set(string key, object? value) => _values[key] = value;
    }

    private sealed class TestContext(string root) : IModuleContext, IFeatureUiContext
    {
        public IModuleSettings Settings { get; } = new MemorySettings();
        public ILogger Log { get; } = new FileLogger(Path.Combine(root, "test.log"));
        public IHotkeyService Hotkeys => throw new NotSupportedException();
        public TestUi Ui { get; } = new();
        public IFeatureUiService FeatureUi => Ui;
        public void Notify(string message) => throw new InvalidOperationException(message);
    }

    private sealed class TestUi : IFeatureUiService
    {
        public TestWindow? Last { get; private set; }
        public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options) => Last = new TestWindow();
        public IFeatureUiSurface CreateSurface(string view) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel") => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel = "Close") => throw new NotSupportedException();
    }

    private sealed class TestWindow : IFeatureUiWindow
    {
        public bool IsVisible { get; private set; }
        public JsonElement? State { get; private set; }
        public event Action<string, JsonElement>? MessageReceived;
        public event Action? ClosedByHost;
        public void Send(string command, object payload) => MessageReceived?.Invoke(command, JsonSerializer.SerializeToElement(payload));
        public void SetState(object state) => State = JsonSerializer.SerializeToElement(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close() { IsVisible = false; ClosedByHost?.Invoke(); }
        public void SetBounds(double left, double top, double width, double height) { }
        public void Dispose() => Close();
    }
}
