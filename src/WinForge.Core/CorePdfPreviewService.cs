using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace WinForge.Core;

public sealed record PdfPreviewResult(BitmapSource Image, int PageCount);

/// <summary>Renders bounded previews in our native WARP helper without loading a GPU PDF renderer into the UI process.</summary>
public static class CorePdfPreviewService
{
    private const int MaximumDimension = 4096;
    private const long MaximumOutputBytes = 25_165_824;
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(30);
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static async Task<PdfPreviewResult> RenderFirstPageAsync(string path, int maximumDimension = 1400,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumDimension is < 64 or > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        var input = Path.GetFullPath(path);
        if (!File.Exists(input)) throw new FileNotFoundException("PDF dosyası bulunamadı.", input);
        cancellation.ThrowIfCancellationRequested();
        var nativeDirectory = Environment.GetEnvironmentVariable("WINFORGE_NATIVE_DIRECTORY") ?? Path.Combine(AppContext.BaseDirectory, "native");
        var host = Path.GetFullPath(Path.Combine(nativeDirectory, "WinForge.ShellHost.exe"));
        if (!File.Exists(host)) throw new FileNotFoundException("WinForge yerel PDF işleyicisi bulunamadı; uygulamanın yerel bileşenlerini yeniden derleyin.", host);

        var directory = Path.Combine(WinForge.Core.AppData.TemporaryDirectory, "PdfPreview", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(directory, "preview.png");
        Directory.CreateDirectory(directory);
        try
        {
            var start = new ProcessStartInfo(host)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "render-pdf", input, output, maximumDimension.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("WinForge PDF işleyicisi başlatılamadı.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellation);
            var stderr = process.StandardError.ReadToEndAsync(cancellation);
            try
            {
                await process.WaitForExitAsync(cancellation).WaitAsync(RenderTimeout, cancellation).ConfigureAwait(false);
                var status = await stdout.ConfigureAwait(false);
                var error = await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"PDF önizlemesi oluşturulamadı ({process.ExitCode}): {error.Trim()}");
                return DecodePreview(output, status, maximumDimension);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: false);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            // This directory and exact output file were created for this operation.
            // No recursive cleanup or caller-selected output paths are involved.
            if (File.Exists(output)) File.Delete(output);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    internal static PdfPreviewResult DecodePreview(string output, string status, int maximumDimension)
    {
        using var json = JsonDocument.Parse(status);
        var root = json.RootElement;
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        var pages = root.GetProperty("pageCount").GetInt32();
        if (root.GetProperty("backend").GetString() != "warp" || pages < 1 || width < 1 || height < 1 ||
            width > maximumDimension || height > maximumDimension)
            throw new InvalidDataException("PDF işleyicisinin boyut veya altyapı bilgisi geçersiz.");
        using var stream = File.OpenRead(output);
        if (stream.Length is < 24 or > MaximumOutputBytes) throw new InvalidDataException("PDF önizleme çıktısı izin verilen boyutu aşıyor.");
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(PngSignature) || !header.Slice(12, 4).SequenceEqual("IHDR"u8) ||
            BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4)) != width ||
            BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4)) != height)
            throw new InvalidDataException("PDF önizleme görseli doğrulanan boyutlarla eşleşmiyor.");
        stream.Position = 0;
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = decoder.Frames[0];
        if (bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            throw new InvalidDataException("PDF önizleme görselinin çözümlenen boyutu geçersiz.");
        bitmap.Freeze();
        return new(bitmap, pages);
    }
}
