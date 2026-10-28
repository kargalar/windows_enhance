using System.IO;
using ZemberekDotNet.Morphology;

namespace WinForge.Modules.Extras;

/// <summary>Provides Turkish word-form validation using Zemberek's analyzer.</summary>
internal static class TurkishMorphologyService
{
    private static readonly object InitializationLock = new();
    private static readonly Lazy<TurkishMorphology?> Analyzer = new(CreateAnalyzer, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static bool IsAvailable => Analyzer.Value is not null;

    internal static bool IsKnownWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Any(character => !char.IsLetter(character))) return false;

        var analyzer = Analyzer.Value;
        if (analyzer is null) return false;

        try
        {
            return analyzer.Analyze(word)
                .GetAnalysisResults()
                .Any(analysis => !analysis.IsUnknown());
        }
        catch
        {
            return false;
        }
    }

    private static TurkishMorphology? CreateAnalyzer()
    {
        lock (InitializationLock)
        {
            var originalDirectory = Environment.CurrentDirectory;
            var resourceDirectory = FindResourceDirectory();
            if (resourceDirectory is null) return null;

            try
            {
                // Zemberek resolves its data files relative to the process working directory.
                Environment.CurrentDirectory = resourceDirectory;
                return TurkishMorphology.CreateWithDefaults();
            }
            catch
            {
                return null;
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }
        }
    }

    private static string? FindResourceDirectory()
    {
        var analyzerDirectory = Path.GetDirectoryName(typeof(TurkishMorphology).Assembly.Location);
        var candidates = new[] { AppContext.BaseDirectory, analyzerDirectory }
            .OfType<string>()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return candidates.FirstOrDefault(HasRequiredResources);
    }

    private static bool HasRequiredResources(string directory) =>
        File.Exists(Path.Combine(directory, "Resources", "tr", "lexicon.bin")) &&
        File.Exists(Path.Combine(directory, "Resources", "Text", "html-char-map-full.txt")) &&
        File.Exists(Path.Combine(directory, "Resources", "tokenization", "abbreviations.txt"));
}
