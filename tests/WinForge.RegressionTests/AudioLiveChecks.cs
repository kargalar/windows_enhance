using System.Diagnostics;
using System.IO;
using Expression = System.Linq.Expressions.Expression;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.RegressionTests;

/// <summary>Opt-in quiet own-session playback, native loopback signal and exact session restoration.</summary>
internal static class AudioLiveChecks
{
    private const int Frequency = 1000;
    private const int TransformSize = 8192;

    internal static void Run(string directory, Action<bool, string> check)
    {
        var endpointLevel = VolumeService.Current();
        var endpointMuted = VolumeService.IsMuted();
        if (endpointMuted || endpointLevel == 0)
            throw new InvalidOperationException($"Actual audio evidence is unavailable: default output muted={endpointMuted}, volume={endpointLevel}%. Master controls were not changed.");
        var path = Path.Combine(directory, "own-quiet-audio-verification.wav");
        WriteTone(path);
        var media = new MediaElement { Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Volume = 0.03 };
        var window = new Window { Content = media, ShowActivated = false, ShowInTaskbar = false, Width = 100, Height = 100, Left = -10000, Top = -10000,
            WindowStartupLocation = WindowStartupLocation.Manual, Title = "WinForge own quiet audio verification" };
        var opened = false;
        Exception? playbackError = null;
        media.MediaOpened += (_, _) => opened = true;
        media.MediaFailed += (_, args) => playbackError = args.ErrorException;
        media.Loaded += (_, _) => media.Pause();
        using var capture = new WasapiLoopbackCapture();
        var signal = new SignalCollector();
        capture.SamplesAvailable += signal.Push;
        OwnSessionLease? sessionLease = null;
        AudioSessionService.Session? originalSession = null;
        float originalScalar = 0;
        var originalMute = false;
        var baselineCaptured = false;
        Exception? testFailure = null;
        try
        {
            window.Show();
            PumpUntil(() => opened || playbackError is not null, TimeSpan.FromSeconds(6), "The own PCM file did not open through Windows media playback.");
            if (playbackError is not null) throw new InvalidOperationException("Windows PCM playback failed.", playbackError);
            capture.Start();
            PumpFor(TimeSpan.FromMilliseconds(350));
            var baseline = signal.Snapshot();
            var baselineSessionIds = AudioSessionService.GetActiveSessions()
                .Where(item => item.ProcessId == (uint)Environment.ProcessId)
                .Select(item => item.InstanceId).ToHashSet(StringComparer.Ordinal);
            signal.Reset();
            media.Play();
            AudioSessionService.Session[] NewPlaybackSessions() => AudioSessionService.GetActiveSessions()
                .Where(item => item.ProcessId == (uint)Environment.ProcessId && !baselineSessionIds.Contains(item.InstanceId)).ToArray();
            PumpUntil(() => NewPlaybackSessions().Length > 0, TimeSpan.FromSeconds(3),
                $"Windows exposed no newly active own playback session after excluding {baselineSessionIds.Count} baseline sessions, including the loopback stream. No session controls were changed.");
            var ownSessions = NewPlaybackSessions();
            if (ownSessions.Length != 1) throw new InvalidOperationException($"Expected exactly one newly active own playback session after excluding {baselineSessionIds.Count} baseline sessions; found {ownSessions.Length}. No session controls were changed.");
            originalSession = ownSessions[0];
            sessionLease = OwnSessionLease.Acquire(originalSession.InstanceId);
            (originalScalar, originalMute) = sessionLease.Read();
            baselineCaptured = true;
            if (originalMute || originalScalar == 0)
                throw new InvalidOperationException($"The own playback session is muted={originalMute}, scalar={originalScalar}. No session or master controls were changed before signal verification.");
            PumpUntil(() => signal.Count >= TransformSize && media.Position.TotalSeconds >= 0.7, TimeSpan.FromSeconds(4),
                $"No complete native PCM frame reached loopback. Capture error: {capture.LastError?.Message ?? "none"}.");
            var tone = signal.Snapshot();
            var measured = Analyze(tone.Samples, tone.SampleRate);
            var baselineEnergy = baseline.Samples.Length == TransformSize ? Analyze(baseline.Samples, baseline.SampleRate).TargetMagnitude : 0;
            check(tone.SampleRate >= 8000 && tone.Samples.Length == TransformSize && measured.TargetMagnitude > 0.0000001 &&
                  measured.TargetMagnitude > baselineEnergy * 3 && Math.Abs(measured.TargetFrequency - Frequency) < 25,
                "Actual own Windows PCM playback reaches WASAPI loopback with a measured 1 kHz FFT component above its pre-playback baseline");
            var analyzer = new AudioSpectrumAnalyzer();
            // Gain affects only copied verification samples, never playback or the endpoint.
            var normalized = tone.Samples.Select(sample => (float)Math.Clamp(sample / Math.Max(0.000001, measured.TargetMagnitude * 4), -1, 1)).ToArray();
            analyzer.Push(normalized, tone.SampleRate);
            var levels = analyzer.GetLevels();
            var strongestBand = Array.IndexOf(levels, levels.Max());
            var upper = Math.Min(18000, tone.SampleRate / 2d);
            var low = 43 * Math.Pow(upper / 43, strongestBand / (double)AudioSpectrumAnalyzer.BandCount);
            var high = 43 * Math.Pow(upper / 43, (strongestBand + 1d) / AudioSpectrumAnalyzer.BandCount);
            check(levels.Max() > 0 && Frequency >= low - tone.SampleRate / 1024d && Frequency <= high + tone.SampleRate / 1024d,
                "The own spectrum analyzer's strongest logarithmic band contains the real captured 1 kHz playback signal");

            if (!AudioSessionService.SetVolume(originalSession.InstanceId, 2)) throw new InvalidOperationException("Own session volume update was rejected.");
            var lowered = sessionLease.Read();
            check(Math.Abs(lowered.Volume - 0.02f) < 0.00001f && !lowered.Muted,
                "Core Audio own-session volume changes read back at the requested native scalar without master-volume changes");
            if (!AudioSessionService.SetMuted(originalSession.InstanceId, true)) throw new InvalidOperationException("Own session mute was rejected.");
            check(sessionLease.Read().Muted, "Core Audio own-session mute reads back from the actual native session");
            sessionLease.Restore(originalScalar, originalMute);
            var restored = sessionLease.Read();
            check(restored.Volume == originalScalar && restored.Muted == originalMute,
                "Own audio-session cleanup restores the exact original floating-point volume and mute values");
            PumpUntil(() => media.Position.TotalSeconds >= 4.8 || playbackError is not null, TimeSpan.FromSeconds(6), "The own five-second PCM file did not advance through Windows playback.");
            if (playbackError is not null) throw new InvalidOperationException("Windows playback failed during the own test.", playbackError);
            check(VolumeService.Current() == endpointLevel && VolumeService.IsMuted() == endpointMuted,
                "Live own-session verification leaves the default endpoint master volume and mute unchanged");
            Console.WriteLine($"Audio live: own PID={Environment.ProcessId}; rate={tone.SampleRate}; target={measured.TargetFrequency:F2} Hz; magnitude={measured.TargetMagnitude:G6}; baseline={baselineEnergy:G6}; session={originalSession.InstanceId}.");
        }
        catch (Exception exception) { testFailure = exception; throw; }
        finally
        {
            List<Exception> errors = [];
            try { if (sessionLease is not null && baselineCaptured) sessionLease.Restore(originalScalar, originalMute); }
            catch (Exception exception) { errors.Add(exception); }
            try { sessionLease?.Dispose(); } catch (Exception exception) { errors.Add(exception); }
            try { media.Stop(); media.Close(); window.Close(); } catch (Exception exception) { errors.Add(exception); }
            try { capture.Dispose(); } catch (Exception exception) { errors.Add(exception); }
            if (errors.Count > 0)
            {
                if (testFailure is not null) errors.Insert(0, testFailure);
                throw new AggregateException("Own audio fixture cleanup failed; original failure is included when present.", errors);
            }
        }
    }

