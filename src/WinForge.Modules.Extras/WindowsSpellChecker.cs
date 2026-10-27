using System.Globalization;
using System.Runtime.InteropServices;

namespace WinForge.Modules.Extras;

/// <summary>Wraps the Windows platform spell checker (ISpellChecker) for single-word corrections.</summary>
internal sealed class WindowsSpellChecker
{
    private const int MaxEditDistance = 5;
    private readonly Dictionary<string, ISpellChecker?> _checkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISpellCheckerFactory? _factory;

    public WindowsSpellChecker()
    {
        try { _factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass(); }
        catch (COMException) { _factory = null; }
    }

    public bool IsAvailable => _factory is not null;

    public bool IsLanguageSupported(string languageTag) => GetChecker(languageTag) is not null;

    public bool IsCorrect(string word, string languageTag)
    {
        var checker = GetChecker(languageTag);
        if (checker is null) return false;
        checker.Check(word, out var errors);
        return errors.Next(out _) != 0;
    }

    public string? FindCorrection(string word, string languageTag)
    {
        var checker = GetChecker(languageTag);
        if (checker is null) return null;
        checker.Check(word, out var errors);
        if (errors.Next(out var error) != 0 || error is null) return null;
        if (error.StartIndex != 0 || error.Length != word.Length) return null;

        var candidate = error.CorrectiveAction switch
        {
            CorrectiveAction.Replace => error.Replacement,
            CorrectiveAction.GetSuggestions => BestSuggestion(checker, word, languageTag),
            _ => null
        };
        return IsSafeCandidate(word, candidate, languageTag) ? candidate : null;
    }

    internal static bool IsSafeCandidate(string word, string? candidate, string languageTag)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Any(char.IsWhiteSpace)) return false;
        if (candidate.Any(character => !char.IsLetter(character))) return false;
        var culture = GetCulture(languageTag);
        var distance = CalculateEditDistance(word.ToLower(culture), candidate.ToLower(culture));
        return distance > 0 && distance <= MaxEditDistance;
    }

    private static string? BestSuggestion(ISpellChecker checker, string word, string languageTag)
    {
        checker.Suggest(word, out var suggestions);
        var candidates = new List<string>();
        var buffer = new string[1];
        while (suggestions.Next(1, buffer, IntPtr.Zero) == 0 && candidates.Count < 10)
        {
            if (IsSafeCandidate(word, buffer[0], languageTag))
                candidates.Add(buffer[0]);
        }
        if (candidates.Count == 0) return null;

        var culture = GetCulture(languageTag);
        var lowerWord = word.ToLower(culture);

        return candidates
            .OrderByDescending(c => IsSubsequence(lowerWord, c.ToLower(culture)))
            .ThenByDescending(c => c.EndsWith(lowerWord[^1].ToString(), StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(c => CalculateEditDistance(lowerWord, c.ToLower(culture)))
            .FirstOrDefault();
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

    internal static CultureInfo GetCulture(string languageTag)
    {
        try { return CultureInfo.GetCultureInfo(languageTag); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    private ISpellChecker? GetChecker(string languageTag)
    {
        if (_factory is null) return null;
        if (_checkers.TryGetValue(languageTag, out var cached)) return cached;
        ISpellChecker? checker = null;
        try
        {
            _factory.IsSupported(languageTag, out var isSupported);
            if (isSupported != 0) _factory.CreateSpellChecker(languageTag, out checker);
        }
        catch (COMException) { checker = null; }
        _checkers[languageTag] = checker;
        return checker;
    }

    private enum CorrectiveAction
    {
        None = 0,
        GetSuggestions = 1,
        Replace = 2,
        Delete = 3
    }

    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    private class SpellCheckerFactoryClass { }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        void get_SupportedLanguages(out IEnumString languages);
        void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int isSupported);
        void CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker checker);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        void get_LanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string languageTag);
        void Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError errors);
        void Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString suggestions);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig] int Next(out ISpellingError? error);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        uint StartIndex { get; }
        uint Length { get; }
        CorrectiveAction CorrectiveAction { get; }
        string Replacement { [return: MarshalAs(UnmanagedType.LPWStr)] get; }
    }

    [ComImport, Guid("00000101-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumString
    {
        [PreserveSig] int Next(int count, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 0), Out] string[] items, IntPtr fetched);
    }
}
