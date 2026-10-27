using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace WinForge.Modules.Extras;

/// <summary>
/// High-performance, self-contained, native word correction engine.
/// Completely independent of Windows COM or Windows language packs.
/// Features Damerau-Levenshtein distance, keyboard proximity weighting,
/// Turkish de-asciifier, common typo dictionaries, suffix morphology, and text expansions.
/// </summary>
internal sealed class SpellEngine
{
    private const int MaxEditDistance = 4;
    private const double MaxFuzzyScoreShort = 1.35;
    private const double MaxFuzzyScoreLong = 2.15;

    public static SpellEngine Instance { get; } = new();

    private readonly ConcurrentDictionary<string, string?> _correctionCache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable => true;

    public bool IsLanguageSupported(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return false;
        var tag = languageTag.Trim().ToLowerInvariant();
        return tag.StartsWith("tr") || tag.StartsWith("en") || tag.StartsWith("de") || tag.StartsWith("fr") || tag.StartsWith("es");
    }

    public bool IsCorrect(string word, string languageTag)
    {
        if (string.IsNullOrWhiteSpace(word)) return true;
        if (word.Any(char.IsDigit)) return true; // Numbers / version codes / identifiers are not misspelled

        var culture = GetCulture(languageTag);
        var lowerWord = word.ToLower(culture);
        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var isEnglish = languageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        // If it's a known common typo, it's NOT correct
        if (isTurkish && SpellLexicons.TurkishCommonTypos.ContainsKey(lowerWord)) return false;
        if (isTurkish && SpellLexicons.TurkishDeasciifierMap.ContainsKey(lowerWord)) return false;
        if (isEnglish && SpellLexicons.EnglishCommonTypos.ContainsKey(lowerWord)) return false;

        // Check exact vocabulary
        if (isTurkish && (SpellVocabulary.TurkishWords.Contains(lowerWord) || SpellLexicons.TurkishTypoTargets.Contains(lowerWord))) return true;
        if (isEnglish && (SpellVocabulary.EnglishWords.Contains(lowerWord) || SpellLexicons.EnglishTypoTargets.Contains(lowerWord))) return true;

        // In dual-mode or fallback, check both
        if (!isTurkish && !isEnglish)
        {
            if (SpellVocabulary.TurkishWords.Contains(lowerWord) || SpellVocabulary.EnglishWords.Contains(lowerWord))
                return true;
        }

        // Check inflection / affixes
        if (isTurkish && IsValidTurkishInflection(lowerWord)) return true;
        if (isEnglish && IsValidEnglishInflection(lowerWord)) return true;

        return false;
    }

