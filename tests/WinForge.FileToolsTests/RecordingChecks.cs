using System.Security.Cryptography;
using WinForge.ClipboardLine;

internal static class RecordingChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge-RecordingChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            check(RecordingTools.FormatDuration(TimeSpan.FromSeconds(3661)) == "1:01:01", "Long recordings use an hours-aware duration label.");
            var library = new ClipboardLibrary(Path.Combine(root, "library"));
            foreach (var name in new[] { "synthetic-rgb-silent.mp4", "synthetic-rgb-tone.mp4", "synthetic-rgb-tone.mov" })
            {
                var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recordings", name);
                var originalHash = SHA256.HashData(File.ReadAllBytes(source));
                var info = await RecordingTools.InspectAsync(source);
                check(Math.Abs(info.Duration.TotalSeconds - 6) < .2 && info.Width == 160 && info.Height == 120, name + " source duration and dimensions are decoded by Windows.");
                check(info.AudioTracks == (name.Contains("silent", StringComparison.Ordinal) ? 0 : 1), name + " source audio track count is correct.");
                var thumbnail = await RecordingTools.PreviewAsync(source);
                check(thumbnail.Image.IsFrozen && thumbnail.Image.PixelWidth > 0 && thumbnail.Image.PixelHeight > 0 && RecordingTools.FormatDuration(thumbnail.Info.Duration) == "00:06", name + " yields a reusable decoded preview frame.");
                var item = library.AddMedia(source); var original = library.MediaPath(item, original: true);
                var job = Path.Combine(root, Path.GetFileNameWithoutExtension(name) + Path.GetExtension(name).Replace('.', '_')); Directory.CreateDirectory(job);
                await RecordingTools.TrimAsync(source, job, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), null, CancellationToken.None);
                var output = Path.Combine(job, "trimmed.mp4"); var trimmed = await RecordingTools.InspectAsync(output);
                check(Math.Abs(trimmed.Duration.TotalSeconds - 2) < .15 && trimmed.Width == info.Width && trimmed.Height == info.Height && trimmed.AudioTracks == info.AudioTracks, name + " trimmed output preserves dimensions and audio with the requested duration.");
                library.SaveRecordingEdit(item.Id, item.Content, output);
                var saved = library.Items.Single(value => value.Id == item.Id);
                check(saved.Content.EndsWith(".mp4", StringComparison.Ordinal) && saved.OriginalFile == item.OriginalFile && saved.Content != item.Content, name + " trim updates only the managed copy.");
                check(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(originalHash) && SHA256.HashData(File.ReadAllBytes(original)).SequenceEqual(originalHash), name + " original bytes remain unchanged.");
                try { library.SaveRecordingEdit(item.Id, item.Content, output); check(false, "Stale trim rejected."); } catch (InvalidOperationException) { check(true, "Stale trim rejected."); }
            }
        }
        finally
        {
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar); var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) == parent && Path.GetFileName(resolved).StartsWith("WinForge-RecordingChecks-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }
}
