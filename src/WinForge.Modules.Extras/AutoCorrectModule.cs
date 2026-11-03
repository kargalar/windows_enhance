using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinForge.Core;
using static WinForge.Modules.Extras.KeyboardInputSender;

namespace WinForge.Modules.Extras;

/// <summary>
/// Autonomous, native auto-correct and typo fixing module.
/// Completely independent of Windows platform spellchecker.
/// Features intelligent Turkish & English corrections, de-asciification,
/// keyboard proximity heuristics, text expansions, and Android-style undo on Backspace.
/// </summary>
public sealed class AutoCorrectModule : ITweakModule, IAsyncTweakModule
{
    private const string AutomaticLanguage = "Klavye düzenine göre";
    private const string TurkishAndEnglish = "Türkçe + English";
    private const string TurkishLanguageTag = "tr-TR";
    private const string EnglishLanguageTag = "en-US";
    private const string DefaultCustomReplacements = "tşk=teşekkürler; saol=sağol; rca=rica ederim; brb=be right back; omw=on my way";
    private const int InjectedFlag = 0x10;
    private const int KeyBackspace = 0x08;
    private const int KeySpace = 0x20;
    private const int KeyShift = 0x10;
    private const int KeyControl = 0x11;
    private const int KeyMenu = 0x12;
    private const int KeyCapsLock = 0x14;
    private const int MaxWordLength = 40;
    private const int MinimumWordLengthDefault = 3;
    private const uint InputKeyboard = 1;
    private const uint KeyeventUnicode = 0x0004;
    private const uint KeyeventKeyup = 0x0002;
    private const uint ToUnicodeNoStateChange = 0x4;

    private static readonly Dictionary<string, string> LanguageTags = new()
    {
        ["Türkçe"] = TurkishLanguageTag,
        ["English"] = EnglishLanguageTag,
        // Retain stored language choices without silently switching their language.
        ["Deutsch"] = "de-DE",
        ["Français"] = "fr-FR",
        ["Español"] = "es-ES"
    };

    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly Action<int, string> _replaceText;
    private readonly SettingDescriptor[] _settings;
    private readonly StringBuilder _word = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private readonly HashSet<string> _sessionIgnoredWords = new(StringComparer.OrdinalIgnoreCase);

    private SpellEngine? _spellChecker;
    private GlobalMouseHook? _mouseHook;
    private AutoCorrectSuggestionWindow? _suggestionWindow;
    private IModuleContext? _context;
    private KeyboardHookThread? _hook;
    private int _hookGeneration;
    private long _observedInput;
    private long _processedInput;
    private bool InputIsCurrent => Volatile.Read(ref _observedInput) == _processedInput;
    private IntPtr _wordWindow;
    private bool _isWordSkipped;
    private long _inputSequence;
    private long _suggestionSequence;
    private int _suggestionReplaceLength;
    private IntPtr _suggestionTargetWindow;
    private string? _suggestionPreviousWords;
    private string _suggestionKeyboardTag = EnglishLanguageTag;
    private string _language = AutomaticLanguage;
    private int _minimumWordLength = MinimumWordLengthDefault;
    private bool _undoOnBackspace = true;
    private bool _deasciifyTurkish = true;
    private bool _showSuggestions = true;
    private bool _allowTextExpansions = true;
    private bool _contextAnalysis = true;
    private bool _personalizedFrequency = true;
    private Dictionary<string, string> _customReplacements = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _excludedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private LastCorrection? _lastCorrection;
    private string? _previousWord;
    private string? _penultimateWord;
    private IntPtr _previousWordWindow;

    public AutoCorrectModule() : this(ReplaceTypedWord) { }

