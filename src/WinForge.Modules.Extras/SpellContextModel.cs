namespace WinForge.Modules.Extras;

/// <summary>
/// N-gram and bigram context model for disambiguating candidates and correcting
/// context-dependent typos (e.g. 'her ver' -> 'her yer', 'in teh' -> 'in the').
/// </summary>
internal static class SpellContextModel
{
    private static readonly Dictionary<string, HashSet<string>> TurkishBigrams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["her"] = ["yer", "yerde", "yerden", "yere", "şey", "şeyi", "şeyin", "zaman", "gün", "biri", "birisi", "an", "halde", "türlü", "sefer", "daim", "kes", "birlikte"],
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
        ["teşekkür"] = ["ederim", "ederiz", "ler", "lerimi"],
        ["rica"] = ["ederim", "ederiz"],
        ["kolay"] = ["gelsin", "oldu", "olacak", "değil", "bir"],
        ["geçmiş"] = ["olsun", "zaman", "yıllar"],
        ["hoş"] = ["geldiniz", "bulduk", "çakalın", "geldin", "sohbet", "vakit"],
        ["kendine"] = ["iyi", "bak", "güven", "dikkat", "has"],
        ["gün"] = ["geçtikçe", "içinde", "boyunca", "aydın", "sonunda"],
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
        ["yok"] = ["mu", "musun", "musunuz", "muydu"],
        ["yer"] = ["vermek", "almak"]
    };

    private static readonly Dictionary<string, HashSet<string>> EnglishBigrams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["in"] = ["the", "a", "an", "order", "front", "fact", "case", "addition", "terms", "spite", "general", "time"],
        ["to"] = ["the", "be", "do", "have", "make", "get", "go", "see", "take", "find", "use", "know", "work"],
        ["of"] = ["the", "course", "all", "them", "these", "those", "our", "its", "their", "this"],
        ["at"] = ["the", "all", "least", "first", "last", "once", "home", "night", "work", "school"],
        ["on"] = ["the", "time", "top", "board", "demand", "demand", "demand", "it", "this", "that"],
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
    internal static double GetBigramBonus(string? previousWord, string candidate, string languageTag)
    {
        if (string.IsNullOrWhiteSpace(previousWord) || string.IsNullOrWhiteSpace(candidate))
            return 0.0;

        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var bigrams = isTurkish ? TurkishBigrams : EnglishBigrams;

        if (bigrams.TryGetValue(previousWord, out var expectedNextWords))
        {
            if (expectedNextWords.Contains(candidate))
                return 0.45; // Substantial boost for matching expected collocation
        }

        return 0.0;
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

        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var bigrams = isTurkish ? TurkishBigrams : EnglishBigrams;

        if (!bigrams.TryGetValue(previousWord, out var expectedNextWords))
            return null;

        // If the typed word is already the expected word, nothing to correct
        if (expectedNextWords.Contains(typedWord))
            return null;

        // Search among expected words for an edit-distance 1 typo
        string? bestMatch = null;
        var lowestDist = double.MaxValue;

        foreach (var expected in expectedNextWords)
        {
            var lengthDiff = Math.Abs(typedWord.Length - expected.Length);
            if (lengthDiff > 1) continue;

            var dist = SpellEngine.CalculateWeightedDamerauLevenshtein(typedWord, expected);
            if (dist <= 1.35 && dist < lowestDist)
            {
                lowestDist = dist;
                bestMatch = expected;
            }
        }

        return bestMatch;
    }
}

