using WinForge.Dictation;
using WinForge.Modules.Capture;

internal static class DictationEngineChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var configuration = new DictationConfiguration("model.bin", "tr", false, false);
        var engines = new List<FakeEngine>();
        using var client = new DictationClient(factory: _ => { var engine = new FakeEngine(); engines.Add(engine); return engine; });
        check(await client.TranscribeAsync([.2f], configuration, default) == "İstanbul", "In-process engine preserves Unicode");
        await client.TranscribeAsync([.2f], configuration, default);
        check(engines.Count == 1 && engines[0].Calls == 2, "Repeated dictation reuses the loaded model");
        await client.TranscribeAsync([.2f], configuration with { Language = "en" }, default);
        check(engines.Count == 2 && engines[0].Disposed, "Configuration changes release the previous model");
        client.Dispose(); client.Dispose();
        check(!client.IsRunning && engines[1].Disposed, "Repeated session shutdown releases the model");
        await client.TranscribeAsync([.2f], configuration, default);
        check(engines.Count == 3, "Module reactivation starts a fresh in-process session");

        var gpuAttempts = 0;
        using var fallback = new DictationClient(factory: config =>
        {
            if (config.Gpu) { gpuAttempts++; throw new InvalidOperationException("GPU unavailable"); }
            return new FakeEngine();
        });
        await fallback.TranscribeAsync([.2f], configuration with { Gpu = true }, default);
        await fallback.TranscribeAsync([.2f], configuration with { Gpu = true }, default);
        check(gpuAttempts == 1 && fallback.Engine == "CPU (GPU yerine)", "GPU failure selects and reuses the CPU backend");

        var blocking = new FakeEngine { Block = true };
        using var active = new DictationClient(factory: _ => blocking);
        var running = active.TranscribeAsync([.2f], configuration, default);
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = active.TranscribeAsync([.2f], configuration, default);
        active.Dispose();
        check(!blocking.Disposed, "Shutdown does not free a model during a native call");
        blocking.Continue.Set();
        foreach (var task in new[] { running, queued })
        {
            try { await task; throw new InvalidOperationException("Cancellation ignored"); }
            catch (OperationCanceledException) { }
        }
        check(blocking.Disposed && blocking.Calls == 1 && !active.IsRunning,
            "Shutdown cancels queued inference and frees the model after the native call returns");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await client.TranscribeAsync([.2f], configuration, cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { check(engines.Count == 3, "Pre-cancelled inference never loads a model"); }
    }

    private sealed class FakeEngine : IDictationEngine
    {
        internal bool Disposed, Block;
        internal int Calls;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Continue = new(false);
        public string Transcribe(float[] samples, DictationConfiguration configuration, CancellationToken token)
        {
            Calls++; Entered.TrySetResult();
            if (Block) Continue.Wait(TimeSpan.FromSeconds(5));
            if (Disposed) throw new InvalidOperationException("Model freed during inference");
            token.ThrowIfCancellationRequested(); return "İstanbul";
        }
        public void Dispose() { Disposed = true; Continue.Dispose(); }
    }
}
