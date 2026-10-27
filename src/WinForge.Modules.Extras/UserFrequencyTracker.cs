using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace WinForge.Modules.Extras;

/// <summary>
/// Tracks personalized word usage frequencies and automatically learns
/// user jargon and recurring terms. Persists data to the local AppData store.
/// </summary>
internal sealed class UserFrequencyTracker
{
    private const int AutoLearnThreshold = 3;
    private static readonly Lazy<UserFrequencyTracker> LazyInstance = new(() => new UserFrequencyTracker());

    public static UserFrequencyTracker Instance => LazyInstance.Value;

    private readonly string _storagePath;
    private readonly ConcurrentDictionary<string, int> _frequencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _unrecognizedCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _learnedWords = new(StringComparer.OrdinalIgnoreCase);
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
        _learnedWords.Contains(word);

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

    public void RecordWord(string word, bool isKnownWord)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2) return;
        var cleanWord = word.Trim();

        _frequencies.AddOrUpdate(cleanWord, 1, (_, current) => Math.Min(current + 1, 10000));

        if (!isKnownWord && !_learnedWords.Contains(cleanWord))
        {
            var occurrences = _unrecognizedCounts.AddOrUpdate(cleanWord, 1, (_, c) => c + 1);
            if (occurrences >= AutoLearnThreshold)
            {
                lock (_saveLock)
                {
                    _learnedWords.Add(cleanWord);
                }
                SaveDebounced();
                return;
            }
        }

        SaveDebounced();
    }

    public void LearnWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        lock (_saveLock)
        {
            _learnedWords.Add(word.Trim());
        }
        SaveImmediate();
    }

    public void Clear()
    {
        lock (_saveLock)
        {
            _frequencies.Clear();
            _unrecognizedCounts.Clear();
            _learnedWords.Clear();
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
                    LearnedWords = [.. _learnedWords]
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
                    foreach (var w in model.LearnedWords) _learnedWords.Add(w);
                }
            }
            catch
            {
                // Stale or corrupt JSON fallback
            }
        }
    }

    private sealed class UserFrequencyModel
    {
        public Dictionary<string, int>? Frequencies { get; set; }
        public List<string>? LearnedWords { get; set; }
    }
}

