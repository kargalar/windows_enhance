using System.Security.Cryptography;

namespace WinForge.Modules.Capture;

public static class WhisperModels
{
    public static readonly IReadOnlyDictionary<string, string> Checksums = new Dictionary<string, string>
    {
        ["ggml-large-v3-turbo.bin"] = "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69",
        ["ggml-large-v3.bin"] = "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2"
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
    private static async Task VerifyAsync(string path, string expected, CancellationToken token)
    {
        await using var input = File.OpenRead(path); var checksum = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        if (!checksum.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Model dosyasının SHA-256 özeti beklenen modelle eşleşmiyor.");
    }
}