    internal AutoCorrectModule(Action<int, string> replaceText)
    {
        _replaceText = replaceText;
        _callback = HandleKeyboard;
        _settings =
        [
            new SettingDescriptor
            {
                Key = "language",
                Label = "Düzeltme dili",
                Kind = SettingKind.Choice,
                Default = AutomaticLanguage,
                Options = [AutomaticLanguage, TurkishAndEnglish, "Türkçe", "English"],
                Description = "Klavye düzenine göre seçilirse etkin uygulamanın klavye dili kullanılır. Türkçe + English seçeneği her iki dili birlikte denetler."
            },
            new SettingDescriptor
            {
                Key = "minimum-word-length",
                Label = "En kısa kelime uzunluğu",
                Kind = SettingKind.Number,
                Default = MinimumWordLengthDefault,
                Min = 2,
                Max = 10,
                Description = "Bundan kısa kelimeler düzeltilmez."
            },
            new SettingDescriptor
            {
                Key = "undo-on-backspace",
                Label = "Backspace ile geri alma",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Otomatik düzeltilen bir kelimenin hemen ardından Backspace'e basıldığında düzeltmeyi geri alır ve orijinal kelimeyi geri yükler."
            },
            new SettingDescriptor
            {
                Key = "context-analysis",
                Label = "Bağlam analizi (N-gram / Collocation)",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Önceki iki kelimeyle ikili ve üçlü ifadeleri değerlendirir; belirsiz önerileri elemek ve uygun düzeltmeyi seçmek için bağlamı kullanır."
            },
            new SettingDescriptor
            {
                Key = "suggestion-strip",
                Label = "Yazarken öneri şeridi",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Kelime tamamlama ve bağlama göre sonraki kelime önerilerini imlecin yanında gösterir; öneriye tıklayarak metne ekleyebilirsiniz."
            },
            new SettingDescriptor
            {
                Key = "personalized-frequency",
                Label = "Kişiselleştirilmiş frekans ve öğrenme",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Doğru kabul edilen kelimeleri ve birlikte kullandığınız kelime dizilerini önceliklendirir. Bilinmeyen kelimeler yalnızca açıkça kişisel sözlüğe eklendiğinde öğrenilir."
            },
            new SettingDescriptor
            {
                Key = "personal-dictionary",
                Label = "Kişisel sözlük",
                Kind = SettingKind.Text,
                Default = "",
                Description = "Özel kişi, ürün veya teknik adları noktalı virgülle ekleyin (ör. WinForge; AntigravityCore)."
            },
            new SettingDescriptor
            {
                Key = "deasciify-turkish",
                Label = "Türkçe karakter tamamlama",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "calisiyorum -> çalışıyorum veya tesekkurler -> teşekkürler gibi Türkçe karakter içermeyen yazımları düzeltir."
            },
            new SettingDescriptor
            {
                Key = "text-expansions",
                Label = "Kısayol genişletmeleri",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "tşk -> teşekkürler, brb -> be right back gibi popüler kısaltmaları otomatik genişletir."
            },
            new SettingDescriptor
            {
                Key = "custom-replacements",
                Label = "Özel kelime kısayolları",
                Kind = SettingKind.Text,
                Default = DefaultCustomReplacements,
                Description = "Kendi kısayollarınızı 'kısaltma=kelime' biçiminde noktalı virgülle ayırarak ekleyin."
            },
            new SettingDescriptor
            {
                Key = "excluded-apps",
                Label = "Otomatik düzeltme dışında bırakılan uygulamalar",
                Kind = SettingKind.Text,
                Default = "",
                Description = "İşlem adlarını noktalı virgülle ayırın (ör. code; terminal)."
            }
        ];
    }

