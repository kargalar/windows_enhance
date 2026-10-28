using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace WinForge.Modules.Extras;

/// <summary>
/// Tracks confirmed word and bigram usage. Unknown input is not treated as
/// spelling evidence; users must explicitly add accepted words to the dictionary.
/// </summary>
internal sealed class UserFrequencyTracker
{
    private const char BigramSeparator = '\u001f';
    private static readonly Lazy<UserFrequencyTracker> LazyInstance = new(() => new UserFrequencyTracker());

    public static UserFrequencyTracker Instance => LazyInstance.Value;

    private readonly string _storagePath;
    private readonly ConcurrentDictionary<string, int> _frequencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _bigramFrequencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _trigramFrequencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _learnedWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _configuredWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _saveLock = new();
    private DateTime _lastSave = DateTime.MinValue;

    public UserFrequencyTracker(string? storagePath = null)
    {
        _storagePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinForge", "AutoCorrectUserFrequencies.json");

        Load();
    }

    public bool IsLearned(string word) =>
        _learnedWords.ContainsKey(word) || _configuredWords.ContainsKey(word);

    public int GetFrequency(string word) =>
        _frequencies.TryGetValue(word, out var count) ? count : 0;

    public double GetFrequencyBonus(string candidate)
    {
        if (_frequencies.TryGetValue(candidate, out var count) && count > 0)
        {
            // Diminishing returns logarithmic bonus (max 0.50)
            return Math.Min(0.50, Math.Log10(count + 1) * 0.25);
        }
        return 0.0;
    }

    public double GetBigramFrequencyBonus(string previousWord, string candidate)
    {
        var key = CreateBigramKey(previousWord, candidate);
        if (key is null || !_bigramFrequencies.TryGetValue(key, out var count) || count < 2) return 0;
        return Math.Min(0.8, Math.Log10(count) * 0.45);
    }

    internal double GetTrigramFrequencyBonus(string previousWords, string candidate)
    {
        var key = CreateTrigramKey(previousWords, candidate);
        if (key is null || !_trigramFrequencies.TryGetValue(key, out var count) || count < 2) return 0;
        return Math.Min(1.0, Math.Log10(count) * 0.55);
    }

    internal IReadOnlyList<WeightedNextWord> GetNextWords(string previousWords, int maximumResults = 12)
    {
        if (maximumResults <= 0) return [];

        var trigramPrefix = CreateTrigramPrefix(previousWords);
        if (trigramPrefix is not null)
        {
            var trigramCandidates = GetCandidates(_trigramFrequencies, trigramPrefix);
            if (trigramCandidates.Length > 0) return trigramCandidates.Take(maximumResults).ToArray();
        }

        var previousWord = previousWords.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        var bigramPrefix = CreateBigramKey(previousWord, string.Empty);
        return bigramPrefix is null ? [] : GetCandidates(_bigramFrequencies, bigramPrefix).Take(maximumResults).ToArray();
    }

