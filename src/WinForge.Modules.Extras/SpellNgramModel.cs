using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace WinForge.Modules.Extras;

/// <summary>Locally stored word and n-gram frequency models for supported languages.</summary>
internal static class SpellNgramModel
{
    private const char ContextSeparator = '\u001f';
    private const int MaximumCorrectionVocabulary = 100_000;
    private const int MaximumInterpolatedCandidates = 64;
    private static readonly Lazy<ModelData> TurkishData = new(() => Load("Turkish"), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<ModelData> EnglishData = new(() => Load("English"), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<ModelData> TurkishDialogueData = new(() => Load("TurkishDialogue"), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<ModelData> EnglishDialogueData = new(() => Load("EnglishDialogue"), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    internal static IReadOnlyCollection<string> TurkishVocabulary => TurkishData.Value.CorrectionWords;
    internal static IReadOnlyCollection<string> EnglishVocabulary => EnglishData.Value.CorrectionWords;

    internal static int GetTurkishVocabularyCount() => TurkishData.Value.Words.Count;
    internal static int GetEnglishVocabularyCount() => EnglishData.Value.Words.Count;

    internal static bool ContainsTurkishWord(string word) => TurkishData.Value.Words.ContainsKey(Normalize(word, "tr-TR"));

    internal static bool ContainsWord(string word, string languageTag) =>
        GetData(languageTag).Words.ContainsKey(Normalize(word, languageTag));

    internal static double GetUnigramBonus(string word, string languageTag)
    {
        var data = GetData(languageTag);
        return data.Unigrams.TryGetValue(Normalize(word, languageTag), out var count)
            ? Math.Min(0.35, Math.Log10(count + 1) * 0.12) : 0;
    }

    internal static double GetBigramBonus(string previousWord, string nextWord, string languageTag)
    {
        var data = GetData(languageTag);
        return data.Bigrams.TryGetValue(CreateContextKey(Normalize(previousWord, languageTag), Normalize(nextWord, languageTag)), out var count)
            ? Math.Min(0.75, Math.Log10(count) * 0.35) : 0;
    }

    internal static double GetTrigramBonus(string previousWords, string nextWord, string languageTag)
    {
        var history = NormalizeHistory(previousWords, languageTag);
        if (history is null) return 0;
        return GetData(languageTag).Trigrams.TryGetValue(CreateContextKey(history, Normalize(nextWord, languageTag)), out var count)
            ? Math.Min(0.9, Math.Log10(count) * 0.4) : 0;
    }

    internal static double GetConversationContextBonus(string previousWords, string nextWord, string languageTag)
    {
        var history = NormalizeHistory(previousWords, languageTag);
        if (history is null) return 0;

        var data = GetConversationData(languageTag);
        var lastWord = history[(history.LastIndexOf(ContextSeparator) + 1)..];
        var bigramBonus = data.Bigrams.TryGetValue(CreateContextKey(lastWord, Normalize(nextWord, languageTag)), out var bigramCount)
            ? Math.Min(0.75, Math.Log10(bigramCount) * 0.35) : 0;
        var trigramBonus = data.Trigrams.TryGetValue(CreateContextKey(history, Normalize(nextWord, languageTag)), out var trigramCount)
            ? Math.Min(0.9, Math.Log10(trigramCount) * 0.4) : 0;
        return Math.Max(bigramBonus, trigramBonus);
    }

    internal static IReadOnlyList<WeightedWord> GetNextWordCandidates(string previousWords, string languageTag)
    {
        return GetNextWordCandidates(previousWords, languageTag, GetData(languageTag));
    }

    internal static IReadOnlyList<WeightedWord> GetConversationNextWordCandidates(string previousWords, string languageTag)
    {
        return GetNextWordCandidates(previousWords, languageTag, GetConversationData(languageTag));
    }

    internal static bool HasConversationContext(string previousWords, string languageTag)
        => GetConversationContextSupport(previousWords, languageTag) > 0;

    internal static long GetConversationContextSupport(string previousWords, string languageTag)
    {
        var history = NormalizeHistory(previousWords, languageTag);
        if (history is null) return 0;

        var data = GetConversationData(languageTag);
        var previousWord = history[(history.LastIndexOf(ContextSeparator) + 1)..];
        if (data.TrigramCandidates.TryGetValue(history, out var trigramCandidates))
            return trigramCandidates.Sum(candidate => (long)candidate.Count);
        return data.BigramCandidates.TryGetValue(previousWord, out var bigramCandidates)
            ? bigramCandidates.Sum(candidate => (long)candidate.Count)
            : 0;
    }

    private static ModelData GetConversationData(string languageTag) =>
        languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            ? TurkishDialogueData.Value : EnglishDialogueData.Value;

    private static IReadOnlyList<WeightedWord> GetNextWordCandidates(
        string previousWords,
        string languageTag,
        ModelData data)
    {
        var history = NormalizeHistory(previousWords, languageTag);
        if (history is null) return [];
        var previousWord = history[(history.LastIndexOf(ContextSeparator) + 1)..];
        IReadOnlyList<WeightedWord> candidates = data.CommonWordCandidates;
        if (data.BigramCandidates.TryGetValue(previousWord, out var bigramCandidates))
            candidates = InterpolateCandidates(bigramCandidates, candidates);
        if (data.TrigramCandidates.TryGetValue(history, out var trigramCandidates))
            candidates = InterpolateCandidates(trigramCandidates, candidates);
        return candidates;
    }

    private static IReadOnlyList<WeightedWord> InterpolateCandidates(
        IReadOnlyList<WeightedWord> trigramCandidates,
        IReadOnlyList<WeightedWord> bigramCandidates)
    {
        // Estimate the context's evidence before pruning its candidate list. Using only
        // the top candidates makes low-diversity contexts look artificially certain.
        var trigramCount = trigramCandidates.Sum(candidate => (long)candidate.Count);
        var trigramTypes = trigramCandidates.Count;
        var trigramWeight = (double)trigramCount / (trigramCount + trigramTypes);
        trigramCandidates = trigramCandidates.Take(MaximumInterpolatedCandidates).ToArray();
        bigramCandidates = bigramCandidates.Take(MaximumInterpolatedCandidates).ToArray();
        var probabilities = new Dictionary<string, (int Count, double Probability)>(StringComparer.Ordinal);

        foreach (var candidate in bigramCandidates)
        {
            probabilities[candidate.Word] = (
                candidate.Count,
                candidate.Probability * (1 - trigramWeight));
        }

        foreach (var candidate in trigramCandidates)
        {
            probabilities.TryGetValue(candidate.Word, out var lowerOrder);
            probabilities[candidate.Word] = (
                Math.Max(lowerOrder.Count, candidate.Count),
                lowerOrder.Probability + candidate.Probability * trigramWeight);
        }

        return probabilities
            .Select(candidate => new WeightedWord(candidate.Key, candidate.Value.Count, candidate.Value.Probability))
            .OrderByDescending(candidate => candidate.Probability)
            .ThenByDescending(candidate => candidate.Count)
            .Take(MaximumInterpolatedCandidates)
            .ToArray();
    }

    private static ModelData Load(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith($"{language}NgramModel.tsv.gz", StringComparison.Ordinal));
        if (resource is null) return ModelData.Empty;

        using var compressed = assembly.GetManifestResourceStream(resource);
        if (compressed is null) return ModelData.Empty;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        var correctionWords = new List<string>(MaximumCorrectionVocabulary);
        var unigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        var bigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        var trigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        var bigramCandidates = new Dictionary<string, List<WeightedWord>>(StringComparer.Ordinal);
        var trigramCandidates = new Dictionary<string, List<WeightedWord>>(StringComparer.Ordinal);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split('\t');
            if (fields.Length == 3 && fields[0] == "u" && int.TryParse(fields[2], out var unigramCount))
            {
                words[fields[1]] = fields[1];
                if (correctionWords.Count < MaximumCorrectionVocabulary)
                {
                    correctionWords.Add(fields[1]);
                }
                unigrams[fields[1]] = unigramCount;
            }
            else if (fields.Length == 4 && fields[0] == "b" && int.TryParse(fields[3], out var bigramCount))
            {
                var previous = CanonicalWord(fields[1], words);
                var next = CanonicalWord(fields[2], words);
                bigrams[CreateContextKey(previous, next)] = bigramCount;
                AddCandidate(bigramCandidates, previous, next, bigramCount);
            }
            else if (fields.Length == 5 && fields[0] == "t" && int.TryParse(fields[4], out var trigramCount))
            {
                var first = CanonicalWord(fields[1], words);
                var second = CanonicalWord(fields[2], words);
                var next = CanonicalWord(fields[3], words);
                var history = CreateContextKey(first, second);
                trigrams[CreateContextKey(history, next)] = trigramCount;
                AddCandidate(trigramCandidates, history, next, trigramCount);
            }
        }

        return new ModelData(words, correctionWords.ToArray(), unigrams, BuildPrefixCandidates(unigrams), BuildCommonWordCandidates(unigrams), bigrams, trigrams,
            Freeze(bigramCandidates), Freeze(trigramCandidates));
    }

    private static IReadOnlyList<WeightedWord> BuildCommonWordCandidates(Dictionary<string, int> unigrams)
    {
        var commonWords = unigrams
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(MaximumInterpolatedCandidates)
            .ToArray();
        var totalCount = commonWords.Sum(entry => (double)entry.Value);
        return commonWords
            .Select(entry => new WeightedWord(entry.Key, entry.Value, entry.Value / totalCount))
            .ToArray();
    }

    internal static IReadOnlyList<WeightedWord> GetPrefixCandidates(string prefix, string languageTag, int maximumResults = 3)
    {
        if (prefix.Length < 2 || maximumResults <= 0)
            return [];

        prefix = Normalize(prefix, languageTag);
        var bucketKey = prefix[..2];
        if (!GetData(languageTag).PrefixCandidates.TryGetValue(bucketKey, out var candidates)) return [];
        return candidates.Where(candidate => candidate.Word.StartsWith(prefix, StringComparison.Ordinal))
            .Take(maximumResults).ToArray();
    }

    private static Dictionary<string, IReadOnlyList<WeightedWord>> BuildPrefixCandidates(Dictionary<string, int> unigrams)
    {
        var buckets = new Dictionary<string, List<WeightedWord>>(StringComparer.Ordinal);
        foreach (var (word, count) in unigrams)
        {
            if (word.Length < 2) continue;
            var prefix = word[..2];
            if (!buckets.TryGetValue(prefix, out var candidates)) buckets[prefix] = candidates = [];
            candidates.Add(new WeightedWord(word, count));
        }

        return buckets.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<WeightedWord>)pair.Value.OrderByDescending(candidate => candidate.Count).ToArray(),
            StringComparer.Ordinal);
    }