    public string? FindCorrection(
        string word,
        string languageTag,
        bool allowDeasciify = true,
        bool allowExpansions = true,
        IReadOnlyDictionary<string, string>? customReplacements = null)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2) return null;
        if (word.Any(char.IsDigit)) return null;

        var culture = GetCulture(languageTag);
        var lowerWord = word.ToLower(culture);

        // 1. Custom user replacements / snippets (highest priority)
        if (customReplacements != null && customReplacements.TryGetValue(lowerWord, out var custom))
            return custom;

        // 2. Built-in text expansions / shortcuts (e.g. tşk -> teşekkürler, brb -> be right back)
        if (allowExpansions && SpellLexicons.DefaultTextExpansions.TryGetValue(lowerWord, out var expansion))
            return expansion;

        // Cache lookup for algorithmic corrections
        var cacheKey = $"{languageTag}:{lowerWord}:{allowDeasciify}";
        if (_correctionCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var correction = ComputeCorrection(lowerWord, languageTag, allowDeasciify, culture);
        _correctionCache[cacheKey] = correction;
        return correction;
    }

    private string? ComputeCorrection(string lowerWord, string languageTag, bool allowDeasciify, CultureInfo culture)
    {
        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var isEnglish = languageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        // 1. Common typos dictionary (O(1) exact mapping)
        if (isTurkish && SpellLexicons.TurkishCommonTypos.TryGetValue(lowerWord, out var trTypo))
            return trTypo;
        if (isEnglish && SpellLexicons.EnglishCommonTypos.TryGetValue(lowerWord, out var enTypo))
            return enTypo;

        // 2. Turkish De-asciifier (e.g. calisiyorum -> çalışıyorum, tesekkurler -> teşekkürler)
        if (isTurkish && allowDeasciify && SpellLexicons.TurkishDeasciifierMap.TryGetValue(lowerWord, out var deasciified))
            return deasciified;

        // 3. Repeated character collapse (e.g. helllo -> hello, nasılsııın -> nasılsın)
        var collapsed = CollapseRepeatedCharacters(lowerWord);
        if (collapsed != lowerWord)
        {
            if (isTurkish)
            {
                if (SpellLexicons.TurkishCommonTypos.TryGetValue(collapsed, out var colTrTypo)) return colTrTypo;
                if (SpellVocabulary.TurkishWords.Contains(collapsed) || IsValidTurkishInflection(collapsed)) return collapsed;
            }
            if (isEnglish)
            {
                if (SpellLexicons.EnglishCommonTypos.TryGetValue(collapsed, out var colEnTypo)) return colEnTypo;
                if (SpellVocabulary.EnglishWords.Contains(collapsed) || IsValidEnglishInflection(collapsed)) return collapsed;
            }
        }

        // 4. Candidate search with Keyboard-Proximity weighted Damerau-Levenshtein distance
        var vocabulary = isTurkish ? SpellVocabulary.TurkishWords : SpellVocabulary.EnglishWords;
        var candidate = FindBestCandidate(lowerWord, vocabulary, culture);

        // If in dual language mode and no candidate found in preferred language, check alternate
        if (candidate is null && isTurkish)
            candidate = FindBestCandidate(lowerWord, SpellVocabulary.EnglishWords, CultureInfo.InvariantCulture);
        else if (candidate is null && isEnglish)
            candidate = FindBestCandidate(lowerWord, SpellVocabulary.TurkishWords, GetCulture("tr-TR"));

        return candidate;
    }

    private static string? FindBestCandidate(string word, HashSet<string> vocabulary, CultureInfo culture)
    {
        string? bestCandidate = null;
        var lowestScore = double.MaxValue;
        var maxAllowedScore = word.Length <= 4 ? MaxFuzzyScoreShort : MaxFuzzyScoreLong;
        var wordFirstChar = word[0];

        foreach (var candidate in vocabulary)
        {
            // Length filter: must be within +/- 2 characters
            var lengthDiff = Math.Abs(word.Length - candidate.Length);
            if (lengthDiff > 2) continue;

            // First character filter: same character or adjacent keyboard character
            var candFirstChar = candidate[0];
            if (candFirstChar != wordFirstChar && SpellLexicons.GetKeyDistance(wordFirstChar, candFirstChar) > 1.6)
                continue;

            // Calculate weighted distance
            var distance = CalculateWeightedDamerauLevenshtein(word, candidate);
            if (distance > maxAllowedScore) continue;

            // Score candidate (lower is better)
            var score = distance;

            // Bonuses
            if (wordFirstChar == candFirstChar) score -= 0.25;
            if (word[^1] == candidate[^1]) score -= 0.15;
            if (IsSubsequence(word, candidate)) score -= 0.20;

            if (score < lowestScore)
            {
                lowestScore = score;
                bestCandidate = candidate;
            }
        }

        return lowestScore <= maxAllowedScore ? bestCandidate : null;
    }

    internal static double CalculateWeightedDamerauLevenshtein(string source, string target)
    {
        var n = source.Length;
        var m = target.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var d = new double[n + 1, m + 1];

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var sChar = source[i - 1];
                var tChar = target[j - 1];

                double cost;
                if (sChar == tChar)
                {
                    cost = 0;
                }
                else
                {
                    var keyDist = SpellLexicons.GetKeyDistance(sChar, tChar);
                    cost = keyDist <= 1.5 ? 0.65 : 1.25;
                }

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1.0,        // Deletion
                             d[i, j - 1] + 1.0),       // Insertion
                    d[i - 1, j - 1] + cost);           // Substitution

                // Transposition
                if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 0.75);
                }
            }
        }

        return d[n, m];
    }

    internal static int CalculateEditDistance(string source, string target)
    {
        var previous = Enumerable.Range(0, target.Length + 1).ToArray();
        for (var row = 1; row <= source.Length; row++)
        {
            var current = new int[target.Length + 1];
            current[0] = row;
            for (var column = 1; column <= target.Length; column++)
            {
                var cost = source[row - 1] == target[column - 1] ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
            }
            previous = current;
        }
        return previous[target.Length];
    }

    internal static bool IsSafeCandidate(string word, string? candidate, string languageTag)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (candidate.Any(c => !char.IsLetter(c) && c != '\'' && c != ' ' && c != '-')) return false;

        var culture = GetCulture(languageTag);
        var distance = CalculateEditDistance(word.ToLower(culture), candidate.ToLower(culture));
        return distance > 0 && distance <= MaxEditDistance;
    }

    private static string CollapseRepeatedCharacters(string text)
    {
        if (text.Length < 3) return text;
        var sb = new StringBuilder(text.Length);
        var repeatCount = 1;
        sb.Append(text[0]);

        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == text[i - 1])
            {
                repeatCount++;
                if (repeatCount <= 2) sb.Append(text[i]);
            }
            else
            {
                repeatCount = 1;
                sb.Append(text[i]);
            }
        }
        return sb.ToString();
    }

    private static bool IsSubsequence(string sub, string full)
    {
        var subIndex = 0;
        var fullIndex = 0;
        while (subIndex < sub.Length && fullIndex < full.Length)
        {
            if (sub[subIndex] == full[fullIndex]) subIndex++;
            fullIndex++;
        }
        return subIndex == sub.Length;
    }

    private static bool IsValidTurkishInflection(string word)
    {
        if (word.Length < 4) return false;

        foreach (var suffix in SpellLexicons.TurkishSuffixes)
        {
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var stem = word[..^suffix.Length];
                if (stem.Length >= 2 && SpellVocabulary.TurkishWords.Contains(stem))
                {
                    // Check Turkish vowel harmony
                    if (HasTurkishVowelHarmony(stem, suffix)) return true;
                }
            }
        }
        return false;
    }

    private static bool HasTurkishVowelHarmony(string stem, string suffix)
    {
        var lastStemVowel = GetLastVowel(stem);
        var firstSuffixVowel = GetFirstVowel(suffix);
        if (lastStemVowel == '\0' || firstSuffixVowel == '\0') return true;

        var isBackStem = IsBackVowel(lastStemVowel);
        var isBackSuffix = IsBackVowel(firstSuffixVowel);
        return isBackStem == isBackSuffix;
    }

    private static char GetLastVowel(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var c = char.ToLowerInvariant(text[i]);
            if (IsVowel(c)) return c;
        }
        return '\0';
    }

    private static char GetFirstVowel(string text)
    {
        foreach (var t in text)
        {
            var c = char.ToLowerInvariant(t);
            if (IsVowel(c)) return c;
        }
        return '\0';
    }

    private static bool IsVowel(char c) =>
        c is 'a' or 'e' or 'ı' or 'i' or 'o' or 'ö' or 'u' or 'ü';

    private static bool IsBackVowel(char c) =>
        c is 'a' or 'ı' or 'o' or 'u';

    private static bool IsValidEnglishInflection(string word)
    {
        if (word.Length < 4) return false;

        foreach (var suffix in SpellLexicons.EnglishSuffixes)
        {
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var stem = word[..^suffix.Length];
                if (stem.Length >= 2 && SpellVocabulary.EnglishWords.Contains(stem))
                    return true;
                // English 'e' drop (e.g. make -> making, care -> caring)
                if (stem.Length >= 2 && SpellVocabulary.EnglishWords.Contains(stem + "e"))
                    return true;
            }
        }
        return false;
    }

    internal static CultureInfo GetCulture(string languageTag)
    {
        try { return CultureInfo.GetCultureInfo(languageTag); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}
