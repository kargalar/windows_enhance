using System.IO.Compression;
using System.IO;
using System.Reflection;

namespace WinForge.Modules.Extras;

/// <summary>High-confidence English non-word corrections learned from annotated errors.</summary>
internal static class SpellErrorModel
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Corrections = new(Load);

    internal static bool TryGetCorrection(string word, out string correction) =>
        Corrections.Value.TryGetValue(word, out correction!);

    private static IReadOnlyDictionary<string, string> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("EnglishSpellingErrorModel.tsv.gz", StringComparison.Ordinal));
        if (resource is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var compressed = assembly.GetManifestResourceStream(resource);
        if (compressed is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split('\t');
            if (fields.Length == 3 && fields[0].All(char.IsLetter) && fields[1].All(char.IsLetter) &&
                int.TryParse(fields[2], out var count) && count >= 2)
                corrections.TryAdd(fields[0], fields[1]);
        }
        return corrections;
    }
}
