using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal sealed class CaptureImageFile(string application, DateTimeOffset capturedAt, bool fullscreen = false)
{
    private static readonly Regex CaptureFilename = new(
        @"^.+ - [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}-[0-9]{2}-[0-9]{2}\.[0-9]{3}( \([0-9]+\))?\.png$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly char[] InvalidNameCharacters = Path.GetInvalidFileNameChars();

    public string Application { get; } = SafeApplicationName(application);
    public DateTimeOffset CapturedAt { get; } = capturedAt;
    public string FileStem => Application + " - " + CapturedAt.ToString("yyyy-MM-dd HH-mm-ss.fff", CultureInfo.InvariantCulture);

    public static string ApplicationName(ForegroundInfo window)
    {
        if (window.Window == 0) return "Masaüstü";
        var executable = Native.ExecutablePathOf(window.Process);
        var process = Path.GetFileNameWithoutExtension(window.App);
        if (process.Equals("WinForge", StringComparison.OrdinalIgnoreCase)) return "WinForge";
        if (process.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && window.Title.Length > 0)
            return window.Title;
        FileVersionInfo? version = null;
        if (executable is not null)
        {
            try { version = FileVersionInfo.GetVersionInfo(executable); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        }
        return DisplayName(process, version?.ProductName, version?.FileDescription, window.Title);
    }

    internal static string DisplayName(string process, string? product, string? description, string title)
    {
        const string game = "Red Dead Redemption";
        if (process.Equals("RDR", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("RDR2", StringComparison.OrdinalIgnoreCase) ||
            product?.StartsWith(game, StringComparison.OrdinalIgnoreCase) == true ||
            (process == "Bilinmeyen" && title.StartsWith(game, StringComparison.OrdinalIgnoreCase))) return game;
        if (!string.IsNullOrWhiteSpace(product) &&
            !product.Replace("®", "").Contains("Windows Operating System", StringComparison.OrdinalIgnoreCase)) return product;
        if (!string.IsNullOrWhiteSpace(description)) return description;
        if (process.Length > 0 && process != "Bilinmeyen") return process;
        return string.IsNullOrWhiteSpace(title) ? "Ekran görüntüsü" : title;
    }

    public static bool IsCaptureFilename(string filename) => CaptureFilename.IsMatch(filename);

    public string Save(HistoryStore store, BitmapSource image) =>
        Write(EncodePng(image), fullscreen ? HistoryStore.FullscreenCaptures + "/" + Application : HistoryStore.CaptureImages,
            store.ResolveMedia, store);

    public string Export(string directory, BitmapSource image) => Export(directory, EncodePng(image));

    internal string Export(string directory, byte[] png)
    {
        var folder = fullscreen ? HistoryStore.FullscreenFolderName + "/" + Application : "";
        return Write(png, folder, name => Path.Combine(directory, name.Replace('/', Path.DirectorySeparatorChar)));
    }

    internal static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        return buffer.ToArray();
    }

    private string Write(byte[] bytes, string folder, Func<string, string> resolve, HistoryStore? store = null)
    {
        for (var index = 1; ; index++)
        {
            var filename = index == 1 ? FileStem + ".png" : $"{FileStem} ({index}).png";
            var name = folder.Length == 0 ? filename : folder + "/" + filename;
            var path = resolve(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            store?.ReserveMedia(name);
            var keepReservation = false;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllBytes(temporary, bytes);
                    try
                    {
                        File.Move(temporary, path);
                        keepReservation = true;
                        return store is null ? path : name;
                    }
                    catch (IOException) when (File.Exists(path)) { }
                }
                if (!bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path))) continue;
                keepReservation = true;
                return store is null ? path : name;
            }
            finally
            {
                if (!keepReservation) store?.ReleasePendingMedia(name);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static string SafeApplicationName(string application)
    {
        var name = new string(application.Select(character =>
            InvalidNameCharacters.Contains(character) ? '-' : character).ToArray()).Trim().TrimEnd('.', ' ');
        if (name.Length > 80) name = name[..(char.IsHighSurrogate(name[79]) ? 79 : 80)].TrimEnd('.', ' ');
        if (name.Length == 0) return "Ekran görüntüsü";
        return HistoryStore.IsValidMediaSegment(name) ? name : "_" + name;
    }
}
