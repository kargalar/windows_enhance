using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WinForge.Dictation;
using WinForge.Modules.Capture;
using WinForge.Core;

internal static class DictationBenchmark
{
    private static readonly string[] Sentences =
    [
        "Merhaba dünya. İstanbul güzel bir şehir. Bugün hava çok güzel.",
        "Yarın sabah toplantıdan önce raporu gözden geçirip Ayşe'ye göndereceğim.",
        "Bu uygulamada sesle yazma özelliğini Türkçe olarak deniyorum.",
        "Lütfen dosyayı masaüstüne kaydet ve bilgisayarı yeniden başlat.",
        "İzmir, Ankara ve İstanbul arasında trenle seyahat etmek istiyorum."
    ];

    internal static async Task RunAsync(string engineDirectory, string turboModel, string smallModel, string? manifest = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge-DictationBench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var samples = new List<float[]>();
            var references = new List<string>();
            var wavFiles = new List<string>();
            if (manifest is null)
            {
                using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
                synth.Voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Türkçe Windows sentez sesi bulunamadı.");
                for (var i = 0; i < Sentences.Length; i++)
                {
                    var wav = Path.Combine(root, $"{i}.wav");
                    using (var speech = await synth.SynthesizeTextToStreamAsync(Sentences[i]))
                    await using (var output = File.Create(wav)) await speech.AsStreamForRead().CopyToAsync(output);
                    wavFiles.Add(wav); references.Add(Sentences[i]);
                }
            }
            else
            {
                var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
                foreach (var line in await File.ReadAllLinesAsync(manifest))
                {
                    var fields = line.Split('\t');
                    if (fields.Length < 2 || Path.GetFileName(fields[0]) != fields[0]) throw new InvalidDataException("Geçersiz ses listesi.");
                    wavFiles.Add(Path.Combine(baseDirectory, fields[0])); references.Add(fields[1]);
                }
            }
            if (wavFiles.Count == 0) throw new InvalidDataException("Ses listesi boş.");
            for (var i = 0; i < wavFiles.Count; i++)
            {
                if (manifest is not null) samples.Add(ReadPcmWave(wavFiles[i]));
                else
                {
                    var raw = Path.Combine(root, $"{i}.f32");
                    await WinForge.FileTools.Engine.ProcessRunner.RunAsync(SharedMediaTools.Ffmpeg, ["-y", "-v", "error", "-i", wavFiles[i], "-ar", "16000", "-ac", "1", "-f", "f32le", raw]);
                    var bytes = await File.ReadAllBytesAsync(raw); var audio = new float[bytes.Length / 4];
                    Buffer.BlockCopy(bytes, 0, audio, 0, bytes.Length); samples.Add(audio); Array.Clear(bytes);
                }
            }
            foreach (var (name, path) in new[] { ("Turbo", turboModel), ("Small", smallModel) })
            {
                using var client = new DictationClient(Path.GetFullPath(engineDirectory));
                var config = new DictationConfiguration(Path.GetFullPath(path), "tr", false, false);
                await client.PrepareAsync(config, default);
                var seconds = 0d; var errors = 0; var words = 0;
                for (var i = 0; i < references.Count; i++)
                {
                    var timer = Stopwatch.StartNew(); var result = await client.TranscribeAsync(samples[i], config, default); seconds += timer.Elapsed.TotalSeconds;
                    var reference = Words(references[i]); var actual = Words(result);
                    var distance = Distance(reference, actual); errors += distance; words += reference.Length;
                    Console.WriteLine($"{name} {i + 1}: {timer.Elapsed.TotalSeconds:F2}s WER={distance}/{reference.Length} | {result}");
                }
                Console.WriteLine($"{name}: {seconds / references.Count:F2}s average; WER {errors}/{words} ({100d * errors / words:F1}%) on {(manifest is null ? "synthetic" : "manifest")} speech");
            }
            foreach (var audio in samples) Array.Clear(audio);
        }
        finally { Directory.Delete(root, true); }
    }

    private static float[] ReadPcmWave(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("WAV RIFF bekleniyor.");
        reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("WAV bekleniyor.");
        ushort format = 0, channels = 0, bits = 0; uint rate = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4)); var length = reader.ReadUInt32(); var next = reader.BaseStream.Position + length + (length & 1);
            if (next > reader.BaseStream.Length) throw new InvalidDataException("Eksik WAV verisi.");
            if (id == "fmt ")
            {
                if (length < 16) throw new InvalidDataException("Geçersiz WAV biçimi.");
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadUInt32();
                reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (id == "data")
            {
                if (format is not (1 or 3) || channels != 1 || rate != 16000 || bits != 32 || length % 4 != 0)
                    throw new InvalidDataException("16 kHz mono 32 bit PCM veya float WAV bekleniyor.");
                var result = new float[length / 4];
                for (var i = 0; i < result.Length; i++) result[i] = format == 3 ? reader.ReadSingle() : reader.ReadInt32() / 2147483648f;
                return result;
            }
            reader.BaseStream.Position = next;
        }
        throw new InvalidDataException("WAV ses verisi bulunamadı.");
    }

    private static string[] Words(string text) => Regex.Matches(text.ToLower(new CultureInfo("tr-TR")), @"[\p{L}\p{N}]+")
        .Select(match => match.Value).ToArray();
    private static int Distance(string[] expected, string[] actual)
    {
        var row = Enumerable.Range(0, actual.Length + 1).ToArray();
        for (var i = 1; i <= expected.Length; i++)
        {
            var previous = row[0]; row[0] = i;
            for (var j = 1; j <= actual.Length; j++)
            {
                var old = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (expected[i - 1] == actual[j - 1] ? 0 : 1));
                previous = old;
            }
        }
        return row[^1];
    }
}
