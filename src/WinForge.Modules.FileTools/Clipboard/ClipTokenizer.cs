using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinForge.ClipboardLine;

/// <summary>CLIP byte-pair encoding, using the same 48,894 merge ranks as ClipBoardLine.</summary>
internal sealed partial class ClipTokenizer
{
    private readonly Dictionary<string, int> _vocabulary;
    private readonly Dictionary<string, int> _ranks;
    private readonly Dictionary<byte, char> _characters;
    private readonly Dictionary<string, string[]> _cache = new(StringComparer.Ordinal);

    internal ClipTokenizer(string vocabularyFile, string mergesFile)
    {
        _vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(vocabularyFile)) ?? throw new InvalidDataException("CLIP sözlüğü geçersiz.");
        _ranks = File.ReadLines(mergesFile).Skip(1).Take(48_894).Select((line, index) => (line, index)).ToDictionary(pair => pair.line, pair => pair.index, StringComparer.Ordinal);
        var bytes = Enumerable.Range(33, 94).Concat(Enumerable.Range(161, 12)).Concat(Enumerable.Range(174, 82)).ToList();
        _characters = bytes.ToDictionary(value => (byte)value, value => (char)value);
        var next = 256;
        for (var value = 0; value < 256; value++) if (!_characters.ContainsKey((byte)value)) _characters[(byte)value] = (char)next++;
    }

    internal long[] Encode(string value)
    {
        value = Whitespace().Replace(value[..Math.Min(value.Length, 2048)].ToLowerInvariant().Trim(), " ");
        var tokens = new List<long> { 49406 };
        foreach (Match match in Words().Matches(value))
        {
            var encoded = new string(Encoding.UTF8.GetBytes(match.Value).Select(character => _characters[character]).ToArray());
            foreach (var part in Split(encoded)) if (_vocabulary.TryGetValue(part, out var id) && id is >= 0 and <= 49407) tokens.Add(id);
        }
        var output = new long[77]; tokens.Take(76).ToArray().CopyTo(output, 0); output[Math.Min(tokens.Count, 76)] = 49407; return output;
    }

    private string[] Split(string word)
    {
        if (_cache.TryGetValue(word, out var cached)) return cached;
        var parts = word.Select(character => character.ToString()).ToList(); if (parts.Count == 0) return [];
        parts[^1] += "</w>";
        while (parts.Count > 1)
        {
            var best = int.MaxValue; var selected = -1;
            for (var index = 0; index < parts.Count - 1; index++)
                if (_ranks.TryGetValue(parts[index] + " " + parts[index + 1], out var rank) && rank < best) { best = rank; selected = index; }
            if (selected < 0) break;
            var first = parts[selected]; var second = parts[selected + 1]; var merged = new List<string>();
            for (var index = 0; index < parts.Count; index++)
                if (parts[index] == first && index + 1 < parts.Count && parts[index + 1] == second) { merged.Add(first + second); index++; }
                else merged.Add(parts[index]);
            parts = merged;
        }
        if (_cache.Count >= 2048) _cache.Clear(); _cache[word] = parts.ToArray(); return _cache[word];
    }

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)] private static partial Regex Whitespace();
    [GeneratedRegex("<\\|startoftext\\|>|<\\|endoftext\\|>|'s|'t|'re|'ve|'m|'ll|'d|[\\p{L}]+|[\\p{N}]|[^\\s\\p{L}\\p{N}]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Words();
}
