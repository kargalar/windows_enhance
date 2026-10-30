using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace WinForge.Core;

/// <summary>Captures the default render mix. All audio COM objects belong to one worker thread.</summary>
public sealed class WasapiLoopbackCapture : IDisposable
{
    private readonly bool _microphone;
    private readonly string? _deviceId;
    public WasapiLoopbackCapture() : this(false, null) { }
    public WasapiLoopbackCapture(bool microphone, string? deviceId = null)
    { _microphone = microphone; _deviceId = deviceId; }
    public delegate void SamplesHandler(ReadOnlySpan<float> samples, int sampleRate);
    public event SamplesHandler? SamplesAvailable;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private bool _disposed;
    private int _workerThreadId;
    private Exception? _lastError;
    public Exception? LastError => Volatile.Read(ref _lastError);
    private const int PollMilliseconds = 10;
    private const int EndpointCheckMilliseconds = 1000;
    private const uint LoopbackFlag = 0x00020000;
    private const uint SilentFlag = 2;
    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid CaptureClientId = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    public void Start()
    {
        TaskCompletionSource ready;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is not null) return;
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _worker = Task.Run(() => Capture(cancellation.Token, ready));
        }
        try { ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch { Dispose(); throw; }
    }

    private void Capture(CancellationToken token, TaskCompletionSource ready)
    {
        _workerThreadId = Environment.CurrentManagedThreadId;
        while (!token.IsCancellationRequested)
        {
            try { CaptureEndpoint(token, ready); }
            catch (Exception exception)
            {
                Volatile.Write(ref _lastError, exception);
                if (!ready.Task.IsCompleted) { ready.TrySetException(exception); return; }
                if (token.WaitHandle.WaitOne(EndpointCheckMilliseconds)) return;
            }
        }
        ready.TrySetCanceled(token);
    }

    private void CaptureEndpoint(CancellationToken token, TaskCompletionSource ready)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr formatPointer = IntPtr.Zero;
        var started = false;
        try
        {
            enumerator = (IMMDeviceEnumerator)CoreAudioDeviceFactory.CreateEnumerator();
            if (string.IsNullOrWhiteSpace(_deviceId)) Check(enumerator.GetDefaultAudioEndpoint(_microphone ? 1 : 0, 0, out device));
            else Check(enumerator.GetDevice(_deviceId, out device));
            Check(device.GetId(out var endpointId));
            var clientId = AudioClientId;
            Check(device.Activate(ref clientId, 1, IntPtr.Zero, out var audioObject));
            client = (IAudioClient)audioObject;
            Check(client.GetMixFormat(out formatPointer));
            var format = SampleFormat.Read(formatPointer);
            Check(client.Initialize(0, _microphone ? 0 : LoopbackFlag, 1_000_000, 0, formatPointer, IntPtr.Zero));
            var captureId = CaptureClientId;
            Check(client.GetService(ref captureId, out var captureObject));
            capture = (IAudioCaptureClient)captureObject;
            Check(client.Start());
            started = true;
            Volatile.Write(ref _lastError, null);
            ready.TrySetResult();
            var lastEndpointCheck = Environment.TickCount64;
            var bytes = Array.Empty<byte>();
            var samples = Array.Empty<float>();
            while (!token.IsCancellationRequested)
            {
                Check(capture.GetNextPacketSize(out var packetSize));
                while (packetSize > 0 && !token.IsCancellationRequested)
                {
                    Check(capture.GetBuffer(out var pointer, out var frames, out var flags, out _, out _));
                    if (frames == 0) break;
                    try
                    {
                        var count = checked((int)frames);
                        var byteCount = checked(count * format.BlockAlign);
                        if (count > format.SampleRate * 2) throw new InvalidOperationException("Ses paketi sınırı aşıldı.");
                        if (samples.Length < count) samples = new float[count];
                        var silent = (flags & SilentFlag) != 0;
                        if (!silent)
                        {
                            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Ses paketi boş.");
                            if (bytes.Length < byteCount) bytes = new byte[byteCount];
                            Marshal.Copy(pointer, bytes, 0, byteCount);
                        }
                        DecodeMono(silent ? [] : bytes.AsSpan(0, byteCount), samples.AsSpan(0, count), format, silent);
                        SamplesAvailable?.Invoke(samples.AsSpan(0, count), format.SampleRate);
                    }
                    finally { Check(capture.ReleaseBuffer(frames)); }
                    Check(capture.GetNextPacketSize(out packetSize));
                }
                if (Environment.TickCount64 - lastEndpointCheck >= EndpointCheckMilliseconds)
                {
                    IMMDevice? current = null;
                    try
                    {
                        if (string.IsNullOrWhiteSpace(_deviceId)) Check(enumerator.GetDefaultAudioEndpoint(_microphone ? 1 : 0, 0, out current));
                        else Check(enumerator.GetDevice(_deviceId, out current));
                        Check(current.GetId(out var currentId));
                        if (!string.Equals(currentId, endpointId, StringComparison.Ordinal)) return;
                    }
                    finally { Release(current); }
                    lastEndpointCheck = Environment.TickCount64;
                }
                token.WaitHandle.WaitOne(PollMilliseconds);
            }
        }
        finally
        {
            if (started) client?.Stop();
            if (formatPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(formatPointer);
            Release(capture); Release(client); Release(device); Release(enumerator);
        }
    }

    internal readonly record struct SampleFormat(int SampleRate, int Channels, int Bits, int BlockAlign, bool FloatingPoint)
    {
        internal static SampleFormat Read(IntPtr pointer)
        {
            var wave = Marshal.PtrToStructure<WaveFormat>(pointer);
            var floating = wave.Tag == 3;
            if (wave.Tag == 0xfffe && wave.ExtraSize >= 22)
            {
                var subtype = Marshal.PtrToStructure<Guid>(IntPtr.Add(pointer, 24));
                floating = subtype == new Guid("00000003-0000-0010-8000-00AA00389B71");
                if (!floating && subtype != new Guid("00000001-0000-0010-8000-00AA00389B71"))
                    throw new NotSupportedException("Ses alt biçimi desteklenmiyor.");
            }
            else if (wave.Tag is not (1 or 3)) throw new NotSupportedException("Ses biçimi desteklenmiyor.");
            if (wave.Channels is < 1 or > 32 || wave.SampleRate is < 8000 or > 384000 ||
                (floating ? wave.Bits is not (32 or 64) : wave.Bits is not (8 or 16 or 24 or 32)) ||
                wave.BlockAlign != wave.Channels * (wave.Bits / 8))
                throw new NotSupportedException("Ses biçimi ölçüleri desteklenmiyor.");
            return new((int)wave.SampleRate, wave.Channels, wave.Bits, wave.BlockAlign, floating);
        }
    }

    internal static void DecodeMono(ReadOnlySpan<byte> input, Span<float> output, SampleFormat format, bool silent)
    {
        if (silent) { output.Clear(); return; }
        if (input.Length < checked(output.Length * format.BlockAlign)) throw new ArgumentException("Eksik ses paketi.", nameof(input));
        var sampleBytes = format.Bits / 8;
        for (var frame = 0; frame < output.Length; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var data = input.Slice(frame * format.BlockAlign + channel * sampleBytes, sampleBytes);
                var value = format.FloatingPoint
                    ? format.Bits == 32 ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data))
                        : BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data))
                    : format.Bits switch
                    {
                        8 => (data[0] - 128) / 128d,
                        16 => BinaryPrimitives.ReadInt16LittleEndian(data) / 32768d,
                        24 => ((data[0] | data[1] << 8 | data[2] << 16) << 8 >> 8) / 8388608d,
                        32 => BinaryPrimitives.ReadInt32LittleEndian(data) / 2147483648d,
                        _ => throw new NotSupportedException()
                    };
                if (double.IsFinite(value)) sum += value;
            }
            output[frame] = (float)Math.Clamp(sum / format.Channels, -1, 1);
        }
    }

    public void Dispose()
    {
        Task? worker;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_worker is { IsCompleted: false }) _cancellation?.Cancel();
            worker = _worker;
        }
        // The worker releases its COM objects on its owning thread, including on device removal.
        if (worker is null || (Environment.CurrentManagedThreadId != _workerThreadId && worker.Wait(TimeSpan.FromSeconds(5)))) _cancellation?.Dispose();
        else _ = worker.ContinueWith(_ => _cancellation?.Dispose(), TaskScheduler.Default);
    }

    private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint SampleRate, AverageBytesPerSecond;
        public ushort BlockAlign, Bits, ExtraSize;
    }


    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong counter);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
