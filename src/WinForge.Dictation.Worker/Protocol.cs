using System.Buffers.Binary;
using System.Text.Json;

namespace WinForge.Dictation;

public sealed record WorkerConfiguration(string Model, string Language, bool Translate, bool Gpu);
public sealed record WorkerReply(bool Success, string Text, string Engine, string Error = "");
public static class DictationProtocol
{
    public const int SampleRate = 16_000;
    public const int MaximumSeconds = 300;
    public const int MaximumAudioBytes = SampleRate * MaximumSeconds * sizeof(float);
    public const int MaximumMessageBytes = 1024 * 1024;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value); await WriteBytesAsync(stream, bytes, MaximumMessageBytes, token);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token) =>
        JsonSerializer.Deserialize<T>(await ReadBytesAsync(stream, MaximumMessageBytes, token)) ?? throw new InvalidDataException("Boş worker mesajı.");
    public static async Task WriteBytesAsync(Stream stream, byte[] bytes, int maximum, CancellationToken token)
    {
        if (bytes.Length > maximum) throw new InvalidDataException("Worker mesaj sınırı aşıldı.");
        var length = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    public static async Task<byte[]> ReadBytesAsync(Stream stream, int maximum, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > maximum) throw new InvalidDataException("Geçersiz worker mesaj boyutu.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token); return bytes;
    }
}
