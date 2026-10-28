using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WinForge.SpellModelTrainer;

internal static class SpellingErrorModelTrainer
{
    private const int DefaultMinimumPairCount = 2;
    private const double MinimumCorrectionConfidence = 0.90;
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    internal static int Run(string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            Console.Error.WriteLine("Usage: WinForge.SpellModelTrainer typo-pairs <TOEFL-Spell-Annotations.tsv> <output.gz> [minimum-count] [minimum-confidence]");
            return 2;
        }

        var sourcePath = Path.GetFullPath(args[0]);
        var outputPath = Path.GetFullPath(args[1]);
        var minimumCount = args.Length >= 3
            ? int.Parse(args[2], CultureInfo.InvariantCulture)
            : DefaultMinimumPairCount;
        var minimumConfidence = args.Length >= 4
            ? double.Parse(args[3], CultureInfo.InvariantCulture)
            : MinimumCorrectionConfidence;
        if (minimumCount < 1)
            throw new ArgumentOutOfRangeException(nameof(args), "Minimum count must be at least 1.");
        if (minimumConfidence is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(args), "Minimum confidence must be greater than 0 and at most 1.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Annotation file not found.", sourcePath);

        var corrections = ReadTrainingCorrections(sourcePath);
        WriteModel(outputPath, corrections, minimumCount, minimumConfidence);
        Console.WriteLine($"Training typo forms: {corrections.Count:N0}");
        Console.WriteLine($"High-confidence corrections: {corrections.Count(pair => IsReliable(pair.Value, minimumCount, minimumConfidence)):N0}");
        Console.WriteLine($"Minimum count: {minimumCount:N0}; minimum confidence: {minimumConfidence:P0}");
        Console.WriteLine($"Model saved: {outputPath}");
        Console.WriteLine("File-level split: 80% train, 20% held out (stable SHA-256 partition)");
        return 0;
    }

    private static Dictionary<string, PairCounts> ReadTrainingCorrections(string sourcePath)
    {
        using var reader = new StreamReader(sourcePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine()?.Split('\t') ?? throw new InvalidDataException("Annotation file is empty.");
        var filenameIndex = Array.IndexOf(header, "Filename");
        var misspellingIndex = Array.IndexOf(header, "Misspelling");
        var typeIndex = Array.IndexOf(header, "Type");
        var correctionIndex = Array.IndexOf(header, "Correction");
        if (filenameIndex < 0 || misspellingIndex < 0 || typeIndex < 0 || correctionIndex < 0)
            throw new InvalidDataException("Annotation file must contain Filename, Misspelling, Type, and Correction columns.");

        var corrections = new Dictionary<string, PairCounts>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split('\t');
            if (fields.Length <= Math.Max(Math.Max(filenameIndex, misspellingIndex), Math.Max(typeIndex, correctionIndex)) ||
                fields[typeIndex] != "M" || IsHeldOutFile(fields[filenameIndex])) continue;

            var misspelling = NormalizeToken(fields[misspellingIndex]);
            var correction = NormalizeToken(fields[correctionIndex]);
            if (misspelling.Length < 2 || correction.Length < 2 || misspelling == correction) continue;

            if (!corrections.TryGetValue(misspelling, out var pairCounts))
                corrections[misspelling] = pairCounts = new PairCounts();
            pairCounts.Increment(correction);
        }
        return corrections;
    }

    private static void WriteModel(string outputPath, Dictionary<string, PairCounts> corrections, int minimumCount, double minimumConfidence)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var file = File.Create(outputPath);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var (misspelling, pairCounts) in corrections.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!TryGetReliableCorrection(pairCounts, minimumCount, minimumConfidence, out var correction, out var count)) continue;
            writer.Write(misspelling); writer.Write('\t'); writer.Write(correction); writer.Write('\t'); writer.WriteLine(count);
        }
    }

    private static bool IsReliable(PairCounts pairCounts, int minimumCount, double minimumConfidence) =>
        TryGetReliableCorrection(pairCounts, minimumCount, minimumConfidence, out _, out _);

    private static bool TryGetReliableCorrection(
        PairCounts pairCounts,
        int minimumCount,
        double minimumConfidence,
        out string correction,
        out int count)
    {
        var best = pairCounts.Corrections.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal).First();
        correction = best.Key;
        count = best.Value;
        return count >= minimumCount && (double)count / pairCounts.Total >= minimumConfidence;
    }

    private static string NormalizeToken(string value) => value.Trim().Normalize(NormalizationForm.FormC).ToLower(EnglishCulture);

    internal static bool IsHeldOutFile(string filename) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(filename))[0] % 5 == 0;

    private sealed class PairCounts
    {
        internal int Total { get; private set; }
        internal Dictionary<string, int> Corrections { get; } = new(StringComparer.Ordinal);

        internal void Increment(string correction)
        {
            Total++;
            Corrections[correction] = Corrections.TryGetValue(correction, out var count) ? count + 1 : 1;
        }
    }
}
