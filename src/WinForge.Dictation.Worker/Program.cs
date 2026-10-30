using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using WinForge.Dictation;

if (args.Length != 3 || !int.TryParse(args[1], out var parentId)) return 2;
using var lifetime = new CancellationTokenSource();
_ = Task.Run(async () =>
{
    try { using var parent = Process.GetProcessById(parentId); await parent.WaitForExitAsync(); }
    catch (ArgumentException) { }
    Environment.Exit(0);
});
using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.InOut, PipeOptions.Asynchronous);
nint model = 0;
try
{
    await pipe.ConnectAsync(15_000);
    var libraryPath = Path.Combine(AppContext.BaseDirectory, args[2], "WinForge.Whisper.dll");
    NativeLibrary.SetDllImportResolver(typeof(Engine).Assembly, (name, _, _) => name == "WinForge.Whisper" ? NativeLibrary.Load(libraryPath) : 0);
    var config = await DictationProtocol.ReadAsync<WorkerConfiguration>(pipe, lifetime.Token);
    model = Engine.Load(config.Model, config.Gpu);
    if (model == 0) throw new InvalidOperationException("Whisper modeli yüklenemedi.");
    await DictationProtocol.WriteAsync(pipe, new WorkerReply(true, "", config.Gpu ? "CUDA" : "CPU"), lifetime.Token);
    while (pipe.IsConnected)
    {
        var bytes = await DictationProtocol.ReadBytesAsync(pipe, DictationProtocol.MaximumAudioBytes, lifetime.Token);
        if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0) throw new InvalidDataException("Geçersiz ses tamponu.");
        var samples = new float[bytes.Length / sizeof(float)]; Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length); Array.Clear(bytes);
        try
        {
            var output = new byte[DictationProtocol.MaximumMessageBytes];
            var length = Engine.Transcribe(model, samples, samples.Length, config.Language, config.Translate, output, output.Length);
            if (length < 0) throw new InvalidOperationException("Whisper ses çözümlemesi başarısız.");
            await DictationProtocol.WriteAsync(pipe, new WorkerReply(true, Encoding.UTF8.GetString(output, 0, length).Trim(), config.Gpu ? "CUDA" : "CPU"), lifetime.Token);
        }
        finally { Array.Clear(samples); }
    }
}
catch (EndOfStreamException) { }
catch (Exception error)
{
    if (pipe.IsConnected) try { await DictationProtocol.WriteAsync(pipe, new WorkerReply(false, "", "", error.Message), CancellationToken.None); } catch (IOException) { }
    return 1;
}
finally { if (model != 0) Engine.Free(model); }
return 0;

internal static class Engine
{
    [DllImport("WinForge.Whisper", EntryPoint = "wf_load", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint Load([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.I1)] bool gpu);
    [DllImport("WinForge.Whisper", EntryPoint = "wf_free", CallingConvention = CallingConvention.Cdecl)] internal static extern void Free(nint model);
    [DllImport("WinForge.Whisper", EntryPoint = "wf_transcribe", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Transcribe(nint model, float[] samples, int count, [MarshalAs(UnmanagedType.LPUTF8Str)] string language,
        [MarshalAs(UnmanagedType.I1)] bool translate, byte[] output, int capacity);
}
