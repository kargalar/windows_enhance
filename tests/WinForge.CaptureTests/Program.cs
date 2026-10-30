using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinForge.Core;
using WinForge.History;
using WinForge.Modules.Capture;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--text-fixture") return TextOutputChecks.RunFixture();
            if (args.Length == 1 && args[0] == "--text-output") { TextOutputChecks.RunAsync(Check).GetAwaiter().GetResult(); return 0; }
            if (args.Length == 3 && args[0] == "--import") return Import(args[1], args[2]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--models") { CopyModels(args[1]).GetAwaiter().GetResult(); return 0; }
            if (args.Length == 4 && args[0] == "--real-worker") { RealWorker(args[1], args[2], args[3] != "cpu", args[3] == "fallback", args[3] == "translate").GetAwaiter().GetResult(); return 0; }
            if (args.Length == 1 && args[0] == "--recording") { Recording().GetAwaiter().GetResult(); return 0; }
            if (args.Length == 1 && args[0] == "--render") { Render(); return 0; }
            if (args.Length == 2 && args[0] == "--discovery") { Discovery(args[1]); return 0; }
            if (args.Length == 1 && args[0] == "--native-capture") { NativeCapture(); return 0; }
            if (args.Length == 1 && args[0] == "--audit") { Audit(); return 0; }
            if (args.Length == 1 && args[0] == "--scrolling") return Scrolling();
            var root = Path.Combine(Path.GetTempPath(), "WinForge-CaptureTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try { PortableDataChecks.Run(root, Check); Storage(root).GetAwaiter().GetResult(); Usage(); Images(root); Speech(); Lifecycle(root); Protocol().GetAwaiter().GetResult(); }
            finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
            Console.WriteLine($"PASS: {checks} capture/history checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS " + message); }
    private static async Task Storage(string root)
    {
        var store = new HistoryStore(Path.Combine(root, "history")); var now = DateTimeOffset.UtcNow;
        var id = store.Add("recall", now, "Code.exe", "İstanbul geliştirme", "Türkçe ekran geçmişi", ocrState: "pending");
        Check(store.Search(new("recall", "geçmişi")).Single().Id == id, "Unicode FTS search");
        Check(store.Search(new("dictation")).Count == 0, "Module histories isolated");
        Check(store.Search(new("recall", "\" OR *")).Count == 0, "Search input remains literal");
        store.CompleteOcr(id, "OCR sonucu"); Check(store.PendingOcr().Count == 0, "OCR completion removes pending job");
        store.CompleteOcr(id, "", true); store.RetryOcr(); Check(store.PendingOcr().Count == 0, "Jobs without images are not queued");
        store.SetTags("Code.exe", ["Kod", "İş", "Kod"]); Check(store.Search(new("recall", Tag: "Kod")).Count == 1, "Application tags filter and deduplicate");
        Check(store.Search(new("recall", From: now.AddMinutes(1))).Count == 0, "Time filters");
        var source = Path.Combine(root, "legacy"); Directory.CreateDirectory(source);
        var image = Path.Combine(source, "capture.webp"); await File.WriteAllBytesAsync(image, [1, 2, 3, 4]);
        var a = await store.ImportMediaAsync(image, default); var b = await store.ImportMediaAsync(image, default);
        Check(a == b && Directory.GetFiles(store.MediaDirectory).Length == 1, "Content addressed media deduplication");
        store.ReleasePendingMedia(a); store.ReleasePendingMedia(b);
        var database = Path.Combine(source, "recall.db");
        using (var connection = new SqliteConnection("Data Source=" + database + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """
                CREATE TABLE activities(id INTEGER PRIMARY KEY,timestamp TEXT,image_path TEXT,app_name TEXT,window_title TEXT,ocr_text TEXT,interval_at_capture INTEGER);
                CREATE TABLE tags(name TEXT); INSERT INTO tags VALUES('Kod');
                CREATE TABLE app_tags(app_name TEXT,tag_name TEXT); INSERT INTO app_tags VALUES('legacy.exe','Kod');
                INSERT INTO activities VALUES(1,'2026-01-01 12:00:00','capture.webp','legacy.exe','Başlık','Geçmiş OCR',60);
                INSERT INTO activities VALUES(2,'2026-01-01 12:01:00','missing.webp','legacy.exe','Eksik görsel',NULL,60);
                """; command.ExecuteNonQuery();
        }
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(database));
        var importer = new LegacyImporter(store); var input = new ImportSource(database, "recall", source);
        var preview = await importer.PreviewAsync(input, default);
        Check(preview.Records == 2 && preview.MissingFiles == 1 && preview.Bytes == 4, "Import preview counts records, bytes and missing media");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); try { await importer.ImportAsync(input, null, cancelled.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { checks++; }
        }
        var result = await importer.ImportAsync(input, null, default);
        Check(result.Imported == 2 && result.MissingFiles == 1, "Legacy schema and missing images preserve records");
        Check((await importer.ImportAsync(input, null, default)).Imported == 0, "Resumed import does not duplicate records");
        var after = SHA256.HashData(await File.ReadAllBytesAsync(database));
        Check(hash.AsSpan().SequenceEqual(after), "Source database unchanged");
        Check(store.Search(new("recall", App: "legacy.exe", Tag: "Kod")).Count == 2, "Imported application tags");
        Check(store.Usage().Single().Estimated && store.Usage().Single().Seconds == 120, "Legacy duration remains explicitly estimated");
        var dictationDb = Path.Combine(source, "history.db");
        using (var connection = new SqliteConnection("Data Source=" + dictationDb + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE transcripts(id INTEGER PRIMARY KEY,created_at TEXT,model INTEGER,text TEXT); INSERT INTO transcripts VALUES(1,'2026-01-01T12:00:00+03:00',0,'Merhaba dünya');"; command.ExecuteNonQuery();
        }
        await importer.ImportAsync(new(dictationDb, "dictation"), null, default);
        Check(store.Search(new("dictation", "Merhaba")).Single().Created.UtcDateTime.Hour == 9, "Whisper import preserves text and timezone");
        store.Delete("recall", id); Check(store.Search(new("recall")).Count == 2, "Single delete");
        Check(store.CollectUnusedMedia() == 0, "Shared media retained while referenced");
        store.Delete("recall", until: new DateTimeOffset(2026, 1, 1, 12, 0, 30, TimeSpan.Zero)); Check(store.Search(new("recall")).Count == 1, "Date range delete");
        store.Delete("recall"); Check(Directory.GetFiles(store.MediaDirectory).Length == 0, "Unreferenced media collected");
        Check(store.Search(new("dictation")).Count == 1, "Deleting recall does not delete dictation");
        var midnight = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        store.AddUsage("cross-midnight", midnight.AddMinutes(-1), midnight.AddMinutes(1), 120);
        Check(Math.Abs(store.Usage(midnight, midnight.AddDays(1)).Single().Seconds - 60) < 0.01, "Usage intervals are clipped at date boundaries");
        var localMidnight = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 1, 2)));
        store.AddUsage("daily-app", localMidnight.AddMinutes(-1), localMidnight.AddMinutes(1), 120);
        var daily = store.UsageByDay(localMidnight.AddMinutes(-1), localMidnight.AddMinutes(1));
        Check(daily.Count == 2 && daily.All(row => Math.Abs(row.Seconds - 60) < 0.01), "Daily app statistics split activity across local midnight");
        store.SetTags("daily-app", ["Work"]);
        Check(store.TagUsage(localMidnight.AddMinutes(-1), localMidnight.AddMinutes(1)).Single(row => row.Tag == "Work").Seconds == 120,
            "Tag statistics aggregate measured application time");
        store.AddUsage("System Idle", localMidnight, localMidnight.AddMinutes(1), 60);
        Check(Math.Abs(store.RecallSummary(localMidnight, localMidnight.AddMinutes(1)).IdleSeconds - 60) < 0.01,
            "Recall summary reports idle time separately from app use");
        store.AddContextUsage("chrome.exe", "website", "https://example.com", localMidnight.AddMinutes(-1), localMidnight.AddMinutes(1), 120);
        Check(store.ContextUsageByDay(localMidnight.AddMinutes(-1), localMidnight.AddMinutes(1)).Count == 2, "Daily website statistics split activity across local midnight");
        var contextTracker = new ContextUsageTracker(store.AddContextUsage);
        contextTracker.Observe("chrome.exe", "website", "https://example.com/docs", midnight, 0);
        contextTracker.Observe("chrome.exe", "website", "https://example.com/next", midnight.AddSeconds(5), 5000);
        Check(store.ContextUsage(midnight, midnight.AddSeconds(5)).Single().Seconds == 5, "Website time is tracked independently of screenshot interval");
        Check(ActivityContextReader.NormalizeUrl("https://user:pass@example.com/path?q=private#section") == "https://example.com/path", "Visited URL omits credentials, query and fragment");
        var oldId = store.Add("recall", now, media: "old.png", ocrState: "pending"); store.Delete("recall", oldId);
        var newId = store.Add("recall", now, media: "new.png", ocrState: "pending"); store.CompleteOcr(oldId, "stale OCR");
        Check(newId > oldId && store.Search(new("recall")).Single().Text == "", "Deleting a capture cannot redirect in-flight OCR to a reused ID");
        var upgrade = Path.Combine(root, "upgrade"); Directory.CreateDirectory(upgrade);
        using (var old = new SqliteConnection("Data Source=" + Path.Combine(upgrade, "history.db") + ";Pooling=False"))
        {
            old.Open(); using var command = old.CreateCommand(); command.CommandText = """
                CREATE TABLE items(id INTEGER PRIMARY KEY,kind TEXT NOT NULL,created TEXT NOT NULL,app TEXT NOT NULL DEFAULT '',title TEXT NOT NULL DEFAULT '',text TEXT NOT NULL DEFAULT '',media TEXT NOT NULL DEFAULT '',seconds REAL NOT NULL DEFAULT 0,estimated INTEGER NOT NULL DEFAULT 0,source TEXT UNIQUE,ocr_state TEXT NOT NULL DEFAULT 'done',icon TEXT NOT NULL DEFAULT '',metadata TEXT NOT NULL DEFAULT '');
                INSERT INTO items(id,kind,created,text) VALUES(7,'recall','2026-01-01T12:00:00.0000000Z','korunan metin');
                CREATE VIRTUAL TABLE search USING fts5(app,title,text,content=items,content_rowid=id,tokenize='unicode61 remove_diacritics 2');
                INSERT INTO search(rowid,app,title,text) VALUES(7,'','','korunan metin');
                PRAGMA user_version=2;
                """; command.ExecuteNonQuery();
        }
        var upgraded = new HistoryStore(upgrade);
        Check(upgraded.Search(new("recall", "korunan")).Single().Id == 7, "Schema upgrade preserves existing data and full-text index");
        upgraded.Delete("recall"); Check(upgraded.Add("recall", now) > 7, "Schema upgrade preserves the high-water mark after all records are deleted");
    }
    private static void Usage()
    {
        var rows = new List<double>(); var tracker = new UsageTracker((_, _, _, seconds) => rows.Add(seconds)); var now = DateTimeOffset.UtcNow;
        tracker.Observe("A", now, 0); tracker.Observe("B", now.AddSeconds(3), 3000); tracker.Observe(null, now.AddSeconds(7), 7000);
        tracker.Observe(null, now.AddHours(1), 3_600_000); Check(rows.SequenceEqual(new[] { 3d, 4d }), "Usage measures transitions rather than screenshot intervals; idle excluded");
        var contexts = new List<double>(); var context = new ContextUsageTracker((_, _, _, _, _, seconds) => contexts.Add(seconds));
        context.Observe("browser.exe", "website", "https://example.com", now, 0);
        context.Observe("browser.exe", "website", "https://example.org", now.AddSeconds(4), 4000);
        context.Observe(null, null, null, now.AddSeconds(6), 6000);
        Check(contexts.SequenceEqual(new[] { 4d, 2d }), "Website transitions close their measured intervals");
    }
    private static void Images(string root)
    {
        var endpoint = new Uri("https://upload.example/api/");
        Check(ImageUploadService.ParseResult("https://cdn.example/image.png", null, endpoint).AbsoluteUri == "https://cdn.example/image.png", "Upload accepts a plain URL response");
        Check(ImageUploadService.ParseResult("{\"data\":{\"url\":\"https://cdn.example/nested.png\"}}", null, endpoint).AbsoluteUri == "https://cdn.example/nested.png", "Upload accepts nested JSON URL responses");
        Check(ImageUploadService.ParseResult("", new Uri("../image.png", UriKind.Relative), endpoint).AbsoluteUri == "https://upload.example/image.png", "Upload resolves relative Location headers");
        try { ImageUploadService.ParseResult("file:///private/image.png", null, endpoint); throw new Exception("Non-HTTP upload response accepted"); }
        catch (InvalidDataException) { checks++; }
        const int width = 80, height = 80; var pixels = new byte[width * height * 4]; new Random(42).NextBytes(pixels);
        var first = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4); first.Freeze();
        var shifted = new byte[pixels.Length]; Array.Copy(pixels, width * 4 * 30, shifted, 0, width * 4 * 50); new Random(8).NextBytes(shifted.AsSpan(width * 4 * 50));
        var next = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, shifted, width * 4); next.Freeze();
        var match = ScrollStitcher.FindOverlap(first, next); Check(match.Overlap == 50 && match.Confidence > 0.99, "Scrolling overlap detected from image content");
        Check(ScrollStitcher.Join(first, next, match.Overlap).PixelHeight == 110, "Scrolling stitch excludes overlapping pixels");
        var area = new Int32Rect(10, 10, 30, 30); var output = CaptureImageEffects.Apply(first, area, false); var rendered = new byte[pixels.Length]; output.CopyPixels(rendered, width * 4, 0);
        Check(rendered[0] == pixels[0] && rendered[10 * width * 4 + 10 * 4] != pixels[10 * width * 4 + 10 * 4], "Pixelation changes only selected area");
        var grayscale = CaptureImageEffects.ApplyPreset(first, "grayscale"); var grayPixels = new byte[pixels.Length]; grayscale.CopyPixels(grayPixels, width * 4, 0);
        Check(Enumerable.Range(0, grayPixels.Length / 4).All(index => grayPixels[index * 4] == grayPixels[index * 4 + 1] && grayPixels[index * 4 + 1] == grayPixels[index * 4 + 2]), "Reusable grayscale effect applies consistently");
        Check(new CaptureArea(-1920, -100, 1920, 1080).IsValid && !new CaptureArea(0, 0, 100000, 100000).IsValid, "Negative monitor coordinates and capture size bounds");
        var store = new HistoryStore(Path.Combine(root, "encoding"));
        var compressed = CaptureServices.Save(store, first, true); store.Add("recall", DateTimeOffset.UtcNow, media: compressed);
        var repeated = CaptureServices.Save(store, first, true); store.Add("recall", DateTimeOffset.UtcNow, media: repeated);
        Check(compressed == repeated && Directory.GetFiles(store.MediaDirectory).Length == 1, "Repeated screenshots share one compressed WebP blob");
        var loaded = CaptureServices.Load(store.ResolveMedia(compressed)); Check(loaded.PixelWidth == width && loaded.PixelHeight == height, "Shared image engine round-trips compressed capture dimensions");
    }
    private static async Task Protocol()
    {
        using var stream = new MemoryStream(); await WinForge.Dictation.DictationProtocol.WriteAsync(stream, new WinForge.Dictation.WorkerReply(true, "Türkçe", "CPU"), default); stream.Position = 0;
        var reply = await WinForge.Dictation.DictationProtocol.ReadAsync<WinForge.Dictation.WorkerReply>(stream, default); Check(reply.Text == "Türkçe", "Worker protocol preserves Unicode");
        using var invalid = new MemoryStream([255, 255, 255, 127]);
        try { await WinForge.Dictation.DictationProtocol.ReadBytesAsync(invalid, 1024, default); throw new Exception("Oversized frame accepted"); } catch (InvalidDataException) { checks++; }
    }
    private static void Speech()
    {
        Check(typeof(WasapiLoopbackCapture).GetConstructor(Type.EmptyTypes) is not null, "Existing loopback capture constructor remains binary compatible");
        var audio = new SpeechBuffer();
        for (var i = 0; i < 100; i++) audio.Append(Enumerable.Repeat(0.1f, 441).ToArray(), 44100);
        var samples = audio.Take(); Check(Math.Abs(samples.Length - 16000) <= 1 && SpeechBuffer.ContainsSpeech(samples), "Packet resampling preserves one second at 44.1 kHz");
        Check(audio.Take().Length == 0, "Consumed audio is cleared");
        Check(!SpeechBuffer.ContainsSpeech(new float[16000]) && !SpeechBuffer.ContainsSpeech([0.5f]), "Silence and short recordings do not invoke Whisper");
        audio.Append([float.NaN, float.PositiveInfinity], 16000); Check(audio.Take().All(float.IsFinite), "Invalid audio samples sanitized");
        var keys = new DictationShortcutState();
        Check(keys.Press(false) == DictationKeyAction.Start && keys.Press(false) == DictationKeyAction.None, "Repeated keydown during one press is ignored");
        Check(keys.Release(TimeSpan.FromMilliseconds(100)) == DictationKeyAction.None, "Quick tap leaves dictation running");
        Check(keys.Press(true) == DictationKeyAction.Stop && keys.Release(TimeSpan.FromMilliseconds(100)) == DictationKeyAction.None, "Second quick tap stops dictation");
        Check(keys.Press(false) == DictationKeyAction.Start && keys.Release(DictationShortcutState.HoldThreshold) == DictationKeyAction.Stop, "Holding the shortcut stops on release");
        keys.Press(false); keys.Reset(); Check(!keys.Held && keys.Release() == DictationKeyAction.None, "Cancellation clears held shortcut state");
    }
    private static async Task<int> Import(string root, string kind)
    {
        var sources = LegacyImporter.Discover(root, kind); var store = new HistoryStore(HistoryStore.DefaultDirectory); var importer = new LegacyImporter(store);
        foreach (var source in sources)
        {
            var preview = await importer.PreviewAsync(source, default);
            Console.WriteLine($"Source: {source.Database}; records={preview.Records}; bytes={preview.Bytes}; missing={preview.MissingFiles}");
            var result = await importer.ImportAsync(source, new Progress<ImportProgress>(p => { if (p.Completed % 5000 == 0 || p.Completed == p.Total) Console.WriteLine($"{p.Completed}/{p.Total}; imported={p.Imported}; missing={p.MissingFiles}"); }), default);
            Console.WriteLine($"Imported={result.Imported}; missing={result.MissingFiles}");
            var settings = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinForge", "settings.json"));
            var module = kind == "recall" ? "screen-history" : "voice-dictation";
            foreach (var (key, value) in LegacySettings.Read(source)) settings.For(module)[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            settings.Save();
        }
        return 0;
    }
    private static async Task CopyModels(string root)
    {
        var directory = Path.Combine(HistoryStore.DefaultDirectory, "models");
        foreach (var name in WhisperModels.Checksums.Keys)
        {
            var source = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).First();
            await WhisperModels.CopyAsync(source, directory, name, default); Console.WriteLine("Verified and copied: " + name);
        }
    }
    private static async Task RealWorker(string worker, string modelDirectory, bool gpu, bool fallback = false, bool translate = false)
    {
        var root = Path.GetFullPath("artifacts/capture-verification"); Directory.CreateDirectory(root);
        var wav = Path.Combine(root, "turkish.wav"); var raw = Path.Combine(root, "turkish.f32");
        using (var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer())
        {
            synth.Voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.First(v => v.Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase));
            using var speech = await synth.SynthesizeTextToStreamAsync("Merhaba dünya. İstanbul güzel bir şehir. Bu bir ekran geçmişi ve sesli yazma denemesidir.");
            await using var output = File.Create(wav); await speech.AsStreamForRead().CopyToAsync(output);
        }
        await WinForge.FileTools.Engine.ProcessRunner.RunAsync(SharedMediaTools.Ffmpeg, ["-y", "-v", "error", "-i", wav, "-ar", "16000", "-ac", "1", "-f", "f32le", raw]);
        var bytes = await File.ReadAllBytesAsync(raw); var samples = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        if (fallback)
        {
            var isolated = Path.Combine(root, "cpu-only-worker"); Directory.CreateDirectory(Path.Combine(isolated, "cpu"));
            foreach (var file in Directory.EnumerateFiles(worker)) File.Copy(file, Path.Combine(isolated, Path.GetFileName(file)), true);
            File.Copy(Path.Combine(worker, "cpu", "WinForge.Whisper.dll"), Path.Combine(isolated, "cpu", "WinForge.Whisper.dll"), true);
            worker = isolated;
        }
        using var client = new DictationClient(Path.GetFullPath(worker));
        var configuration = new WinForge.Dictation.WorkerConfiguration(Path.Combine(modelDirectory, translate ? "ggml-large-v3.bin" : "ggml-large-v3-turbo.bin"), "tr", translate, gpu);
        var result = await client.TranscribeAsync(samples, configuration, default);
        Check(translate ? result.Contains("world", StringComparison.OrdinalIgnoreCase) && result.Contains("city", StringComparison.OrdinalIgnoreCase) :
            result.Contains("İstanbul", StringComparison.OrdinalIgnoreCase) && result.Contains("deneme", StringComparison.OrdinalIgnoreCase),
            (translate ? "Real Large-model English translation: " : "Real Turkish Whisper transcription: ") + client.Engine);
        if (fallback) Check(client.Engine.Contains("GPU yerine", StringComparison.Ordinal), "Missing GPU backend falls back to CPU");
        File.WriteAllText(Path.Combine(root, translate ? "translation-result.txt" : gpu ? "gpu-result.txt" : "cpu-result.txt"), result);
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(10)))
        {
            try { await client.TranscribeAsync(samples, configuration, cancel.Token); throw new InvalidOperationException("Cancellation ignored."); }
            catch (OperationCanceledException) { Check(!client.IsRunning, "Cancellation terminates the owned worker"); }
        }
        Array.Clear(samples); Array.Clear(bytes);
    }
    private static async Task Recording()
    {
        var root = Path.GetFullPath("artifacts/capture-verification"); Directory.CreateDirectory(root);
        foreach (var (gif, microphone) in new[] { (false, false), (false, true), (true, false) })
        {
            await using var recording = new ScreenRecording(root, new CaptureArea(0, 0, 320, 200), microphone, !gif, true);
            await Task.Delay(1200); recording.TogglePause(); await Task.Delay(500); recording.TogglePause(); await Task.Delay(1000);
            var file = await recording.FinishAsync(gif, default); var info = await new WinForge.FileTools.Engine.MediaEngine(SharedMediaTools.Ffmpeg, SharedMediaTools.Create().Executable("ffprobe")).InspectAsync(file);
            Check(info.Video is { Width: 320, Height: 200 }, "Recording dimensions: " + (gif ? "GIF" : "MP4"));
            Check(info.Duration > 1.8 && info.Duration < 2.6, "Pause is excluded from recording duration");
            Check(gif ? !info.HasAudio : info.HasAudio, "Requested recording audio track survives muxing");
            if (!microphone) File.Copy(file, Path.Combine(root, gif ? "test.gif" : "test.mp4"), true);
        }
        var cancelled = new ScreenRecording(root, new CaptureArea(0, 0, 320, 200), false, false, false);
        await Task.Delay(250); cancelled.RequestCancel(); await cancelled.DisposeAsync();
        Check(!Directory.EnumerateDirectories(Path.Combine(root, "recording-jobs")).Any(), "Recording cancellation removes the owned job and temporary files");
    }
    private static void Lifecycle(string root)
    {
        var context = new TestContext(root);
        foreach (var module in new ITweakModule[] { new RecallModule(), new DictationModule(), new ScreenCaptureModule() })
        {
            module.Initialize(context); Check(!module.IsActive, module.Name + " defaults to disabled");
            if (module is DictationModule dictation)
                Check(dictation.GetQuickActions("").Any(action => action.Name == "Dikteyi başlat / durdur"),
                    "Whisper exposes a direct start/stop quick action");
            if (module is ScreenCaptureModule capture)
                Check(new[] { "Pencere yakala", "Etkin pencereyi yakala", "Monitörü yakala", "Tüm masaüstünü yakala", "Kayan ekranı yakala", "Ekran videosu kaydet", "Ekran GIF'i kaydet" }
                    .All(name => capture.GetQuickActions("").Any(action => action.Name == name)),
                    "ShareX-style capture modes are directly searchable quick actions");
            if (module is RecallModule recall)
            {
                Check(recall.GetQuickActions("").Any(action => action.Name == "Ekran geçmişini duraklat / sürdür"),
                    "Recall exposes its privacy pause action");
                Check(recall.Settings.Any(setting => setting.Key == "pause-hotkey"),
                    "Recall exposes an optional privacy pause hotkey");
            }
            module.Activate(); Check(module.IsActive, module.Name + " activates");
            module.Deactivate(); module.Deactivate(); Check(!module.IsActive && context.Keys.Count == 0, module.Name + " deactivates idempotently and releases shortcuts");
        }
    }
    private sealed class TestContext(string root) : IModuleContext
    {
        public TestHotkeys Keys { get; } = new();
        public IHotkeyService Hotkeys => Keys;
        public IModuleSettings Settings { get; } = new TestSettings(root);
        public ILogger Log { get; } = new FileLogger(Path.Combine(root, "test.log"));
        public void Notify(string message) { }
    }
    private sealed class TestSettings(string root) : IModuleSettings
    {
        public bool GetBool(string key, bool fallback = false) => fallback;
        public int GetInt(string key, int fallback = 0) => fallback;
        public string GetString(string key, string fallback = "") => key == "storage-directory" ? Path.Combine(root, "history") : fallback;
        public void Set(string key, object? value) { }
    }
    private sealed class TestHotkeys : IHotkeyService
    {
        public int Count;
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback) { Count++; return new Registration(() => Count--); }
        private sealed class Registration(Action release) : IDisposable
        { private bool disposed; public void Dispose() { if (!disposed) { disposed = true; release(); } } }
    }
    private static void Discovery(string modules)
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge-Discovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var package = Path.GetDirectoryName(Path.GetFullPath(modules))!;
            var context = new PackageContext(Path.Combine(package, "WinForge.dll"));
            try
            {
                var assembly = context.LoadFromAssemblyPath(Path.Combine(Path.GetFullPath(modules), "WinForge.Modules.Capture.dll"));
                foreach (var type in new[] { "RecallModule", "DictationModule", "ScreenCaptureModule" })
                {
                    var instance = Activator.CreateInstance(assembly.GetType("WinForge.Modules.Capture." + type)!);
                    Check(instance is not null && !(bool)instance.GetType().GetProperty("IsActive")!.GetValue(instance)!, "Packaged module discovery: " + type);
                }
                var history = context.LoadFromAssemblyName(new System.Reflection.AssemblyName("WinForge.History"));
                Check(Activator.CreateInstance(history.GetType("WinForge.History.HistoryStore")!, Path.Combine(root, "history")) is not null, "Packaged SQLite provider and native library load in isolated context");
            }
            finally { context.Unload(); }
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class PackageContext(string mainAssembly) : System.Runtime.Loader.AssemblyLoadContext(isCollectible: true)
    {
        private readonly System.Runtime.Loader.AssemblyDependencyResolver resolver = new(mainAssembly);
        protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName name)
        { var path = resolver.ResolveAssemblyToPath(name); return path is null ? null : LoadFromAssemblyPath(path); }
        protected override nint LoadUnmanagedDll(string name)
        { var path = resolver.ResolveUnmanagedDllToPath(name); return path is null ? 0 : LoadUnmanagedDllFromPath(path); }
    }
    private static void Render()
    {
        var root = Path.GetFullPath("artifacts/capture-verification"); Directory.CreateDirectory(root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.LightSlateGray, null, new Rect(0, 0, 800, 450));
            context.DrawRectangle(Brushes.DodgerBlue, null, new Rect(60, 60, 300, 200));
        }
        var image = new RenderTargetBitmap(800, 450, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        using var imageStream = new MemoryStream();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(imageStream);
        var imageData = "data:image/png;base64," + Convert.ToBase64String(imageStream.ToArray());
        var cases = new (string View, string File, double Width, double Height, object State)[]
        {
            ("nashi", "nashi.png", 1280, 860, new
            {
                page = "home", busy = false, status = "Dosyalar bilgisayarınızda kalır.",
                files = new[] { new { path = "C:/Belgeler/taslak.docx", name = "taslak.docx", directory = "C:/Belgeler" } },
                choices = new[] { new { id = "convert", title = "Dönüştür", symbol = "⇄" }, new { id = "compress", title = "Sıkıştır", symbol = "◈" }, new { id = "editImage", title = "Görseli düzenle", symbol = "✎" } },
                formats = new[] { "pdf", "docx", "png" },
                library = new[] { new { id = "pdf-tools", name = "PDF araçları", action = "convert", created = "Bugün", paths = new[] { "C:/Belgeler/rapor.pdf" }, available = 1, missing = false } },
                recent = new[] { new { id = "recent-doc", name = "rapor.pdf", action = "convert", created = "Az önce", paths = new[] { "C:/Belgeler/rapor.pdf" }, available = 1, missing = false } },
                query = "", recentQuery = "", downloadUrl = "", downloadKind = "video", downloadKinds = new[] { "video", "audio", "thumbnail" }, quality = 80,
                mediaReady = true, officeReady = false, setupLabel = "Medya araçları hazır", appearance = "system"
            }),
            ("capture-history", "history.png", 1180, 780, new
            {
                title = "Ekran Geçmişi", kind = "recall", search = "", app = "", tag = "", from = "", until = "", offset = 0,
                status = "Örnek kayıtlar", selectedId = 1L, selectedText = "Türkçe OCR önizlemesi", selectedTags = "örnek, ekran",
                preview = imageData, cursor = new { x = 140, y = 110, width = 16, height = 16 }, mediaType = "image", confirmDelete = false,
                confirmDeleteRange = false, confirmDeleteAll = false,
                rows = new[] { new { id = 1L, label = "Örnek çalışma ekranı", app = "Örnek.exe", title = "Türkçe OCR önizlemesi", created = DateTimeOffset.Now.ToString("g"), icon = (string?)null, hasMedia = true, mediaType = "image", tags = "örnek, ekran" } }
            }),
            ("capture-usage-statistics", "usage-statistics.png", 1080, 700, new
            {
                from = DateTimeOffset.Now.Date.ToString("dd.MM.yyyy"), until = "", status = "Örnek kullanım verileri", summary = "Bugün · 25 dakika",
                applications = new[] { new { app = "Örnek.exe", seconds = 1500, duration = "25 dk", estimated = false } },
                days = new[] { new { date = "Bugün", active = 1500, idle = 400 } },
                dayApplications = new[] { new { date = "Bugün", app = "Örnek.exe", duration = "25 dk", estimated = false } },
                contexts = new[] { new { date = "Bugün", type = "website", app = "chrome.exe", context = "example.com", duration = "10 dk" } },
                tags = new[] { new { tag = "örnek", seconds = 1500, duration = "25 dk", applications = 1 } }
            }),
            ("nashi-tool-editor", "tool-editor.png", 1040, 780, new
            {
                title = "Görseli boyutlandır", file = "afiş.png", media = true, playing = false, currentTime = 14.25, duration = 92.4,
                canPlayRange = true, showComparison = true, comparisonLabel = "İşlenmiş önizleme", processingPreview = false,
                playbackStatus = "Özgün dosya korunur.",
                controls = new object[]
                {
                    new { type = "help", id = "preserve", label = "Değişiklikler yeni bir kopyaya uygulanır." },
                    new { type = "text", id = "width", label = "Genişlik", value = "1920" },
                    new { type = "text", id = "height", label = "Yükseklik", value = "1080" },
                    new { type = "checkbox", id = "metadata", label = "Metadata korunsun", value = true }
                }
            }),
            ("capture-editor", "editor.png", 1180, 800, new { image = imageData, initialTool = (int)DrawingTool.Freehand, workflowCompletion = false, status = "" }),
            ("app-dialog", "app-dialog.png", 520, 280, new
            {
                title = "Set up media tools",
                message = "Download FFmpeg (~180 MB), yt-dlp and Deno directly from their official GitHub releases? SHA-256 hashes are verified before use.",
                confirmLabel = "Download tools", cancelLabel = "Cancel", isConfirmation = true
            })
        };
        var uiDirectory = FindUiDirectory();
        var webViewProfile = Path.Combine(Path.GetTempPath(), "WinForge-CapturePreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webViewProfile);
        var webViewEnvironment = CoreWebView2Environment.CreateAsync(null, webViewProfile);
        foreach (var item in cases)
        {
            var window = new ReactCaptureWindow(item.View, item.Width, item.Height, item.State, uiDirectory, webViewEnvironment);
            try
            {
                window.Show();
                PumpUntil(() => window.IsReady, TimeSpan.FromSeconds(20));
                PumpFor(TimeSpan.FromMilliseconds(500));
                var capture = window.CaptureAsync(Path.Combine(root, item.File));
                PumpUntil(() => capture.IsCompleted, TimeSpan.FromSeconds(20));
                capture.GetAwaiter().GetResult();
            }
            finally
            {
                window.Close();
            }
        }
        app.Shutdown();
    }

    private static string FindUiDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "WinForge.App", "wwwroot");
            if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
        }
        throw new DirectoryNotFoundException("React UI output was not found. Build WinForge.App before rendering UI previews.");
    }

    private static void PumpFor(TimeSpan duration)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        PumpUntil(() => started.Elapsed >= duration, duration + TimeSpan.FromSeconds(2));
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        var started = System.Diagnostics.Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            if (!condition() && started.Elapsed < timeout) return;
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!condition()) throw new TimeoutException("React UI did not become ready before the preview timeout.");
    }

    private sealed class ReactCaptureWindow : Window
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly string view;
        private readonly object state;
        private readonly string uiDirectory;
        private readonly Task<CoreWebView2Environment> webViewEnvironment;
        private readonly WebView2 webView = new() { DefaultBackgroundColor = System.Drawing.Color.White };
        private bool ready;
        private Exception? initializationError;

        public ReactCaptureWindow(string view, double width, double height, object state, string uiDirectory,
            Task<CoreWebView2Environment> webViewEnvironment)
        {
            this.view = view;
            this.state = state;
            this.uiDirectory = uiDirectory;
            this.webViewEnvironment = webViewEnvironment;
            Width = width;
            Height = height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = webView;
            Loaded += OnLoaded;
            Closed += (_, _) => webView.Dispose();
        }

        public bool IsReady => ready || initializationError is not null;

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            try
            {
                await webView.EnsureCoreWebView2Async(await webViewEnvironment);
                var core = webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.SetVirtualHostNameToFolderMapping("appassets.winforge", uiDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += (_, navigation) =>
                {
                    if (!navigation.Uri.StartsWith("https://appassets.winforge/", StringComparison.OrdinalIgnoreCase)) navigation.Cancel = true;
                };
                core.WebMessageReceived += (_, messageArgs) =>
                {
                    using var message = JsonDocument.Parse(messageArgs.WebMessageAsJson);
                    if (message.RootElement.GetProperty("command").GetString() != "feature.ready") return;
                    core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "feature.state", payload = state }, JsonOptions));
                    ready = true;
                };
                core.Navigate($"https://appassets.winforge/index.html?view={Uri.EscapeDataString(view)}");
            }
            catch (Exception error)
            {
                initializationError = error;
            }
        }

        public async Task CaptureAsync(string path)
        {
            if (initializationError is not null) throw new InvalidOperationException("React UI preview could not initialize.", initializationError);
            await using var output = File.Create(path);
            await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
        }
    }
    private static void NativeCapture()
    {
        foreach (var display in DesktopDisplays.Enumerate())
        {
            var window = new Window { WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false, Width = 240, Height = 160, Background = Brushes.Magenta, Topmost = true };
            window.Show(); var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Native.SetWindowPos(handle, Native.HWND_TOPMOST, display.Bounds.Left + 40, display.Bounds.Top + 40, 240, 160, 0x0040);
            window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render); Thread.Sleep(100);
            try
            {
                var area = CaptureServices.WindowArea(handle); var image = CaptureServices.Capture(area);
                var pixel = new byte[4]; image.CopyPixels(new Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), pixel, 4, 0);
                Check(area.Width == image.PixelWidth && area.Height == image.PixelHeight && pixel[0] > 240 && pixel[1] < 15 && pixel[2] > 240,
                    "Physical pixel capture of owned fixture on " + display.DeviceName);
            }
            finally { window.Close(); }
        }
    }
    private static void Audit()
    {
        var store = new HistoryStore(HistoryStore.DefaultDirectory);
        using var connection = store.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind,count(*) FROM items GROUP BY kind"; using var reader = command.ExecuteReader();
        while (reader.Read()) Console.WriteLine(reader.GetString(0) + ": " + reader.GetInt64(1)); reader.Close();
        command.CommandText = "SELECT DISTINCT media FROM items WHERE media<>''"; using var media = command.ExecuteReader(); var missing = 0;
        while (media.Read()) if (!File.Exists(store.ResolveMedia(media.GetString(0)))) missing++;
        Check(missing == 0, "All imported media references resolve inside WinForge storage");
        var item = store.Search(new("recall", Limit: 200)).First(row => row.Media.Length > 0);
        var image = CaptureServices.Load(store.ResolveMedia(item.Media));
        Check(image.PixelWidth > 0 && image.PixelHeight > 0, "Imported WebP image decodes through the shared image engine");
        var settingsPath = Path.Combine(Path.GetTempPath(), "WinForge-capture-audit-" + Guid.NewGuid().ToString("N"), "settings.json");
        var settings = new SettingsStore(settingsPath);
        Check(!settings.Enabled.Overlaps(new[] { "screen-history", "voice-dictation", "screen-capture" }), "Import did not enable recording modules");
    }
    private static int Scrolling()
    {
        var app = new Application(); var content = new System.Windows.Controls.StackPanel(); var random = new Random(73);
        for (var i = 0; i < 80; i++) content.Children.Add(new System.Windows.Controls.Border
        {
            Height = 24,
            Background = new SolidColorBrush(Color.FromRgb((byte)random.Next(255), (byte)random.Next(255), (byte)random.Next(255))),
            Child = new System.Windows.Controls.TextBlock { Text = "WinForge kayan ekran testi · satır " + i }
        });
        var viewer = new System.Windows.Controls.ScrollViewer { Content = content, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible };
        var window = new Window { Title = "WinForge kayan ekran testi", Width = 600, Height = 500, MinWidth = 600, MaxWidth = 600, MinHeight = 500, MaxHeight = 500, WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Content = viewer }; var failed = false;
        window.Loaded += async (_, _) =>
        {
            try
            {
                window.Activate(); TextOutputChecks.Focus(new System.Windows.Interop.WindowInteropHelper(window).Handle); await Task.Delay(800);
                var bounds = CaptureServices.WindowArea(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                var area = new CaptureArea(bounds.X + 30, bounds.Y + 60, bounds.Width - 80, bounds.Height - 90);
                var point = new POINT(area.X + area.Width / 2, area.Y + area.Height / 2); var target = WindowFromPoint(point); Native.GetWindowThreadProcessId(target, out var targetProcess);
                Console.WriteLine($"Scrolling geometry: {bounds}; logical={window.ActualWidth}x{window.ActualHeight}; target={targetProcess}; own={Environment.ProcessId}");
                var result = await ScrollingCapture.RunAsync(area, default);
                Directory.CreateDirectory("artifacts/capture-verification");
                var output = new PngBitmapEncoder(); output.Frames.Add(BitmapFrame.Create(result.Image)); using (var file = File.Create("artifacts/capture-verification/scrolling.png")) output.Save(file);
                Console.WriteLine($"Scrolling fixture: visible={area.Height}, stitched={result.Image.PixelHeight}, extent={viewer.ExtentHeight:0}, viewport={viewer.ViewportHeight:0}, confidence={result.Confidence:0.000}, partial={result.Partial}, offset={viewer.VerticalOffset:0}");
                Check(result.Image.PixelHeight > area.Height * 2 && result.Confidence >= 0.85 && !result.Partial, "Real scrolling window captures and stitches beyond the visible screen");
            }
            catch (Exception error) { failed = true; Console.Error.WriteLine(error); }
            finally { window.Close(); app.Shutdown(); }
        };
        app.Run(window); return failed ? 1 : 0;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
}