    private static WeightedNextWord[] GetCandidates(ConcurrentDictionary<string, int> frequencies, string keyPrefix) => frequencies
            .Where(entry => entry.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) && entry.Value >= 2)
            .Select(entry => new WeightedNextWord(entry.Key[(entry.Key.LastIndexOf(BigramSeparator) + 1)..], entry.Value))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Word, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void RecordWord(string word, bool isKnownWord) => RecordWord(word, isKnownWord, null);

    public void RecordWord(string word, bool isKnownWord, string? previousWord)
    {
        // Typing an unrecognized token is not evidence that it is spelled correctly.
        // Auto-learning from repetitions otherwise permanently protects repeated typos.
        if (!isKnownWord || string.IsNullOrWhiteSpace(word) || word.Length < 2) return;
        var cleanWord = word.Trim();

        _frequencies.AddOrUpdate(cleanWord, 1, (_, current) => Math.Min(current + 1, 10000));
        var bigramKey = CreateBigramKey(previousWord, cleanWord);
        if (bigramKey is not null)
        {
            _bigramFrequencies.AddOrUpdate(bigramKey, 1, (_, current) => Math.Min(current + 1, 10000));
        }
        var trigramKey = CreateTrigramKey(previousWord, cleanWord);
        if (trigramKey is not null)
        {
            _trigramFrequencies.AddOrUpdate(trigramKey, 1, (_, current) => Math.Min(current + 1, 10000));
        }
        SaveDebounced();
    }

    public void LearnWord(string word)
    {
        LearnWords([word]);
    }

    public void LearnWords(IEnumerable<string> words)
    {
        var changed = false;
        lock (_saveLock)
        {
            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word)) continue;
                var cleanWord = word.Trim();
                if (cleanWord.Length is < 2 or > 40 || cleanWord.Any(character => !char.IsLetter(character))) continue;
                if (!_learnedWords.TryAdd(cleanWord, 0)) continue;

                _frequencies.AddOrUpdate(cleanWord, 1, (_, current) => Math.Min(current + 1, 10000));
                changed = true;
            }
        }
        if (changed) SaveImmediate();
    }

    public void SetConfiguredWords(IEnumerable<string> words)
    {
        var configuredWords = words
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => word.Trim())
            .Where(word => word.Length is >= 2 and <= 40 && word.All(char.IsLetter))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_saveLock)
        {
            _configuredWords.Clear();
            foreach (var word in configuredWords) _configuredWords.TryAdd(word, 0);
        }
        SaveImmediate();
    }

    public void Clear()
    {
        lock (_saveLock)
        {
            _frequencies.Clear();
            _bigramFrequencies.Clear();
            _trigramFrequencies.Clear();
            _learnedWords.Clear();
            _configuredWords.Clear();
            if (File.Exists(_storagePath))
            {
                try { File.Delete(_storagePath); } catch { /* Ignore cleanup errors */ }
            }
        }
    }

    private void SaveDebounced()
    {
        if ((DateTime.UtcNow - _lastSave).TotalSeconds < 5) return;
        SaveImmediate();
    }

    public void SaveImmediate()
    {
        lock (_saveLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var model = new UserFrequencyModel
                {
                    Frequencies = new Dictionary<string, int>(_frequencies, StringComparer.OrdinalIgnoreCase),
                    BigramFrequencies = new Dictionary<string, int>(_bigramFrequencies, StringComparer.OrdinalIgnoreCase),
                    TrigramFrequencies = new Dictionary<string, int>(_trigramFrequencies, StringComparer.OrdinalIgnoreCase),
                    LearnedWords = [.. _learnedWords.Keys],
                    ConfiguredWords = [.. _configuredWords.Keys]
                };

                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
                _lastSave = DateTime.UtcNow;
            }
            catch
            {
                // Non-blocking disk failure tolerance
            }
        }
    }

    private void Load()
    {
        lock (_saveLock)
        {
            try
            {
                if (!File.Exists(_storagePath)) return;
                var json = File.ReadAllText(_storagePath);
                var model = JsonSerializer.Deserialize<UserFrequencyModel>(json);
                if (model is null) return;

                if (model.Frequencies is not null)
                {
                    foreach (var (k, v) in model.Frequencies) _frequencies[k] = v;
                }
                if (model.LearnedWords is not null)
                {
                    foreach (var w in model.LearnedWords) _learnedWords.TryAdd(w, 0);
                }
                if (model.ConfiguredWords is not null)
                {
                    foreach (var word in model.ConfiguredWords) _configuredWords.TryAdd(word, 0);
                }
                if (model.BigramFrequencies is not null)
                {
                    foreach (var (key, count) in model.BigramFrequencies)
                    {
                        if (key.Contains(BigramSeparator) && count > 0) _bigramFrequencies[key] = Math.Min(count, 10000);
                    }
                }
                if (model.TrigramFrequencies is not null)
                {
                    foreach (var (key, count) in model.TrigramFrequencies)
                    {
                        if (key.Count(character => character == BigramSeparator) == 2 && count > 0)
                            _trigramFrequencies[key] = Math.Min(count, 10000);
                    }
                }
            }
            catch
            {
                // Stale or corrupt JSON fallback
            }
        }
    }

    private static string? CreateBigramKey(string? previousWord, string word)
    {
        if (string.IsNullOrWhiteSpace(previousWord)) return null;
        var previous = previousWord.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (string.IsNullOrEmpty(previous) || previous.Length > 40 || word.Length > 40) return null;
        return $"{previous}{BigramSeparator}{word}";
    }

    private static string? CreateTrigramKey(string? previousWords, string word)
    {
        var prefix = CreateTrigramPrefix(previousWords);
        return prefix is null || word.Length > 40 || word.Any(character => !char.IsLetter(character))
            ? null
            : $"{prefix}{word}";
    }

    private static string? CreateTrigramPrefix(string? previousWords)
    {
        if (string.IsNullOrWhiteSpace(previousWords)) return null;
        var words = previousWords.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2) return null;
        var context = words.TakeLast(2).ToArray();
        if (context.Any(word => word.Length is 0 or > 40 || word.Any(character => !char.IsLetter(character)))) return null;
        return $"{context[0]}{BigramSeparator}{context[1]}{BigramSeparator}";
    }

    internal readonly record struct WeightedNextWord(string Word, int Count);

    private sealed class UserFrequencyModel
    {
        public Dictionary<string, int>? Frequencies { get; set; }
        public Dictionary<string, int>? BigramFrequencies { get; set; }
        public Dictionary<string, int>? TrigramFrequencies { get; set; }
        public List<string>? LearnedWords { get; set; }
        public List<string>? ConfiguredWords { get; set; }
    }
}

