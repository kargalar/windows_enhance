using System.Runtime.InteropServices;
using WinForge.Dictation;

namespace WinForge.Modules.Capture;

public sealed class SpeechBuffer
{
    public const double SilenceThreshold = 0.002;
    private readonly object gate = new();
    private readonly List<float> samples = [];
    private double position;
    public bool Full { get { lock (gate) return samples.Count >= DictationProtocol.SampleRate * DictationProtocol.MaximumSeconds; } }
    public void Append(ReadOnlySpan<float> packet, int sampleRate)
    {
        if (sampleRate < 8000 || sampleRate > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        lock (gate)
        {
            if (packet.IsEmpty || Full) return;
            var step = (double)sampleRate / DictationProtocol.SampleRate;
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
    public static bool ContainsSpeech(float[] audio) => audio.Length >= DictationProtocol.SampleRate / 4 &&
        Math.Sqrt(audio.Sum(sample => (double)sample * sample) / audio.Length) >= SilenceThreshold;
}

public enum DictationKeyAction { None, Start, Stop }
public sealed class DictationShortcutState
{
    public bool Held { get; private set; }
    public DictationKeyAction Press(bool holdMode, bool recording)
    {
        if (!holdMode) return recording ? DictationKeyAction.Stop : DictationKeyAction.Start;
        if (Held) return DictationKeyAction.None;
        Held = true; return DictationKeyAction.Start;
    }
    public DictationKeyAction Release()
    { var action = Held ? DictationKeyAction.Stop : DictationKeyAction.None; Held = false; return action; }
    public void Reset() => Held = false;
}