    public string Id => "auto-correct";
    public string Name => "Otomatik Kelime Düzeltme";
    public string Category => "Verimlilik";
    public string Description => "Yazım hatalı bir kelimeyi yazıp boşluğa veya noktalama işaretine bastığınızda yerel altyapısıyla otomatik olarak düzeltir (Türkçe & İngilizce de-asciifier, klavye yakınlığı ve Backspace ile geri alma destekli).";
    public string? Warning => "Tuş vuruşlarını izler ve yanlış yazılmış kelimeyi silip yerine düzeltmesini yazar. Şifre alanlarında ve oyunlarda kapalı tutun veya uygulamayı hariç bırakın.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        ReadSettings();
        var keyboardLanguageTag = GetKeyboardLanguageTag(Native.GetForegroundWindow());
        PrepareLanguageModels(ResolveLanguageTags(_language, keyboardLanguageTag));
        ActivateInputHooks();
    }

    public async Task ActivateAsync()
    {
        if (IsActive) return;
        ReadSettings();
        var keyboardLanguageTag = GetKeyboardLanguageTag(Native.GetForegroundWindow());
        var languages = ResolveLanguageTags(_language, keyboardLanguageTag);
        await Task.Run(() => PrepareLanguageModels(languages));
        ActivateInputHooks();
    }

    public Task DeactivateAsync() { Deactivate(); return Task.CompletedTask; }
    public Task ApplyAsync() { Apply(); return Task.CompletedTask; }

    private static void PrepareLanguageModels(IReadOnlyList<string> selectedLanguages)
    {
        if (selectedLanguages.Any(languageTag => languageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase)))
        {
            _ = TurkishMorphologyService.IsAvailable;
            _ = SpellNgramModel.GetTurkishVocabularyCount();
        }
        if (selectedLanguages.Any(languageTag => languageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase)))
            _ = SpellNgramModel.GetEnglishVocabularyCount();
    }

    private void ActivateInputHooks()
    {
        _spellChecker = SpellEngine.Instance;
        _suggestionWindow = new AutoCorrectSuggestionWindow(AcceptSuggestion, (_context as IFeatureUiContext)?.FeatureUi);
        ++_hookGeneration;
        try { _hook = new KeyboardHookThread(_callback); }
        catch
        {
            _suggestionWindow.Close();
            _suggestionWindow = null;
            throw;
        }
        _mouseHook = new GlobalMouseHook();
        _mouseHook.LeftButtonDown += point =>
        {
            if (_suggestionWindow?.ContainsScreenPoint(point) != true) InvalidateQueuedInput();
        };
        _mouseHook.RightButtonDown += _ => InvalidateQueuedInput();
        try { _mouseHook.Install(); }
        catch
        {
            _hook?.Dispose();
            _hook = null;
            _mouseHook.Dispose();
            _mouseHook = null;
            _suggestionWindow.Close();
            _suggestionWindow = null;
            throw;
        }
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
        ++_hookGeneration;
        InvalidateInput();
        _sessionIgnoredWords.Clear();
        _mouseHook?.Dispose();
        _mouseHook = null;
        _suggestionWindow?.Close();
        _suggestionWindow = null;
        if (_hook is not null)
        {
            _hook?.Dispose();
            _hook = null;
        }
        _spellChecker = null;
        UserFrequencyTracker.Instance.SaveImmediate();
    }

    public void Apply()
    {
        if (!IsActive) return;
        InvalidateQueuedInput();
        ReadSettings();
    }

    internal static string MatchCase(string original, string replacement, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement)) return replacement;
        if (original.Length > 1 && original.All(char.IsUpper)) return replacement.ToUpper(culture);
        if (char.IsUpper(original[0]))
        {
            var firstChar = char.ToUpper(replacement[0], culture);
            return replacement.Length > 1 ? firstChar + replacement[1..] : firstChar.ToString();
        }
        return replacement.ToLower(culture);
    }

    internal static string ResolveLanguageTag(string setting, string keyboardLanguageTag) =>
        LanguageTags.TryGetValue(setting, out var tag) ? tag : keyboardLanguageTag;

    internal static IReadOnlyList<string> ResolveLanguageTags(string setting, string keyboardLanguageTag) =>
        setting == TurkishAndEnglish
            ? [TurkishLanguageTag, EnglishLanguageTag]
            : [ResolveLanguageTag(setting, keyboardLanguageTag)];

    private void ReadSettings()
    {
        _language = _context?.Settings.GetString("language", AutomaticLanguage) ?? AutomaticLanguage;
        _minimumWordLength = Math.Clamp(_context?.Settings.GetInt("minimum-word-length", MinimumWordLengthDefault) ?? MinimumWordLengthDefault, 2, 10);
        _undoOnBackspace = _context?.Settings.GetBool("undo-on-backspace", true) ?? true;
        _contextAnalysis = _context?.Settings.GetBool("context-analysis", true) ?? true;
        _showSuggestions = _context?.Settings.GetBool("suggestion-strip", true) ?? true;
        _personalizedFrequency = _context?.Settings.GetBool("personalized-frequency", true) ?? true;
        var personalWords = (_context?.Settings.GetString("personal-dictionary", "") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        UserFrequencyTracker.Instance.SetConfiguredWords(personalWords);
        _deasciifyTurkish = _context?.Settings.GetBool("deasciify-turkish", true) ?? true;
        _allowTextExpansions = _context?.Settings.GetBool("text-expansions", true) ?? true;
        _customReplacements = ParseCustomReplacements(_context?.Settings.GetString("custom-replacements", DefaultCustomReplacements) ?? DefaultCustomReplacements);
        _excludedProcesses = ParseExcludedProcesses(_context?.Settings.GetString("excluded-apps", "") ?? "");
    }

    private void ClearPreviousWord()
    {
        _previousWord = null;
        _penultimateWord = null;
        _previousWordWindow = IntPtr.Zero;
    }

    private void InvalidateQueuedInput()
    {
        // A click or settings change must also discard keyboard events queued
        // before it, otherwise delayed events could recreate an obsolete word.
        Interlocked.Increment(ref _hookGeneration);
        InvalidateInput();
    }

    private void InvalidateInput()
    {
        _inputSequence++;
        ResetWord();
        ClearPreviousWord();
        _lastCorrection = null;
        _suggestionWindow?.Hide();
    }

    private void RememberWord(string word, IntPtr window, char delimiter)
    {
        if (delimiter != ' ') { ClearPreviousWord(); return; }
        var words = word.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in words)
        {
            _penultimateWord = window == _previousWordWindow ? _previousWord : null;
            _previousWord = token;
            _previousWordWindow = window;
        }
    }

    private static Dictionary<string, string> ParseCustomReplacements(string text)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return dictionary;

        var pairs = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            {
                dictionary[parts[0]] = parts[1];
            }
        }
        return dictionary;
    }

    private IntPtr HandleKeyboard(int code, IntPtr messagePointer, IntPtr dataPointer)
    {
        if (code >= 0 && messagePointer.ToInt32() is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(dataPointer);
            if ((info.flags & InjectedFlag) == 0)
            {
                // Capture transient key state before returning to Windows. All UI,
                // spell checking and process lookups run outside the native hook.
                var observed = Interlocked.Increment(ref _observedInput);
                var generation = Volatile.Read(ref _hookGeneration);
                var window = Native.GetForegroundWindow();
                var key = (int)info.vkCode;
                var shortcut = HasShortcutModifiers() || HotkeyService.IsCapturing;
                var character = shortcut || IsModifierKey(key) || key is KeySpace or KeyBackspace
                    ? '\0' : TranslateCharacter(key, info.scanCode, window);
                _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (!IsActive || generation != _hookGeneration) return;
                    _processedInput = observed;
                    try { TrackKey(key, window, shortcut, character); }
                    catch (Exception exception) { _context?.Log.Error("Otomatik düzeltme klavye girdisi işlenemedi.", exception); }
                }));
            }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, messagePointer, dataPointer);
    }

    private void TrackKey(int key, IntPtr window, bool shortcut, char character)
    {
        _inputSequence++;
        if (IsModifierKey(key)) return;

        if ((_wordWindow != IntPtr.Zero && window != _wordWindow) ||
            (_previousWordWindow != IntPtr.Zero && window != _previousWordWindow)) InvalidateInput();
        if (shortcut || HotkeyService.IsCapturing) { InvalidateInput(); return; }

        if (key == KeyBackspace)
        {
            if (_undoOnBackspace && _lastCorrection is not null && _lastCorrection.Window == window &&
                _lastCorrection.Sequence + 1 == _inputSequence)
            {
                var correction = _lastCorrection;
                _lastCorrection = null;
                _sessionIgnoredWords.Add(correction.OriginalWord);
                if (_personalizedFrequency && UserFrequencyTracker.Instance.RecordRejectedCorrection(
                        correction.OriginalWord, correction.Replacement, correction.LanguageTag))
                    _dispatcher.BeginInvoke(DispatcherPriority.Background,
                        () => UserFrequencyTracker.Instance.SaveImmediate());
                var sequence = _inputSequence;
                ResetWord();
                ClearPreviousWord();
                _dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (!IsActive || sequence != _inputSequence || !InputIsCurrent || Native.GetForegroundWindow() != window) return;
                    UndoCorrection(correction);
                    _word.Append(correction.OriginalWord);
                    _wordWindow = window;
                });
                return;
            }
            if (_word.Length == 0) ClearPreviousWord();
            RemoveLastCharacter();
            return;
        }

        // Any non-backspace keystroke cancels undo for the previous correction
        _lastCorrection = null;

        if (key == KeySpace)
        {
            HandleWordEnd(window, ' ');
            return;
        }

        if (IsPunctuationTrigger(character))
        {
            HandleWordEnd(window, character);
            return;
        }

        // Navigation, Enter, Tab and Delete invalidate both the word and its context.
        if (character == '\0' || char.IsControl(character)) { InvalidateInput(); return; }

        if ((char.IsLetter(character) || (_word.Length > 0 && IsWordInternalCharacter(character))) && _word.Length < MaxWordLength)
        {
            AppendCharacter(character, window);
        }
        else
        {
            MarkWordSkipped();
        }
    }

    private static bool IsPunctuationTrigger(char c) =>
        c is '.' or ',' or '!' or '?' or ';' or ':';

    private static bool IsWordInternalCharacter(char c) =>
        c is '\'' or '’' or '-';

    private void AppendCharacter(char character, IntPtr window)
    {
        if (_word.Length == 0) _wordWindow = window;
        _word.Append(character);
        QueueCurrentWordSuggestions(window);
    }

    private void RemoveLastCharacter()
    {
        if (_word.Length > 0) _word.Length--;
        // Once an identifier or other unsupported token is encountered, its
        // remaining prefix is unknown. Backspace cannot safely reconstruct it.
        if (_word.Length > 0) QueueCurrentWordSuggestions(_wordWindow);
        else _suggestionWindow?.Hide();
    }

    private void MarkWordSkipped()
    {
        _word.Clear();
        _isWordSkipped = true;
        ClearPreviousWord();
    }

    private void ResetWord()
    {
        _word.Clear();
        _isWordSkipped = false;
        _wordWindow = IntPtr.Zero;
    }

    private void HandleWordEnd(IntPtr window, char triggerChar)
    {
        var word = _word.ToString();
        var isEligible = !_isWordSkipped && word.Length > 0 && window == _wordWindow;
        ResetWord();
        if (!isEligible || IsExcludedWindow(window))
        {
            _suggestionWindow?.Hide();
            if (triggerChar != ' ' || word.Length > 0) ClearPreviousWord();
            return;
        }
        var sequence = _inputSequence;
        var keyboardTag = GetKeyboardLanguageTag(window);
        var previous = window == _previousWordWindow
            ? string.Join(' ', new[] { _penultimateWord, _previousWord }.Where(token => !string.IsNullOrEmpty(token))) : null;
        // Capture every completed word immediately, even if a queued correction is
        // cancelled because more input arrives before the dispatcher runs.
        RememberWord(word, window, triggerChar);
        _dispatcher.BeginInvoke(DispatcherPriority.Input, () => ApplyCorrection(word, window, sequence, keyboardTag, triggerChar, previous));
        if (triggerChar == ' ')
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => QueueNextWordPredictions(window, sequence, keyboardTag));
    }

    private async void ApplyCorrection(string word, IntPtr window, long sequence, string keyboardTag, char triggerChar, string? previous)
    {
        try
        {
            if (!IsActive || _spellChecker is null || sequence != _inputSequence || !InputIsCurrent || Native.GetForegroundWindow() != window) return;
            if (_sessionIgnoredWords.Contains(word) || word.Length < _minimumWordLength)
            {
                return;
            }

            var engine = _spellChecker;
            var language = _language;
            var contextAnalysis = _contextAnalysis;
            var deasciify = _deasciifyTurkish;
            var expansions = _allowTextExpansions;
            var personalized = _personalizedFrequency;
            var replacements = _customReplacements;
            var analysis = await AnalyzeAsync(sequence, () =>
            {
                var languageTags = ResolveLanguageTags(language, keyboardTag);
                var supportedTags = languageTags.Where(engine.IsLanguageSupported).ToArray();
                if (supportedTags.Length == 0) return (Known: false, Tag: keyboardTag, Correction: (string?)null);

                var preferredTag = supportedTags.Contains(keyboardTag, StringComparer.OrdinalIgnoreCase)
                    ? keyboardTag
                    : supportedTags[0];
                var orderedTags = supportedTags.OrderBy(tag => StringComparer.OrdinalIgnoreCase.Equals(tag, preferredTag) ? 0 : 1);

                var hasExplicitReplacement = replacements.ContainsKey(word.ToLower(SpellEngine.GetCulture(preferredTag))) ||
                    (expansions && SpellLexicons.DefaultTextExpansions.ContainsKey(word));
                var isKnown = supportedTags.Any(tag => engine.IsKnownWord(word, tag, personalized));

                // Preserve words valid in either active language. Single-language mode still
                // permits the model's explicitly curated real-word confusion rules.
                var preserveKnown = isKnown && supportedTags.Length > 1 && !hasExplicitReplacement;
                var result = orderedTags
                    .Select(tag => (Tag: tag, Correction: preserveKnown ? null : engine.FindCorrection(
                        word, tag, previous, contextAnalysis, deasciify, expansions, personalized, replacements)))
                    .FirstOrDefault(candidate => candidate.Correction is not null);

                return (Known: isKnown, Tag: result.Tag ?? preferredTag, Correction: result.Correction);
            });
            if (!IsActive || sequence != _inputSequence || !InputIsCurrent || Native.GetForegroundWindow() != window) return;
            var isKnown = analysis.Known;
            var result = (Tag: analysis.Tag, Correction: analysis.Correction);

            if (result.Correction is null)
            {
                RecordPersonalizedUsage(word, isKnown, previous, result.Tag);
                return;
            }
            if (string.Equals(word, result.Correction, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (sequence != _inputSequence || !InputIsCurrent || Native.GetForegroundWindow() != window) return;

            var culture = SpellEngine.GetCulture(result.Tag);
            var replacement = MatchCase(word, result.Correction, culture);

            // Replace typed word and the trigger delimiter (word.Length + 1 characters)
            _replaceText(word.Length + 1, replacement + triggerChar);
            _lastCorrection = new LastCorrection(word, replacement, window, _inputSequence, result.Tag);
            _context?.Log.Info($"Otomatik düzeltme: {word} → {replacement}");

            RecordPersonalizedUsage(replacement, isKnownWord: true, previous, result.Tag);
            ClearPreviousWord();
            if (!string.IsNullOrEmpty(previous)) RememberWord(previous, window, ' ');
            RememberWord(replacement, window, triggerChar);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _context?.Log.Error("Otomatik düzeltme uygulanamadı.", exception);
        }
    }

    private void QueueCurrentWordSuggestions(IntPtr window)
    {
        if (!_showSuggestions)
        {
            _suggestionWindow?.Hide();
            return;
        }

        var word = _word.ToString();
        var sequence = _inputSequence;
        if (word.Length < _minimumWordLength || _isWordSkipped || IsExcludedWindow(window))
        {
            _suggestionWindow?.Hide();
            return;
        }

        var keyboardTag = GetKeyboardLanguageTag(window);
        var previous = window == _previousWordWindow
            ? string.Join(' ', new[] { _penultimateWord, _previousWord }.Where(token => !string.IsNullOrEmpty(token))) : null;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            var engine = _spellChecker;
            if (!IsActive || sequence != _inputSequence || !InputIsCurrent || Native.GetForegroundWindow() != window || engine is null) return;

            var language = _language;
            var contextAnalysis = _contextAnalysis;
            var deasciify = _deasciifyTurkish;
            var expansions = _allowTextExpansions;
            var personalized = _personalizedFrequency;
            var replacements = _customReplacements;
            try
            {
                // Coalesce rapid typing before starting vocabulary/morphology searches.
                await Task.Delay(60);
                var suggestions = await AnalyzeAsync(sequence, () =>
                {
                    var tags = ResolveLanguageTags(language, keyboardTag)
                        .Where(engine.IsLanguageSupported)
                        .OrderBy(tag => StringComparer.OrdinalIgnoreCase.Equals(tag, keyboardTag) ? 0 : 1)
                        .ToArray();
                    var suggestions = tags.SelectMany(tag => engine.GetSuggestions(
                            word, tag, previous, contextAnalysis, deasciify, expansions,
                            personalized, replacements))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(3)
                        .ToArray();
                return suggestions;
                });
                if (IsActive && sequence == _inputSequence && InputIsCurrent && Native.GetForegroundWindow() == window)
                    ShowSuggestions(suggestions, window, sequence, word.Length, keyboardTag, previous);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { _context?.Log.Error("Kelime önerileri hesaplanamadı.", exception); }
        }));
    }

    private async void QueueNextWordPredictions(IntPtr window, long sequence, string keyboardTag)
    {
        if (!_showSuggestions || !IsActive || sequence != _inputSequence || !InputIsCurrent ||
            Native.GetForegroundWindow() != window || !_contextAnalysis)
            return;

        var engine = _spellChecker;
        if (engine is null) return;
        var history = string.Join(' ', new[] { _penultimateWord, _previousWord }.Where(token => !string.IsNullOrEmpty(token)));
        if (string.IsNullOrEmpty(history))
        {
            _suggestionWindow?.Hide();
            return;
        }

        var language = _language;
        var personalized = _personalizedFrequency;
        try
        {
            var suggestions = await AnalyzeAsync(sequence, () =>
            {
                var suggestions = ResolveLanguageTags(language, keyboardTag)
                    .Where(engine.IsLanguageSupported)
                    .OrderBy(tag => StringComparer.OrdinalIgnoreCase.Equals(tag, keyboardTag) ? 0 : 1)
                    .SelectMany(tag => SpellContextModel.GetNextWordSuggestions(
                        history, string.Empty, tag, 3, UserFrequencyTracker.Instance, personalized))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToArray();
            return suggestions;
            });
            if (IsActive && sequence == _inputSequence && InputIsCurrent && Native.GetForegroundWindow() == window)
                ShowSuggestions(suggestions, window, sequence, 0, keyboardTag, history);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _context?.Log.Error("Sonraki kelime önerileri hesaplanamadı.", exception); }
    }

    // Never occupy the dispatcher that services the system-wide keyboard hooks.
    // Serialize model work and discard obsolete requests before expensive analysis starts.
    private async Task<T> AnalyzeAsync<T>(long sequence, Func<T> analyze)
    {
        await _analysisGate.WaitAsync();
        try
        {
            if (sequence != _inputSequence || !InputIsCurrent) throw new OperationCanceledException();
            return await Task.Run(analyze);
        }
        finally { _analysisGate.Release(); }
    }

    private void ShowSuggestions(
        IReadOnlyList<string> suggestions,
        IntPtr window,
        long sequence,
        int replaceLength,
        string keyboardTag,
        string? previousWords)
    {
        if (suggestions.Count == 0 || !AutoCorrectSuggestionWindow.TryGetCaret(window, out var caret))
        {
            _suggestionWindow?.Hide();
            return;
        }

        _suggestionSequence = sequence;
        _suggestionTargetWindow = window;
        _suggestionReplaceLength = replaceLength;
        _suggestionKeyboardTag = keyboardTag;
        _suggestionPreviousWords = previousWords;
        _suggestionWindow?.ShowSuggestions(suggestions, window, caret);
    }

    private void AcceptSuggestion(string suggestion)
    {
        var window = _suggestionTargetWindow;
        if (!IsActive || _suggestionSequence != _inputSequence || !InputIsCurrent || window == IntPtr.Zero ||
            Native.GetForegroundWindow() != window || string.IsNullOrWhiteSpace(suggestion)) return;

        var accepted = _suggestionReplaceLength > 0
            ? MatchCase(_word.ToString(), suggestion, SpellEngine.GetCulture(_suggestionKeyboardTag))
            : suggestion;
        _replaceText(_suggestionReplaceLength, accepted + " ");
        _inputSequence++;
        _lastCorrection = null;
        ResetWord();
        RecordAcceptedSuggestion(accepted, _suggestionPreviousWords, _suggestionKeyboardTag);
        RememberWord(accepted, window, ' ');
        _suggestionWindow?.Hide();
        QueueNextWordPredictions(window, _inputSequence, _suggestionKeyboardTag);
    }

    private void RecordAcceptedSuggestion(string suggestion, string? previousWords, string keyboardTag)
    {
        if (!_personalizedFrequency || _spellChecker is null) return;

        var history = previousWords?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(2).ToList() ?? [];
        foreach (var word in suggestion.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var languageTag = ResolveLanguageTags(_language, keyboardTag)
                .FirstOrDefault(tag => _spellChecker.IsLanguageSupported(tag) &&
                                       _spellChecker.IsKnownWord(word, tag, allowLearning: true));
            if (languageTag is not null)
            {
                RecordPersonalizedUsage(word, true, string.Join(' ', history), languageTag);
                history.Add(word);
                if (history.Count > 2) history.RemoveAt(0);
            }
        }
    }

    private void RecordPersonalizedUsage(string word, bool isKnownWord, string? previous, string languageTag)
    {
        if (!_personalizedFrequency || !isKnownWord) return;

        var history = GetLearnableHistory(previous, historyWord =>
            _spellChecker?.IsKnownWord(historyWord, languageTag, allowLearning: true) == true ||
            UserFrequencyTracker.Instance.IsLearned(historyWord));
        UserFrequencyTracker.Instance.RecordWord(word, isKnownWord: true, history);
    }

    internal static string GetLearnableHistory(string? previous, Func<string, bool> isKnownWord)
    {
        var words = previous?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(2).ToArray() ?? [];
        var start = words.Length;
        while (start > 0 && isKnownWord(words[start - 1])) start--;
        return string.Join(' ', words[start..]);
    }

    private void UndoCorrection(LastCorrection correction)
    {
        // The physical Backspace has already removed the delimiter before dispatch.
        var charactersToDelete = correction.Replacement.Length;
        _replaceText(charactersToDelete, correction.OriginalWord);
    }

    private static void ReplaceTypedWord(int charactersToDelete, string text)
    {
        var inputs = new List<INPUT>();
        for (var index = 0; index < charactersToDelete; index++)
        {
            inputs.Add(CreateKey(KeyBackspace, keyUp: false));
            inputs.Add(CreateKey(KeyBackspace, keyUp: true));
        }
        foreach (var character in text)
        {
            inputs.Add(CreateUnicode(character, keyUp: false));
            inputs.Add(CreateUnicode(character, keyUp: true));
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT CreateKey(int key, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = (ushort)key, Flags = keyUp ? KeyeventKeyup : 0 } }
    };

    private static INPUT CreateUnicode(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KEYBDINPUT { ScanCode = character, Flags = KeyeventUnicode | (keyUp ? KeyeventKeyup : 0) } }
    };

    private static char TranslateCharacter(int key, uint scanCode, IntPtr window)
    {
        var layout = GetKeyboardLayout(Native.GetWindowThreadProcessId(window, out _));
        var state = new byte[256];
        if (IsKeyDown(KeyShift)) state[KeyShift] = 0x80;
        if ((GetKeyState(KeyCapsLock) & 1) != 0) state[KeyCapsLock] = 1;
        if (IsKeyDown(KeyControl) && IsKeyDown(KeyMenu)) { state[KeyControl] = 0x80; state[KeyMenu] = 0x80; }
        var buffer = new StringBuilder(4);
        return ToUnicodeEx((uint)key, scanCode, state, buffer, buffer.Capacity, ToUnicodeNoStateChange, layout) == 1 ? buffer[0] : '\0';
    }

    private static string GetKeyboardLanguageTag(IntPtr window)
    {
        var layout = (long)GetKeyboardLayout(Native.GetWindowThreadProcessId(window, out _));
        var lowId = (int)(layout & 0xFFFF);
        var highId = (int)((layout >> 16) & 0xFFFF);
        if (lowId == 0x041F || highId == 0x041F) return "tr-TR";

        try { return CultureInfo.GetCultureInfo(lowId).Name; }
        catch (CultureNotFoundException) { return "en-US"; }
    }

    private bool IsExcludedWindow(IntPtr window)
    {
        if (_excludedProcesses.Count == 0) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return _excludedProcesses.Contains(process.ProcessName);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal static HashSet<string> ParseExcludedProcesses(string value) => value
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizeProcessName)
        .Where(name => name.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static string NormalizeProcessName(string value)
    {
        var fileName = Path.GetFileName(value.Trim().Trim('"', '\''));
        return Path.GetFileNameWithoutExtension(fileName).Trim();
    }

    private static bool HasShortcutModifiers() =>
        (IsKeyDown(KeyControl) && !IsKeyDown(KeyMenu)) || (IsKeyDown(KeyMenu) && !IsKeyDown(KeyControl)) || IsKeyDown(0x5B) || IsKeyDown(0x5C);

    private static bool IsModifierKey(int key) => key is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyboardState, StringBuilder buffer, int bufferLength, uint flags, IntPtr keyboardLayout);

    private sealed record LastCorrection(
        string OriginalWord,
        string Replacement,
        IntPtr Window,
        long Sequence,
        string LanguageTag);
}
