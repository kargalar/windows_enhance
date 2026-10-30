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
    private const double MaxTurkishSuggestionScoreLong = 2.5;
    private const double MinimumCandidateMargin = 0.30;
    private const double MinimumTurkishCandidateMargin = 0.40;
    private const double MinimumTurkishFuzzyCandidateMargin = 0.30;
    private const double MinimumTurkishLongTailCandidateMargin = 0.50;
    private const double MaximumKnownTurkishCandidateScore = 0.0;
    private const double MaximumStrongTwoDiacriticCandidateScore = -0.2;
    private const double MaximumUniqueLongDiacriticCandidateScore = 0.05;
    private const double MaximumUniqueMultipleDiacriticCandidateScore = 1.2;
    private const double MinimumTwoDiacriticCandidateMargin = 0.7;
    private const double MinimumKnownTurkishFrequencyAdvantage = 0.12;
    private const double MinimumKnownWordFrequencyLead = 0.05;
    private const double MinimumRepeatedCharacterFrequencyAdvantage = 0.12;
    private const int MaximumKnownTurkishDiacriticPositions = 2;
    private const int MaximumExhaustiveTurkishDiacriticPositions = 6;
    private const int MinimumUniqueLongDiacriticWordLength = 6;
    private const double AdjacentTranspositionBonus = 0.15;
    private const int MaximumCachedCorrections = 2048;
    private const int MaximumWordLength = 40;
    private const int MaximumShortWordEditDistance = 2;
    private const int MaximumFuzzyCharacterEdits = 3;
    private const int MaximumEnglishEditDistance = 3;
    private const int MaximumTurkishEditDistance = MaximumTurkishDeasciifyPositions;
    private const int MaximumTurkishDeasciifyPositions = 9;
    private const int MaximumTurkishMorphologyCandidateCacheEntries = 8_192;
    private const double TurkishDiacriticSubstitutionCost = 0.25;
    private const double KeyboardSubstitutionBaseCost = 0.25;
    private const double KeyboardSubstitutionDistanceScale = 0.42;
    private const double MaximumKeyboardSubstitutionCost = 1.25;
    private const string EnglishLanguageTag = "en-US";
    private static readonly IReadOnlyDictionary<char, char> TurkishDiacriticVariants = new Dictionary<char, char>
    {
        ['c'] = 'ç', ['ç'] = 'c',
        ['g'] = 'ğ', ['ğ'] = 'g',
        ['i'] = 'ı', ['ı'] = 'i',
        ['o'] = 'ö', ['ö'] = 'o',
        ['s'] = 'ş', ['ş'] = 's',
        ['u'] = 'ü', ['ü'] = 'u'
    };
    private static readonly ConcurrentDictionary<string, bool> TurkishMorphologyCandidateCache = new(StringComparer.Ordinal);
    private static readonly Lazy<HashSet<string>> TurkishCandidateVocabulary = new(() =>
    {
        var words = new HashSet<string>(SpellVocabulary.TurkishWords, StringComparer.Ordinal);
        words.UnionWith(SpellNgramModel.TurkishVocabulary);
        words.UnionWith(SpellLexicons.TurkishTypoTargets);
        return words;
    });
    private static readonly Lazy<HashSet<string>> TurkishLongTailCandidateVocabulary = new(() =>
    {
        var words = new HashSet<string>(SpellNgramModel.TurkishCandidateWords, StringComparer.Ordinal);
        words.ExceptWith(TurkishCandidateVocabulary.Value);
        return words;
    });
    private static readonly Lazy<Dictionary<int, Dictionary<char, string[]>>> TurkishCandidatesByLengthAndFirstCharacter =
        new(() => IndexByLengthAndCharacter(TurkishCandidateVocabulary.Value, 0));
    private static readonly Lazy<Dictionary<int, Dictionary<char, string[]>>> TurkishCandidatesByLengthAndSecondCharacter =
        new(() => IndexByLengthAndCharacter(TurkishCandidateVocabulary.Value, 1));
    private static readonly Lazy<Dictionary<int, Dictionary<char, string[]>>> TurkishLongTailCandidatesByLengthAndFirstCharacter =
        new(() => IndexByLengthAndCharacter(TurkishLongTailCandidateVocabulary.Value, 0));
    private static readonly Lazy<Dictionary<int, Dictionary<char, string[]>>> TurkishLongTailCandidatesByLengthAndSecondCharacter =
        new(() => IndexByLengthAndCharacter(TurkishLongTailCandidateVocabulary.Value, 1));
    private static readonly Lazy<HashSet<string>> EnglishCandidateVocabulary = new(() =>
    {
        var words = new HashSet<string>(SpellVocabulary.EnglishWords, StringComparer.OrdinalIgnoreCase);
        words.UnionWith(SpellNgramModel.EnglishVocabulary);
        words.ExceptWith(SpellLexicons.EnglishCommonTypos.Keys);
        return words;
    });
    private static readonly Lazy<Dictionary<int, Dictionary<char, string[]>>> EnglishCandidatesByLengthAndFirstCharacter =
        new(() => IndexByLengthAndCharacter(EnglishCandidateVocabulary.Value, 0));
    private static readonly Lazy<Dictionary<int, Dictionary<char, string[]>>> EnglishCandidatesByLengthAndSecondCharacter =
        new(() => IndexByLengthAndCharacter(EnglishCandidateVocabulary.Value, 1));

    public static SpellEngine Instance { get; } = new();

    private readonly ConcurrentDictionary<string, string?> _correctionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly UserFrequencyTracker _frequencyTracker;

    public SpellEngine() : this(UserFrequencyTracker.Instance) { }

    internal SpellEngine(UserFrequencyTracker frequencyTracker) => _frequencyTracker = frequencyTracker;

    public bool IsAvailable => true;

    public bool IsLanguageSupported(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return false;
        var tag = languageTag.Trim().ToLowerInvariant();
        return tag is "tr" or "en" || tag.StartsWith("tr-") || tag.StartsWith("en-");
    }

    public bool IsCorrect(string word, string languageTag) => IsKnownWord(word, languageTag, true);

    internal bool IsKnownWord(string word, string languageTag, bool allowLearning)
    {
        if (string.IsNullOrWhiteSpace(word)) return true;
        word = word.Normalize(NormalizationForm.FormC);
        if (word.Any(char.IsDigit)) return true; // Numbers / version codes / identifiers are not misspelled

        var culture = GetCulture(languageTag);
        var lowerWord = word.ToLower(culture);
        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var isEnglish = languageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        if (allowLearning && _frequencyTracker.IsLearned(lowerWord)) return true;

        // If it's a known common typo, it's NOT correct
        if (isTurkish && SpellLexicons.TurkishCommonTypos.ContainsKey(lowerWord)) return false;
        if (isEnglish && SpellLexicons.EnglishCommonTypos.ContainsKey(lowerWord)) return false;

        // Check exact vocabulary
        if (isTurkish && (TurkishCandidateVocabulary.Value.Contains(lowerWord) || SpellNgramModel.ContainsTurkishWord(lowerWord))) return true;
        if (isEnglish && (EnglishCandidateVocabulary.Value.Contains(lowerWord) || SpellNgramModel.ContainsWord(lowerWord, languageTag) ||
                          SpellLexicons.EnglishTypoTargets.Contains(lowerWord))) return true;
        if (isEnglish && SpellErrorModel.TryGetCorrection(lowerWord, out _)) return false;

        if (isTurkish && TurkishMorphologyService.IsKnownWord(lowerWord)) return true;

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
        bool allowDeasciify,
        bool allowExpansions,
        IReadOnlyDictionary<string, string>? customReplacements = null) =>
        FindCorrection(word, languageTag, null, true, allowDeasciify, allowExpansions, true, customReplacements);

    public string? FindCorrection(string word, string languageTag) =>
        FindCorrection(word, languageTag, null, true, true, true, true, null);

    internal IReadOnlyList<string> GetSuggestions(
        string word,
        string languageTag,
        string? previousWords,
        bool allowContext,
        bool allowDeasciify,
        bool allowExpansions,
        bool allowLearning,
        IReadOnlyDictionary<string, string>? customReplacements = null,
        int maximumResults = 3)
    {
        if (string.IsNullOrWhiteSpace(word) || maximumResults <= 0 || !IsLanguageSupported(languageTag)) return [];

        word = word.Normalize(NormalizationForm.FormC);
        previousWords = previousWords?.Normalize(NormalizationForm.FormC);
        var culture = GetCulture(languageTag);
        var suggestions = new List<string>(maximumResults);
        var correction = FindCorrection(word, languageTag, previousWords, allowContext, allowDeasciify,
            allowExpansions, allowLearning, customReplacements);
        AddSuggestion(correction);

        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var isKnownWord = IsKnownWord(word, languageTag, allowLearning);
        if (word.Length >= 3 && word.All(char.IsLetter) && isTurkish && isKnownWord)
        {
            var knownWordCandidates = GetTurkishDiacriticAlternatives(word.ToLower(culture));
            var rankedKnownWordCorrections = GetRankedCandidates(word.ToLower(culture), knownWordCandidates,
                string.IsNullOrWhiteSpace(previousWords) ? null : previousWords.ToLower(culture),
                allowContext, allowLearning, languageTag, maximumResults);
            foreach (var candidate in rankedKnownWordCorrections) AddSuggestion(candidate.Word);
        }

        // Keep a likely spelling correction visible even when prefix completions
        // would otherwise fill the entire strip for an unknown word.
        var rankedCorrections = word.Length >= 3 && word.All(char.IsLetter) && !isKnownWord
            ? GetRankedCandidates(word.ToLower(culture), GetCandidates(word.ToLower(culture), isTurkish),
                string.IsNullOrWhiteSpace(previousWords) ? null : previousWords.ToLower(culture),
                allowContext, allowLearning, languageTag, maximumResults)
            : [];
        if (rankedCorrections.Count > 0) AddSuggestion(rankedCorrections[0].Word);

        if (allowContext && !string.IsNullOrWhiteSpace(previousWords))
        {
            foreach (var suggestion in SpellContextModel.GetNextWordSuggestions(previousWords, word, languageTag, maximumResults))
                AddSuggestion(suggestion);
        }

        if (languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var suggestion in SpellNgramModel.GetPrefixCandidates(word, languageTag, maximumResults))
                AddSuggestion(suggestion.Word);
        }
        else if (languageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var suggestion in SpellNgramModel.GetPrefixCandidates(word, languageTag, maximumResults))
                AddSuggestion(suggestion.Word);
            foreach (var suggestion in SpellVocabulary.GetEnglishPrefixCandidates(word, maximumResults))
                AddSuggestion(suggestion);
        }

        if (word.Length >= 3 && word.All(char.IsLetter) && !isKnownWord)
        {
            var lowerWord = word.ToLower(culture);
            foreach (var candidate in rankedCorrections) AddSuggestion(candidate.Word);

            if (isTurkish && suggestions.Count < maximumResults)
            {
                var longTailCandidates = CollectCompatibleCandidates(
                    lowerWord, 2, isTurkish: true, useTurkishLongTail: true);
                var rankedLongTailCandidates = GetRankedCandidates(word.ToLower(culture),
                    longTailCandidates,
                    string.IsNullOrWhiteSpace(previousWords) ? null : previousWords.ToLower(culture),
                    allowContext, allowLearning, languageTag, maximumResults - suggestions.Count);
                foreach (var candidate in rankedLongTailCandidates) AddSuggestion(candidate.Word);
            }
        }

        return suggestions;

        void AddSuggestion(string? suggestion)
        {
            if (string.IsNullOrWhiteSpace(suggestion) || StringComparer.OrdinalIgnoreCase.Equals(suggestion, word) ||
                suggestions.Contains(suggestion, StringComparer.OrdinalIgnoreCase)) return;
            var normalizedSuggestion = suggestion.ToLower(culture);
            var commonTypos = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
                ? SpellLexicons.TurkishCommonTypos
                : SpellLexicons.EnglishCommonTypos;
            if (commonTypos.ContainsKey(normalizedSuggestion)) return;
            if (allowLearning && _frequencyTracker.IsCorrectionRejected(word, normalizedSuggestion, languageTag)) return;
            if (suggestions.Count < maximumResults) suggestions.Add(suggestion);
        }
    }

    public string? FindCorrection(
        string word,
        string languageTag,
        string? previousWord = null,
        bool allowContext = true,
        bool allowDeasciify = true,
        bool allowExpansions = true,
        bool allowLearning = true,
        IReadOnlyDictionary<string, string>? customReplacements = null)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2 || word.Length > MaximumWordLength) return null;
        word = word.Normalize(NormalizationForm.FormC);
        previousWord = previousWord?.Normalize(NormalizationForm.FormC);

        var culture = GetCulture(languageTag);
        var lowerWord = word.ToLower(culture);
        var lowerPrevWord = string.IsNullOrWhiteSpace(previousWord) ? null : previousWord.ToLower(culture).Trim();

        // 1. Custom user replacements / snippets (highest priority)
        if (customReplacements != null && customReplacements.TryGetValue(lowerWord, out var custom))
            return custom;

        if (!IsLanguageSupported(languageTag) || word.Any(c => !char.IsLetter(c))) return null;
        if (allowLearning && _frequencyTracker.IsLearned(lowerWord)) return null;

        // 2. Built-in text expansions / shortcuts (e.g. tşk -> teşekkürler, brb -> be right back)
        if (allowExpansions && SpellLexicons.DefaultTextExpansions.TryGetValue(lowerWord, out var expansion) &&
            !(allowLearning && _frequencyTracker.IsCorrectionRejected(lowerWord, expansion, languageTag)))
            return expansion;

        // Explicit spelling mappings take precedence over contextual guesses.
        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var isEnglish = languageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var typos = isTurkish ? SpellLexicons.TurkishCommonTypos : SpellLexicons.EnglishCommonTypos;
        if (typos.TryGetValue(lowerWord, out var typo) &&
            !(allowLearning && _frequencyTracker.IsCorrectionRejected(lowerWord, typo, languageTag))) return typo;
        if (isEnglish && SpellErrorModel.TryGetCorrection(lowerWord, out var annotatedCorrection) &&
            !IsKnownWord(word, languageTag, allowLearning) &&
            !(allowLearning && _frequencyTracker.IsCorrectionRejected(lowerWord, annotatedCorrection, languageTag)))
            return annotatedCorrection;
        if (isTurkish && !allowDeasciify && SpellLexicons.TurkishDeasciifierMap.ContainsKey(lowerWord)) return null;
        if (isTurkish && allowDeasciify && SpellLexicons.TurkishDeasciifierMap.TryGetValue(lowerWord, out var deasciified) &&
            !(allowLearning && _frequencyTracker.IsCorrectionRejected(lowerWord, deasciified, languageTag)))
        {
            if (SpellVocabulary.TurkishWords.Contains(lowerWord)) return null;
            return deasciified;
        }
        if (word.Skip(1).Any(char.IsUpper) && !word.All(char.IsUpper)) return null;

        // 3. Context-induced typo correction (e.g. 'her ver' -> 'her yer')
        if (allowContext && lowerPrevWord is not null)
        {
            var contextCorrection = SpellContextModel.TryFindContextCorrection(lowerPrevWord, lowerWord, languageTag);
            if (contextCorrection is not null &&
                !(allowLearning && _frequencyTracker.IsCorrectionRejected(lowerWord, contextCorrection, languageTag)))
                return contextCorrection;
        }

        if (isTurkish)
        {
            var repeatedCharacterCorrection = FindHighConfidenceRepeatedCharacterCorrection(
                lowerWord, lowerPrevWord, allowContext, allowLearning, languageTag);
            if (repeatedCharacterCorrection is not null &&
                !(allowLearning && _frequencyTracker.IsCorrectionRejected(lowerWord, repeatedCharacterCorrection, languageTag)))
                return repeatedCharacterCorrection;
        }

        var isKnownWord = IsKnownWord(word, languageTag, allowLearning);
        if (isTurkish && (isKnownWord || CountTurkishDiacriticVariants(lowerWord) <= MaximumExhaustiveTurkishDiacriticPositions))
        {
            var highConfidenceDiacriticCorrection = FindHighConfidenceTurkishKnownWordCorrection(
                lowerWord, lowerPrevWord, allowContext, allowLearning, languageTag);
            if (highConfidenceDiacriticCorrection is not null &&
                !(allowLearning && _frequencyTracker.IsCorrectionRejected(lowerWord, highConfidenceDiacriticCorrection, languageTag)))
                return highConfidenceDiacriticCorrection;
        }

        if (isKnownWord) return null;

        // Cache lookup for algorithmic corrections
        var cacheKey = $"{languageTag}:{lowerPrevWord}:{lowerWord}:{allowDeasciify}:{allowContext}";
        if (!allowLearning && _correctionCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var correction = ComputeCorrection(lowerWord, languageTag, lowerPrevWord, allowContext, allowDeasciify, allowLearning);
        // Personalized rankings change as the user types; they must not be cached.
        if (!allowLearning)
        {
            if (_correctionCache.Count >= MaximumCachedCorrections) _correctionCache.Clear();
            _correctionCache[cacheKey] = correction;
        }
        return correction;
    }

    private string? FindHighConfidenceRepeatedCharacterCorrection(
        string word,
        string? previousWord,
        bool allowContext,
        bool allowLearning,
        string languageTag)
    {
        var trailingRunStart = word.Length - 1;
        while (trailingRunStart > 0 && word[trailingRunStart - 1] == word[^1]) trailingRunStart--;
        if (word.Length - trailingRunStart < 2) return null;

        var collapsed = CollapseRepeatedCharactersToSingle(word);
        if (collapsed == word || !IsKnownWord(collapsed, languageTag, allowLearning)) return null;
        var typedWordIsKnown = IsKnownWord(word, languageTag, allowLearning);

        var frequencyAdvantage = SpellNgramModel.GetUnigramBonus(collapsed, languageTag) -
                                 SpellNgramModel.GetUnigramBonus(word, languageTag);
        var contextBonus = allowContext
            ? SpellContextModel.GetBigramBonus(previousWord, collapsed, languageTag, _frequencyTracker, allowLearning)
            : 0;
        return frequencyAdvantage >= MinimumRepeatedCharacterFrequencyAdvantage ||
               contextBonus >= MinimumCandidateMargin ||
               (!typedWordIsKnown && word.Length - collapsed.Length >= 2)
            ? collapsed
            : null;
    }

    private string? FindHighConfidenceTurkishKnownWordCorrection(
        string word,
        string? previousWord,
        bool allowContext,
        bool allowLearning,
        string languageTag)
    {
        var candidates = GetTurkishDiacriticAlternatives(word);
        var rankedCandidates = GetRankedCandidates(
            word, candidates, previousWord, allowContext, allowLearning, languageTag, maximumResults: 2);
        if (rankedCandidates.Count == 0) return null;

        var best = rankedCandidates[0];
        var editDistance = CalculateEditDistance(word, best.Word);
        if (editDistance < 1)
            return null;

        if (editDistance <= MaximumKnownTurkishDiacriticPositions &&
            IsKnownWord(word, languageTag, allowLearning) && allowContext)
        {
            var frequencyAdvantage = SpellNgramModel.GetUnigramBonus(best.Word, languageTag) -
                                     SpellNgramModel.GetUnigramBonus(word, languageTag);
            var typedContextEvidence = allowContext
                ? SpellContextModel.GetBigramBonus(previousWord, word, languageTag, _frequencyTracker, allowLearning)
                : 0;
            var candidateContextEvidence = allowContext
                ? SpellContextModel.GetBigramBonus(previousWord, best.Word, languageTag, _frequencyTracker, allowLearning)
                : 0;
            // Keep a valid typed word when unigram evidence is nearly tied unless context favors the alternative.
            if (frequencyAdvantage < MinimumKnownWordFrequencyLead &&
                candidateContextEvidence - typedContextEvidence < MinimumCandidateMargin)
                return null;
        }

        if (CountTurkishDiacritics(best.Word) < CountTurkishDiacritics(word))
        {
            var typedContextBonus = allowContext
                ? SpellContextModel.GetBigramBonus(previousWord, word, languageTag, _frequencyTracker, allowLearning)
                : 0;
            var lessDiacriticContextBonus = allowContext
                ? SpellContextModel.GetBigramBonus(previousWord, best.Word, languageTag, _frequencyTracker, allowLearning)
                : 0;
            return lessDiacriticContextBonus - typedContextBonus >= MinimumCandidateMargin
                ? best.Word
                : null;
        }

        if (editDistance > MaximumKnownTurkishDiacriticPositions)
        {
            if (best.Word.Count(c => c == 'ı') < word.Count(c => c == 'ı')) return null;
            var isUniqueMultipleDiacriticCandidate = CountTurkishDiacriticVariants(word) > MaximumKnownTurkishDiacriticPositions &&
                                                     rankedCandidates.Count == 1 &&
                                                     best.Score <= MaximumUniqueMultipleDiacriticCandidateScore;
            return isUniqueMultipleDiacriticCandidate ? best.Word : null;
        }

        if (editDistance == 2)
        {
            var hasClearCandidateMargin = rankedCandidates.Count == 1 ||
                                          rankedCandidates[1].Score - best.Score >= MinimumTwoDiacriticCandidateMargin;
            var hasStrongCandidateScore = best.Score <= MaximumStrongTwoDiacriticCandidateScore;
            var isUniqueLongWordCandidate = word.Length >= MinimumUniqueLongDiacriticWordLength &&
                                            rankedCandidates.Count == 1 &&
                                            best.Score <= MaximumUniqueLongDiacriticCandidateScore;
            if (!hasClearCandidateMargin || (!hasStrongCandidateScore && !isUniqueLongWordCandidate))
                return null;
            return best.Word;
        }

        if (best.Score > MaximumKnownTurkishCandidateScore ||
            (rankedCandidates.Count > 1 &&
             rankedCandidates[1].Score - best.Score < MinimumTurkishCandidateMargin))
            return null;

        var candidateFrequencyAdvantage = SpellNgramModel.GetUnigramBonus(best.Word, languageTag) -
                                          SpellNgramModel.GetUnigramBonus(word, languageTag);
        var candidateContextBonus = allowContext
            ? SpellContextModel.GetBigramBonus(previousWord, best.Word, languageTag, _frequencyTracker, allowLearning)
            : 0;
        return candidateFrequencyAdvantage >= MinimumKnownTurkishFrequencyAdvantage || candidateContextBonus >= MinimumCandidateMargin
            ? best.Word
            : null;
    }

    private static int CountTurkishDiacritics(string word) =>
        word.Count(character => character is 'ç' or 'ğ' or 'ö' or 'ş' or 'ü');

    private static int CountTurkishDiacriticVariants(string word) =>
        word.Count(TurkishDiacriticVariants.ContainsKey);

    private static IReadOnlyCollection<string> GetTurkishDiacriticAlternatives(string word)
    {
        var positions = new List<(int Index, char Replacement)>();
        for (var index = 0; index < word.Length; index++)
            if (TurkishDiacriticVariants.TryGetValue(word[index], out var replacement))
                positions.Add((index, replacement));

        if (positions.Count == 0) return [];
        var characters = word.ToCharArray();
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        if (positions.Count <= MaximumExhaustiveTurkishDiacriticPositions)
        {
            var variantCount = 1 << positions.Count;
            for (var mask = 1; mask < variantCount; mask++)
            {
                for (var bit = 0; bit < positions.Count; bit++)
                    characters[positions[bit].Index] = (mask & (1 << bit)) != 0
                        ? positions[bit].Replacement
                        : word[positions[bit].Index];

                AddCandidate(new string(characters));
            }
        }
        else
        {
            foreach (var position in positions)
            {
                characters[position.Index] = position.Replacement;
                AddCandidate(new string(characters));
                characters[position.Index] = word[position.Index];
            }

            for (var first = 0; first < positions.Count - 1; first++)
            {
                characters[positions[first].Index] = positions[first].Replacement;
                for (var second = first + 1; second < positions.Count; second++)
                {
                    characters[positions[second].Index] = positions[second].Replacement;
                    var candidate = new string(characters);
                    if (TurkishCandidateVocabulary.Value.Contains(candidate)) candidates.Add(candidate);
                    characters[positions[second].Index] = word[positions[second].Index];
                }
                characters[positions[first].Index] = word[positions[first].Index];
            }
        }

        return candidates;

        void AddCandidate(string candidate)
        {
            if (TurkishCandidateVocabulary.Value.Contains(candidate) || IsKnownTurkishMorphologyCandidate(candidate))
                candidates.Add(candidate);
        }
    }

    private string? ComputeCorrection(
        string lowerWord,
        string languageTag,
        string? lowerPrevWord,
        bool allowContext,
        bool allowDeasciify,
        bool allowLearning)
    {
        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var isEnglish = languageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase);

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

        // 4. Candidate search with Keyboard-Proximity weighted Damerau-Levenshtein distance + Context + Frequency
        var candidate = FindBestCandidate(lowerWord, GetCandidates(lowerWord, isTurkish), lowerPrevWord, allowContext, allowLearning, languageTag);

        return candidate;
    }

    private string? FindBestCandidate(
        string word,
        IEnumerable<string> candidates,
        string? previousWord,
        bool allowContext,
        bool allowLearning,
        string languageTag)
    {
        var rankedCandidates = GetRankedCandidates(word, candidates, previousWord, allowContext, allowLearning, languageTag, 2);
        if (TryGetAutomaticCorrection(rankedCandidates, word.Length, languageTag, out var correction))
            return correction;

        if (!languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase)) return null;

        var longTailCandidates = CollectCompatibleCandidates(
            word, 2, isTurkish: true, useTurkishLongTail: true);
        var rankedLongTailCandidates = GetRankedCandidates(
            word, longTailCandidates, previousWord, allowContext, allowLearning, languageTag, 2);
        var combinedCandidates = rankedCandidates
            .Concat(rankedLongTailCandidates)
            .GroupBy(candidate => candidate.Word, StringComparer.Ordinal)
            .Select(group => group.OrderBy(candidate => candidate, Comparer<RankedCandidate>.Create(CompareCandidates)).First())
            .OrderBy(candidate => candidate, Comparer<RankedCandidate>.Create(CompareCandidates))
            .Take(2)
            .ToArray();
        return TryGetAutomaticCorrection(
            combinedCandidates, word.Length, languageTag, out correction, MinimumTurkishLongTailCandidateMargin)
            ? correction
            : null;
    }

    private static bool TryGetAutomaticCorrection(
        IReadOnlyList<RankedCandidate> candidates,
        int wordLength,
        string languageTag,
        out string? correction,
        double? minimumMarginOverride = null)
    {
        correction = null;
        if (candidates.Count == 0) return false;

        var best = candidates[0];
        var maximumScore = GetMaximumAutomaticCorrectionScore(wordLength);
        var minimumMargin = minimumMarginOverride ?? (languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            ? MinimumTurkishFuzzyCandidateMargin
            : MinimumCandidateMargin);
        if (best.Distance > maximumScore || best.Score > maximumScore ||
            (candidates.Count > 1 && candidates[1].Score - best.Score < minimumMargin))
            return false;

        correction = best.Word;
        return true;
    }

    private IReadOnlyList<RankedCandidate> GetRankedCandidates(
        string word,
        IEnumerable<string> candidates,
        string? previousWord,
        bool allowContext,
        bool allowLearning,
        string languageTag,
        int maximumResults)
    {
        var rankedCandidates = new List<RankedCandidate>();
        if (maximumResults <= 0) return rankedCandidates;
        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var maxAllowedScore = GetMaximumSuggestionScore(word.Length, isTurkish);
        var wordFirstChar = word[0];
        var transpositionCandidates = GetAdjacentTranspositions(word);

        foreach (var candidate in candidates)
        {
            if (allowLearning && _frequencyTracker.IsCorrectionRejected(word, candidate, languageTag)) continue;
            // Length filter: must be within +/- 2 characters
            var lengthDiff = Math.Abs(word.Length - candidate.Length);
            if (lengthDiff > 2) continue;

            // Relaxed compatibility filter:
            // Allow same char, adjacent key, missing initial letter (e.g. ncak -> ancak),
            // extra initial letter, transposition, or short words <= 5.
            var candFirstChar = candidate[0];
            var isCompatible =
                candFirstChar == wordFirstChar ||
                (isTurkish && AreTurkishDiacriticVariants(wordFirstChar, candFirstChar)) ||
                SpellLexicons.GetKeyDistance(wordFirstChar, candFirstChar) <= SpellLexicons.NearbyKeyDistance ||
                (candidate.Length > 1 && candidate[1] == wordFirstChar) ||
                (word.Length > 1 && candidate[0] == word[1]);

            if (!isCompatible) continue;

            var maximumEditDistance = word.Length <= 4
                ? MaximumShortWordEditDistance
                : isTurkish ? MaximumTurkishEditDistance : MaximumEnglishEditDistance;
            if (!HasMinimumSharedCharacters(word, candidate, isTurkish,
                    Math.Min(maximumEditDistance, MaximumFuzzyCharacterEdits))) continue;

            // Calculate weighted distance
            var distance = CalculateWeightedDamerauLevenshtein(word, candidate, maxAllowedScore,
                allowTurkishDiacriticEquivalence: isTurkish);
            if (distance > maxAllowedScore) continue;
            if (CalculateEditDistance(word, candidate) > maximumEditDistance) continue;

            // Score candidate (lower is better)
            var score = distance;

            // Bonuses
            if (wordFirstChar == candFirstChar) score -= 0.25;
            if (word[^1] == candidate[^1]) score -= 0.15;
            if (IsSubsequence(word, candidate)) score -= 0.20;
            if (transpositionCandidates.Contains(candidate)) score -= AdjacentTranspositionBonus;

            // Bigram Context bonus
            if (allowContext && previousWord is not null)
            {
                score -= SpellContextModel.GetBigramBonus(previousWord, candidate, languageTag, _frequencyTracker, allowLearning);
            }

            // Personalized user frequency bonus
            if (allowLearning)
            {
                score -= _frequencyTracker.GetFrequencyBonus(candidate);
            }

            score -= SpellNgramModel.GetUnigramBonus(candidate, languageTag);

            rankedCandidates.Add(new RankedCandidate(candidate, score, distance));
            rankedCandidates.Sort(CompareCandidates);
            if (rankedCandidates.Count > maximumResults) rankedCandidates.RemoveAt(rankedCandidates.Count - 1);
        }

        return rankedCandidates;
    }

    private static bool HasMinimumSharedCharacters(
        string word,
        string candidate,
        bool isTurkish,
        int maximumEditDistance)
    {
        Span<byte> characterCounts = stackalloc byte[128];
        foreach (var character in word)
        {
            var normalized = NormalizeForCharacterOverlap(character, isTurkish);
            if (normalized >= characterCounts.Length) return true;
            characterCounts[normalized]++;
        }

        var sharedCharacters = 0;
        foreach (var character in candidate)
        {
            var normalized = NormalizeForCharacterOverlap(character, isTurkish);
            if (normalized >= characterCounts.Length) return true;
            if (characterCounts[normalized] == 0) continue;
            characterCounts[normalized]--;
            sharedCharacters++;
        }

        return sharedCharacters >= Math.Max(0, Math.Min(word.Length, candidate.Length) - maximumEditDistance);
    }

    private static char NormalizeForCharacterOverlap(char character, bool isTurkish)
    {
        if (!isTurkish) return character;
        return character switch
        {
            'ç' => 'c',
            'ğ' => 'g',
            'ı' => 'i',
            'ö' => 'o',
            'ş' => 's',
            'ü' => 'u',
            _ => character
        };
    }

    private static double GetMaximumAutomaticCorrectionScore(int wordLength) =>
        wordLength <= 4
            ? MaxFuzzyScoreShort
            : MaxFuzzyScoreLong;

    private static double GetMaximumSuggestionScore(int wordLength, bool isTurkish) =>
        wordLength <= 4
            ? MaxFuzzyScoreShort
            : isTurkish ? MaxTurkishSuggestionScoreLong : MaxFuzzyScoreLong;

    private static int CompareCandidates(RankedCandidate left, RankedCandidate right)
    {
        var scoreComparison = left.Score.CompareTo(right.Score);
        return scoreComparison != 0 ? scoreComparison : StringComparer.OrdinalIgnoreCase.Compare(left.Word, right.Word);
    }

    private readonly record struct RankedCandidate(string Word, double Score, double Distance);

    private static IEnumerable<string> GetCandidates(string word, bool isTurkish)
    {
        var candidates = CollectCompatibleCandidates(word, 2, isTurkish);

        if (isTurkish) AddTurkishDiacriticCandidates(candidates, word);

        var extraCandidates = new HashSet<string>(
            (isTurkish ? SpellLexicons.TurkishTypoTargets : SpellLexicons.EnglishTypoTargets)
                .Where(target => target.All(char.IsLetter)), StringComparer.Ordinal);

        foreach (var transposition in GetAdjacentTranspositions(word))
        {
            var isKnownTransposition = isTurkish
                ? SpellNgramModel.ContainsTurkishWord(transposition)
                : SpellVocabulary.EnglishWords.Contains(transposition);
            if (isKnownTransposition) candidates.Add(transposition);
        }

        // Correct the root while preserving the typed inflection (kitp + lar -> kitaplar).
        var suffixes = isTurkish ? SpellLexicons.TurkishSuffixes : SpellLexicons.EnglishSuffixes;
        foreach (var suffix in suffixes.Distinct(StringComparer.Ordinal))
        {
            if (!word.EndsWith(suffix, StringComparison.Ordinal) || word.Length - suffix.Length < 3) continue;
            var stem = word[..^suffix.Length];
            foreach (var root in CollectCompatibleCandidates(stem, 1, isTurkish))
            {
                if (CalculateWeightedDamerauLevenshtein(stem, root, 1.0) > 1.0) continue;
                var inflected = root + suffix;
                if (isTurkish ? IsValidTurkishInflection(inflected) : IsValidEnglishInflection(inflected)) extraCandidates.Add(inflected);
            }
        }
        candidates.UnionWith(extraCandidates);
        return candidates;
    }

    private static void AddTurkishDiacriticCandidates(HashSet<string> candidates, string word)
    {
        var positions = new List<(int Index, char Replacement)>();
        for (var index = 0; index < word.Length; index++)
        {
            var replacement = word[index] switch
            {
                'c' => 'ç',
                'g' => 'ğ',
                'i' => 'ı',
                'o' => 'ö',
                's' => 'ş',
                'u' => 'ü',
                _ => '\0'
            };
            if (replacement != '\0') positions.Add((index, replacement));
        }

        if (positions.Count is 0 or > MaximumTurkishDeasciifyPositions) return;

        var variantCount = 1 << positions.Count;
        var characters = word.ToCharArray();
        for (var mask = 1; mask < variantCount; mask++)
        {
            for (var bit = 0; bit < positions.Count; bit++)
                characters[positions[bit].Index] = (mask & (1 << bit)) != 0 ? positions[bit].Replacement : word[positions[bit].Index];

            var candidate = new string(characters);
            if (TurkishCandidateVocabulary.Value.Contains(candidate) ||
                ((mask & (mask - 1)) == 0 && IsKnownTurkishMorphologyCandidate(candidate)))
                candidates.Add(candidate);
        }
    }

    private static bool IsKnownTurkishMorphologyCandidate(string candidate)
    {
        if (TurkishMorphologyCandidateCache.TryGetValue(candidate, out var isKnown)) return isKnown;
        isKnown = TurkishMorphologyService.IsKnownWord(candidate);
        if (TurkishMorphologyCandidateCache.Count >= MaximumTurkishMorphologyCandidateCacheEntries)
            TurkishMorphologyCandidateCache.Clear();
        return TurkishMorphologyCandidateCache.GetOrAdd(candidate, isKnown);
    }

    private static HashSet<string> GetAdjacentTranspositions(string word)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var characters = word.ToCharArray();
        for (var index = 0; index < characters.Length - 1; index++)
        {
            if (characters[index] == characters[index + 1]) continue;
            (characters[index], characters[index + 1]) = (characters[index + 1], characters[index]);
            candidates.Add(new string(characters));
            (characters[index], characters[index + 1]) = (characters[index + 1], characters[index]);
        }
        return candidates;
    }

    private static HashSet<string> CollectCompatibleCandidates(
        string word,
        int maximumLengthDifference,
        bool isTurkish,
        bool useTurkishLongTail = false)
    {
        var firstCharacterIndex = isTurkish
            ? useTurkishLongTail
                ? TurkishLongTailCandidatesByLengthAndFirstCharacter.Value
                : TurkishCandidatesByLengthAndFirstCharacter.Value
            : EnglishCandidatesByLengthAndFirstCharacter.Value;
        var secondCharacterIndex = isTurkish
            ? useTurkishLongTail
                ? TurkishLongTailCandidatesByLengthAndSecondCharacter.Value
                : TurkishCandidatesByLengthAndSecondCharacter.Value
            : EnglishCandidatesByLengthAndSecondCharacter.Value;
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var possibleFirstCharacters = new HashSet<char> { word[0] };
        possibleFirstCharacters.UnionWith(SpellLexicons.GetNearbyKeyCharacters(word[0]));
        if (word.Length > 1) possibleFirstCharacters.Add(word[1]);

        var minimumLength = Math.Max(1, word.Length - maximumLengthDifference);
        var maximumLength = word.Length + maximumLengthDifference;
        for (var length = minimumLength; length <= maximumLength; length++)
        {
            if (firstCharacterIndex.TryGetValue(length, out var firstCharacterBuckets))
            {
                foreach (var character in possibleFirstCharacters)
                {
                    if (firstCharacterBuckets.TryGetValue(character, out var bucket)) candidates.UnionWith(bucket);
                }
            }

            if (secondCharacterIndex.TryGetValue(length, out var secondCharacterBuckets) &&
                secondCharacterBuckets.TryGetValue(word[0], out var secondCharacterBucket))
                candidates.UnionWith(secondCharacterBucket);
        }

        return candidates;
    }

    private static Dictionary<int, Dictionary<char, string[]>> IndexByLengthAndCharacter(
        IEnumerable<string> words,
        int characterIndex) => words
        .Where(word => word.Length > characterIndex)
        .GroupBy(word => word.Length)
        .ToDictionary(lengthGroup => lengthGroup.Key,
            lengthGroup => lengthGroup
                .GroupBy(word => word[characterIndex])
                .ToDictionary(characterGroup => characterGroup.Key,
                    characterGroup => characterGroup.Order(StringComparer.Ordinal).ToArray()));

    internal static double CalculateWeightedDamerauLevenshtein(
        string source,
        string target,
        double maximumScore = double.MaxValue,
        bool allowTurkishDiacriticEquivalence = false)
    {
        var n = source.Length;
        var m = target.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var twoRowsBack = new double[m + 1];
        var previousRow = new double[m + 1];
        var currentRow = new double[m + 1];

        for (var j = 0; j <= m; j++) previousRow[j] = j;

        for (var i = 1; i <= n; i++)
        {
            currentRow[0] = i;
            var minimumInRow = currentRow[0];
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
                    if (allowTurkishDiacriticEquivalence && AreTurkishDiacriticVariants(sChar, tChar))
                    {
                        cost = TurkishDiacriticSubstitutionCost;
                    }
                    else
                    {
                        cost = CalculateKeyboardSubstitutionCost(sChar, tChar);
                    }
                }

                currentRow[j] = Math.Min(
                    Math.Min(previousRow[j] + 1.0,        // Deletion
                             currentRow[j - 1] + 1.0),     // Insertion
                    previousRow[j - 1] + cost);           // Substitution

                // Transposition
                if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                {
                    currentRow[j] = Math.Min(currentRow[j], twoRowsBack[j - 2] + 0.75);
                }
                minimumInRow = Math.Min(minimumInRow, currentRow[j]);
            }

            if (minimumInRow > maximumScore) return double.MaxValue;
            (twoRowsBack, previousRow, currentRow) = (previousRow, currentRow, twoRowsBack);
        }

        return previousRow[m];
    }

    internal static double CalculateKeyboardSubstitutionCost(char source, char target) =>
        Math.Min(MaximumKeyboardSubstitutionCost,
            KeyboardSubstitutionBaseCost + SpellLexicons.GetKeyDistance(source, target) * KeyboardSubstitutionDistanceScale);

    private static bool AreTurkishDiacriticVariants(char left, char right) =>
        (left, right) is ('c', 'ç') or ('ç', 'c') or ('g', 'ğ') or ('ğ', 'g') or
        ('i', 'ı') or ('ı', 'i') or ('o', 'ö') or ('ö', 'o') or ('s', 'ş') or ('ş', 's') or ('u', 'ü') or ('ü', 'u');

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

    private static string CollapseRepeatedCharactersToSingle(string text)
    {
        if (text.Length < 2) return text;

        var builder = new StringBuilder(text.Length);
        builder.Append(text[0]);
        for (var index = 1; index < text.Length; index++)
            if (text[index] != text[index - 1]) builder.Append(text[index]);
        return builder.ToString();
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

    private static readonly HashSet<string> ShortNounStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ev", "su", "el", "at", "ay", "un", "ot", "ok", "ip", "ek", "iş", "uç", "ad", "av", "öz", "ön", "yol", "gün", "kol", "baş", "göz", "diş", "dil", "can"
    };

    private static readonly HashSet<string> ShortVerbStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "gel", "git", "al", "ver", "ol", "yap", "et", "gör", "bak", "kal", "dur",
        "çık", "gir", "yaz", "oku", "sev", "aç", "ye", "iç", "uyu", "koş", "sor",
        "kaç", "bul", "duy", "bil", "koy", "tut", "at", "kes", "çiz", "dön", "bırak"
    };

    private static readonly HashSet<string> VerbalSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ma", "me", "madan", "meden", "makta", "mekte", "yor", "ıyor", "iyor", "uyor", "üyor",
        "dı", "di", "du", "dü", "tı", "ti", "tu", "tü", "mış", "miş", "muş", "müş",
        "acak", "ecek", "yacak", "yecek", "alım", "elim", "sın", "sin", "sun", "sün"
    };

    private static bool IsValidTurkishInflection(string word)
    {
        if (word.Length < 4) return false;
        if (SpellLexicons.TurkishCommonTypos.ContainsKey(word)) return false;

        foreach (var suffix in SpellLexicons.TurkishSuffixes)
        {
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var stem = word[..^suffix.Length];
                if (stem.Length < 2) continue;
                if (stem.Length == 2 && !ShortNounStems.Contains(stem)) continue;
                if (VerbalSuffixes.Contains(suffix) && stem.Length <= 3 && !ShortVerbStems.Contains(stem))
                    continue;

                if (SpellVocabulary.TurkishWords.Contains(stem) ||
                    SpellNgramModel.TurkishMorphologyVocabulary.Contains(stem))
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
                if (stem.Length >= 2 && IsKnownEnglishStem(stem) && !HasKnownEnglishTransposition(word))
                    return true;
                // English 'e' drop (e.g. make -> making, care -> caring)
                if (stem.Length >= 2 && IsKnownEnglishStem(stem + "e") && !HasKnownEnglishTransposition(word))
                    return true;
            }
        }
        return false;
    }

    private static bool IsKnownEnglishStem(string stem) =>
        SpellVocabulary.EnglishWords.Contains(stem) ||
        SpellNgramModel.ContainsWord(stem, EnglishLanguageTag);

    private static bool HasKnownEnglishTransposition(string word)
    {
        var characters = word.ToCharArray();
        for (var index = 0; index < characters.Length - 1; index++)
        {
            if (characters[index] == characters[index + 1]) continue;

            (characters[index], characters[index + 1]) = (characters[index + 1], characters[index]);
            var transposition = new string(characters);
            (characters[index], characters[index + 1]) = (characters[index + 1], characters[index]);

            if (SpellVocabulary.EnglishWords.Contains(transposition) ||
                SpellNgramModel.ContainsWord(transposition, EnglishLanguageTag)) return true;
        }

        return false;
    }

    internal static CultureInfo GetCulture(string languageTag)
    {
        try { return CultureInfo.GetCultureInfo(languageTag); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}
