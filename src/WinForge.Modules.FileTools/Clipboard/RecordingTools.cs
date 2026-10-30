using Windows.Media.Editing;
using Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using System.Globalization;
using System.Runtime.InteropServices;
using WinForge.FileTools.Engine;

namespace WinForge.ClipboardLine;

internal sealed record RecordingInfo(TimeSpan Duration, uint Width, uint Height, int AudioTracks);
internal sealed record RecordingPreview(RecordingInfo Info, BitmapSource Image);

internal static class RecordingTools
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromMilliseconds(150);

    internal static async Task<RecordingInfo> InspectAsync(string file, CancellationToken token = default)
    {
        var clip = await OpenAsync(file, token);
        return Describe(clip);
    }

    internal static async Task<RecordingPreview> PreviewAsync(string file, CancellationToken token = default)
    {
        var clip = await OpenAsync(file, token);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        var position = TimeSpan.FromTicks(Math.Min(clip.OriginalDuration.Ticks / 2, TimeSpan.FromSeconds(1).Ticks));
        using var thumbnail = await composition.GetThumbnailAsync(position, 320, 180, VideoFramePrecision.NearestFrame).AsTask(token);
        using var input = thumbnail.AsStreamForRead();
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
        return new RecordingPreview(Describe(clip), bitmap);
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        var seconds = Math.Max(0L, (long)Math.Floor(duration.TotalSeconds));
        return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
    }

    internal static async Task TrimAsync(string source, string outputDirectory, TimeSpan start, TimeSpan end,
        IProgress<double>? progress, CancellationToken token)
    {
        var clip = await OpenAsync(source, token);
        var info = Describe(clip);
        if (start < TimeSpan.Zero || end > info.Duration || end - start < MinimumDuration)
            throw new ArgumentException("Kayıt içinde en az 100 ms uzunluğunda bir aralık seçin.");
        clip.TrimTimeFromStart = start; clip.TrimTimeFromEnd = info.Duration - end;
        var composition = new MediaComposition(); composition.Clips.Add(clip);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(outputDirectory)).AsTask(token);
        var output = await folder.CreateFileAsync("trimmed.mp4", CreationCollisionOption.FailIfExists).AsTask(token);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Audio = info.AudioTracks == 0 ? null : AudioEncodingProperties.CreateAac(44_100, 2, 128_000);
        var original = clip.GetVideoEncodingProperties();
        profile.Video.Width = original.Width; profile.Video.Height = original.Height;
        profile.Video.FrameRate.Numerator = original.FrameRate.Numerator; profile.Video.FrameRate.Denominator = original.FrameRate.Denominator;
        profile.Video.PixelAspectRatio.Numerator = 1; profile.Video.PixelAspectRatio.Denominator = 1;
        if (original.Bitrate > 0) profile.Video.Bitrate = original.Bitrate;
        try
        {
            try
            {
                var result = await composition.RenderToFileAsync(output, MediaTrimmingPreference.Precise, profile).AsTask(token, progress);
                if (result != TranscodeFailureReason.None) throw new InvalidOperationException("Windows kaydı kodlayamadı: " + result);
            }
            catch (Exception error) when (error is COMException or InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                await TrimWithAvailableEncoderAsync(source, output.Path, start, end, token);
            }
            var rendered = await InspectAsync(output.Path, token);
            if ((rendered.Duration - (end - start)).Duration() > DurationTolerance || rendered.Width != info.Width || rendered.Height != info.Height || rendered.AudioTracks != info.AudioTracks)
                throw new InvalidDataException("Kırpılan kaydın süresi, boyutu veya ses parçası doğrulanamadı.");
        }
        catch { if (File.Exists(output.Path)) File.Delete(output.Path); throw; }
    }

    private static async Task TrimWithAvailableEncoderAsync(string source, string output, TimeSpan start, TimeSpan end, CancellationToken token)
    {
        var tools = new Toolchain(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForge", "Nashi", "Tools", "v1"), Environment.GetEnvironmentVariable("PATH"));
        string encoder;
        try { encoder = tools.Executable("ffmpeg"); }
        catch (FileNotFoundException error) { throw new InvalidOperationException("Windows video kodlayıcısı bu kaydı işleyemedi. Nashi araç kurulumuyla medya araçlarını hazırlayın.", error); }
        if (File.Exists(output)) File.Delete(output);
        static string Number(TimeSpan value) => value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        await ProcessRunner.RunAsync(encoder, ["-v", "error", "-i", Path.GetFullPath(source), "-ss", Number(start), "-t", Number(end - start), "-map", "0:v:0", "-map", "0:a:0?", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart", output], token);
    }

    private static async Task<MediaClip> OpenAsync(string path, CancellationToken token)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(token);
        return await MediaClip.CreateFromFileAsync(file).AsTask(token);
    }

    private static RecordingInfo Describe(MediaClip clip)
    {
        var properties = clip.GetVideoEncodingProperties();
        if (clip.OriginalDuration <= TimeSpan.Zero || properties.Width == 0 || properties.Height == 0)
            throw new InvalidDataException("Bu kayıt oynatılabilir bir video parçası içermiyor.");
        return new RecordingInfo(clip.OriginalDuration, properties.Width, properties.Height, clip.EmbeddedAudioTracks.Count);
    }
}
