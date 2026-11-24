using WinForge.Dictation;

namespace WinForge.Modules.Capture;

internal static class DictationDefaults
{
    public const string TranslationModel = "ggml-large-v3.bin";
    public static readonly string[] ModelFiles = ["ggml-large-v3-turbo.bin", TranslationModel, "ggml-small.bin"];
    public const string Hotkey = "Shift+Space";
    public const string TranslationHotkey = "Ctrl+Shift+Space";
}

// Dispose resets a reusable module session. Native resources are freed only after inference returns.
internal sealed class DictationClient(string? engineDirectory = null,
    Func<DictationConfiguration, IDictationEngine>? factory = null, TimeSpan? timeout = null) : IDisposable
{
    private readonly object state = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource lifetime = new();
    private IDictationEngine? engine;
    private DictationConfiguration? loaded, fallbackConfiguration;
    public string Engine { get; private set; } = "";
    internal bool IsRunning => engine is not null;

    public Task PrepareAsync(DictationConfiguration configuration, CancellationToken token) => ExecuteAsync(null, configuration, token);

    public Task<string> TranscribeAsync(float[] samples, DictationConfiguration configuration, CancellationToken token)
    {
        if (samples.Length == 0 || samples.Length > DictationAudio.SampleRate * DictationAudio.MaximumSeconds)
            throw new ArgumentException("Geçersiz kayıt uzunluğu.");
        return ExecuteAsync(samples, configuration, token);
    }

    private async Task<string> ExecuteAsync(float[]? samples, DictationConfiguration configuration, CancellationToken token)
    {
        CancellationTokenSource operation;
        CancellationToken sessionToken;
        lock (state) { sessionToken = lifetime.Token; operation = CancellationTokenSource.CreateLinkedTokenSource(token, sessionToken); }
        using (operation)
        {
            operation.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));
            var cancellation = operation.Token;
            try { await gate.WaitAsync(cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !sessionToken.IsCancellationRequested)
            { throw new TimeoutException("Dikte çözümlemesi zaman aşımına uğradı. Daha kısa bir kayıt deneyin."); }
            try
            {
                return await Task.Run(() =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (configuration.Gpu && configuration.Model == fallbackConfiguration?.Model)
                        return Run(samples, configuration with { Gpu = false }, cancellation, fallback: true);
                    try { return Run(samples, configuration, cancellation); }
                    catch (Exception) when (configuration.Gpu && !cancellation.IsCancellationRequested)
                    {
                        ReleaseEngine();
                        var text = Run(samples, configuration with { Gpu = false }, cancellation, fallback: true);
                        fallbackConfiguration = configuration;
                        return text;
                    }
                }, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !sessionToken.IsCancellationRequested)
            {
                ReleaseEngine();
                throw new TimeoutException("Dikte çözümlemesi zaman aşımına uğradı. Daha kısa bir kayıt deneyin.");
            }
            catch { ReleaseEngine(); throw; }
            finally
            {
                lock (state)
                {
                    if (cancellation.IsCancellationRequested) { ReleaseEngine(); fallbackConfiguration = null; }
                    gate.Release();
                }
            }
        }
    }

    private string Run(float[]? samples, DictationConfiguration configuration, CancellationToken token, bool fallback = false)
    {
        token.ThrowIfCancellationRequested();
        // Language and translation are inference options, not model-loading options.
        if (loaded?.Model != configuration.Model || loaded?.Gpu != configuration.Gpu || engine is null)
        {
            ReleaseEngine();
            engine = factory is null ? new NativeDictationEngine(engineDirectory, configuration) : factory(configuration);
            loaded = configuration;
        }
        token.ThrowIfCancellationRequested();
        var text = samples is null ? "" : engine.Transcribe(samples, configuration, token);
        token.ThrowIfCancellationRequested();
        Engine = fallback ? "CPU (GPU yerine)" : configuration.Gpu ? "CUDA" : "CPU";
        return text;
    }

    private void ReleaseEngine()
    {
        engine?.Dispose(); engine = null; loaded = null; Engine = "";
    }

    public void Dispose()
    {
        lock (state)
        {
            lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
            if (!gate.Wait(0)) return;
            try { ReleaseEngine(); fallbackConfiguration = null; }
            finally { gate.Release(); }
        }
    }
}
