using System.Reflection;
using System.IO;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;

namespace WinForge.RegressionTests;

internal static class AutoCorrectCorpusChecks
{
    private const int EvaluationWordCount = 1_000;
    private const int MaximumNextWordEvaluationContexts = 100_000;

    internal static int Run(string corpusPath, string languageTag, Assembly extras, Action<bool, string> check)
    {
        var engineType = extras.GetType("WinForge.Modules.Extras.SpellEngine")!;
        var engine = engineType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var isCorrect = engineType.GetMethod("IsCorrect", [typeof(string), typeof(string)])!;
        var findCorrection = engineType.GetMethods()
            .Single(method => method.Name == "FindCorrection" && method.GetParameters().Length == 8);
        var modelType = extras.GetType("WinForge.Modules.Extras.SpellNgramModel")!;
        var isTurkish = languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        var containsWord = isTurkish
            ? modelType.GetMethod("ContainsTurkishWord", BindingFlags.Static | BindingFlags.NonPublic)!
            : modelType.GetMethod("ContainsWord", BindingFlags.Static | BindingFlags.NonPublic)!;

        var vocabulary = SampleHeldOutWords(corpusPath, languageTag, _ => true);

        var correctSuggestions = 0;
        var anySuggestions = 0;
        var totalTypos = 0;
        var falseCorrections = 0;
        var validWordsChecked = 0;
        var falseCorrectionExamples = new List<string>();

        foreach (var word in vocabulary)
        {
            var unchanged = (string?)findCorrection.Invoke(engine,
                [word, languageTag, null, false, false, false, false, null]);
            validWordsChecked++;
            if (unchanged is not null && !StringComparer.OrdinalIgnoreCase.Equals(unchanged, word))
            {
                falseCorrections++;
                if (falseCorrectionExamples.Count < 20) falseCorrectionExamples.Add($"{word} -> {unchanged}");
            }

            var isInModel = isTurkish
                ? (bool)containsWord.Invoke(null, [word])!
                : (bool)containsWord.Invoke(null, [word, languageTag])!;
            if (!(bool)isCorrect.Invoke(engine, [word, languageTag])! || !isInModel) continue;
            var typo = TransposeMiddleCharacters(word);
            if (typo is null) continue;

            totalTypos++;
            var result = (string?)findCorrection.Invoke(engine,
                [typo, languageTag, null, false, false, false, false, null]);
            if (result is not null) anySuggestions++;
            if (StringComparer.OrdinalIgnoreCase.Equals(result, word)) correctSuggestions++;

        }

        var exactAccuracy = totalTypos == 0 ? 0 : (double)correctSuggestions / totalTypos;
        var suggestionPrecision = anySuggestions == 0 ? 0 : (double)correctSuggestions / anySuggestions;
        var falseCorrectionRate = validWordsChecked == 0 ? 0 : (double)falseCorrections / validWordsChecked;

        Console.WriteLine($"Held-out {languageTag} corpus words: {vocabulary.Count:N0}");
        Console.WriteLine($"Synthetic transpositions: {totalTypos:N0}");
        Console.WriteLine($"Exact correction accuracy: {exactAccuracy:P2}");
        Console.WriteLine($"Suggestion precision: {suggestionPrecision:P2}");
        Console.WriteLine($"Valid held-out forms changed: {falseCorrections:N0}/{validWordsChecked:N0} ({falseCorrectionRate:P2})");
        if (falseCorrectionExamples.Count > 0)
            Console.WriteLine($"False correction examples: {string.Join(", ", falseCorrectionExamples)}");
        check(vocabulary.Count >= EvaluationWordCount / 2 && totalTypos > 0,
            "Held-out corpus benchmark evaluates enough trained vocabulary forms");

        return totalTypos;
    }

