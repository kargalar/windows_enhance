using System.Runtime.InteropServices;
using System.Text;
using WinForge.Dictation;

namespace WinForge.Modules.Capture;

internal interface IDictationEngine : IDisposable
{
    string Transcribe(float[] samples, DictationConfiguration configuration, CancellationToken token);
}

internal sealed class NativeDictationEngine : IDictationEngine
{
    // The native OpenMP runtime can retain worker threads after a model is freed.
    // Unloading its parent DLL while those threads are exiting can crash the process.
    // Keep one library reference per backend for the lifetime of this process; models
    // themselves are still released at the end of each session.
    private static readonly object LibraryGate = new();
    private static readonly Dictionary<string, nint> Libraries = new(StringComparer.OrdinalIgnoreCase);
    private nint library, model;
    private readonly FreeModel free;
    private readonly TranscribeAudio transcribe;

    internal NativeDictationEngine(string? directory, DictationConfiguration configuration)
    {
        var path = Path.GetFullPath(Path.Combine(directory ?? Path.Combine(AppContext.BaseDirectory, "dictation"),
            configuration.Gpu ? "cuda" : "cpu", "WinForge.Whisper.dll"));
        if (!File.Exists(path)) throw new FileNotFoundException("Whisper motoru bulunamadı. build-dictation.ps1 ile hazırlayın.", path);
        lock (LibraryGate)
        {
            if (!Libraries.TryGetValue(path, out library))
            {
                library = NativeLibrary.Load(path, typeof(NativeDictationEngine).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
                Libraries.Add(path, library);
            }
        }
        try
        {
            var load = Export<LoadModel>("wf_load");
            free = Export<FreeModel>("wf_free");
            transcribe = Export<TranscribeAudio>("wf_transcribe_cancellable");
            model = load(configuration.Model, configuration.Gpu);
            if (model == 0) throw new InvalidOperationException("Whisper modeli yüklenemedi.");
        }
        catch { library = 0; throw; }
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    public string Transcribe(float[] samples, DictationConfiguration configuration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var output = new byte[DictationAudio.MaximumMessageBytes];
        ShouldAbort abort = _ => token.IsCancellationRequested;
        try
        {
            var length = transcribe(model, samples, samples.Length, configuration.Language, configuration.Translate,
                output, output.Length, abort, 0);
            GC.KeepAlive(abort);
            token.ThrowIfCancellationRequested();
            if (length < 0 || length > output.Length) throw new InvalidOperationException("Whisper ses çözümlemesi başarısız.");
            return Encoding.UTF8.GetString(output, 0, length).Trim();
        }
        finally { Array.Clear(output); }
    }

    public void Dispose()
    {
        if (model != 0) { free(model); model = 0; }
        library = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint LoadModel([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.I1)] bool gpu);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeModel(nint model);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private delegate bool ShouldAbort(nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TranscribeAudio(nint model, float[] samples, int count,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string language, [MarshalAs(UnmanagedType.I1)] bool translate,
        [Out] byte[] output, int capacity, ShouldAbort abort, nint data);
}
