using System.Runtime.InteropServices;
using System.Diagnostics;
using WinForge.Dictation;

namespace WinForge.Modules.Capture;

public sealed class SpeechBuffer
{
    public const double SilenceThreshold = 0.002;
    private readonly object gate = new();
    private readonly List<float> samples = [];
    private double position;
    private float previousSample;
    private const int FilterTaps = 63;
    private readonly float[] filterHistory = new float[FilterTaps];
    private double[]? filterCoefficients;
    private int filterIndex, filterRate;
    public bool Full { get { lock (gate) return samples.Count >= DictationAudio.SampleRate * DictationAudio.MaximumSeconds; } }
    public void Append(ReadOnlySpan<float> packet, int sampleRate)
    {
        if (sampleRate < 8000 || sampleRate > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        lock (gate)
        {
            var limit = DictationAudio.SampleRate * DictationAudio.MaximumSeconds;
            if (packet.IsEmpty || samples.Count >= limit) return;
            if (sampleRate != filterRate)
            {
                Array.Clear(filterHistory); filterIndex = 0; filterRate = sampleRate;
                filterCoefficients = sampleRate > DictationAudio.SampleRate ? CreateFilter(sampleRate) : null;
                position = 0; previousSample = 0;
            }
            // Suppress frequencies above the 16 kHz output's Nyquist limit before
            // decimation; otherwise microphone noise aliases into the speech band.
            float[]? filtered = null;
            if (filterCoefficients is not null)
            {
                filtered = new float[packet.Length];
                for (var i = 0; i < packet.Length; i++)
                {
                    filterHistory[filterIndex] = float.IsFinite(packet[i]) ? Math.Clamp(packet[i], -1, 1) : 0;
                    double sum = 0; var index = filterIndex;
                    for (var tap = 0; tap < FilterTaps; tap++)
                    {
                        sum += filterHistory[index] * filterCoefficients[tap];
                        if (--index < 0) index = FilterTaps - 1;
                    }
                    filterIndex = (filterIndex + 1) % FilterTaps;
                    filtered[i] = (float)sum;
                }
                packet = filtered;
            }
            var step = (double)sampleRate / DictationAudio.SampleRate;
            while (position < packet.Length && samples.Count < limit)
            {
                var left = (int)Math.Floor(position);
                var fraction = position - left;
                // A fractional sample at the packet boundary needs the first sample
                // of the next packet; repeating the last sample introduces clicks.
                if (left == packet.Length - 1 && fraction > 0) break;
                var first = left < 0 ? previousSample : packet[left];
                var second = fraction == 0 ? first : packet[left + 1];
                var value = (float)(first + (second - first) * fraction);
                samples.Add(float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0); position += step;
            }
            previousSample = packet[^1];
            position -= packet.Length;
        }
    }
    private static double[] CreateFilter(int rate)
    {
        var coefficients = new double[FilterTaps];
        var cutoff = 7200d / rate; double sum = 0;
        for (var i = 0; i < FilterTaps; i++)
        {
            var x = i - (FilterTaps - 1) / 2;
            var sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
            coefficients[i] = sinc * (0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FilterTaps - 1)));
            sum += coefficients[i];
        }
        for (var i = 0; i < FilterTaps; i++) coefficients[i] /= sum;
        return coefficients;
    }
    public float[] Take(bool trimSilence = false) => Take(out _, trimSilence);
    public float[] Take(out float peak, bool trimSilence = false)
    {
        lock (gate)
        {
            var audio = CollectionsMarshal.AsSpan(samples);
            peak = 0;
            foreach (var sample in audio) peak = Math.Max(peak, Math.Abs(sample));
            var start = 0;
            var end = audio.Length;
            if (trimSilence)
            {
                // Inspect 20 ms RMS windows; retain 200 ms around speech for word boundaries.
                const int frame = DictationAudio.SampleRate / 50;
                const int padding = DictationAudio.SampleRate / 5;
                var first = -1;
                var last = 0;
                for (var offset = 0; offset < audio.Length; offset += frame)
                {
                    var length = Math.Min(frame, audio.Length - offset);
                    double energy = 0;
                    foreach (var sample in audio.Slice(offset, length)) energy += (double)sample * sample;
                    if (energy / length < SilenceThreshold * SilenceThreshold) continue;
                    if (first < 0) first = offset;
                    last = offset + length;
                }
                start = first < 0 ? 0 : Math.Max(0, first - padding);
                end = first < 0 ? 0 : Math.Min(audio.Length, last + padding);
            }
            var result = audio[start..end].ToArray();
            Clear();
            return result;
        }
    }
    public void Clear()
    {
        lock (gate) { CollectionsMarshal.AsSpan(samples).Clear(); samples.Clear(); position = 0; previousSample = 0; Array.Clear(filterHistory); filterIndex = 0; }
    }
    public static bool ContainsSpeech(float[] audio)
    {
        if (audio.Length < DictationAudio.SampleRate / 4) return false;
        // Match the trimming detector: long pauses between quiet utterances must not
        // dilute their energy below the threshold for the entire recording.
        const int frame = DictationAudio.SampleRate / 50;
        for (var offset = 0; offset < audio.Length; offset += frame)
        {
            var length = Math.Min(frame, audio.Length - offset);
            double energy = 0;
            for (var i = offset; i < offset + length; i++) energy += (double)audio[i] * audio[i];
            if (energy / length >= SilenceThreshold * SilenceThreshold) return true;
        }
        return false;
    }
}

public enum DictationKeyAction { None, Start, Stop }
public sealed class DictationShortcutState
{
    public static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(300);
    public bool Held { get; private set; }
    private bool startsRecording;
    private long pressedAt;

    public DictationKeyAction Press(bool recording)
    {
        if (Held) return DictationKeyAction.None;
        Held = true;
        startsRecording = !recording;
        pressedAt = Stopwatch.GetTimestamp();
        return recording ? DictationKeyAction.Stop : DictationKeyAction.Start;
    }

    public DictationKeyAction Release() => Release(pressedAt == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(pressedAt));

    public DictationKeyAction Release(TimeSpan heldDuration)
    {
        var action = Held && startsRecording && heldDuration >= HoldThreshold
            ? DictationKeyAction.Stop
            : DictationKeyAction.None;
        Reset();
        return action;
    }

    public void Reset() { Held = false; startsRecording = false; pressedAt = 0; }
}
