using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Tesseract;
using WinForge.Core;

namespace WinForge.ClipboardLine;

/// <summary>Runs packaged English and Turkish recognition locally, independent of installed Windows language packs.</summary>
internal static class LocalOcr
{
    private static readonly Model[] Models =
    [
        new("eng", 5_199_098, "45b4cb346724ac1774f1c36f42f182b887bcdb28ebe63e6fff90ac41f3fcff91", "5dc5d8d640a212c9d6184921ba103b186f50e0fed9ee716c53e6b312b400d747"),
        new("tur", 4_680_866, "384ba0dc28040451b7818d7d60e0a88df0d3003fa5a01d713a468779bc3d8c04", "f0127d0f3745f9c65e2ae7ec6b23198fbe5aa186a61b35661426f5e1ef9dedd7")
    ];

    private static readonly SemaphoreSlim EngineGate = new(1, 1);
    private static string? _cachedDirectory;
    private static TesseractEngine? _cachedEngine;

    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Registers the packaged Tesseract eng+tur engine with WinForge.Core when FileTools loads.")]
    internal static void RegisterWithCore()
    {
        OcrRecognitionService.RegisterSupplementaryEngine(RecognizeGrayscaleAsync);
    }

    internal static Task<string> RecognizeAsync(string imageFile, string libraryDirectory, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        await EngineGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var engine = GetOrCreateEngine(libraryDirectory, token);
            using var image = Pix.LoadFromFile(imageFile);
            using var page = engine.Process(image, PageSegMode.Auto);
            var result = page.GetText();
            if (string.IsNullOrWhiteSpace(result))
            {
                using var sparsePage = engine.Process(image, PageSegMode.SparseText);
                var sparse = sparsePage.GetText();
                if (!string.IsNullOrWhiteSpace(sparse))
                    result = sparse;
            }
            token.ThrowIfCancellationRequested();
            return result[..Math.Min(result.Length, ClipboardLibrary.MaximumTextLength)];
        }
        finally
        {
            EngineGate.Release();
        }
    }, token);

    internal static Task<SupplementaryOcrResult> RecognizeGrayscaleAsync(
        byte[] grayscalePixels,
        int width,
        int height,
        bool singleLineHint,
        CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        var bmp = BuildGrayscaleBmp(grayscalePixels, width, height);
        await EngineGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var engine = GetOrCreateEngine(AppData.DirectoryPath, token);
            using var image = Pix.LoadFromMemory(bmp);

            var primaryMode = singleLineHint ? PageSegMode.SingleLine : PageSegMode.Auto;
            var best = ProcessWithMode(engine, image, primaryMode);

            if (string.IsNullOrWhiteSpace(best.Text))
            {
                var fallbackMode = singleLineHint ? PageSegMode.SingleBlock : PageSegMode.SparseText;
                var fallback = ProcessWithMode(engine, image, fallbackMode);
                if (!string.IsNullOrWhiteSpace(fallback.Text))
                    best = fallback;
            }

            if (string.IsNullOrWhiteSpace(best.Text) && !singleLineHint)
            {
                var block = ProcessWithMode(engine, image, PageSegMode.SingleBlock);
                if (!string.IsNullOrWhiteSpace(block.Text))
                    best = block;
            }

            token.ThrowIfCancellationRequested();
            return best;
        }
        finally
        {
            EngineGate.Release();
        }
    }, token);

    private static SupplementaryOcrResult ProcessWithMode(TesseractEngine engine, Pix image, PageSegMode mode)
    {
        using var page = engine.Process(image, mode);
        var text = (page.GetText() ?? string.Empty).Trim();
        var confidence = text.Length == 0 ? 0f : page.GetMeanConfidence();
        return new SupplementaryOcrResult(text, confidence);
    }

    private static TesseractEngine GetOrCreateEngine(string libraryDirectory, CancellationToken token)
    {
        var directory = PrepareModels(libraryDirectory, token);
        var normalized = Path.GetFullPath(directory);
        if (_cachedEngine is not null && string.Equals(_cachedDirectory, normalized, StringComparison.OrdinalIgnoreCase))
            return _cachedEngine;

        _cachedEngine?.Dispose();
        _cachedEngine = null;
        _cachedDirectory = null;

        var engine = new TesseractEngine(normalized, "eng+tur", EngineMode.Default);
        engine.SetVariable("user_defined_dpi", "300");
        _cachedEngine = engine;
        _cachedDirectory = normalized;
        return engine;
    }

    private static byte[] BuildGrayscaleBmp(byte[] grayscale, int width, int height)
    {
        const int headerSize = 14 + 40;
        const int paletteSize = 256 * 4;
        const int pixelOffset = headerSize + paletteSize;

        var rowStride = (width + 3) & ~3;
        var imageSize = checked(rowStride * height);
        var totalSize = checked(pixelOffset + imageSize);
        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();

        // BITMAPFILEHEADER
        buffer[0] = (byte)'B';
        buffer[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(2, 4), totalSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(10, 4), pixelOffset);

        // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(22, 4), height); // bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(26, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(28, 2), 8);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(34, 4), imageSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(38, 4), 11811); // 300 DPI in px/m
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(42, 4), 11811);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(46, 4), 256);

        for (var i = 0; i < 256; i++)
        {
            var entry = headerSize + i * 4;
            buffer[entry] = (byte)i;
            buffer[entry + 1] = (byte)i;
            buffer[entry + 2] = (byte)i;
            buffer[entry + 3] = 0;
        }

        for (var y = 0; y < height; y++)
        {
            var srcRow = (height - 1 - y) * width;
            var dstRow = pixelOffset + y * rowStride;
            Buffer.BlockCopy(grayscale, srcRow, buffer, dstRow, width);
        }

        return buffer;
    }

    private static string PrepareModels(string libraryDirectory, CancellationToken token)
    {
        var directory = Path.Combine(libraryDirectory, "ocr-data"); Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("OCR veri klasörü bağlantı olamaz.");
        foreach (var model in Models)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(directory, model.Name + ".traineddata");
            if (ValidModel(target, model)) continue;
            var source = Path.Combine(AppData.DirectoryPath, "tessdata", model.Name + ".traineddata.gz");
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
