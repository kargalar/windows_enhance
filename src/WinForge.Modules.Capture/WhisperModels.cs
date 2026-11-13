using System.Security.Cryptography;
using System.Net.Http;

namespace WinForge.Modules.Capture;

public static class WhisperModels
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly IReadOnlyDictionary<string, long> Sizes = new Dictionary<string, long>
    {
        ["ggml-large-v3-turbo.bin"] = 1624555275,
        ["ggml-large-v3.bin"] = 3095033483,
        ["ggml-small.bin"] = 487601967
    };
    public static readonly IReadOnlyDictionary<string, string> Checksums = new Dictionary<string, string>
    {
        ["ggml-large-v3-turbo.bin"] = "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69",
        ["ggml-large-v3.bin"] = "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2",
        ["ggml-small.bin"] = "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"
    };
    public static async Task CopyAsync(string source, string directory, string name, CancellationToken token)
    {
        if (!Checksums.TryGetValue(name, out var expected)) throw new ArgumentException("Bilinmeyen model.");
        Directory.CreateDirectory(directory); var destination = Path.Combine(directory, name);
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        { await VerifyAsync(source, expected, token); return; }
        if (File.Exists(destination))
        {
            try { await VerifyAsync(destination, expected, token); return; }
            catch (InvalidDataException) { }
        }
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) await input.CopyToAsync(output, token);
            await VerifyAsync(temporary, expected, token); File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static Task DownloadAsync(string directory, string name, IProgress<double>? progress, CancellationToken token)
    {
        if (!Checksums.TryGetValue(name, out var checksum) || !Sizes.TryGetValue(name, out var size))
            throw new ArgumentException("Bilinmeyen model.", nameof(name));
        var uri = new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + name);
        return DownloadAsync(Http, uri, directory, name, checksum, size, progress, token);
    }
    internal static async Task DownloadAsync(HttpClient http, Uri uri, string directory, string name,
        string expected, long size, IProgress<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, name);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != size)
                throw new InvalidDataException("Model dosyasının boyutu beklenen değerle eşleşmiyor.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920]; long total = 0; var lastReport = Environment.TickCount64;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    total += read;
                    if (total > size) throw new InvalidDataException("Model dosyası beklenenden büyük.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    if (Environment.TickCount64 - lastReport >= 250) { progress?.Report((double)total / size); lastReport = Environment.TickCount64; }
                }
                if (total != size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model dosyası eksik veya SHA-256 özeti eşleşmiyor.");
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
            progress?.Report(1);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task VerifyAsync(string path, string expected, CancellationToken token)
    {
        await using var input = File.OpenRead(path); var checksum = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        if (!checksum.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Model dosyasının SHA-256 özeti beklenen modelle eşleşmiyor.");
    }
}
