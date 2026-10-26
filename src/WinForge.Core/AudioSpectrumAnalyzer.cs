using System.Numerics;

namespace WinForge.Core;

/// <summary>Streaming, bounded-memory spectrum analysis independent of capture packets and UI timing.</summary>
public sealed class AudioSpectrumAnalyzer
{
    public const int BandCount = 24;
    private const int FrameSize = 1024;
    private const float Decay = 0.78f;
    private const float Attack = 0.58f;
    private const double MinimumFrequency = 43;
    private const double MaximumFrequency = 18000;
    private static readonly double[] Window = Enumerable.Range(0, FrameSize)
        .Select(index => 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (FrameSize - 1))).ToArray();
    private readonly object _gate = new();
    private readonly float[] _pending = new float[FrameSize];
    private readonly float[] _levels = new float[BandCount];
    private readonly Complex[] _fft = new Complex[FrameSize];
    private int _pendingCount;
    private int _sampleRate;

    public void Push(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        lock (_gate)
        {
            if (_sampleRate != sampleRate)
            {
                _sampleRate = sampleRate;
                _pendingCount = 0;
                Array.Clear(_levels);
            }
            foreach (var sample in samples)
            {
                _pending[_pendingCount++] = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
                if (_pendingCount < FrameSize) continue;
                _pendingCount = 0;
                AnalyzeFrame();
            }
        }
    }

    public float[] GetLevels()
    {
        lock (_gate)
        {
            var result = new float[BandCount];
            for (var index = 0; index < BandCount; index++) result[index] = _levels[index] *= Decay;
            return result;
        }
    }

    private void AnalyzeFrame()
    {
        for (var index = 0; index < FrameSize; index++) _fft[index] = new Complex(_pending[index] * Window[index], 0);
        Transform(_fft);
        var maximum = Math.Min(MaximumFrequency, _sampleRate / 2d);
        var binsPerHz = (double)FrameSize / _sampleRate;
        for (var band = 0; band < BandCount; band++)
        {
            var start = MinimumFrequency * Math.Pow(maximum / MinimumFrequency, (double)band / BandCount);
            var end = MinimumFrequency * Math.Pow(maximum / MinimumFrequency, (double)(band + 1) / BandCount);
            var firstBin = Math.Clamp((int)Math.Floor(start * binsPerHz), 1, FrameSize / 2 - 1);
            var lastBin = Math.Clamp((int)Math.Ceiling(end * binsPerHz), firstBin + 1, FrameSize / 2);
            double peak = 0;
            for (var bin = firstBin; bin < lastBin; bin++) peak = Math.Max(peak, _fft[bin].Magnitude * 2 / FrameSize);
            var decibels = peak <= 0.000001 ? -90 : 20 * Math.Log10(peak);
            var target = (float)Math.Clamp((decibels + 60) / 48, 0, 1);
            var factor = target > _levels[band] ? Attack : 1 - Decay;
            _levels[band] += (target - _levels[band]) * factor;
        }
    }

    internal static void Transform(Complex[] values)
    {
        var length = values.Length;
        if (length < 2 || (length & (length - 1)) != 0) throw new ArgumentException("FFT uzunluğu ikinin kuvveti olmalıdır.", nameof(values));
        for (int index = 1, reversed = 0; index < length; index++)
        {
            var bit = length >> 1;
            for (; (reversed & bit) != 0; bit >>= 1) reversed ^= bit;
            reversed ^= bit;
            if (index < reversed) (values[index], values[reversed]) = (values[reversed], values[index]);
        }
        for (var size = 2; size <= length; size <<= 1)
        {
            var step = new Complex(Math.Cos(-2 * Math.PI / size), Math.Sin(-2 * Math.PI / size));
            for (var offset = 0; offset < length; offset += size)
            {
                var rotation = Complex.One;
                var half = size / 2;
                for (var index = 0; index < half; index++)
                {
                    var even = values[offset + index];
                    var odd = rotation * values[offset + index + half];
                    values[offset + index] = even + odd;
                    values[offset + index + half] = even - odd;
                    rotation *= step;
                }
            }
        }
    }
}
