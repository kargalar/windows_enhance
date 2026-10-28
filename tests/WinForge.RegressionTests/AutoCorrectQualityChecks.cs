using System.Reflection;
using System.IO;
using System.Text;

namespace WinForge.RegressionTests;

internal static class AutoCorrectQualityChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    internal static void Run(Assembly extras, Action<bool, string> check)
    {
        check(extras.GetManifestResourceNames().Any(name => name.EndsWith("TurkishNgramModel.tsv.gz", StringComparison.Ordinal)),
            "A trained Turkish frequency model is embedded in the application module");
        var ngramType = extras.GetType("WinForge.Modules.Extras.SpellNgramModel")!;
        check((bool)ngramType.GetMethod("ContainsTurkishWord", PrivateStatic)!.Invoke(null, ["Türkiye"])!,
            "Training vocabulary recognizes unseen held-out Turkish words");
        check((int)ngramType.GetMethod("GetTurkishVocabularyCount", PrivateStatic)!.Invoke(null, null)! >= 50_000,
            "Trained Turkish frequency model contains a broad word-form vocabulary");
        check(extras.GetManifestResourceNames().Any(name => name.EndsWith("EnglishNgramModel.tsv.gz", StringComparison.Ordinal)),
            "A trained English n-gram model is embedded in the application module");
        check(extras.GetManifestResourceNames().Any(name => name.EndsWith("EnglishSpellingErrorModel.tsv.gz", StringComparison.Ordinal)),
            "A high-confidence English spelling-error model is embedded in the application module");
        check((int)ngramType.GetMethod("GetEnglishVocabularyCount", PrivateStatic)!.Invoke(null, null)! >= 100_000,
            "Trained English frequency model contains a broad word-form vocabulary");
        check(extras.GetManifestResourceNames().Any(name => name.EndsWith("TurkishDialogueNgramModel.tsv.gz", StringComparison.Ordinal)) &&
              extras.GetManifestResourceNames().Any(name => name.EndsWith("EnglishDialogueNgramModel.tsv.gz", StringComparison.Ordinal)),
            "Licensed conversational n-gram models are embedded for both supported languages");

        var morphologyType = extras.GetType("WinForge.Modules.Extras.TurkishMorphologyService")!;
        check((bool)morphologyType.GetProperty("IsAvailable", PrivateStatic)!.GetValue(null)!,
            "Turkish morphological analyzer loads its packaged runtime resources");
        var morphologyRecognizes = morphologyType.GetMethod("IsKnownWord", PrivateStatic)!;
        foreach (var word in new[]
        {
            "yoğunlaşacağım", "blokesini", "bakıldıklarını", "sanmışız", "ilgilendirirdi",
            "okumasaydım", "fonlanmasıyla", "yeğlediğiniz", "konulduğumuzu", "algılamışız"
        })
        {
            check((bool)morphologyRecognizes.Invoke(null, [word])!, $"Morphology recognizes valid inflection '{word}'");
        }
        check(!(bool)morphologyRecognizes.Invoke(null, ["bfunları"])!,
            "Morphology rejects a transposed nonword instead of protecting it as valid");

        var engineType = extras.GetType("WinForge.Modules.Extras.SpellEngine")!;
        var engine = Activator.CreateInstance(engineType)!;
        var getSuggestions = engineType.GetMethod("GetSuggestions", PrivateInstance)!;
        var prefixSuggestions = (IReadOnlyList<string>)getSuggestions.Invoke(engine,
            ["merh", "tr-TR", null, true, true, false, false, null, 3])!;
        check(prefixSuggestions.Contains("merhaba", StringComparer.OrdinalIgnoreCase),
            "Word suggestions complete a typed Turkish prefix using trained word frequencies");
        var englishPrefixSuggestions = (IReadOnlyList<string>)getSuggestions.Invoke(engine,
            ["tha", "en-US", null, true, true, false, false, null, 3])!;
        check(englishPrefixSuggestions.Contains("that", StringComparer.OrdinalIgnoreCase),
            "Word suggestions complete an English prefix using dictionary frequency order");
        var contextType = extras.GetType("WinForge.Modules.Extras.SpellContextModel")!;
        var getPredictions = contextType.GetMethods(PrivateStatic)
            .Single(method => method.Name == "GetNextWordSuggestions" && method.GetParameters().Length == 4);
        var predictions = (IReadOnlyList<string>)getPredictions.Invoke(null, ["çok", "teş", "tr-TR", 3])!;
        check(predictions.Contains("teşekkürler", StringComparer.OrdinalIgnoreCase),
            "Next-word suggestions use corpus and collocation context with prefix filtering");
        var englishCorpusPredictions = (IReadOnlyList<string>)getPredictions.Invoke(null, ["the United", "sta", "en-US", 3])!;
        check(englishCorpusPredictions.Contains("states", StringComparer.OrdinalIgnoreCase),
            "English next-word suggestions use the trained corpus trigram model");
        var weightedWordType = ngramType.GetNestedType("WeightedWord", BindingFlags.NonPublic)!;
        Array MakeCandidates(params (string Word, int Count, double Probability)[] values)
        {
            var candidates = Array.CreateInstance(weightedWordType, values.Length);
            for (var index = 0; index < values.Length; index++)
                candidates.SetValue(Activator.CreateInstance(weightedWordType,
                    [values[index].Word, values[index].Count, values[index].Probability]), index);
            return candidates;
        }
        var interpolate = ngramType.GetMethod("InterpolateCandidates", PrivateStatic)!;
        var smoothed = ((System.Collections.IEnumerable)interpolate.Invoke(null,
            [MakeCandidates(("specific", 1, 1d)), MakeCandidates(("specific", 10, 0.5d), ("general", 10, 0.5d))])!)
            .Cast<object>().ToArray();
        double Probability(object candidate) => (double)weightedWordType.GetProperty("Probability")!.GetValue(candidate)!;
        var specificProbability = smoothed.Single(candidate =>
            StringComparer.Ordinal.Equals(weightedWordType.GetProperty("Word")!.GetValue(candidate), "specific"));
        var generalProbability = smoothed.Single(candidate =>
            StringComparer.Ordinal.Equals(weightedWordType.GetProperty("Word")!.GetValue(candidate), "general"));
        check(Probability(generalProbability) > 0 && Probability(specificProbability) > Probability(generalProbability) &&
              Math.Abs(smoothed.Sum(Probability) - 1) < 0.0001,
            "Trigram next-word probabilities back off to normalized bigram evidence");
        var fallbackPredictions = (IReadOnlyList<string>)getPredictions.Invoke(null,
            ["qzxwv", string.Empty, "en-US", 3])!;
        check(fallbackPredictions.Count == 3,
            "Next-word suggestions fall back to common words when context is unseen");
        var conversationCandidates = (System.Collections.IEnumerable)ngramType
            .GetMethod("GetConversationNextWordCandidates", PrivateStatic)!
            .Invoke(null, ["do you", "en-US"])!;
        check(conversationCandidates.Cast<object>().Any(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    candidate.GetType().GetProperty("Word")!.GetValue(candidate) as string, "have")),
            "English conversational trigram model contains frequent daily-dialogue phrases");
        var apostropheCandidates = (System.Collections.IEnumerable)ngramType
            .GetMethod("GetConversationNextWordCandidates", PrivateStatic)!
            .Invoke(null, ["I don't", "en-US"])!;
        check(apostropheCandidates.Cast<object>().Any(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    candidate.GetType().GetProperty("Word")!.GetValue(candidate) as string, "want")),
            "English conversational context normalizes apostrophes like its training tokenizer");
        var trackerType = extras.GetType("WinForge.Modules.Extras.UserFrequencyTracker")!;
        var personalizedTrackerPath = Path.Combine(Path.GetTempPath(), "WinForge.Tests", $"prediction-{Guid.NewGuid():N}.json");
        var personalizedTracker = Activator.CreateInstance(trackerType, [personalizedTrackerPath])!;
        try
        {
            var recordWord = trackerType.GetMethod("RecordWord", [typeof(string), typeof(bool), typeof(string)])!;
            recordWord.Invoke(personalizedTracker, ["meeting", true, "zzxqproject"]);
            recordWord.Invoke(personalizedTracker, ["meeting", true, "zzxqproject"]);
            var personalizedPredictions = contextType.GetMethods(PrivateStatic)
                .Single(method => method.Name == "GetNextWordSuggestions" && method.GetParameters().Length == 6)
                .Invoke(null, ["some zzxqproject", string.Empty, "en-US", 3, personalizedTracker, true]) as IReadOnlyList<string>;
            check(personalizedPredictions?.Contains("meeting", StringComparer.OrdinalIgnoreCase) == true,
                "Accepted personalized bigrams become next-word predictions when corpus context is absent");
            var unpersonalizedPredictions = contextType.GetMethods(PrivateStatic)
                .Single(method => method.Name == "GetNextWordSuggestions" && method.GetParameters().Length == 6)
                .Invoke(null, ["some zzxqproject", string.Empty, "en-US", 3, personalizedTracker, false]) as IReadOnlyList<string>;
            check(unpersonalizedPredictions?.Contains("meeting", StringComparer.OrdinalIgnoreCase) != true,
                "Disabling personalization removes user-specific next-word predictions");
            for (var i = 0; i < 10; i++) recordWord.Invoke(personalizedTracker, ["tomorrow", true, "zzxqproject zzxqsync"]);
            var personalizedTrigramPredictions = contextType.GetMethods(PrivateStatic)
                .Single(method => method.Name == "GetNextWordSuggestions" && method.GetParameters().Length == 6)
                .Invoke(null, ["zzxqproject zzxqsync", string.Empty, "en-US", 3, personalizedTracker, true]) as IReadOnlyList<string>;
            check(personalizedTrigramPredictions?.Contains("tomorrow", StringComparer.OrdinalIgnoreCase) == true,
                "Accepted personalized trigrams predict words from two-word context");
            var trigramBonus = (double)trackerType.GetMethod("GetTrigramFrequencyBonus", PrivateInstance)!
                .Invoke(personalizedTracker, ["zzxqproject zzxqsync", "tomorrow"])!;
            check(trigramBonus > 0.5, "Repeated personalized trigrams outrank generic context evidence");
        }
        finally { trackerType.GetMethod("Clear")!.Invoke(personalizedTracker, null); }
        var find = engineType.GetMethods().Single(method => method.Name == "FindCorrection" && method.GetParameters().Length == 8);
        string? Correct(string word, string tag = "tr-TR", string? history = null, bool context = true,
            bool deasciify = true, bool expansions = true, IReadOnlyDictionary<string, string>? custom = null) =>
            (string?)find.Invoke(engine, [word, tag, history, context, deasciify, expansions, false, custom]);

        foreach (var (word, tag, history) in new (string, string, string?)[]
        {
            ("kitap", "tr-TR", null), ("kitaplar", "tr-TR", "bir"),
            ("bende", "tr-TR", null), ("sende", "tr-TR", null),
            ("my", "en-US", "in"), ("good", "en-US", "for"),
            ("cat", "en-US", "at"), ("well", "en-US", "very"),
            ("teşekkür", "tr-TR", "çok"), ("computer", "en-US", null)
        }) check(Correct(word, tag, history) is null, $"Valid word '{history} {word}' remains unchanged");

        foreach (var word in new[] { "objectivists", "floodings", "hydroxylations" })
            check(Correct(word, "en-US") is null,
                $"English inflection '{word}' is preserved when its stem is in the trained model");

        foreach (var word in new[]
        {
            "komşumdu", "girmeliydik", "yönetimdeyim", "gözlenmeyen", "görülmemekle",
            "öngöremiyorum", "dondurulduğunu", "geçebileceğine"
        })
        {
            check(Correct(word) is null, $"Observed Turkish inflection '{word}' remains unchanged");
        }

        foreach (var token in new[] { "fooBar", "my_variable", "test@example.com", "v10", "don't", "kitap'ım", "https://example.com", "qzxwv" })
            check(Correct(token) is null, $"Identifier, structured or unrelated token '{token}' is preserved");
        check(Correct("teh", "de-DE") is null, "Unsupported languages never fall back to English corrections");

        check(Correct("ver", history: "her", context: false) is null, "Disabling context preserves real words");
        check(Correct("ver", history: "her") == "yer", "Enabling context is independent of a previous cached result");
        check(Correct("calisiyorum", deasciify: false) is null, "Disabling Turkish character completion is respected");
        check(Correct("calisiyorum") == "çalışıyorum", "Turkish character completion can be enabled after a disabled lookup");
        check(Correct("brb", "en-US") == "be right back", "Built-in shortcuts expand");
        check(Correct("brb", "en-US", expansions: false) != "be right back", "Disabling expansions is respected after a previous expansion");
        check(Correct("kitap", custom: new Dictionary<string, string> { ["kitap"] = "kitap listesi" }) == "kitap listesi",
            "Explicit user shortcuts take precedence over valid vocabulary");
        check(Correct("teh", "en-US", "thank") == "the", "Explicit spelling fixes take precedence over contextual guesses");

        foreach (var (word, expected) in new[]
        {
            ("genlel", "genel"), ("iyileşitr", "iyileştir"), ("iyleştirme", "iyileştirme"),
            ("sarkilar", "şarkılar"),
            ("şuan", "şu an"), ("kitp", "kitap"), ("kitplar", "kitaplar")
        }) check(Correct(word) == expected, $"Typing error '{word}' resolves to '{expected}'");
        check(Correct("seviyenie") == "seviyesine", "Context demo typo 'seviyenie' is corrected to 'seviyesine'");
        check(Correct("yakisikli") == "yakışıklı", "Multiple missing Turkish diacritics do not exhaust the edit-distance budget");
        check(Correct("yapildigini") == "yapıldığını",
            "Five missing Turkish diacritics remain eligible when their weighted distance is low");
        check(Correct("calistigi") == "çalıştığı",
            "Six missing Turkish diacritics remain eligible when their weighted distance is low");
        check(Correct("benım") == "benim", "A high-confidence Turkish diacritic typo is corrected despite morphology acceptance");
        check(Correct("gundur") == "gündür", "Two unambiguous Turkish diacritic omissions are corrected in a known word");
        var affectionateSuggestions = (IReadOnlyList<string>)getSuggestions.Invoke(engine,
            ["askim", "tr-TR", null, true, true, false, false, null, 3])!;
        check(affectionateSuggestions.Contains("aşkım", StringComparer.OrdinalIgnoreCase),
            "Ambiguous affectionate spelling keeps the preferred diacritic form in suggestions");
        check(Correct("olum") is null, "Ambiguous unaccented Turkish word 'olum' is left for context instead of being forced to 'ölüm'");
        var ambiguousTurkishSuggestions = (IReadOnlyList<string>)getSuggestions.Invoke(engine,
            ["olum", "tr-TR", null, true, true, false, false, null, 3])!;
        check(ambiguousTurkishSuggestions.Contains("ölüm", StringComparer.OrdinalIgnoreCase),
            "Suggestion strip offers a Turkish diacritic alternative without auto-correcting an ambiguous word");
        check(Correct("successfull", "en-US") == "successful",
            "A frequent annotated English misspelling uses the learned correction model");
        var ambiguousSuggestions = (IReadOnlyList<string>)getSuggestions.Invoke(engine,
            ["elevant", "en-US", null, true, true, false, false, null, 3])!;
        check(ambiguousSuggestions.Contains("relevant", StringComparer.OrdinalIgnoreCase),
            "Suggestion strip exposes likely corrections when safe autocorrection abstains");

        var contextFix = contextType.GetMethod("TryFindContextCorrection", PrivateStatic)!;
        string? Context(string history, string word, string tag = "tr-TR") => (string?)contextFix.Invoke(null, [history, word, tag]);
        check(Context("  HER  ", "VER") == "yer", "Context normalizes whitespace and Turkish casing");
        check(Context("seviyesine", "kayın") == "yakın",
            "Turkish context resolves the valid-word confusion 'seviyesine kayın' to 'seviyesine yakın'");
        check(Correct("kayın", history: "seviyesine") == "yakın",
            "SpellEngine applies a high-confidence Turkish real-word collocation correction");
        check(Correct("şu", history: "tutuyor ve") is null,
            "Known Turkish diacritics are not removed without stronger collocation evidence");
        check(Context("yakın mı", "şua") == "şu an" && Correct("şua", history: "yakın mı") == "şu an",
            "Context resolves the two-word Turkish typo 'yakın mı şua' to 'yakın mı şu an'");
        check(Context("her şey", "yolundq") == "yolunda", "Turkish trigrams correct phrase endings");
        check(Context("at the", "mometn", "en-US") == "moment", "English trigrams correct phrase endings");
        check(Context("I don't", "wanr", "en-US") == "want", "English dialogue trigrams correct conversational typos");
        check(Context("her", "yerx") is null, "Context abstains when collocation candidates are ambiguous");
        check(Context("in", "my", "en-US") is null, "Context preserves valid words outside its phrase list");
        check(Context("in", "teh", "de-DE") is null, "Unsupported languages do not use English collocations");
        check(Context("yer", "alıyro") == "alıyor", "Merged collocations retain conjugated verb candidates");

        var rank = engineType.GetMethod("FindBestCandidate", PrivateInstance)!;
        string? Rank(string? history, bool useContext, params string[] candidates) =>
            (string?)rank.Invoke(engine, ["ths", new HashSet<string>(candidates), history, useContext, false, "en-US"]);
        check(Rank(null, false, "this", "thus") is null && Rank(null, false, "thus", "this") is null,
            "Ambiguous fuzzy candidates abstain independently of enumeration order");
        check(Rank("of", true, "this", "thus") == "this", "Bigram evidence disambiguates equally close candidates");
        var getRankedCandidates = engineType.GetMethod("GetRankedCandidates", PrivateInstance)!;
        var borderlineCandidates = new HashSet<string> { "abczey" };
        var borderlineRanked = ((System.Collections.IEnumerable)getRankedCandidates.Invoke(engine,
            ["abcdef", borderlineCandidates, null, false, false, "tr-TR", 3])!).Cast<object>().ToArray();
        var borderlineScore = (double)borderlineRanked[0].GetType().GetProperty("Score")!.GetValue(borderlineRanked[0])!;
        check(borderlineScore is > 2.15 and <= 2.5 &&
              rank.Invoke(engine, ["abcdef", borderlineCandidates, null, false, false, "tr-TR"]) is null,
            "Borderline Turkish candidates can be suggested while automatic correction keeps its stricter score limit");
        var rankWithTransposition = (string?)rank.Invoke(engine,
            ["teh", new HashSet<string> { "the", "tea" }, null, false, false, "tr-TR"]);
        check(rankWithTransposition == "the",
            "A precise adjacent-key transposition can outrank a plausible but incorrect neighbor");
        var rankedTurkishDiacritics = ((System.Collections.IEnumerable)getRankedCandidates.Invoke(engine,
            ["askim", new HashSet<string> { "aksim", "aşkım" }, null, false, false, "tr-TR", 3])!).Cast<object>().ToArray();
        var bestTurkishDiacritic = rankedTurkishDiacritics.FirstOrDefault();
        check(bestTurkishDiacritic is not null &&
              StringComparer.Ordinal.Equals(bestTurkishDiacritic.GetType().GetProperty("Word")!.GetValue(bestTurkishDiacritic), "aşkım"),
            $"Keyboard transposition bonuses do not bury a stronger Turkish diacritic candidate ({string.Join(", ", rankedTurkishDiacritics.Select(candidate => $"{candidate.GetType().GetProperty("Word")!.GetValue(candidate)}:{candidate.GetType().GetProperty("Score")!.GetValue(candidate)}"))})");

        VerifyHistory(extras, check);
        VerifyLearning(extras, check);
        VerifyUndo(extras, check);
    }

    private static void VerifyUndo(Assembly extras, Action<bool, string> check)
    {
        var type = extras.GetType("WinForge.Modules.Extras.AutoCorrectModule")!;
        var text = "önceki tamam"; // The user's physical Backspace removed the trailing space.
        Action<int, string> replace = (count, replacement) => text = text[..^count] + replacement;
        var module = type.GetConstructors(PrivateInstance).Single().Invoke([replace]);
        var correctionType = type.GetNestedType("LastCorrection", BindingFlags.NonPublic)!;
        var correction = Activator.CreateInstance(correctionType, ["tamma", "tamam", new IntPtr(42), 1L])!;
        type.GetMethod("UndoCorrection", PrivateInstance)!.Invoke(module, [correction]);
        check(text == "önceki tamma", "Undo restores the original typo without deleting the preceding word or its separator");

        type.GetField("<IsActive>k__BackingField", PrivateInstance)!.SetValue(module, true);
        type.GetField("_spellChecker", PrivateInstance)!.SetValue(module,
            Activator.CreateInstance(extras.GetType("WinForge.Modules.Extras.SpellEngine")!));
        type.GetField("_inputSequence", PrivateInstance)!.SetValue(module, 10L);
        type.GetMethod("ApplyCorrection", PrivateInstance)!.Invoke(module, ["tamma", new IntPtr(42), 9L, "tr-TR", ' ', null]);
        check(text == "önceki tamma", "A queued correction cannot modify text after newer input invalidates its sequence");
    }

    private static void VerifyLearning(Assembly extras, Action<bool, string> check)
    {
        var trackerType = extras.GetType("WinForge.Modules.Extras.UserFrequencyTracker")!;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinForge.Tests", $"ranking-{Guid.NewGuid():N}.json");
        var tracker = Activator.CreateInstance(trackerType, [path])!;
        try
        {
            var engineType = extras.GetType("WinForge.Modules.Extras.SpellEngine")!;
            var engine = engineType.GetConstructors(PrivateInstance).Single().Invoke([tracker]);
            var find = engineType.GetMethods().Single(method => method.Name == "FindCorrection" && method.GetParameters().Length == 8);
            string? Correct(bool learning) => (string?)find.Invoke(engine, ["kitp", "tr-TR", null, false, true, false, learning, null]);
            var record = trackerType.GetMethod("RecordWord", [typeof(string), typeof(bool), typeof(string)])!;
            for (var i = 0; i < 100; i++) record.Invoke(tracker, ["kitp", false, null]);
            check(!(bool)trackerType.GetMethod("IsLearned")!.Invoke(tracker, ["kitp"])!,
                "Repeated unknown input never becomes learned just because it was typed often");
            check(Correct(false) == "kitap", "Baseline correction can be cached with learning disabled");
            trackerType.GetMethod("LearnWord")!.Invoke(tracker, ["kitp"]);
            check(Correct(true) is null, "Newly learned words override previously cached corrections");
            check(Correct(false) == "kitap", "Disabling personalization ignores learned vocabulary");
            var setConfiguredWords = trackerType.GetMethod("SetConfiguredWords")!;
            setConfiguredWords.Invoke(tracker, [new[] { "MyProjectTerm" }]);
            check((bool)trackerType.GetMethod("IsLearned")!.Invoke(tracker, ["MyProjectTerm"])!,
                "Configured personal dictionary words are recognized");
            setConfiguredWords.Invoke(tracker, [Array.Empty<string>()]);
            check(!(bool)trackerType.GetMethod("IsLearned")!.Invoke(tracker, ["MyProjectTerm"])!,
                "Removing configured words removes them from the active personal dictionary");
            var rank = engineType.GetMethod("FindBestCandidate", PrivateInstance)!;
            var candidates = new HashSet<string> { "this", "thus" };
            string? Rank(bool learning, string? history = null) => (string?)rank.Invoke(engine, ["ths", candidates, history, history is not null, learning, "en-US"]);
            check(Rank(true) is null, "Unseen equally close candidates remain ambiguous");
            var recordWord = trackerType.GetMethod("RecordWord", [typeof(string), typeof(bool), typeof(string)])!;
            for (var i = 0; i < 20; i++) recordWord.Invoke(tracker, ["thus", true, null]);
            check(Rank(true) == "thus" && Rank(false) is null, "Current user word frequencies affect only personalized rankings");
            for (var i = 0; i < 100; i++) recordWord.Invoke(tracker, ["this", true, "xyz"]);
            var learnedBigramBonus = (double)trackerType.GetMethod("GetBigramFrequencyBonus")!.Invoke(tracker, ["xyz", "this"])!;
            check(learnedBigramBonus > 0.7, "Repeated valid phrases receive a personalized bigram weight");
            check(Rank(true, "xyz") == "this" && Rank(false, "xyz") is null,
                "Personal bigram frequencies disambiguate personalized corrections and persist separately");
            trackerType.GetMethod("SaveImmediate")!.Invoke(tracker, null);
            var restored = Activator.CreateInstance(trackerType, [path])!;
            check((bool)trackerType.GetMethod("IsLearned")!.Invoke(restored, ["kitp"])!, "Learned words survive persistence without touching the user's dictionary");
            check((double)trackerType.GetMethod("GetBigramFrequencyBonus")!.Invoke(restored, ["xyz", "this"])! > 0,
                "Personal bigram frequencies survive persistence");
            var recordTrigram = trackerType.GetMethod("RecordWord", [typeof(string), typeof(bool), typeof(string)])!;
            for (var i = 0; i < 3; i++) recordTrigram.Invoke(tracker, ["fox", true, "quick brown"]);
            trackerType.GetMethod("SaveImmediate")!.Invoke(tracker, null);
            var trigramRestored = Activator.CreateInstance(trackerType, [path])!;
            check((double)trackerType.GetMethod("GetTrigramFrequencyBonus", PrivateInstance)!
                    .Invoke(trigramRestored, ["quick brown", "fox"])! > 0,
                "Personal trigram frequencies survive persistence");
        }
        finally { trackerType.GetMethod("Clear")!.Invoke(tracker, null); }
    }

    private static void VerifyHistory(Assembly extras, Action<bool, string> check)
    {
        var moduleType = extras.GetType("WinForge.Modules.Extras.AutoCorrectModule")!;
        var module = Activator.CreateInstance(moduleType)!;
        var resolve = moduleType.GetMethod("ResolveLanguageTags", PrivateStatic)!;
        check(((IReadOnlyList<string>)resolve.Invoke(null, ["Deutsch", "tr-TR"])!).SequenceEqual(new[] { "de-DE" }),
            "Previously stored unsupported language settings do not silently switch to the keyboard language");
        var remember = moduleType.GetMethod("RememberWord", PrivateInstance)!;
        var previous = moduleType.GetField("_previousWord", PrivateInstance)!;
        var penultimate = moduleType.GetField("_penultimateWord", PrivateInstance)!;
        var window = new IntPtr(42);
        void Remember(string word, char delimiter = ' ') => remember.Invoke(module, [word, window, delimiter]);

        Remember("her");
        Remember("şey");
        check((string?)previous.GetValue(module) == "şey" && (string?)penultimate.GetValue(module) == "her",
            "Module retains two preceding words");
        Remember("yolunda", '.');
        check(previous.GetValue(module) is null && penultimate.GetValue(module) is null, "Sentence boundaries clear all context");
        Remember("bu");
        remember.Invoke(module, ["arada", new IntPtr(43), ' ']);
        check(penultimate.GetValue(module) is null, "Context cannot cross application windows");
        Remember("rica ederim");
        check((string?)previous.GetValue(module) == "ederim" && (string?)penultimate.GetValue(module) == "rica",
            "Multiword expansions contribute individual tokens to history");

        var buffer = (StringBuilder)moduleType.GetField("_word", PrivateInstance)!.GetValue(module)!;
        buffer.Append("in");
        moduleType.GetField("_wordWindow", PrivateInstance)!.SetValue(module, window);
        moduleType.GetMethod("HandleWordEnd", PrivateInstance)!.Invoke(module, [window, ' ']);
        check((string?)previous.GetValue(module) == "in", "Short words enter history before any queued correction runs");
        moduleType.GetMethod("InvalidateInput", PrivateInstance)!.Invoke(module, null);
        check(previous.GetValue(module) is null && penultimate.GetValue(module) is null && buffer.Length == 0,
            "Caret and focus invalidation clear pending word and context");
    }
}
