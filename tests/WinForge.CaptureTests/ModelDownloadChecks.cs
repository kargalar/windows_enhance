using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using WinForge.Modules.Capture;

internal static class ModelDownloadChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "model-download");
        Directory.CreateDirectory(directory);
        var data = System.Text.Encoding.UTF8.GetBytes("model test fixture");
        var checksum = Convert.ToHexString(SHA256.HashData(data));
        using var http = new HttpClient(new FixtureHandler(data));
        var url = new Uri("https://example.invalid/model");
        await WhisperModels.DownloadAsync(http, url, directory, "model.bin", checksum, data.Length, null, default);
        check((await File.ReadAllBytesAsync(Path.Combine(directory, "model.bin"))).SequenceEqual(data), "Model download verifies and installs complete content");
        try
        {
            await WhisperModels.DownloadAsync(http, url, directory, "model.bin", new string('0', 64), data.Length, null, default);
            throw new InvalidOperationException("Invalid checksum accepted");
        }
        catch (InvalidDataException) { }
        check((await File.ReadAllBytesAsync(Path.Combine(directory, "model.bin"))).SequenceEqual(data), "Invalid download preserves the installed model");
        try
        {
            await WhisperModels.DownloadAsync(http, url, directory, "model.bin", checksum, data.Length + 1, null, default);
            throw new InvalidOperationException("Invalid length accepted");
        }
        catch (InvalidDataException) { }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try
        {
            await WhisperModels.DownloadAsync(http, url, directory, "model.bin", checksum, data.Length, null, cancelled.Token);
            throw new InvalidOperationException("Cancellation ignored");
        }
        catch (OperationCanceledException) { }
        var blocked = new BlockingStream();
        using var slow = new HttpClient(new StreamingHandler(blocked));
        using var stop = new CancellationTokenSource();
        var pending = WhisperModels.DownloadAsync(slow, url, directory, "model.bin", checksum, data.Length, null, stop.Token);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        try { await pending; throw new InvalidOperationException("In-progress cancellation ignored"); }
        catch (OperationCanceledException) { }
        check(!Directory.EnumerateFiles(directory, "*.tmp").Any() && (await File.ReadAllBytesAsync(Path.Combine(directory, "model.bin"))).SequenceEqual(data),
            "Failed and interrupted downloads leave no temporary files and preserve the installed model");
    }
    private sealed class FixtureHandler(byte[] data) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
    }
    private sealed class StreamingHandler(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
    }
    private sealed class BlockingStream : Stream
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
