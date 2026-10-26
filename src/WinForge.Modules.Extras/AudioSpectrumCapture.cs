using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Connects the native capture worker to the shared streaming spectrum analyzer.</summary>
internal sealed class AudioSpectrumCapture : IDisposable
{
    private readonly AudioSpectrumAnalyzer _analyzer = new();
    private WasapiLoopbackCapture? _capture;

    public void Start()
    {
        if (_capture is not null) return;
        var capture = new WasapiLoopbackCapture();
        capture.SamplesAvailable += _analyzer.Push;
        try { capture.Start(); _capture = capture; }
        catch { capture.SamplesAvailable -= _analyzer.Push; capture.Dispose(); throw; }
    }

    public float[] GetLevels() => _analyzer.GetLevels();

    public void Dispose()
    {
        var capture = Interlocked.Exchange(ref _capture, null);
        if (capture is null) return;
        capture.SamplesAvailable -= _analyzer.Push;
        capture.Dispose();
    }
}
