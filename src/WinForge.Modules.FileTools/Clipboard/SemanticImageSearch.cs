using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using WinForge.FileTools.UI;

namespace WinForge.ClipboardLine;

/// <summary>Offline CPU MobileCLIP-S2 inference. No query or image is sent over the network.</summary>
internal sealed class SemanticImageSearch : IDisposable
{
    internal const int Dimensions = 512;
    internal const int TensorSide = 256;
    private readonly string _models;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, float[][]> _vectors = new(StringComparer.Ordinal);
    private InferenceSession? _imageSession;
    private InferenceSession? _textSession;
    private ClipTokenizer? _tokenizer;
    private bool _disposed;
    private string _modelHash = "";

    internal SemanticImageSearch(string libraryDirectory, string? modelsDirectory = null)
    {
        _models = modelsDirectory ?? Path.Combine(AppContext.BaseDirectory, "models");
        _cacheDirectory = Path.Combine(libraryDirectory, "image-search"); Directory.CreateDirectory(_cacheDirectory);
    }

    private void Initialize()
    {
        if (_imageSession is not null) return;
        var configPath = Path.Combine(_models, "search-model-config.json");
        using var configuration = JsonDocument.Parse(File.ReadAllText(configPath));
        foreach (var entry in configuration.RootElement.GetProperty("files").EnumerateObject())
        {
            if (Path.GetFileName(entry.Name) != entry.Name) throw new InvalidDataException("Model yolu geçersiz.");
            var path = Path.Combine(_models, entry.Name);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Model dosyası bağlantı olamaz.");
            using var file = File.OpenRead(path);
            if (file.Length != entry.Value.GetProperty("bytes").GetInt64() || Convert.ToHexStringLower(SHA256.HashData(file)) != entry.Value.GetProperty("sha256").GetString()) throw new InvalidDataException("Görsel arama modeli bütünlük denetimini geçemedi.");
        }
        _modelHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(_models, "model-source.json"))));
        _tokenizer = new ClipTokenizer(Path.Combine(_models, "clip-vocab.json"), Path.Combine(_models, "clip-merges.txt"));
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        var image = new InferenceSession(Path.Combine(_models, "mobileclip-s2-image.onnx"), options);
        try { _textSession = new InferenceSession(Path.Combine(_models, "mobileclip-s2-text.onnx"), options); _imageSession = image; }
        catch { image.Dispose(); throw; }
    }

    internal async Task<IReadOnlyList<(Guid Id, float Score)>> SearchAsync(string query, IReadOnlyList<(Guid Id, string File)> images, CancellationToken token)
    {
        if (query.Length is < 1 or > 2048) throw new ArgumentException("Görsel arama metni 1–2048 karakter olmalı.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() =>
            {
                Initialize(); token.ThrowIfCancellationRequested();
                using var input = OrtValue.CreateTensorValueFromMemory(_tokenizer!.Encode(query), [1L, 77L]);
                var text = Infer(_textSession!, "text", input); var scores = new List<(Guid Id, float Score)>();
                foreach (var image in images)
                {
                    token.ThrowIfCancellationRequested(); var vectors = ImageVectors(image.File, token);
                    if (vectors.Length > 0) scores.Add((image.Id, vectors.Max(vector => Dot(text, vector))));
                }
                return RankedMatches(scores);
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private float[][] ImageVectors(string file, CancellationToken token)
    {
        var key = Path.GetFileNameWithoutExtension(file);
        if (key.Length != 64 || key.Any(character => !char.IsAsciiHexDigitLower(character))) throw new InvalidDataException("Görsel arama kimliği geçersiz.");
        if (_vectors.TryGetValue(key, out var vectors)) return vectors;
        var cacheFile = Path.Combine(_cacheDirectory, key + ".json");
        try
        {
            if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length < 100_000)
            {
                var cache = JsonSerializer.Deserialize<VectorCache>(File.ReadAllText(cacheFile));
                if (cache is not null && cache.Model == _modelHash && cache.Key == key && ValidVectors(cache.Vectors)) return Remember(key, cache.Vectors);
            }
        }
        catch (Exception error) when (error is JsonException or IOException) { }
        var source = ImageEngine.Load(file);
        var factor = Math.Min(1, 1024d / Math.Max(source.PixelWidth, source.PixelHeight));
        if (factor < 1) source = new TransformedBitmap(source, new ScaleTransform(factor, factor));
        source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4]; source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        var result = new List<float[]>();
        foreach (var tensor in ImageTensors(pixels, source.PixelWidth, source.PixelHeight))
        {
            token.ThrowIfCancellationRequested(); using var input = OrtValue.CreateTensorValueFromMemory(tensor, [1L, 3L, TensorSide, TensorSide]);
            result.Add(Infer(_imageSession!, "image", input));
        }
        vectors = result.ToArray(); token.ThrowIfCancellationRequested();
        var temporary = cacheFile + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new VectorCache(_modelHash, key, vectors))); File.Move(temporary, cacheFile, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Remember(key, vectors);
    }

    private float[][] Remember(string key, float[][] vectors) { if (_vectors.Count >= 256) _vectors.Clear(); _vectors[key] = vectors; return vectors; }
    private static bool ValidVectors(float[][]? vectors) => vectors is { Length: <= 4 } && vectors.All(vector => vector is { Length: Dimensions } && vector.All(float.IsFinite) && Math.Abs(vector.Sum(value => value * value) - 1) < .02);
    private static float[] Infer(InferenceSession session, string name, OrtValue input)
    {
        using var options = new RunOptions(); using var output = session.Run(options, new Dictionary<string, OrtValue> { [name] = input }, session.OutputNames);
        return Normalize(output[0].GetTensorDataAsSpan<float>().ToArray());
    }
    internal static float[] Normalize(float[] values)
    {
        if (values.Length != Dimensions || values.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Model vektörü geçersiz.");
        var sum = 0f; foreach (var value in values) sum += value * value;
        var norm = Math.Sqrt(sum); if (norm <= 0 || !double.IsFinite(norm)) throw new InvalidDataException("Model vektörü boş.");
        return values.Select(value => (float)(value / norm)).ToArray();
    }
    private static float Dot(float[] left, float[] right) { var score = 0f; for (var index = 0; index < Dimensions; index++) score += left[index] * right[index]; return score; }
    internal static IReadOnlyList<(Guid Id, float Score)> RankedMatches(IReadOnlyList<(Guid Id, float Score)> scores)
    {
        if (scores.Count == 0) return [];
        var best = Math.Max(0, scores.Max(value => value.Score)); var mean = scores.Average(value => value.Score);
        var spread = Math.Max(.01, Math.Sqrt(scores.Average(value => Math.Pow(value.Score - mean, 2))));
        return scores.Where(value => float.IsFinite(value.Score) && value.Score >= Math.Max(.22, best - .045) && (scores.Count < 8 || value.Score >= .30 || (value.Score - mean) / spread >= 1.25)).OrderByDescending(value => value.Score).ThenBy(value => value.Id).Take(200).ToArray();
    }

    internal static IEnumerable<float[]> ImageTensors(byte[] pixels, int width, int height)
    {
        if (width is < 1 or > 1024 || height is < 1 or > 1024 || pixels.Length != width * height * 4) throw new ArgumentException("Görsel arama pikselleri geçersiz.");
        var longest = Math.Max(width, height); var shortest = Math.Min(width, height);
        if (shortest < 120 || (double)longest / shortest > 4) yield break;
        var regions = new List<Rect?> { null };
        if ((double)longest / shortest > 1.3)
        {
            var tiles = Math.Min(3, (int)Math.Ceiling((double)longest / shortest));
            for (var index = 0; index < tiles; index++) { var offset = (double)(longest - shortest) * index / (tiles - 1); regions.Add(new Rect(width >= height ? offset : 0, width >= height ? 0 : offset, shortest, shortest)); }
        }
        foreach (var crop in regions) yield return ImageTensor(pixels, width, height, crop);
    }

    private static float[] ImageTensor(byte[] pixels, int width, int height, Rect? crop)
    {
        var output = new float[3 * TensorSide * TensorSide]; var scale = (double)TensorSide / Math.Max(width, height);
        var fit = crop is null ? new Rect((TensorSide - width * scale) / 2, (TensorSide - height * scale) / 2, width * scale, height * scale) : new Rect(0, 0, TensorSide, TensorSide);
        var region = crop ?? new Rect(0, 0, width, height);
        for (var y = 0; y < TensorSide; y++) for (var x = 0; x < TensorSide; x++)
        {
            var at = y * TensorSide + x;
            if (x + .5 < fit.X || x + .5 >= fit.Right || y + .5 < fit.Y || y + .5 >= fit.Bottom) { output[at] = output[TensorSide * TensorSide + at] = output[2 * TensorSide * TensorSide + at] = 128f / 255; continue; }
            var fx = Math.Clamp(region.X + (x + .5 - fit.X) / fit.Width * region.Width - .5, 0, width - 1);
            var fy = Math.Clamp(region.Y + (y + .5 - fit.Y) / fit.Height * region.Height - .5, 0, height - 1);
            var x0 = (int)fx; var y0 = (int)fy; var x1 = Math.Min(width - 1, x0 + 1); var y1 = Math.Min(height - 1, y0 + 1); var dx = fx - x0; var dy = fy - y0;
            double Sample(int channel) => (pixels[(y0 * width + x0) * 4 + channel] * (1 - dx) + pixels[(y0 * width + x1) * 4 + channel] * dx) * (1 - dy) + (pixels[(y1 * width + x0) * 4 + channel] * (1 - dx) + pixels[(y1 * width + x1) * 4 + channel] * dx) * dy;
            var alpha = Sample(3) / 255;
            for (var channel = 0; channel < 3; channel++) output[channel * TensorSide * TensorSide + at] = (float)((Sample(2 - channel) * alpha + 128 * (1 - alpha)) / 255);
        }
        return output;
    }

    public void Dispose()
    {
        _gate.Wait(); try { _disposed = true; _imageSession?.Dispose(); _textSession?.Dispose(); _vectors.Clear(); } finally { _gate.Release(); }
    }
    private sealed record VectorCache(string Model, string Key, float[][] Vectors);
}
