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
    public bool Full { get { lock (gate) return samples.Count >= DictationAudio.SampleRate * DictationAudio.MaximumSeconds; } }
    public void Append(ReadOnlySpan<float> packet, int sampleRate)
    {
        if (sampleRate < 8000 || sampleRate > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        lock (gate)
        {
            var limit = DictationAudio.SampleRate * DictationAudio.MaximumSeconds;
            if (packet.IsEmpty || samples.Count >= limit) return;
            var step = (double)sampleRate / DictationAudio.SampleRate;
            while (position < packet.Length && samples.Count < limit)
            {
                var left = (int)position; var right = Math.Min(left + 1, packet.Length - 1);
                var value = (float)(packet[left] + (packet[right] - packet[left]) * (position - left));
                samples.Add(float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0); position += step;
            }
            position -= packet.Length;
        }
    }
    public float[] Take(bool trimSilence = false)
    {
        lock (gate)
        {
            var audio = CollectionsMarshal.AsSpan(samples);
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
        lock (gate) { CollectionsMarshal.AsSpan(samples).Clear(); samples.Clear(); position = 0; }
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
