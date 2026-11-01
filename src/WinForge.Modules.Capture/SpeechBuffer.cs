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
            if (packet.IsEmpty || Full) return;
            var step = (double)sampleRate / DictationAudio.SampleRate;
            while (position < packet.Length && !Full)
            {
                var left = (int)position; var right = Math.Min(left + 1, packet.Length - 1);
                var value = (float)(packet[left] + (packet[right] - packet[left]) * (position - left));
                samples.Add(float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0); position += step;
            }
            position -= packet.Length;
        }
    }
    public float[] Take()
    {
        lock (gate) { var result = samples.ToArray(); Clear(); return result; }
    }
    public void Clear()
    {
        lock (gate) { CollectionsMarshal.AsSpan(samples).Clear(); samples.Clear(); position = 0; }
    }
    public static bool ContainsSpeech(float[] audio) => audio.Length >= DictationAudio.SampleRate / 4 &&
        Math.Sqrt(audio.Sum(sample => (double)sample * sample) / audio.Length) >= SilenceThreshold;
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
