using System.IO.Compression;
using System.Security.Cryptography;
using Tesseract;

namespace WinForge.ClipboardLine;

/// <summary>Runs packaged English and Turkish recognition locally, independent of installed Windows language packs.</summary>
internal static class LocalOcr
{
    private static readonly Model[] Models =
    [
        new("eng", 5_199_098, "45b4cb346724ac1774f1c36f42f182b887bcdb28ebe63e6fff90ac41f3fcff91", "5dc5d8d640a212c9d6184921ba103b186f50e0fed9ee716c53e6b312b400d747"),
        new("tur", 4_680_866, "384ba0dc28040451b7818d7d60e0a88df0d3003fa5a01d713a468779bc3d8c04", "f0127d0f3745f9c65e2ae7ec6b23198fbe5aa186a61b35661426f5e1ef9dedd7")
    ];

    internal static Task<string> RecognizeAsync(string imageFile, string libraryDirectory, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var directory = PrepareModels(libraryDirectory, token);
        using var engine = new TesseractEngine(directory, "eng+tur", EngineMode.Default);
        using var image = Pix.LoadFromFile(imageFile);
        using var page = engine.Process(image);
        var result = page.GetText();
        token.ThrowIfCancellationRequested();
        return result[..Math.Min(result.Length, ClipboardLibrary.MaximumTextLength)];
    }, token);

    private static string PrepareModels(string libraryDirectory, CancellationToken token)
    {
        var directory = Path.Combine(libraryDirectory, "ocr-data"); Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("OCR veri klasörü bağlantı olamaz.");
        foreach (var model in Models)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(directory, model.Name + ".traineddata");
            if (ValidModel(target, model)) continue;
            var source = Path.Combine(AppContext.BaseDirectory, "tessdata", model.Name + ".traineddata.gz");
            using var input = File.OpenRead(source);
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != model.ArchiveHash) throw new InvalidDataException("OCR dil paketi bütünlük denetimini geçemedi.");
            input.Position = 0;
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[64 * 1024]; long written = 0; int count;
                    while ((count = gzip.Read(buffer)) > 0)
                    {
                        token.ThrowIfCancellationRequested(); written += count;
                        if (written > model.Size) throw new InvalidDataException("OCR dil paketi boyutu geçersiz.");
                        output.Write(buffer, 0, count);
                    }
                    output.Flush(true);
                }
                if (!ValidModel(temporary, model)) throw new InvalidDataException("OCR dil paketi bütünlük denetimini geçemedi.");
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return directory;
    }

    private static bool ValidModel(string file, Model model)
    {
        if (!File.Exists(file)) return false;
        var info = new FileInfo(file);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length != model.Size) return false;
        using var input = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(input)) == model.FileHash;
    }

    private sealed record Model(string Name, long Size, string ArchiveHash, string FileHash);
}