    private static void AddCandidate(Dictionary<string, List<WeightedWord>> candidates, string history, string word, int count)
    {
        if (!candidates.TryGetValue(history, out var values)) candidates[history] = values = [];
        values.Add(new WeightedWord(word, count));
    }

    private static Dictionary<string, IReadOnlyList<WeightedWord>> Freeze(Dictionary<string, List<WeightedWord>> candidates) =>
        candidates.ToDictionary(pair => pair.Key, pair =>
        {
            var totalCount = pair.Value.Sum(entry => entry.Count);
            return (IReadOnlyList<WeightedWord>)pair.Value
                .Select(entry => entry with { Probability = (double)entry.Count / totalCount })
                .OrderByDescending(entry => entry.Count)
                .ToArray();
        }, StringComparer.Ordinal);

    private static string CanonicalWord(string value, Dictionary<string, string> words) =>
        words.TryGetValue(value, out var canonical) ? canonical : value;

    private static ModelData GetData(string languageTag) =>
        languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? TurkishData.Value : EnglishData.Value;

    private static string Normalize(string word, string languageTag)
    {
        var normalized = word.ToLower(
            languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? TurkishCulture : EnglishCulture);
        return normalized.Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("’", string.Empty, StringComparison.Ordinal);
    }

    private static string? NormalizeHistory(string history, string languageTag)
    {
        var words = history.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return null;
        return words.TakeLast(2).Select(word => Normalize(word, languageTag)).Aggregate((left, right) => CreateContextKey(left, right));
    }

    private static string CreateContextKey(string previous, string next) => $"{previous}{ContextSeparator}{next}";

    internal readonly record struct WeightedWord(string Word, int Count, double Probability = 0);

    private sealed record ModelData(
        Dictionary<string, string> Words,
        string[] CorrectionWords,
        Dictionary<string, int> Unigrams,
        Dictionary<string, IReadOnlyList<WeightedWord>> PrefixCandidates,
        IReadOnlyList<WeightedWord> CommonWordCandidates,
        Dictionary<string, int> Bigrams,
        Dictionary<string, int> Trigrams,
        Dictionary<string, IReadOnlyList<WeightedWord>> BigramCandidates,
        Dictionary<string, IReadOnlyList<WeightedWord>> TrigramCandidates)
    {
        internal static ModelData Empty { get; } = new([], [], [], [], [], [], [], [], []);
    }
}