    internal static int RunNextWord(string corpusPath, string languageTag, Assembly extras, Action<bool, string> check)
    {
        var contextType = extras.GetType("WinForge.Modules.Extras.SpellContextModel")!;
        var getSuggestions = contextType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "GetNextWordSuggestions" && method.GetParameters().Length == 4);
        var ngramType = extras.GetType("WinForge.Modules.Extras.SpellNgramModel")!;
        var getGeneralCandidates = ngramType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "GetNextWordCandidates" && method.GetParameters().Length == 2);
        var getConversationCandidates = ngramType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "GetConversationNextWordCandidates" && method.GetParameters().Length == 2);
        var getConversationSupport = ngramType.GetMethod("GetConversationContextSupport", BindingFlags.Static | BindingFlags.NonPublic)!;
        var culture = CultureInfo.GetCultureInfo(languageTag);
        var attempts = 0;
        var sample = new List<NextWordExample>(MaximumNextWordEvaluationContexts);
        var random = new Random(20261028);
        var coveredContexts = 0;
        var topOneHits = 0;
        var topThreeHits = 0;
        var generalTopOneHits = 0;
        var generalTopThreeHits = 0;
        var conversationTopOneHits = 0;
        var conversationTopThreeHits = 0;
        var supportExamples = new List<ConversationSupportExample>();
        var confidenceExamples = new List<ConversationConfidenceExample>();

        using var reader = new StreamReader(corpusPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            foreach (var words in TokenizeSentences(line, culture))
            {
                for (var index = 2; index < words.Count; index++)
                {
                    var history = $"{words[index - 2]} {words[index - 1]}";
                    var expected = words[index];
                    attempts++;
                    var example = new NextWordExample(history, expected);
                    if (sample.Count < MaximumNextWordEvaluationContexts)
                    {
                        sample.Add(example);
                    }
                    else
                    {
                        var replacement = random.Next(attempts);
                        if (replacement < sample.Count) sample[replacement] = example;
                    }
                }
            }
        }

        foreach (var example in sample)
        {
            var suggestions = (IReadOnlyList<string>)getSuggestions.Invoke(null,
                [example.History, string.Empty, languageTag, 3])!;
            var general = GetCandidateWords(getGeneralCandidates, example.History, languageTag);
            var conversation = GetCandidateWords(getConversationCandidates, example.History, languageTag);
            var conversationConfidence = GetTopProbability(getConversationCandidates, example.History, languageTag);
            var conversationSupport = (long)getConversationSupport.Invoke(null, [example.History, languageTag])!;
            if (suggestions.Count == 0) continue;

            coveredContexts++;
            if (StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], example.Expected)) topOneHits++;
            if (suggestions.Contains(example.Expected, StringComparer.OrdinalIgnoreCase)) topThreeHits++;
            if (general.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(general[0], example.Expected)) generalTopOneHits++;
            if (general.Contains(example.Expected, StringComparer.OrdinalIgnoreCase)) generalTopThreeHits++;
            if (conversation.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(conversation[0], example.Expected)) conversationTopOneHits++;
            if (conversation.Contains(example.Expected, StringComparer.OrdinalIgnoreCase)) conversationTopThreeHits++;
            supportExamples.Add(new ConversationSupportExample(
                conversationSupport,
                StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], example.Expected),
                suggestions.Contains(example.Expected, StringComparer.OrdinalIgnoreCase)));
            confidenceExamples.Add(new ConversationConfidenceExample(
                conversationConfidence,
                StringComparer.OrdinalIgnoreCase.Equals(conversation.FirstOrDefault(), example.Expected),
                StringComparer.OrdinalIgnoreCase.Equals(general.FirstOrDefault(), example.Expected),
                StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], example.Expected)));
        }

        var coverage = sample.Count == 0 ? 0 : (double)coveredContexts / sample.Count;
        var topOneAccuracy = sample.Count == 0 ? 0 : (double)topOneHits / sample.Count;
        var topThreeAccuracy = sample.Count == 0 ? 0 : (double)topThreeHits / sample.Count;
        Console.WriteLine($"Held-out {languageTag} next-word contexts: {attempts:N0}");
        Console.WriteLine($"Evaluated contexts: {sample.Count:N0} (fixed-seed reservoir sample, maximum {MaximumNextWordEvaluationContexts:N0})");
        Console.WriteLine($"Prediction coverage: {coverage:P2} ({coveredContexts:N0}/{sample.Count:N0})");
        Console.WriteLine($"Top-1 accuracy: {topOneAccuracy:P2}");
        Console.WriteLine($"Top-3 accuracy: {topThreeAccuracy:P2}");
        Console.WriteLine($"General model top-1/top-3: {(sample.Count == 0 ? 0 : (double)generalTopOneHits / sample.Count):P2}/{(sample.Count == 0 ? 0 : (double)generalTopThreeHits / sample.Count):P2}");
        Console.WriteLine($"Conversation model top-1/top-3: {(sample.Count == 0 ? 0 : (double)conversationTopOneHits / sample.Count):P2}/{(sample.Count == 0 ? 0 : (double)conversationTopThreeHits / sample.Count):P2}");
        PrintSupportBuckets(supportExamples);
        PrintConfidenceBuckets(confidenceExamples);
        check(attempts >= EvaluationWordCount,
            "Held-out next-word benchmark evaluates enough context windows");
        return attempts;

        static List<string> GetCandidateWords(MethodInfo method, string history, string languageTag) =>
            ((System.Collections.IEnumerable)method.Invoke(null, [history, languageTag])!)
            .Cast<object>()
            .Select(candidate => candidate.GetType().GetProperty("Word")!.GetValue(candidate) as string)
            .OfType<string>()
            .Take(3)
            .ToList();

        static double GetTopProbability(MethodInfo method, string history, string languageTag)
        {
            var candidates = ((System.Collections.IEnumerable)method.Invoke(null, [history, languageTag])!).Cast<object>().ToArray();
            return candidates.Length == 0
                ? 0
                : (double)candidates[0].GetType().GetProperty("Probability")!.GetValue(candidates[0])!;
        }

        static void PrintSupportBuckets(IReadOnlyList<ConversationSupportExample> examples)
        {
            foreach (var (label, predicate) in new (string, Func<long, bool>)[]
                     {
                         ("none", support => support == 0),
                         ("1-9", support => support is >= 1 and < 10),
                         ("10-49", support => support is >= 10 and < 50),
                         ("50+", support => support >= 50)
                     })
            {
                var bucket = examples.Where(example => predicate(example.Support)).ToArray();
                if (bucket.Length == 0) continue;
                Console.WriteLine($"Conversation support {label}: {bucket.Length:N0}; top-1 {(double)bucket.Count(example => example.TopOne) / bucket.Length:P2}; top-3 {(double)bucket.Count(example => example.TopThree) / bucket.Length:P2}");
            }
        }

        static void PrintConfidenceBuckets(IReadOnlyList<ConversationConfidenceExample> examples)
        {
            foreach (var (label, predicate) in new (string, Func<double, bool>)[]
                     {
                         ("<0.05", confidence => confidence < 0.05),
                         ("0.05-0.15", confidence => confidence is >= 0.05 and < 0.15),
                         ("0.15-0.35", confidence => confidence is >= 0.15 and < 0.35),
                         ("0.35+", confidence => confidence >= 0.35)
                     })
            {
                var bucket = examples.Where(example => predicate(example.ConversationConfidence)).ToArray();
                if (bucket.Length == 0) continue;
                Console.WriteLine($"Conversation confidence {label} ({bucket.Length:N0}): model {bucket.Count(example => example.ConversationTopOne) / (double)bucket.Length:P2}, general {bucket.Count(example => example.GeneralTopOne) / (double)bucket.Length:P2}, merged {bucket.Count(example => example.MergedTopOne) / (double)bucket.Length:P2}");
            }
        }
    }

    internal static int RunAnnotatedErrors(
        string annotationPath,
        string languageTag,
        Assembly extras,
        Action<bool, string> check,
        bool heldOutOnly = false)
    {
        var engineType = extras.GetType("WinForge.Modules.Extras.SpellEngine")!;
        var engine = engineType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var findCorrection = engineType.GetMethods()
            .Single(method => method.Name == "FindCorrection" && method.GetParameters().Length == 8);
        var getCandidates = engineType.GetMethod("GetCandidates", BindingFlags.Static | BindingFlags.NonPublic)!;
        var getSuggestions = engineType.GetMethod("GetSuggestions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var isCorrect = engineType.GetMethod("IsCorrect", [typeof(string), typeof(string)])!;
        using var reader = new StreamReader(annotationPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine()?.Split('\t') ?? throw new InvalidDataException("Annotation file is empty.");
        var filenameIndex = Array.IndexOf(header, "Filename");
        var misspellingIndex = Array.IndexOf(header, "Misspelling");
        var typeIndex = Array.IndexOf(header, "Type");
        var correctionIndex = Array.IndexOf(header, "Correction");
        if (filenameIndex < 0 || misspellingIndex < 0 || typeIndex < 0 || correctionIndex < 0)
            throw new InvalidDataException("Annotation file must contain Filename, Misspelling, Type, and Correction columns.");

        var evaluated = 0;
        var exactCorrections = 0;
        var suggestions = 0;
        var candidateHits = 0;
        var suggestionCoverage = 0;
        var suggestionTopOneHits = 0;
        var suggestionTopThreeHits = 0;
        var examples = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split('\t');
            if (fields.Length <= Math.Max(Math.Max(filenameIndex, misspellingIndex), Math.Max(typeIndex, correctionIndex)) ||
                fields[typeIndex] != "M" || (heldOutOnly && !IsHeldOutFile(fields[filenameIndex]))) continue;

            var misspelling = fields[misspellingIndex].Trim();
            var expected = fields[correctionIndex].Trim();
            if (misspelling.Length < 2 || expected.Length == 0 ||
                misspelling.Any(character => !char.IsLetter(character)) || expected.Any(character => !char.IsLetter(character))) continue;

            evaluated++;
            var candidates = ((System.Collections.IEnumerable)getCandidates.Invoke(null,
                [misspelling, languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase)])!).Cast<string>();
            var targetAvailable = candidates.Contains(expected, StringComparer.OrdinalIgnoreCase);
            if (targetAvailable) candidateHits++;
            var actual = (string?)findCorrection.Invoke(engine,
                [misspelling, languageTag, null, true, true, true, false, null]);
            if (actual is not null) suggestions++;
            if (StringComparer.OrdinalIgnoreCase.Equals(actual, expected)) exactCorrections++;
            else if (actual is not null && examples.Count < 20) examples.Add($"{misspelling} -> {actual} (expected {expected})");

            var rankedSuggestions = (IReadOnlyList<string>)getSuggestions.Invoke(engine,
                [misspelling, languageTag, null, true, true, true, false, null, 3])!;
            if (rankedSuggestions.Count > 0) suggestionCoverage++;
            if (rankedSuggestions.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(rankedSuggestions[0], expected))
                suggestionTopOneHits++;
            if (rankedSuggestions.Contains(expected, StringComparer.OrdinalIgnoreCase)) suggestionTopThreeHits++;
        }

        var accuracy = evaluated == 0 ? 0 : (double)exactCorrections / evaluated;
        var coverage = evaluated == 0 ? 0 : (double)suggestions / evaluated;
        var candidateRecall = evaluated == 0 ? 0 : (double)candidateHits / evaluated;
        var precision = suggestions == 0 ? 0 : (double)exactCorrections / suggestions;
        Console.WriteLine($"Annotated {languageTag} spelling errors: {evaluated:N0}" + (heldOutOnly ? " (file-level held-out split)" : string.Empty));
        Console.WriteLine($"Exact correction accuracy: {accuracy:P2} ({exactCorrections:N0}/{evaluated:N0})");
        Console.WriteLine($"Correction coverage: {coverage:P2} ({suggestions:N0}/{evaluated:N0})");
        Console.WriteLine($"Candidate recall: {candidateRecall:P2} ({candidateHits:N0}/{evaluated:N0})");
        Console.WriteLine($"Suggestion precision: {precision:P2} ({exactCorrections:N0}/{suggestions:N0})");
        Console.WriteLine($"Suggestion-strip coverage: {(evaluated == 0 ? 0 : (double)suggestionCoverage / evaluated):P2}");
        Console.WriteLine($"Suggestion-strip top-1: {(evaluated == 0 ? 0 : (double)suggestionTopOneHits / evaluated):P2}");
        Console.WriteLine($"Suggestion-strip top-3: {(evaluated == 0 ? 0 : (double)suggestionTopThreeHits / evaluated):P2}");
        if (examples.Count > 0) Console.WriteLine($"Incorrect corrections: {string.Join("; ", examples)}");
        check(evaluated >= 1_000, "Annotated spelling benchmark evaluates a substantial set of real errors");
        return evaluated;
    }

    internal static int RunTurkishTweetCorrections(
        string datasetPath,
        Assembly extras,
        Action<bool, string> check,
        int maximumRows = int.MaxValue)
    {
        if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        var engineType = extras.GetType("WinForge.Modules.Extras.SpellEngine")!;
        var engine = engineType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var findCorrection = engineType.GetMethods()
            .Single(method => method.Name == "FindCorrection" && method.GetParameters().Length == 8);
        var getCandidates = engineType.GetMethod("GetCandidates", BindingFlags.Static | BindingFlags.NonPublic)!;
        var getRankedCandidates = engineType.GetMethod("GetRankedCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getSuggestions = engineType.GetMethod("GetSuggestions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var isCorrect = engineType.GetMethod("IsCorrect", [typeof(string), typeof(string)])!;
        var contextModelType = extras.GetType("WinForge.Modules.Extras.SpellContextModel")!;
        var getContextBonus = contextModelType.GetMethod("GetBigramBonus", BindingFlags.Static | BindingFlags.NonPublic)!;
        var corrections = 0;
        var exactCorrections = 0;
        var candidateHits = 0;
        var evaluatedErrors = 0;
        var suggestionCoverage = 0;
        var suggestionTopOne = 0;
        var suggestionTopThree = 0;
        var unchangedWords = 0;
        var falseCorrections = 0;
        var incorrectExamples = new List<string>();
        var falseCorrectionExamples = new List<string>();
        var skippedRows = 0;
        var alignedRows = 0;

        using var reader = new StreamReader(datasetPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        _ = reader.ReadLine();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split('\t');
            if (fields.Length < 4)
            {
                skippedRows++;
                continue;
            }

            var inputs = SplitTokens(fields[1]);
            var expectedWords = SplitTokens(fields[2]);
            var annotations = fields[3].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (inputs.Count != expectedWords.Count || inputs.Count != annotations.Length)
            {
                skippedRows++;
                continue;
            }
            if (alignedRows >= maximumRows) break;
            alignedRows++;

            for (var index = 0; index < inputs.Count; index++)
            {
                var input = inputs[index];
                var expected = expectedWords[index];
                var tag = annotations[index];
                if (!IsStandaloneWord(input) || !IsStandaloneWord(expected)) continue;

                var isAnnotatedError = tag.Contains("OOV-ill_formed", StringComparison.Ordinal) &&
                                       !StringComparer.OrdinalIgnoreCase.Equals(input, expected);
                if (!isAnnotatedError)
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(input, expected))
                    {
                        unchangedWords++;
                        var falseCorrection = (string?)findCorrection.Invoke(engine,
                            [input, "tr-TR", GetHistory(inputs, index), true, true, false, false, null]);
                        if (falseCorrection is not null &&
                            !StringComparer.OrdinalIgnoreCase.Equals(input, falseCorrection))
                        {
                            falseCorrections++;
                            if (falseCorrectionExamples.Count < 25)
                                falseCorrectionExamples.Add($"{GetHistory(inputs, index) ?? "(başlangıç)"} [{input}] -> {falseCorrection}");
                        }
                    }
                    continue;
                }

                evaluatedErrors++;
                var history = GetHistory(inputs, index);
                var candidates = ((System.Collections.IEnumerable)getCandidates.Invoke(null, [input, true])!).Cast<string>().ToArray();
                var expectedInCandidates = candidates.Contains(expected, StringComparer.OrdinalIgnoreCase);
                if (expectedInCandidates) candidateHits++;
                var inputIsKnown = (bool)isCorrect.Invoke(engine, [input, "tr-TR"])!;
                var ranked = ((System.Collections.IEnumerable)getRankedCandidates.Invoke(engine,
                    [input.ToLower(CultureInfo.GetCultureInfo("tr-TR")), candidates, history, true, false, "tr-TR", 3])!)
                    .Cast<object>()
                    .Select(candidate => $"{candidate.GetType().GetProperty("Word")!.GetValue(candidate)}:{candidate.GetType().GetProperty("Score")!.GetValue(candidate)}")
                    .ToArray();
                var expectedContextBonus = (double)getContextBonus.Invoke(null, [history, expected, "tr-TR", null, false])!;
                var typedContextBonus = (double)getContextBonus.Invoke(null, [history, input, "tr-TR", null, false])!;
                var actual = (string?)findCorrection.Invoke(engine,
                    [input, "tr-TR", history, true, true, false, false, null]);
                if (actual is not null) corrections++;
                if (StringComparer.OrdinalIgnoreCase.Equals(actual, expected)) exactCorrections++;

                var suggestions = (IReadOnlyList<string>)getSuggestions.Invoke(engine,
                    [input, "tr-TR", history, true, true, false, false, null, 3])!;
                if (suggestions.Count > 0) suggestionCoverage++;
                if (suggestions.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], expected))
                    suggestionTopOne++;
                if (suggestions.Contains(expected, StringComparer.OrdinalIgnoreCase)) suggestionTopThree++;
                if (!StringComparer.OrdinalIgnoreCase.Equals(actual, expected) && incorrectExamples.Count < 25)
                    incorrectExamples.Add($"{history ?? "(başlangıç)"} [{input}] -> {actual ?? "(öneri yok)"}; beklenen: {expected}; adayda: {expectedInCandidates}; biliniyor: {inputIsKnown}; bağlam: {expectedContextBonus:F2}/{typedContextBonus:F2}; sıralama: {string.Join(", ", ranked)}; liste: {string.Join(", ", suggestions)}");
            }
        }

        Console.WriteLine($"TurkishTweets aligned rows: {alignedRows:N0}; skipped rows: {skippedRows:N0}");
        Console.WriteLine($"Annotated Turkish misspellings: {evaluatedErrors:N0}");
        Console.WriteLine($"Exact correction accuracy: {(evaluatedErrors == 0 ? 0 : (double)exactCorrections / evaluatedErrors):P2}");
        Console.WriteLine($"Correction coverage: {(evaluatedErrors == 0 ? 0 : (double)corrections / evaluatedErrors):P2}");
        Console.WriteLine($"Correction precision: {(corrections == 0 ? 0 : (double)exactCorrections / corrections):P2}");
        Console.WriteLine($"Candidate recall: {(evaluatedErrors == 0 ? 0 : (double)candidateHits / evaluatedErrors):P2}");
        Console.WriteLine($"Suggestion coverage: {(evaluatedErrors == 0 ? 0 : (double)suggestionCoverage / evaluatedErrors):P2}");
        Console.WriteLine($"Suggestion top-1/top-3: {(evaluatedErrors == 0 ? 0 : (double)suggestionTopOne / evaluatedErrors):P2}/{(evaluatedErrors == 0 ? 0 : (double)suggestionTopThree / evaluatedErrors):P2}");
        Console.WriteLine($"Unchanged reference words changed: {falseCorrections:N0}/{unchangedWords:N0}");
        if (incorrectExamples.Count > 0) Console.WriteLine($"Incorrect correction examples: {string.Join(" | ", incorrectExamples)}");
        if (falseCorrectionExamples.Count > 0) Console.WriteLine($"False corrections of clean words: {string.Join(" | ", falseCorrectionExamples)}");
        var minimumAlignedRows = Math.Min(maximumRows, 1_000);
        check(alignedRows >= minimumAlignedRows &&
              (maximumRows < 1_000 || unchangedWords >= 1_000 && evaluatedErrors >= 1_000),
            maximumRows < 1_000
                ? "TurkishTweets diagnostic sample evaluates the requested number of rows"
                : "TurkishTweets benchmark evaluates real misspellings and unchanged words");
        return alignedRows;

        static List<string> SplitTokens(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        static bool IsStandaloneWord(string word) => word.Length is >= 2 and <= 40 && word.All(char.IsLetter);
        static string? GetHistory(IReadOnlyList<string> words, int index) => index == 0
            ? null
            : string.Join(' ', words.Skip(Math.Max(0, index - 2)).Take(Math.Min(2, index)));
    }

    internal static int RunTurkishTweetNextWords(string datasetPath, Assembly extras, Action<bool, string> check)
    {
        var contextType = extras.GetType("WinForge.Modules.Extras.SpellContextModel")!;
        var getSuggestions = contextType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "GetNextWordSuggestions" && method.GetParameters().Length == 4);
        var ngramType = extras.GetType("WinForge.Modules.Extras.SpellNgramModel")!;
        var getGeneralCandidates = ngramType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "GetNextWordCandidates" && method.GetParameters().Length == 2);
        var getConversationCandidates = ngramType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "GetConversationNextWordCandidates" && method.GetParameters().Length == 2);
        var getConversationSupport = ngramType.GetMethod("GetConversationContextSupport", BindingFlags.Static | BindingFlags.NonPublic)!;
        var attempts = 0;
        var covered = 0;
        var topOneHits = 0;
        var topThreeHits = 0;
        var generalTopOneHits = 0;
        var generalTopThreeHits = 0;
        var conversationTopOneHits = 0;
        var conversationTopThreeHits = 0;
        var conversationBackedContexts = 0;
        var conversationBackedTopOneHits = 0;
        var conversationBackedTopThreeHits = 0;
        var conversationBackedGeneralTopOneHits = 0;
        var conversationBackedGeneralTopThreeHits = 0;
        var conversationBackedModelTopOneHits = 0;
        var conversationBackedModelTopThreeHits = 0;
        var unseenConversationTopOneHits = 0;
        var unseenConversationTopThreeHits = 0;
        var unseenGeneralTopOneHits = 0;
        var unseenGeneralTopThreeHits = 0;
        var supportExamples = new List<ConversationSupportExample>();
        var confidenceExamples = new List<ConversationConfidenceExample>();

        using var reader = new StreamReader(datasetPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        _ = reader.ReadLine();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split('\t');
            if (fields.Length < 4) continue;
            var words = SplitTokens(fields[2]);
            for (var index = 2; index < words.Length; index++)
            {
                if (!IsStandaloneWord(words[index - 2]) || !IsStandaloneWord(words[index - 1]) || !IsStandaloneWord(words[index]))
                    continue;
                attempts++;
                var history = $"{words[index - 2]} {words[index - 1]}";
                var suggestions = (IReadOnlyList<string>)getSuggestions.Invoke(null,
                    [history, string.Empty, "tr-TR", 3])!;
                var general = GetCandidateWords(getGeneralCandidates, history);
                var conversationDistribution = ((System.Collections.IEnumerable)getConversationCandidates.Invoke(null,
                    [history, "tr-TR"])!).Cast<object>().ToArray();
                var conversation = conversationDistribution
                    .Select(candidate => candidate.GetType().GetProperty("Word")!.GetValue(candidate) as string)
                    .OfType<string>().Take(3).ToList();
                var conversationSupport = (long)getConversationSupport.Invoke(null, [history, "tr-TR"])!;
                var hasConversationEvidence = conversationSupport > 0;
                if (suggestions.Count == 0) continue;
                covered++;
                if (StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], words[index])) topOneHits++;
                if (suggestions.Contains(words[index], StringComparer.OrdinalIgnoreCase)) topThreeHits++;
                if (general.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(general[0], words[index])) generalTopOneHits++;
                if (general.Contains(words[index], StringComparer.OrdinalIgnoreCase)) generalTopThreeHits++;
                if (conversation.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(conversation[0], words[index])) conversationTopOneHits++;
                if (conversation.Contains(words[index], StringComparer.OrdinalIgnoreCase)) conversationTopThreeHits++;
                supportExamples.Add(new ConversationSupportExample(
                    conversationSupport,
                    StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], words[index]),
                    suggestions.Contains(words[index], StringComparer.OrdinalIgnoreCase)));
                var conversationConfidence = conversationDistribution.Length == 0
                    ? 0
                    : (double)conversationDistribution[0].GetType().GetProperty("Probability")!.GetValue(conversationDistribution[0])!;
                confidenceExamples.Add(new ConversationConfidenceExample(
                    conversationConfidence,
                    StringComparer.OrdinalIgnoreCase.Equals(conversation.FirstOrDefault(), words[index]),
                    StringComparer.OrdinalIgnoreCase.Equals(general.FirstOrDefault(), words[index]),
                    StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], words[index])));
                if (hasConversationEvidence)
                {
                    conversationBackedContexts++;
                    if (StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], words[index])) conversationBackedTopOneHits++;
                    if (suggestions.Contains(words[index], StringComparer.OrdinalIgnoreCase)) conversationBackedTopThreeHits++;
                    if (general.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(general[0], words[index])) conversationBackedGeneralTopOneHits++;
                    if (general.Contains(words[index], StringComparer.OrdinalIgnoreCase)) conversationBackedGeneralTopThreeHits++;
                    if (conversation.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(conversation[0], words[index])) conversationBackedModelTopOneHits++;
                    if (conversation.Contains(words[index], StringComparer.OrdinalIgnoreCase)) conversationBackedModelTopThreeHits++;
                }
                else
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(suggestions[0], words[index])) unseenConversationTopOneHits++;
                    if (suggestions.Contains(words[index], StringComparer.OrdinalIgnoreCase)) unseenConversationTopThreeHits++;
                    if (general.Count > 0 && StringComparer.OrdinalIgnoreCase.Equals(general[0], words[index])) unseenGeneralTopOneHits++;
                    if (general.Contains(words[index], StringComparer.OrdinalIgnoreCase)) unseenGeneralTopThreeHits++;
                }
            }
        }

        Console.WriteLine($"TurkishTweets next-word contexts: {attempts:N0}");
        Console.WriteLine($"Prediction coverage: {(attempts == 0 ? 0 : (double)covered / attempts):P2}");
        Console.WriteLine($"Top-1 accuracy: {(attempts == 0 ? 0 : (double)topOneHits / attempts):P2}");
        Console.WriteLine($"Top-3 accuracy: {(attempts == 0 ? 0 : (double)topThreeHits / attempts):P2}");
        Console.WriteLine($"General model top-1/top-3: {(attempts == 0 ? 0 : (double)generalTopOneHits / attempts):P2}/{(attempts == 0 ? 0 : (double)generalTopThreeHits / attempts):P2}");
        Console.WriteLine($"Conversation model top-1/top-3: {(attempts == 0 ? 0 : (double)conversationTopOneHits / attempts):P2}/{(attempts == 0 ? 0 : (double)conversationTopThreeHits / attempts):P2}");
        Console.WriteLine($"Conversation-backed contexts: {conversationBackedContexts:N0}/{attempts:N0}");
        Console.WriteLine($"Merged backed top-1/top-3: {(conversationBackedContexts == 0 ? 0 : (double)conversationBackedTopOneHits / conversationBackedContexts):P2}/{(conversationBackedContexts == 0 ? 0 : (double)conversationBackedTopThreeHits / conversationBackedContexts):P2}");
        Console.WriteLine($"General backed top-1/top-3: {(conversationBackedContexts == 0 ? 0 : (double)conversationBackedGeneralTopOneHits / conversationBackedContexts):P2}/{(conversationBackedContexts == 0 ? 0 : (double)conversationBackedGeneralTopThreeHits / conversationBackedContexts):P2}");
        Console.WriteLine($"Conversation-backed model top-1/top-3: {(conversationBackedContexts == 0 ? 0 : (double)conversationBackedModelTopOneHits / conversationBackedContexts):P2}/{(conversationBackedContexts == 0 ? 0 : (double)conversationBackedModelTopThreeHits / conversationBackedContexts):P2}");
        Console.WriteLine($"Merged unseen top-1/top-3: {(attempts == conversationBackedContexts ? 0 : (double)unseenConversationTopOneHits / (attempts - conversationBackedContexts)):P2}/{(attempts == conversationBackedContexts ? 0 : (double)unseenConversationTopThreeHits / (attempts - conversationBackedContexts)):P2}");
        Console.WriteLine($"General unseen top-1/top-3: {(attempts == conversationBackedContexts ? 0 : (double)unseenGeneralTopOneHits / (attempts - conversationBackedContexts)):P2}/{(attempts == conversationBackedContexts ? 0 : (double)unseenGeneralTopThreeHits / (attempts - conversationBackedContexts)):P2}");
        PrintSupportBuckets(supportExamples);
        PrintConfidenceBuckets(confidenceExamples);
        check(attempts >= 1_000, "TurkishTweets next-word benchmark evaluates enough real contexts");
        return attempts;

        static string[] SplitTokens(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        static bool IsStandaloneWord(string word) => word.Length is >= 2 and <= 40 && word.All(char.IsLetter);
        static List<string> GetCandidateWords(MethodInfo method, string history) =>
            ((System.Collections.IEnumerable)method.Invoke(null, [history, "tr-TR"])!)
            .Cast<object>()
            .Select(candidate => candidate.GetType().GetProperty("Word")!.GetValue(candidate) as string)
            .OfType<string>()
            .Take(3)
            .ToList();

        static void PrintSupportBuckets(IReadOnlyList<ConversationSupportExample> examples)
        {
            foreach (var (label, predicate) in new (string, Func<long, bool>)[]
                     {
                         ("none", support => support == 0),
                         ("1-9", support => support is >= 1 and < 10),
                         ("10-49", support => support is >= 10 and < 50),
                         ("50+", support => support >= 50)
                     })
            {
                var bucket = examples.Where(example => predicate(example.Support)).ToArray();
                if (bucket.Length == 0) continue;
                Console.WriteLine($"Conversation support {label}: {bucket.Length:N0}; top-1 {(double)bucket.Count(example => example.TopOne) / bucket.Length:P2}; top-3 {(double)bucket.Count(example => example.TopThree) / bucket.Length:P2}");
            }
        }

        static void PrintConfidenceBuckets(IReadOnlyList<ConversationConfidenceExample> examples)
        {
            foreach (var (label, predicate) in new (string, Func<double, bool>)[]
                     {
                         ("<0.05", confidence => confidence < 0.05),
                         ("0.05-0.15", confidence => confidence is >= 0.05 and < 0.15),
                         ("0.15-0.35", confidence => confidence is >= 0.15 and < 0.35),
                         ("0.35+", confidence => confidence >= 0.35)
                     })
            {
                var bucket = examples.Where(example => predicate(example.ConversationConfidence)).ToArray();
                if (bucket.Length == 0) continue;
                Console.WriteLine($"Conversation confidence {label} ({bucket.Length:N0}): model {bucket.Count(example => example.ConversationTopOne) / (double)bucket.Length:P2}, general {bucket.Count(example => example.GeneralTopOne) / (double)bucket.Length:P2}, merged {bucket.Count(example => example.MergedTopOne) / (double)bucket.Length:P2}");
            }
        }
    }

    private static bool IsHeldOutFile(string filename) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(filename))[0] % 5 == 0;

    private readonly record struct NextWordExample(string History, string Expected);
    private readonly record struct ConversationSupportExample(long Support, bool TopOne, bool TopThree);
    private readonly record struct ConversationConfidenceExample(
        double ConversationConfidence,
        bool ConversationTopOne,
        bool GeneralTopOne,
        bool MergedTopOne);

    private static IEnumerable<List<string>> TokenizeSentences(string text, CultureInfo culture)
    {
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('.' or '!' or '?' or '\n' or '\r')) continue;
            var sentence = Tokenize(text[start..index], culture);
            if (sentence.Count > 0) yield return sentence;
            start = index + 1;
        }
        var remaining = Tokenize(text[start..], culture);
        if (remaining.Count > 0) yield return remaining;
    }

    private static List<string> Tokenize(string text, CultureInfo culture)
    {
        var words = new List<string>();
        var buffer = new StringBuilder();
        foreach (var character in text.Normalize(NormalizationForm.FormC).ToLower(culture))
        {
            if (char.IsLetter(character)) buffer.Append(character);
            else if (character is '\'' or '’' && buffer.Length > 0) { }
            else if (buffer.Length > 0)
            {
                if (buffer.Length <= 40) words.Add(buffer.ToString());
                buffer.Clear();
            }
        }
        if (buffer.Length is >= 2 and <= 40) words.Add(buffer.ToString());
        return words;
    }

    private static List<string> SampleHeldOutWords(string corpusPath, string languageTag, Func<string, bool> isEligible)
    {
        var random = new Random(20261028);
        var culture = System.Globalization.CultureInfo.GetCultureInfo(languageTag);
        var sample = new List<string>(EvaluationWordCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        using var reader = new StreamReader(corpusPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) is not null && scanned < 250_000)
        {
            var index = 0;
            while (index < line.Length)
            {
                while (index < line.Length && !char.IsLetter(line[index])) index++;
                var start = index;
                while (index < line.Length && char.IsLetter(line[index])) index++;
                if (index - start is < 5 or > 16) continue;

                var word = line[start..index].ToLower(culture);
                if (!seen.Add(word) || !isEligible(word)) continue;
                scanned++;
                if (sample.Count < EvaluationWordCount) sample.Add(word);
                else
                {
                    var replacement = random.Next(scanned);
                    if (replacement < sample.Count) sample[replacement] = word;
                }
            }
        }
        return sample;
    }

    private static string? TransposeMiddleCharacters(string word)
    {
        var index = word.Length / 2;
        if (index >= word.Length - 1 || word[index] == word[index + 1]) return null;
        var characters = word.ToCharArray();
        (characters[index], characters[index + 1]) = (characters[index + 1], characters[index]);
        return new string(characters);
    }
}
