using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinForge.Modules.DynamicIsland;

/// <summary>Session-only file references. Observes new downloads without reading or moving their contents.</summary>
internal sealed class IslandFiles : IDisposable
{
    private const int MaximumEntries = 8;
    private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase) { ".crdownload", ".part", ".partial", ".tmp", ".download" };
    private readonly List<string> _shelf = [];
    private readonly List<string> _downloads = [];
    private FileSystemWatcher? _watcher;
    internal IReadOnlyList<string> Shelf => _shelf;
    internal IReadOnlyList<string> Downloads => _downloads;
    internal event Action<string>? DownloadAdded;

    internal void WatchDownloads(string configuredFolder, Action<Action> dispatch)
    {
        _watcher?.Dispose();
        _watcher = null;
        var folder = string.IsNullOrWhiteSpace(configuredFolder) ? DownloadsFolder() : Environment.ExpandEnvironmentVariables(configuredFolder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("İndirilenler klasörü bulunamadı.");
        var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
        void Enqueue(string path) => dispatch(() =>
        {
            if (!ReferenceEquals(_watcher, watcher) || !File.Exists(path) || TemporaryExtensions.Contains(Path.GetExtension(path))) return;
            var added = !_downloads.Contains(path, StringComparer.OrdinalIgnoreCase);
            Add(_downloads, path);
            if (added) DownloadAdded?.Invoke(Path.GetFileName(path));
        });
        watcher.Created += (_, args) => Enqueue(args.FullPath);
        watcher.Renamed += (_, args) => Enqueue(args.FullPath);
        _watcher = watcher;
        watcher.EnableRaisingEvents = true;
    }

    internal void StopDownloads() { _watcher?.Dispose(); _watcher = null; _downloads.Clear(); }

    internal void AddFiles(System.Windows.Window owner)
    {
        var dialog = new OpenFileDialog { Title = "Ada tepsisine dosya ekle", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(owner) != true) return;
        foreach (var path in dialog.FileNames) Add(_shelf, path);
    }

    internal void ClearShelf() => _shelf.Clear();

    internal void Reveal(string path)
    {
        if (!_shelf.Concat(_downloads).Contains(path, StringComparer.OrdinalIgnoreCase) || !File.Exists(path)) return;
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add("/select,");
        start.ArgumentList.Add(path);
        Process.Start(start)?.Dispose();
    }

    private static void Add(List<string> list, string path)
    {
        list.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > MaximumEntries) list.RemoveRange(MaximumEntries, list.Count - MaximumEntries);
    }

    private static string DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStringUni(pointer)!; }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    public void Dispose() { StopDownloads(); _shelf.Clear(); }
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
