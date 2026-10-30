using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;
using WinForge.FileTools.Engine;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal static class SharedMediaTools
{
    public static Toolchain Create() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForge", "Nashi", "Tools", "v1"), Environment.GetEnvironmentVariable("PATH"), ["ffmpeg", "ffprobe"]);
    public static string Ffmpeg => Create().Executable("ffmpeg");
}

internal sealed class ScreenRecording : IAsyncDisposable
{
    private const int FramesPerSecond = 15;
    private readonly string directory;
    private readonly CaptureArea area;
    private readonly string encoder;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Stopwatch clock = new();
    private readonly List<AudioTrack> tracks = [];
    private readonly object gate = new();
    private Process? process;
    private Task? video;
    private Task<string>? errors;
    private bool paused, disposed;
    public bool Paused { get { lock (gate) return paused; } }
    public string? Error { get; private set; }
    public ScreenRecording(string parent, CaptureArea area, bool microphone, bool systemAudio, bool cursor)
    {
        this.area = area with { Width = area.Width / 2 * 2, Height = area.Height / 2 * 2 };
        if (!this.area.IsValid) throw new ArgumentException("Kayıt alanı en az 2×2 piksel olmalı.");
        encoder = SharedMediaTools.Ffmpeg;
        directory = Path.Combine(parent, "recording-jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            process = Start(encoder, ["-y", "-v", "error", "-f", "rawvideo", "-pixel_format", "bgr0", "-video_size", $"{this.area.Width}x{this.area.Height}", "-framerate", FramesPerSecond.ToString(CultureInfo.InvariantCulture), "-i", "pipe:0", "-an", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", Path.Combine(directory, "video.mp4")], true);
            errors = process.StandardError.ReadToEndAsync();
            if (microphone) tracks.Add(new AudioTrack(directory, true, () => clock.IsRunning && !Paused, () => clock.Elapsed));
            if (systemAudio) tracks.Add(new AudioTrack(directory, false, () => clock.IsRunning && !Paused, () => clock.Elapsed));
            clock.Start(); video = Task.Run(() => VideoAsync(cursor, lifetime.Token));
        }
        catch { process?.Kill(true); foreach (var track in tracks) track.Dispose(); throw; }
    }
    public void TogglePause()
    {
        lock (gate) { paused = !paused; if (paused) clock.Stop(); else clock.Start(); }
    }
    public void RequestCancel()
    {
        lifetime.Cancel();
        if (process is { HasExited: false }) process.Kill(true);
        foreach (var track in tracks) track.Dispose();
    }
    private async Task VideoAsync(bool cursor, CancellationToken token)
    {
        var written = 0L;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Paused) { await Task.Delay(30, token); continue; }
                if (written >= (long)(clock.Elapsed.TotalSeconds * FramesPerSecond) + 1) { await Task.Delay(8, token); continue; }
                if (!CaptureServices.HasSpace(directory)) throw new IOException("Disk alanı azaldı; kayıt durduruldu.");
                var (pixels, width, height) = DesktopCapture.Capture(area.X, area.Y, area.Width, area.Height);
                if (cursor) DrawCursor(pixels, width, height);
                var expected = (long)(clock.Elapsed.TotalSeconds * FramesPerSecond) + 1;
                while (written < expected && !token.IsCancellationRequested)
                { await process!.StandardInput.BaseStream.WriteAsync(pixels, token); written++; }
                foreach (var track in tracks) if (track.Error is { } error) throw new IOException("Ses aygıtı kaydı kesildi.", error);
                await Task.Delay(10, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Error = error.Message; clock.Stop(); foreach (var track in tracks) track.Stop(); }
        finally { process?.StandardInput.Close(); }
    }
    private void DrawCursor(byte[] pixels, int width, int height)
    {
        Native.GetCursorPos(out var point); var px = point.X - area.X; var py = point.Y - area.Y;
        for (var y = -6; y <= 6; y++) for (var x = -6; x <= 6; x++)
        {
            if (x * x + y * y > 36 || px + x < 0 || px + x >= width || py + y < 0 || py + y >= height) continue;
            var index = ((py + y) * width + px + x) * 4; pixels[index] = 0; pixels[index + 1] = 220; pixels[index + 2] = 255;
        }
    }
    public async Task<string> FinishAsync(bool gif, CancellationToken token)
    {
        lifetime.Cancel(); clock.Stop(); if (video is not null) await video;
        foreach (var track in tracks) { track.Complete(clock.Elapsed); track.Dispose(); }
        if (process is null) throw new InvalidOperationException("Kayıt başlamadı.");
        await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
        var stderr = errors is null ? "" : await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException("Video kodlanamadı: " + stderr);
        var output = Path.Combine(directory, gif ? "capture.gif" : "capture.mp4");
        var args = new List<string> { "-y", "-v", "error", "-i", Path.Combine(directory, "video.mp4") };
        var audioTracks = tracks.Where(t => t.SampleRate > 0 && new FileInfo(t.Path).Length > 0).ToArray();
        if (!gif)
        {
            foreach (var track in audioTracks) args.AddRange(["-f", "f32le", "-ar", track.SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "1", "-i", track.Path]);
            if (audioTracks.Length > 1) args.AddRange(["-filter_complex", "[1:a][2:a]amix=inputs=2:duration=longest[a]", "-map", "0:v", "-map", "[a]"]);
            else if (audioTracks.Length == 1) args.AddRange(["-map", "0:v", "-map", "1:a"]);
            args.AddRange(["-c:v", "copy", "-c:a", "aac", "-t", clock.Elapsed.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), "-movflags", "+faststart"]);
        }
        else args.AddRange(["-filter_complex", "[0:v]fps=12,split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0"]);
        args.Add(output); await ProcessRunner.RunAsync(encoder, args, token); return output;
    }
    private static Process Start(string file, IEnumerable<string> arguments, bool input)
    {
        var info = new ProcessStartInfo(file) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = input, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException("Video kodlayıcısı başlatılamadı.");
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; lifetime.Cancel();
        if (video is not null) await video;
        foreach (var track in tracks) track.Dispose();
        if (process is not null) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); process.Dispose(); }
        lifetime.Dispose();
        Directory.Delete(directory, true);
    }
    private sealed class AudioTrack : IDisposable
    {
        private readonly WasapiLoopbackCapture capture;
        private readonly FileStream output;
        private readonly Func<bool> enabled;
        private readonly Func<TimeSpan> position;
        public string Path { get; }
        public int SampleRate { get; private set; }
        public Exception? Error => capture.LastError;
        public AudioTrack(string directory, bool microphone, Func<bool> enabled, Func<TimeSpan> position)
        {
            this.enabled = enabled; this.position = position; Path = System.IO.Path.Combine(directory, microphone ? "microphone.f32" : "system.f32");
            output = File.Create(Path); capture = new(microphone); capture.SamplesAvailable += Samples;
            try { capture.Start(); } catch { capture.Dispose(); output.Dispose(); throw; }
        }
        private void Samples(ReadOnlySpan<float> samples, int rate)
        {
            if (!enabled()) return;
            if (SampleRate != 0 && SampleRate != rate) throw new InvalidOperationException("Ses örnekleme hızı değişti.");
            SampleRate = rate;
            var startSample = Math.Max(0L, (long)(position().TotalSeconds * rate) - samples.Length);
            var skip = (int)Math.Min(samples.Length, Math.Max(0, output.Position / sizeof(float) - startSample));
            if (startSample * sizeof(float) > output.Position) output.Position = startSample * sizeof(float);
            output.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples[skip..]));
        }
        public void Complete(TimeSpan duration)
        {
            capture.Dispose();
            if (SampleRate == 0) SampleRate = 48000;
            output.SetLength((long)(duration.TotalSeconds * SampleRate) * sizeof(float));
        }
        public void Stop() => capture.Dispose();
        public void Dispose() { capture.Dispose(); output.Dispose(); }
    }
}
