using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace WinForge.Modules.Extras;

internal static class SelectionTranslationLanguage
{
    private static readonly Lazy<LanguageModel> Turkish = new(() => Load("Turkish"));
    private static readonly Lazy<LanguageModel> English = new(() => Load("English"));

    internal static (string Source, string Target) ShortcutPair(string input, string configuredTarget)
    {
        if (!configuredTarget.Equals("en", StringComparison.OrdinalIgnoreCase))
            return ("tr", configuredTarget);

        SelectionTranslationService.ValidateInput(input);
        var normalized = input.Normalize(NormalizationForm.FormC).Replace('İ', 'i').ToLowerInvariant();
        var words = Regex.Matches(normalized, @"\p{L}+");
        if (words.Count == 0)
            throw new InvalidOperationException("Seçimin dili belirlenemedi. Türkçe veya İngilizce bir metin seçin.");

        var turkish = Turkish.Value;
        var english = English.Value;
        var score = 0.0;
        foreach (Match word in words)
        {
            var value = word.Value;
            score += Math.Log(turkish.WordFrequency(value) / english.WordFrequency(value));

            // Character trigrams supply evidence for inflected words and
            // spellings that don't occur in the dialogue vocabulary.
            var padded = " " + value + " ";
            var gramScore = 0.0;
            for (var i = 0; i <= padded.Length - 3; i++)
            {
                var gram = padded.Substring(i, 3);
                gramScore += Math.Log(turkish.GramFrequency(gram) / english.GramFrequency(gram));
            }
            score += 2 * gramScore / value.Length;
        }

        if (Math.Abs(score) < 0.5)
            throw new InvalidOperationException("Seçimin dili belirlenemedi. Daha uzun bir Türkçe veya İngilizce metin seçin.");

        return score > 0 ? ("tr", "en") : ("en", "tr");
    }

    private static LanguageModel Load(string language)
    {
        var resource = $"WinForge.Modules.Extras.Resources.{language}DialogueNgramModel.tsv.gz";
        using var stream = typeof(SelectionTranslationLanguage).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Dil algılama modeli bulunamadı.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var words = new Dictionary<string, double>(StringComparer.Ordinal);
        var grams = new Dictionary<string, double>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line && line.StartsWith("u\t", StringComparison.Ordinal))
        {
            var fields = line.Split('\t');
            if (fields.Length != 3 || !int.TryParse(fields[2], out var count) || count <= 0) continue;

            var word = fields[1];
            var weight = Math.Sqrt(count);
            AddWord(word, weight);
            if (language == "Turkish")
            {
                var ascii = word.Replace('ç', 'c').Replace('ğ', 'g').Replace('ı', 'i')
                    .Replace('ö', 'o').Replace('ş', 's').Replace('ü', 'u');
                if (ascii != word) AddWord(ascii, weight / 4);
            }
        }
        return new LanguageModel(words, grams, words.Values.Sum(), grams.Values.Sum());

        void AddWord(string word, double weight)
        {
            words[word] = words.GetValueOrDefault(word) + weight;
            var padded = " " + word + " ";
            for (var i = 0; i <= padded.Length - 3; i++)
            {
                var gram = padded.Substring(i, 3);
                grams[gram] = grams.GetValueOrDefault(gram) + weight;
            }
        }
    }

    private sealed record LanguageModel(Dictionary<string, double> Words, Dictionary<string, double> Grams,
        double TotalWords, double TotalGrams)
    {
        internal double WordFrequency(string word) => Words.GetValueOrDefault(word) / TotalWords + 0.00002;
        internal double GramFrequency(string gram) => Grams.GetValueOrDefault(gram) / TotalGrams + 0.00001;
    }
}