    private static (double TargetFrequency, double TargetMagnitude) Analyze(float[] samples, int rate)
    {
        var fft = samples.Select((sample, index) => new Complex(sample * (0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (TransformSize - 1))), 0)).ToArray();
        AudioSpectrumAnalyzer.Transform(fft);
        var first = Math.Max(1, (int)((Frequency - 25d) * TransformSize / rate));
        var last = Math.Min(TransformSize / 2 - 1, (int)Math.Ceiling((Frequency + 25d) * TransformSize / rate));
        var bin = Enumerable.Range(first, last - first + 1).MaxBy(index => fft[index].Magnitude);
        return (bin * (double)rate / TransformSize, fft[bin].Magnitude * 2 / TransformSize);
    }
    private static void WriteTone(string path)
    {
        const int rate = 48000;
        const int frames = rate * 5;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + frames * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(frames * 2);
        for (var index = 0; index < frames; index++)
        {
            var fade = Math.Min(1, Math.Min(index, frames - index - 1) / (rate * 0.04));
            writer.Write((short)Math.Round(Math.Sin(2 * Math.PI * Frequency * index / rate) * short.MaxValue * 0.1 * fade));
        }
    }
    private static void PumpUntil(Func<bool> ready, TimeSpan timeout, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!ready()) { if (clock.Elapsed >= timeout) throw new TimeoutException(message); PumpFor(TimeSpan.FromMilliseconds(20)); }
    }
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private sealed class SignalCollector
    {
        private readonly object _gate = new();
        private readonly Queue<float> _samples = new(TransformSize);
        private int _rate;
        internal int Count { get { lock (_gate) return _samples.Count; } }
        internal void Reset() { lock (_gate) _samples.Clear(); }
        internal void Push(ReadOnlySpan<float> samples, int rate)
        {
            lock (_gate)
            {
                if (_rate != rate) _samples.Clear();
                _rate = rate;
                foreach (var sample in samples) { if (_samples.Count == TransformSize) _samples.Dequeue(); _samples.Enqueue(sample); }
            }
        }
        internal (float[] Samples, int SampleRate) Snapshot() { lock (_gate) return (_samples.ToArray(), _rate); }
    }

    private sealed class OwnSessionLease(object control) : IDisposable
    {
        private object? _control = control;
        internal (float Volume, bool Muted) Read()
        {
            var volume = (ISimpleAudioVolume)(_control ?? throw new ObjectDisposedException(nameof(OwnSessionLease)));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out var scalar));
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            return (scalar, muted);
        }
        internal void Restore(float scalar, bool muted)
        {
            var volume = (ISimpleAudioVolume)(_control ?? throw new ObjectDisposedException(nameof(OwnSessionLease)));
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(volume.SetMasterVolume(scalar, ref context));
            Marshal.ThrowExceptionForHR(volume.SetMute(muted, ref context));
        }
        public void Dispose() { var value = Interlocked.Exchange(ref _control, null); if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        internal static OwnSessionLease Acquire(string instanceId)
        {
            // Reuse production enumeration while retaining one owned control reference for exact native readback/restore.
            var fixture = new SessionFinder(instanceId);
            var enumerate = typeof(AudioSessionService).GetMethod("WithSessionEnumerator", BindingFlags.Static | BindingFlags.NonPublic)!;
            var actionType = enumerate.GetParameters()[0].ParameterType;
            var argument = Expression.Parameter(actionType.GenericTypeArguments[0]);
            var body = Expression.Call(Expression.Constant(fixture), typeof(SessionFinder).GetMethod(nameof(SessionFinder.Inspect))!, Expression.Convert(argument, typeof(object)));
            enumerate.Invoke(null, [Expression.Lambda(actionType, body, argument).Compile()]);
            return new(fixture.Control ?? throw new InvalidOperationException("The exact own audio session disappeared before native readback."));
        }
    }
    private sealed class SessionFinder(string instanceId)
    {
        internal object? Control { get; private set; }
        public void Inspect(object enumerator)
        {
            var enumeration = typeof(AudioSessionService).GetNestedType("IAudioSessionEnumerator", BindingFlags.NonPublic)!;
            var details = typeof(AudioSessionService).GetNestedType("IAudioSessionControl2", BindingFlags.NonPublic)!;
            object[] countArgs = [0];
            Marshal.ThrowExceptionForHR((int)enumeration.GetMethod("GetCount")!.Invoke(enumerator, countArgs)!);
            for (var index = 0; index < (int)countArgs[0]; index++)
            {
                object? control = null;
                try
                {
                    object?[] getArgs = [index, null];
                    Marshal.ThrowExceptionForHR((int)enumeration.GetMethod("GetSession")!.Invoke(enumerator, getArgs)!);
                    control = getArgs[1]!;
                    object[] idArgs = [""];
                    object[] pidArgs = [0u];
                    Marshal.ThrowExceptionForHR((int)details.GetMethod("GetSessionInstanceIdentifier")!.Invoke(control, idArgs)!);
                    Marshal.ThrowExceptionForHR((int)details.GetMethod("GetProcessId")!.Invoke(control, pidArgs)!);
                    if ((string)idArgs[0] != instanceId || (uint)pidArgs[0] != (uint)Environment.ProcessId) continue;
                    Control = control;
                    control = null;
                    return;
                }
                finally { if (control is not null && Marshal.IsComObject(control)) Marshal.ReleaseComObject(control); }
            }
        }
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float scalar, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float scalar);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
