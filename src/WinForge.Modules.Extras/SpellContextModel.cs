namespace WinForge.Modules.Extras;

/// <summary>
/// N-gram and bigram context model for disambiguating candidates and correcting
/// context-dependent typos (e.g. 'her ver' -> 'her yer', 'in teh' -> 'in the').
/// </summary>
internal static class SpellContextModel
{
    private const double BigramBonus = 0.45;
    private const double TrigramBonus = 0.85;
    private const double MaximumContextDistance = 1.35;
    private const double MinimumCandidateMargin = 0.30;
    private const double GeneralModelWeight = 0.55;
    private const double ConversationModelWeight = 0.45;
    private const int MaximumPersonalizedPredictions = 12;

    // Curated phrases provide stronger evidence than a single preceding word.
    private static readonly Dictionary<string, HashSet<string>> TurkishTrigrams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["her şey"] = ["yolunda", "güzel"],
        ["bir süre"] = ["sonra", "önce"],
        ["ne yazık"] = ["ki"],
        ["iyi ki"] = ["varsın", "geldin"],
        ["en kısa"] = ["zamanda"],
        ["çok teşekkür"] = ["ederim", "ederiz"],
        ["kendine iyi"] = ["bak"],
        ["bu yüzden"] = ["de"]
    };

    private static readonly Dictionary<string, HashSet<string>> EnglishTrigrams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["in order"] = ["to"],
        ["as soon"] = ["as"],
        ["as well"] = ["as"],
        ["a lot"] = ["of"],
        ["thank you"] = ["very"],
        ["looking forward"] = ["to"],
        ["at the"] = ["moment", "same", "end"],
        ["for the"] = ["first", "same"]
    };

    private static readonly Dictionary<string, Dictionary<string, string>> TurkishRealWordConfusions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["her"] = new(StringComparer.OrdinalIgnoreCase) { ["ver"] = "yer" },
        ["seviyesine"] = new(StringComparer.OrdinalIgnoreCase) { ["kayın"] = "yakın" },
        ["mı"] = new(StringComparer.OrdinalIgnoreCase) { ["şua"] = "şu an" }
    };

    private static readonly Dictionary<string, HashSet<string>> TurkishBigrams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["her"] = ["yer", "yerde", "yerden", "yere", "şey", "şeyi", "şeyin", "zaman", "gün", "biri", "birisi", "an", "halde", "türlü", "sefer", "daim"],
        ["bir"] = ["şey", "şeyi", "sürü", "sürüsü", "arada", "kere", "an", "nebze", "parça", "kez", "türlü", "tane", "gün", "zaman", "aralık", "anda", "biri", "kaçı", "taraftan", "yandan"],
        ["hiç"] = ["bir", "biri", "kimse", "fark", "zaman", "olmazsa", "değil", "biriyle", "birisi", "yok"],
        ["çok"] = ["iyi", "güzel", "fazla", "az", "kolay", "zor", "önemli", "hızlı", "teşekkürler", "büyük", "küçük", "yakın", "uzak", "geç", "erken", "net"],
        ["daha"] = ["iyi", "fazla", "çok", "az", "önce", "sonra", "kolay", "hızlı", "net", "güzel", "büyük", "yüksek", "rahat"],
        ["en"] = ["iyi", "çok", "büyük", "güzel", "hızlı", "önemli", "kolay", "yüksek", "son", "erken", "başta", "az"],
        ["bu"] = ["metinde", "arada", "şekilde", "yüzden", "nedenle", "sefer", "konuda", "durumda", "kadar", "akşam", "sabah", "yıl", "ay", "gün"],
        ["o"] = ["zaman", "kadar", "gün", "yüzden", "halde", "şekilde", "arada"],
        ["şu"] = ["anda", "şekilde", "kadar", "sıralar"],
        ["ne"] = ["zaman", "kadar", "oldu", "var", "yok", "güzel", "yapıyorsun", "haber", "dersin"],
        ["nasıl"] = ["bir", "oldu", "yapılır", "olur", "gidiyor", "yardımcı", "çalışır"],
        ["kesinlikle"] = ["katılıyorum", "doğru", "katılmıyorum", "haklısın", "yapma", "olmaz", "gerekiyor"],
        ["gerçekten"] = ["çok", "güzel", "iyi", "harika", "doğru", "öyle", "önemli"],
        ["teşekkür"] = ["ederim", "ederiz"],
        ["rica"] = ["ederim", "ederiz"],
        ["kolay"] = ["gelsin", "oldu", "olacak", "değil", "bir"],
        ["geçmiş"] = ["olsun", "zaman", "yıllar"],
        ["hoş"] = ["geldiniz", "bulduk", "geldin", "sohbet", "vakit"],
        ["kendine"] = ["iyi", "bak", "güven", "dikkat"],
        ["gün"] = ["geçtikçe", "içinde", "boyunca", "sonunda"],
        ["seviyesine"] = ["yakın", "göre", "kadar", "ulaştı", "çıktı"],
        ["önemli"] = ["olan", "bir", "ölçüde", "derecede", "nokta"],
        ["fark"] = ["etmez", "etti", "etmedi", "eder", "etmiyor", "var"],
        ["terk"] = ["etti", "etmek", "etmedi", "etmiş"],
        ["yardım"] = ["etmek", "etti", "et", "edin", "ediyor", "almak"],
        ["kabul"] = ["etmek", "etti", "et", "edildi", "ediyorum", "gördü"],
        ["iptal"] = ["etmek", "etti", "et", "edildi", "edilsin"],
        ["devam"] = ["etmek", "etti", "et", "ediyor", "edecek"],
        ["tahmin"] = ["etmek", "etti", "et", "ediyorum", "edilemez"],
        ["yer"] = ["almak", "alıyor", "aldı", "alacak", "vermek", "verdi"],
        ["var"] = ["mı", "mısın", "mısınız", "mıydı"],
        ["yok"] = ["mu", "musun", "musunuz", "muydu"]
    };

    private static readonly Dictionary<string, HashSet<string>> EnglishBigrams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["in"] = ["the", "a", "an", "order", "front", "fact", "case", "addition", "terms", "spite", "general", "time"],
        ["to"] = ["the", "be", "do", "have", "make", "get", "go", "see", "take", "find", "use", "know", "work"],
        ["of"] = ["the", "course", "all", "them", "these", "those", "our", "its", "their", "this"],
        ["at"] = ["the", "all", "least", "first", "last", "once", "home", "night", "work", "school"],
        ["on"] = ["the", "time", "top", "board", "demand", "it", "this", "that"],
        ["for"] = ["example", "the", "instance", "sure", "now", "you", "them", "us", "this", "that"],
        ["with"] = ["the", "regard", "respect", "you", "them", "us", "all", "each", "care"],
        ["you"] = ["are", "can", "will", "have", "know", "should", "could", "would", "do", "get", "see"],
        ["they"] = ["are", "were", "have", "had", "will", "can", "would", "could", "should", "do"],
        ["we"] = ["are", "can", "will", "have", "need", "should", "could", "would", "do", "know"],
        ["i"] = ["am", "have", "will", "think", "can", "know", "would", "could", "should", "want", "like"],
        ["thank"] = ["you", "very"],
        ["very"] = ["much", "good", "well", "nice", "important", "fast", "happy", "big", "small"],
        ["as"] = ["well", "soon", "far", "long", "much", "many", "usual", "if", "though"],
        ["have"] = ["been", "to", "a", "done", "seen", "made", "had", "come", "gone"],
        ["has"] = ["been", "to", "a", "come", "done", "made", "seen"],
        ["would"] = ["like", "be", "have", "rather", "prefer"]
    };

    /// <summary>
    /// Returns a contextual bonus score (subtracted from fuzzy candidate distance).
    /// </summary>
    internal static double GetBigramBonus(
        string? previousWord,
        string candidate,
        string languageTag,
        UserFrequencyTracker? frequencyTracker = null,
        bool includePersonalized = true)
    {
        if (string.IsNullOrWhiteSpace(previousWord) || string.IsNullOrWhiteSpace(candidate))
            return 0.0;

        var (bigrams, trigrams) = GetModels(languageTag);
        var history = NormalizeHistory(previousWord, languageTag);
        if (trigrams.TryGetValue(history, out var phraseWords) && phraseWords.Contains(candidate))
            return TrigramBonus;
        var curatedBonus = bigrams.TryGetValue(LastWord(history), out var words) && words.Contains(candidate) ? BigramBonus : 0;
        var learnedBonus = includePersonalized
            ? Math.Max(
                (frequencyTracker ?? UserFrequencyTracker.Instance).GetBigramFrequencyBonus(LastWord(history), candidate),
                (frequencyTracker ?? UserFrequencyTracker.Instance).GetTrigramFrequencyBonus(history, candidate))
            : 0;
        var corpusBonus = SpellNgramModel.GetBigramBonus(LastWord(history), candidate, languageTag);
        var corpusTrigramBonus = SpellNgramModel.GetTrigramBonus(history, candidate, languageTag);
        var conversationBonus = SpellNgramModel.GetConversationContextBonus(history, candidate, languageTag);
        return Math.Max(Math.Max(curatedBonus, learnedBonus),
            Math.Max(Math.Max(corpusBonus, corpusTrigramBonus), conversationBonus));
    }

    /// <summary>
    /// Detects context typos where a typed word is technically a valid word or close typo,
    /// but in the context of the previous word it represents a clear, high-probability collocation error
    /// (e.g. previous='her', typed='ver' -> 'yer'; previous='in', typed='teh' -> 'the').
    /// </summary>
    internal static string? TryFindContextCorrection(string? previousWord, string typedWord, string languageTag)
    {
        if (string.IsNullOrWhiteSpace(previousWord) || string.IsNullOrWhiteSpace(typedWord))
            return null;

        if (!SpellEngine.Instance.IsLanguageSupported(languageTag)) return null;
        var culture = SpellEngine.GetCulture(languageTag);
        var history = NormalizeHistory(previousWord, languageTag);
        typedWord = typedWord.ToLower(culture);
        var (bigrams, trigrams) = GetModels(languageTag);
        var curatedCandidates = trigrams.TryGetValue(history, out var trigramWords)
            ? trigramWords
            : bigrams.TryGetValue(LastWord(history), out var bigramWords) ? bigramWords : [];
        var corpusCandidates = SpellNgramModel.GetNextWordCandidates(history, languageTag);
        var conversationCandidates = SpellNgramModel.HasConversationContext(history, languageTag)
            ? SpellNgramModel.GetConversationNextWordCandidates(history, languageTag)
            : [];
        var corpusCandidateCounts = corpusCandidates.Concat(conversationCandidates)
            .GroupBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(candidate => candidate.Count), StringComparer.OrdinalIgnoreCase);
        var candidates = curatedCandidates
            .Concat(corpusCandidates.Select(candidate => candidate.Word))
            .Concat(conversationCandidates.Select(candidate => candidate.Word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0) return null;

        // If the typed word is already the expected word, nothing to correct
        if (candidates.Contains(typedWord, StringComparer.OrdinalIgnoreCase))
            return null;

        // Preserve known words when they are plausible continuations in the model.
        // Explicit curated collocations may still resolve a close real-word typo.
        var isKnownWord = SpellEngine.Instance.IsKnownWord(typedWord, languageTag, false);
        if (languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase) &&
            TurkishRealWordConfusions.TryGetValue(LastWord(history), out var confusions) &&
            confusions.TryGetValue(typedWord, out var correction))
            return correction;

        if (isKnownWord)
            return null;

        var curatedMatches = curatedCandidates
            .Select(candidate => (Candidate: candidate, Distance: CalculateTypedDistance(typedWord, candidate)))
            .Where(match => match.Distance <= MaximumContextDistance)
            .OrderBy(match => match.Distance)
            .Take(2)
            .ToArray();
        if (curatedMatches.Length == 1 ||
            (curatedMatches.Length > 1 && curatedMatches[1].Distance - curatedMatches[0].Distance >= MinimumCandidateMargin))
            return curatedMatches[0].Candidate;

        // Search among expected words for an edit-distance 1 typo
        string? bestMatch = null;
        var lowestDist = double.MaxValue;
        var runnerUpDistance = double.MaxValue;

        foreach (var expected in candidates)
        {
            var lengthDiff = Math.Abs(typedWord.Length - expected.Length);
            if (lengthDiff > 1) continue;

            var dist = SpellEngine.CalculateWeightedDamerauLevenshtein(typedWord, expected);
            var count = corpusCandidateCounts.TryGetValue(expected, out var candidateCount) ? candidateCount : 0;
            var score = dist - (count > 0 ? Math.Min(0.4, Math.Log10(count) * 0.18) : 0);
            if (score < lowestDist)
            {
                runnerUpDistance = lowestDist;
                lowestDist = score;
                bestMatch = expected;
            }
            else if (score < runnerUpDistance) runnerUpDistance = score;
        }

        return bestMatch is not null && CalculateTypedDistance(typedWord, bestMatch) <= MaximumContextDistance &&
               runnerUpDistance - lowestDist >= MinimumCandidateMargin
            ? bestMatch : null;
    }

    internal static IReadOnlyList<string> GetNextWordSuggestions(
        string previousWords,
        string prefix,
        string languageTag,
        int maximumResults = 3) =>
        GetNextWordSuggestions(previousWords, prefix, languageTag, maximumResults, null, false);

    internal static IReadOnlyList<string> GetNextWordSuggestions(
        string previousWords,
        string prefix,
        string languageTag,
        int maximumResults,
        UserFrequencyTracker? frequencyTracker,
        bool includePersonalized)
    {
        if (string.IsNullOrWhiteSpace(previousWords) || maximumResults <= 0 ||
            !SpellEngine.Instance.IsLanguageSupported(languageTag))
            return [];

        var culture = SpellEngine.GetCulture(languageTag);
        prefix = prefix.ToLower(culture);
        var (bigrams, trigrams) = GetModels(languageTag);
        var history = NormalizeHistory(previousWords, languageTag);
        var curatedCandidates = trigrams.TryGetValue(history, out var trigramWords)
            ? trigramWords
            : bigrams.TryGetValue(LastWord(history), out var bigramWords) ? bigramWords : [];
        var corpusCandidates = SpellNgramModel.GetNextWordCandidates(history, languageTag);
        var conversationCandidates = SpellNgramModel.GetConversationNextWordCandidates(history, languageTag);
        var personalizedCandidates = includePersonalized && frequencyTracker is not null
            ? frequencyTracker.GetNextWords(history, MaximumPersonalizedPredictions)
            : [];

        return corpusCandidates.Select(candidate =>
                (candidate.Word, candidate.Count, Score: candidate.Probability * GeneralModelWeight))
            .Concat(conversationCandidates.Select(candidate =>
                (candidate.Word, candidate.Count, Score: candidate.Probability * ConversationModelWeight)))
            .Concat(curatedCandidates.Select(candidate => (Word: candidate, Count: 0, Score: 0d)))
            .Concat(personalizedCandidates.Select(candidate =>
                (candidate.Word, candidate.Count, Score: Math.Max(
                    frequencyTracker!.GetBigramFrequencyBonus(LastWord(history), candidate.Word),
                    frequencyTracker.GetTrigramFrequencyBonus(history, candidate.Word)))))
            .Where(candidate => candidate.Word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Word: group.Key,
                Count: group.Max(candidate => candidate.Count),
                Score: group.Sum(candidate => candidate.Score)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Count)
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .Select(candidate => candidate.Word)
            .ToArray();
    }

    private static double CalculateTypedDistance(string typedWord, string expected) =>
        SpellEngine.CalculateWeightedDamerauLevenshtein(typedWord, expected);

    private static string NormalizeHistory(string history, string languageTag) =>
        string.Join(' ', history.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(2))
            .ToLower(SpellEngine.GetCulture(languageTag));

    private static string LastWord(string history) => history[(history.LastIndexOf(' ') + 1)..];

    private static (Dictionary<string, HashSet<string>> Bigrams, Dictionary<string, HashSet<string>> Trigrams) GetModels(string languageTag) =>
        languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            ? (TurkishBigrams, TurkishTrigrams) : (EnglishBigrams, EnglishTrigrams);
}

