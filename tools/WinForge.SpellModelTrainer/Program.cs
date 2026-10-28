using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace WinForge.SpellModelTrainer;

internal static class Program
{
    private const int MaximumVocabulary = 900_000;
    private const int MinimumVocabularyWordCount = 1;
    private const int MaximumContextWords = 12_000;
    private const int MaximumBigramCount = 250_000;
    private const int MaximumTrigramCount = 150_000;
    private const int MinimumNgramCount = 2;
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "typo-pairs")
            return SpellingErrorModelTrainer.Run(args.Skip(1).ToArray());

        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("Usage: WinForge.SpellModelTrainer <training-text> <output.gz> [culture-tag]");
            return 2;
        }

        var sourcePath = Path.GetFullPath(args[0]);
        var outputPath = Path.GetFullPath(args[1]);
        var culture = CultureInfo.GetCultureInfo(args.Length == 3 ? args[2] : "tr-TR");
        if (!culture.Name.StartsWith("tr", StringComparison.OrdinalIgnoreCase) &&
            !culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only Turkish and English models are supported.", nameof(args));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Training corpus not found.", sourcePath);

        Console.WriteLine($"Pass 1/2: counting {culture.EnglishName} word forms");
        var corpusWordCounts = CountWords(sourcePath, culture);
        var vocabulary = corpusWordCounts
            .Where(pair => pair.Value >= MinimumVocabularyWordCount && pair.Key.Length is >= 2 and <= 40)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(MaximumVocabulary)
            .Select((pair, index) => (pair.Key, pair.Value, Id: index))
            .ToArray();

        var wordIds = vocabulary.ToDictionary(entry => entry.Key, entry => entry.Id, StringComparer.Ordinal);
        var contextIds = vocabulary.Take(MaximumContextWords).Select(entry => entry.Id).ToHashSet();
        corpusWordCounts.Clear();

        Console.WriteLine($"Pass 2/2: counting candidate pairs for {contextIds.Count:N0} common contexts");
        var bigrams = CountBigrams(sourcePath, wordIds, contextIds, culture);
        var selectedBigramContexts = bigrams
            .Where(pair => pair.Value >= MinimumNgramCount)
            .OrderByDescending(pair => pair.Value)
            .Take(MaximumBigramCount / 2)
            .Select(pair => pair.Key)
            .ToHashSet();
        var trigrams = CountTrigrams(sourcePath, wordIds, selectedBigramContexts, culture);

        WriteModel(outputPath, vocabulary, bigrams, trigrams);
        Console.WriteLine($"Model saved: {outputPath}");
        Console.WriteLine($"Words: {Math.Min(vocabulary.Length, MaximumVocabulary):N0}; " +
                          $"bigrams: {Math.Min(bigrams.Count, MaximumBigramCount):N0}; " +
                          $"trigrams: {Math.Min(trigrams.Count, MaximumTrigramCount):N0}");
        return 0;
    }

    private static Dictionary<string, int> CountWords(string path, CultureInfo culture)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sentence in ReadSentences(path))
        {
            foreach (var token in Tokenize(sentence, culture)) Increment(counts, token);
        }
        return counts;
    }

    private static Dictionary<ulong, int> CountBigrams(
        string path,
        Dictionary<string, int> wordIds,
        HashSet<int> contextIds,
        CultureInfo culture)
    {
        var counts = new Dictionary<ulong, int>();
        foreach (var sentence in ReadSentences(path))
        {
            var previousId = -1;
            foreach (var token in Tokenize(sentence, culture))
            {
                if (!wordIds.TryGetValue(token, out var currentId)) { previousId = -1; continue; }
                if (previousId >= 0 && contextIds.Contains(previousId)) Increment(counts, PairKey(previousId, currentId));
                previousId = currentId;
            }
        }
        return counts;
    }

    private static Dictionary<ulong, int> CountTrigrams(
        string path,
        Dictionary<string, int> wordIds,
        HashSet<ulong> selectedContexts,
        CultureInfo culture)
    {
        var counts = new Dictionary<ulong, int>();
        foreach (var sentence in ReadSentences(path))
        {
            var previousId = -1;
            var penultimateId = -1;
            foreach (var token in Tokenize(sentence, culture))
            {
                if (!wordIds.TryGetValue(token, out var currentId)) { previousId = penultimateId = -1; continue; }
                if (penultimateId >= 0)
                {
                    var contextKey = PairKey(penultimateId, previousId);
                    if (selectedContexts.Contains(contextKey)) Increment(counts, TripleKey(penultimateId, previousId, currentId));
                }
                penultimateId = previousId;
                previousId = currentId;
            }
        }
        return counts;
    }

    private static IEnumerable<string> ReadSentences(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            foreach (var sentence in SplitSentences(line)) yield return sentence;
        }
    }

    private static IEnumerable<string> SplitSentences(string line)
    {
        var start = 0;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] is not ('.' or '!' or '?' or '\n' or '\r')) continue;
            if (index > start) yield return line[start..index];
            start = index + 1;
        }
        if (start < line.Length) yield return line[start..];
    }

    private static IEnumerable<string> Tokenize(string sentence, CultureInfo culture)
    {
        var buffer = new StringBuilder();
        var normalized = sentence.Normalize(NormalizationForm.FormC).ToLower(culture);
        foreach (var character in normalized)
        {
            if (char.IsLetter(character)) buffer.Append(character);
            else if (character is '\'' or '’' && buffer.Length > 0) { }
            else if (buffer.Length > 0)
            {
                if (buffer.Length <= 40) yield return buffer.ToString();
                buffer.Clear();
            }
        }
        if (buffer.Length is >= 2 and <= 40) yield return buffer.ToString();
    }

    private static void WriteModel(
        string outputPath,
        (string Key, int Value, int Id)[] vocabulary,
        Dictionary<ulong, int> bigrams,
        Dictionary<ulong, int> trigrams)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var file = File.Create(outputPath);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var wordsById = new string[vocabulary.Length];
        foreach (var word in vocabulary)
        {
            wordsById[word.Id] = word.Key;
            writer.Write("u\t"); writer.Write(word.Key); writer.Write('\t'); writer.WriteLine(word.Value);
        }

        foreach (var pair in SelectFrequent(bigrams, MaximumBigramCount))
        {
            var first = checked((int)(pair.Key >> 32));
            var second = checked((int)(pair.Key & uint.MaxValue));
            WriteEntry(writer, "b", wordsById[first], wordsById[second], pair.Value);
        }

        foreach (var pair in SelectFrequent(trigrams, MaximumTrigramCount))
        {
            var first = checked((int)(pair.Key >> 40));
            var second = checked((int)((pair.Key >> 20) & 0xFFFFF));
            var third = checked((int)(pair.Key & 0xFFFFF));
            writer.Write("t\t"); writer.Write(wordsById[first]); writer.Write('\t');
            writer.Write(wordsById[second]); writer.Write('\t'); writer.Write(wordsById[third]); writer.Write('\t'); writer.WriteLine(pair.Value);
        }
    }

    private static IEnumerable<KeyValuePair<ulong, int>> SelectFrequent(Dictionary<ulong, int> counts, int limit) =>
        counts.Where(pair => pair.Value >= MinimumNgramCount)
            .OrderByDescending(pair => pair.Value)
            .Take(limit);

    private static void WriteEntry(StreamWriter writer, string type, string previous, string next, int count)
    {
        writer.Write(type); writer.Write('\t'); writer.Write(previous); writer.Write('\t');
        writer.Write(next); writer.Write('\t'); writer.WriteLine(count);
    }

    private static ulong PairKey(int first, int second) => ((ulong)(uint)first << 32) | (uint)second;

    private static ulong TripleKey(int first, int second, int third) =>
        ((ulong)(uint)first << 40) | ((ulong)(uint)second << 20) | (uint)third;

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var count) ? Math.Min(count + 1, 10000) : 1;

    private static void Increment(Dictionary<ulong, int> counts, ulong key) =>
        counts[key] = counts.TryGetValue(key, out var count) ? Math.Min(count + 1, 10000) : 1;
}
