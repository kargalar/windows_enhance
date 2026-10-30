using System.Diagnostics;
using System.IO.Pipes;
using WinForge.Dictation;

namespace WinForge.Modules.Capture;

internal static class DictationDefaults
{
    public static readonly string[] ModelFiles = ["ggml-large-v3-turbo.bin", "ggml-large-v3.bin"];
    public const string Hotkey = "Shift+Space";
}

internal sealed class DictationClient(string? workerDirectory = null) : IDisposable
{
    private Process? process;
    private NamedPipeServerStream? pipe;
    private WorkerConfiguration? loaded;
    private WorkerConfiguration? fallbackConfiguration;
    public string Engine { get; private set; } = "";
    internal bool IsRunning => process is { HasExited: false };
    private async Task PrepareAsync(WorkerConfiguration configuration, CancellationToken token)
    {
        if (configuration == loaded && process is { HasExited: false } && pipe is { IsConnected: true }) return;
        Dispose();
        var worker = Path.Combine(workerDirectory ?? Path.Combine(AppContext.BaseDirectory, "dictation"), "WinForge.Dictation.Worker.exe");
        if (!File.Exists(worker)) throw new FileNotFoundException("Dikte worker bulunamadı. build-dictation.ps1 ile hazırlayın.", worker);
        var backend = configuration.Gpu ? "cuda" : "cpu";
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(worker)!, backend, "WinForge.Whisper.dll"))) throw new FileNotFoundException(backend + " Whisper motoru bulunamadı.");
        var name = "WinForge-Dictation-" + Guid.NewGuid().ToString("N");
        pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(worker) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(worker)! };
        foreach (var argument in new[] { name, Environment.ProcessId.ToString(), backend }) start.ArgumentList.Add(argument);
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Dikte worker başlatılamadı.");
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token); connection.CancelAfter(TimeSpan.FromSeconds(15));
            await pipe.WaitForConnectionAsync(connection.Token);
            await DictationProtocol.WriteAsync(pipe, configuration, token);
            using var load = CancellationTokenSource.CreateLinkedTokenSource(token); load.CancelAfter(TimeSpan.FromMinutes(3));
            var reply = await DictationProtocol.ReadAsync<WorkerReply>(pipe, load.Token);
            if (!reply.Success) throw new InvalidOperationException(reply.Error);
            loaded = configuration; Engine = reply.Engine;
        }
        catch (Exception) when (token.IsCancellationRequested) { Dispose(); throw new OperationCanceledException(token); }
        catch { Dispose(); throw; }
    }
    public async Task<string> TranscribeAsync(float[] samples, WorkerConfiguration configuration, CancellationToken token)
    {
        if (samples.Length == 0 || samples.Length > DictationProtocol.SampleRate * DictationProtocol.MaximumSeconds) throw new ArgumentException("Geçersiz kayıt uzunluğu.");
        try
        {
            if (configuration == fallbackConfiguration)
            {
                var text = await RunAsync(samples, configuration with { Gpu = false }, token); Engine = "CPU (GPU yerine)"; return text;
            }
            return await RunAsync(samples, configuration, token);
        }
        catch (Exception) when (token.IsCancellationRequested) { Dispose(); throw new OperationCanceledException(token); }
        catch (Exception) when (configuration.Gpu && !token.IsCancellationRequested)
        {
            Dispose(); var text = await RunAsync(samples, configuration with { Gpu = false }, token);
            fallbackConfiguration = configuration; Engine = "CPU (GPU yerine)"; return text;
        }
        finally { if (token.IsCancellationRequested) Dispose(); }
    }
    private async Task<string> RunAsync(float[] samples, WorkerConfiguration configuration, CancellationToken token)
    {
        await PrepareAsync(configuration, token);
        var bytes = new byte[samples.Length * sizeof(float)]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            await DictationProtocol.WriteBytesAsync(pipe!, bytes, DictationProtocol.MaximumAudioBytes, timeout.Token);
            var reply = await DictationProtocol.ReadAsync<WorkerReply>(pipe!, timeout.Token);
            if (!reply.Success) throw new InvalidOperationException(reply.Error); return reply.Text;
        }
        catch { Dispose(); throw; }
        finally { Array.Clear(bytes); }
    }
    public void Dispose()
    {
        loaded = null; pipe?.Dispose(); pipe = null;
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            process.Dispose(); process = null;
        }
    }
}
