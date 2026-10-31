using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;
using WinForge.Core;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        var native = Path.GetFullPath(args.FirstOrDefault(argument => argument.StartsWith("--native=", StringComparison.Ordinal))?[9..]
            ?? Path.Combine(AppContext.BaseDirectory, "native"));
        var root = Path.Combine(Path.GetTempPath(), "WinForge.FolderSizes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var item = Path.Combine(root, "WinForge fixture folder");
            var nested = Path.Combine(item, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(item, "first.bin"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(nested, "second.bin"), new byte[2048]);
            Directory.CreateDirectory(Path.Combine(root, "Empty fixture folder"));
            VerifyScanner(native, root, item);
            foreach (var (name, size) in new[] { ("A large", 8192), ("M medium", 4096), ("Z small", 512) })
            {
                var folder = Path.Combine(root, name);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "value.bin"), new byte[size]);
            }
            if (args.Contains("--fixture")) VerifyFixture(native, root, item, args.Contains("--coexist"), args.Contains("--early-sort"));
            var module = args.FirstOrDefault(argument => argument.StartsWith("--module=", StringComparison.Ordinal))?[9..];
            if (args.Contains("--live") || module is not null) VerifyExplorer(native, root, item, module);
            Console.WriteLine($"PASS: {_checks} folder-size checks.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinForge.FolderSizes.Tests")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }

    private static void VerifyScanner(string native, string root, string item)
    {
        var host = Path.Combine(native, "WinForge.FolderSizeHost.exe");
        Check(Scan(host, item) == 3072, "Nested files are counted once");
        Check(Scan(host, Path.Combine(root, "Empty fixture folder")) == 0, "Empty folders return zero");
        using (var missing = Process.Start(new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, ArgumentList = { "scan", Path.Combine(root, "missing") } })!)
        {
            var output = missing.StandardOutput.ReadToEnd();
            missing.WaitForExit();
            using var result = JsonDocument.Parse(output);
            Check(missing.ExitCode != 0 && result.RootElement.GetProperty("status").GetInt32() == 2,
                "Unavailable folders report an incomplete scan rather than a zero size");
        }
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(item, "cycle"), item);
            Check(Scan(host, item) == 3072, "Directory links are not followed");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("SKIP: symbolic links are unavailable to this account."); }
        var sparseFolder = Path.Combine(root, "sparse");
        Directory.CreateDirectory(sparseFolder);
        using (var file = new FileStream(Path.Combine(sparseFolder, "large.bin"), FileMode.CreateNew, FileAccess.Write))
        {
            const uint makeSparse = 0x000900C4;
            if (DeviceIoControl(file.SafeFileHandle, makeSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                const long sparseBytes = 4L * 1024 * 1024 * 1024 + 5;
                file.SetLength(sparseBytes);
                Check(Scan(host, sparseFolder) == (ulong)sparseBytes, "Sizes above 4 GiB are calculated without truncation");
            }
            else Console.WriteLine("SKIP: sparse files are unavailable on this volume.");
        }
    }

    private static ulong Scan(string host, string path)
    {
        using var process = Process.Start(new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "scan", path } })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30000) || process.ExitCode != 0) throw new InvalidOperationException($"Scan failed: {error} {output}");
        using var json = JsonDocument.Parse(output);
        return json.RootElement.GetProperty("bytes").GetUInt64();
    }

    private static void VerifyFixture(string native, string root, string item, bool coexist, bool earlySort)
    {
        // Windhawk's folder-size mod excludes Plex*.exe. Isolate this owned test process from an
        // already-running copy of that mod without changing the user's Windhawk configuration.
        var executable = Path.Combine(root, "PlexWinForgeFolderSizeFixture.exe");
        File.Copy(Path.Combine(native, "WinForge.FolderSizeFixture.exe"), executable);
        using var fixture = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = false,
            RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { root } })!;
        IntPtr handle = IntPtr.Zero;
        ExplorerFolderSizeSession? session = null;
        ShellDiagnosticsSession? diagnostics = null;
        try
        {
            var line = fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidOperationException(fixture.StandardError.ReadToEnd());
            var parts = line.Split(' ');
            var processId = uint.Parse(parts[0], CultureInfo.InvariantCulture);
            handle = new IntPtr(long.Parse(parts[1], CultureInfo.InvariantCulture));
            Check(processId == fixture.Id, "Owned Explorer view process was identified");
            if (coexist) diagnostics = ShellDiagnosticsSession.ConnectAsync(processId, native).GetAwaiter().GetResult();
            session = ExplorerFolderSizeSession.ConnectAsync(processId, native).GetAwaiter().GetResult();
            Check(session.IsConnected, "Owned Explorer view callback attached");
            if (diagnostics is not null)
            {
                WaitUntil(() => diagnostics.ReadVisualTree().Any(node => node.Name == "WinForgeFolderSizeCoexist"), "shared XAML visual tree");
                diagnostics.Apply(true, [new ShellStyleRule("#WinForgeFolderSizeCoexist", new Dictionary<string, string> { ["Opacity"] = "0.5" })]);
                WaitUntil(() => diagnostics.ReadVisualTree().Any(node => node.Name == "WinForgeFolderSizeCoexist" && Math.Abs(node.Opacity - 0.5) < 0.01),
                    "shared XAML style application");
                Check(diagnostics.IsConnected, "Folder sizes coexist with the existing XAML diagnostics session");
            }
            SHChangeNotify(0x00001000, 0x00002005, root, IntPtr.Zero);
            if (earlySort) SendMessage(handle, 0x8002, IntPtr.Zero, IntPtr.Zero);
            WaitUntil(() => session.Status.Completed >= 2 && session.Status.CacheHits >= 2,
                "owned Explorer view receives folder sizes", () => session.Status.ToString());
            Console.WriteLine($"Fixture status: {session.Status}");
            WaitUntil(() => SizeIs(handle, "WinForge fixture folder", "3"), "displayed folder size");
            WaitUntil(() => !string.IsNullOrWhiteSpace(ReadFolderSize(handle, "Empty fixture folder")), "displayed empty folder size");
            Check(!string.IsNullOrWhiteSpace(ReadFolderSize(handle, "Empty fixture folder")), "The empty folder's size is visible in the Size column");
            Check(SizeIs(handle, "WinForge fixture folder", "3"), "The Size column displays the calculated 3 KB total");
            WaitUntil(() => SizeIs(handle, "A large", "8") && SizeIs(handle, "M medium", "4") &&
                !string.IsNullOrWhiteSpace(ReadFolderSize(handle, "Z small")), "all sortable folder sizes");
            if (!earlySort) SendMessage(handle, 0x8002, IntPtr.Zero, IntPtr.Zero);
            WaitUntil(() => FolderOrder(handle).SequenceEqual(new[] { "Empty fixture folder", "Z small", "WinForge fixture folder", "M medium", "A large", "sparse" }),
                "ascending folder size order", () => string.Join(", ", FolderOrder(handle)));
            Check(true, "Folder size sorting places folders in ascending order");
            SendMessage(handle, 0x8002, new IntPtr(1), IntPtr.Zero);
            WaitUntil(() => FolderOrder(handle).SequenceEqual(new[] { "sparse", "A large", "M medium", "WinForge fixture folder", "Z small", "Empty fixture folder" }),
                "descending folder size order", () => string.Join(", ", FolderOrder(handle)));
            Check(true, "Folder size sorting places folders in descending order");
            var before = session.Status;
            File.AppendAllBytes(Path.Combine(item, "nested", "second.bin"), new byte[4096]);
            WaitUntil(() => session.Status.Completed > before.Completed && session.Status.CacheHits > before.CacheHits,
                "owned Explorer view updates after a file changes", () => session.Status.ToString());
            WaitUntil(() => SizeIs(handle, "WinForge fixture folder", "7"), "updated visible folder size");
            Check(true, "Directory watcher updates the displayed size to 7 KB");
            WaitUntil(() => FolderOrder(handle).SequenceEqual(new[] { "sparse", "A large", "WinForge fixture folder", "M medium", "Z small", "Empty fixture folder" }),
                "updated descending folder size order", () => string.Join(", ", FolderOrder(handle)));
            Check(true, "Folder size sorting updates after a nested file changes");
            SendMessage(handle, 0x8002, new IntPtr(2), IntPtr.Zero);
            WaitUntil(() => FolderOrder(handle).SequenceEqual(new[] { "A large", "Empty fixture folder", "M medium", "sparse", "WinForge fixture folder", "Z small" }),
                "name ordering after size sorting", () => string.Join(", ", FolderOrder(handle)));
            Check(true, "Name sorting remains unchanged");
            session.StopAsync().GetAwaiter().GetResult();
            session = null;
            SendMessage(handle, 0x8001, IntPtr.Zero, IntPtr.Zero);
            WaitUntil(() => string.IsNullOrWhiteSpace(ReadFolderSize(handle, "WinForge fixture folder")), "original empty folder size");
            Check(!fixture.HasExited, "Disabling restores the original blank Size column and leaves the view running");
            session = ExplorerFolderSizeSession.ConnectAsync(processId, native).GetAwaiter().GetResult();
            SendMessage(handle, 0x8001, IntPtr.Zero, IntPtr.Zero);
            WaitUntil(() => SizeIs(handle, "WinForge fixture folder", "7"), "size after reconnect");
            var heartbeat = (Timer)typeof(ExplorerFolderSizeSession).GetField("_heartbeat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            heartbeat.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var view = (System.IO.MemoryMappedFiles.MemoryMappedViewAccessor)typeof(ExplorerFolderSizeSession)
                .GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            view.Write(8, 0UL);
            WaitUntil(() => session.Status.Closed, "heartbeat-loss restoration", () => session.Status.ToString());
            SendMessage(handle, 0x8001, IntPtr.Zero, IntPtr.Zero);
            WaitUntil(() => string.IsNullOrWhiteSpace(ReadFolderSize(handle, "WinForge fixture folder")), "blank size after heartbeat loss");
            Check(!fixture.HasExited, "Heartbeat loss restores the original callback and allows reconnection");
        }
        finally
        {
            session?.Dispose();
            diagnostics?.Dispose();
            if (handle != IntPtr.Zero) PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
            if (!fixture.WaitForExit(5000)) { fixture.Kill(); fixture.WaitForExit(); }
        }
    }

    private static string ReadFolderSize(IntPtr handle, string name)
    {
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var row = root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, name)));
            var size = row?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Size"));
            return size is not null && size.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                ? ((ValuePattern)pattern).Current.Value : "";
        }
        catch (ElementNotAvailableException) { return ""; }
    }

    private static string[] FolderOrder(IntPtr handle)
    {
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var names = new HashSet<string>(["Empty fixture folder", "Z small", "WinForge fixture folder", "M medium", "A large", "sparse"], StringComparer.Ordinal);
            var row = root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, "WinForge fixture folder")));
            for (var depth = 0; row is not null && depth < 8; depth++)
            {
                row = TreeWalker.ControlViewWalker.GetParent(row);
                var rows = row?.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                var result = rows?.Cast<AutomationElement>().Select(element => element.Current.Name).Where(names.Contains).ToArray() ?? [];
                if (result.Length > 1) return result;
            }
            return [];
        }
        catch (ElementNotAvailableException) { return []; }
    }

    private static bool SizeIs(IntPtr handle, string name, string kilobytes)
    {
        var size = ReadFolderSize(handle, name).Trim();
        return size.StartsWith(kilobytes, StringComparison.Ordinal) && size.Contains("KB", StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyExplorer(string native, string root, string item, string? modulePath)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic? window = null;
        ExplorerFolderSizeSession? session = null;
        ITweakModule? module = null;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { root } });
            WaitUntil(() =>
            {
                foreach (var candidate in shell.Windows())
                {
                    try
                    {
                        var location = (string)candidate.LocationURL;
                        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile &&
                            string.Equals(Path.TrimEndingDirectorySeparator(uri.LocalPath), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
                        { window = candidate; return true; }
                    }
                    catch (COMException) { }
                }
                return false;
            }, "controlled Explorer window");
            var handle = new IntPtr(Convert.ToInt64(window!.HWND, CultureInfo.InvariantCulture));
            GetWindowThreadProcessId(handle, out var processId);
            Check(processId != 0, "Controlled Explorer window process was identified");
            window.Document.CurrentViewMode = 4; // FVM_DETAILS
            if (modulePath is not null)
            {
                var type = Assembly.LoadFrom(Path.GetFullPath(modulePath)).GetType("WinForge.Modules.Extras.ExplorerFolderSizesModule", throwOnError: true)!;
                module = (ITweakModule)Activator.CreateInstance(type)!;
                module.Initialize(new TestContext());
                ((IAsyncTweakModule)module).ActivateAsync().GetAwaiter().GetResult();
                Check(module.IsActive, "Explorer folder-size module activated");
                var sessions = (Dictionary<int, ExplorerFolderSizeSession>)type.GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
                if (!sessions.TryGetValue((int)processId, out session))
                    throw new InvalidOperationException($"Module did not attach to the controlled Explorer process: {module.Warning}");
            }
            else session = ExplorerFolderSizeSession.ConnectAsync(processId, native).GetAwaiter().GetResult();
            Check(session.IsConnected, "Explorer callback attached");
            window.Refresh();
            WaitUntil(() => session.Status.Completed >= 2 && session.Status.CacheHits >= 2 && SizeIs(handle, "WinForge fixture folder", "3"),
                "Explorer folder-size column", () => session.Status.ToString());
            Check(true, "Actual Explorer displays the calculated 3 KB folder size");
            Console.WriteLine($"Explorer status: {session.Status}");
            WaitUntil(() => SizeIs(handle, "A large", "8") && SizeIs(handle, "M medium", "4") &&
                !string.IsNullOrWhiteSpace(ReadFolderSize(handle, "Z small")), "actual Explorer sortable folders");
            window.Document.SortColumns = "prop:System.Size";
            string[] ascending = ["Empty fixture folder", "Z small", "WinForge fixture folder", "M medium", "A large", "sparse"];
            string[] descending = [.. ascending.Reverse()];
            WaitUntil(() => FolderOrder(handle).SequenceEqual(ascending) || FolderOrder(handle).SequenceEqual(descending),
                "actual Explorer size order", () => string.Join(", ", FolderOrder(handle)));
            var initiallyAscending = FolderOrder(handle).SequenceEqual(ascending);
            Check(true, "Actual Explorer sorts folders by size");
            var header = AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header));
            var sizeHeader = header?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Size"));
            if (sizeHeader is null || !sizeHeader.TryGetCurrentPattern(InvokePattern.Pattern, out var sortPattern))
                throw new InvalidOperationException("The Explorer Size column cannot be invoked for descending sort.");
            ((InvokePattern)sortPattern).Invoke();
            var reversed = initiallyAscending ? descending : ascending;
            WaitUntil(() => FolderOrder(handle).SequenceEqual(reversed), "actual Explorer reversed size order", () => string.Join(", ", FolderOrder(handle)));
            Check(true, "Actual Explorer reverses folder size order");
            var before = session.Status;
            File.AppendAllBytes(Path.Combine(item, "nested", "second.bin"), new byte[4096]);
            WaitUntil(() => session.Status.Completed > before.Completed && SizeIs(handle, "WinForge fixture folder", "7"),
                "updated Explorer folder size", () => session.Status.ToString());
            Check(true, "Actual Explorer updates the folder size to 7 KB after a file changes");
            string[] afterAscending = ["Empty fixture folder", "Z small", "M medium", "WinForge fixture folder", "A large", "sparse"];
            var afterChange = initiallyAscending ? afterAscending.Reverse().ToArray() : afterAscending;
            WaitUntil(() => FolderOrder(handle).SequenceEqual(afterChange),
                "actual Explorer reordered after a change", () => string.Join(", ", FolderOrder(handle)));
            Check(true, "Actual Explorer reorders folders after their size changes");
            if (module is not null)
            {
                ((IAsyncTweakModule)module).ApplyAsync().GetAwaiter().GetResult();
                window.Refresh();
                WaitUntil(() => SizeIs(handle, "WinForge fixture folder", "7"), "folder size after module settings are applied");
                Check(true, "Applying module settings reconnects and restores calculated sizes");
                ((IAsyncTweakModule)module).DeactivateAsync().GetAwaiter().GetResult();
                Check(!module.IsActive, "Explorer folder-size module deactivated");
                module = null;
            }
            else session.StopAsync().GetAwaiter().GetResult();
            session = null;
            window.Refresh();
            Check(Process.GetProcessById((int)processId).HasExited == false, "Disabling the callback leaves Explorer running");
        }
        finally
        {
            if (module is not null) ((IAsyncTweakModule)module).DeactivateAsync().GetAwaiter().GetResult();
            else session?.Dispose();
            if (window is not null) { try { window.Quit(); } catch (Exception exception) when (exception is COMException or MissingMemberException) { } Marshal.FinalReleaseComObject(window); }
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private sealed class TestContext : IModuleContext, IModuleSettings, ILogger
    {
        public IModuleSettings Settings => this;
        public ILogger Log => this;
        public IHotkeyService Hotkeys => throw new NotSupportedException();
        public string Path => "";
        public bool GetBool(string key, bool fallback = false) => fallback;
        public int GetInt(string key, int fallback = 0) => fallback;
        public string GetString(string key, string fallback = "") => fallback;
        public void Set(string key, object? value) { }
        public void Notify(string message) { }
        public void Info(string message) => Console.WriteLine(message);
        public void Error(string message, Exception? exception = null) => Console.WriteLine($"{message} {exception}");
    }

    private static void WaitUntil(Func<bool> predicate, string operation, Func<string>? details = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(25))
        {
            if (predicate()) return;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Timed out waiting for {operation}. {details?.Invoke()}");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
        Console.WriteLine($"PASS: {description}");
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string path, IntPtr item2);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, int inputSize,
        IntPtr output, int outputSize, out int returned, IntPtr overlapped);
}
