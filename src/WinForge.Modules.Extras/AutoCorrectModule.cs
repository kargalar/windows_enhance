using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinForge.Core;
using static WinForge.Modules.Extras.KeyboardInputSender;

namespace WinForge.Modules.Extras;

public sealed class AutoCorrectModule : ITweakModule
{
    private const string AutomaticLanguage = "Klavye düzenine göre";
    private const string TurkishAndEnglish = "Türkçe + English";
    private const string TurkishLanguageTag = "tr-TR";
    private const string EnglishLanguageTag = "en-US";
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
        ["Deutsch"] = "de-DE",
        ["Français"] = "fr-FR",
        ["Español"] = "es-ES"
    };

    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly SettingDescriptor[] _settings;
    private readonly StringBuilder _word = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private WindowsSpellChecker? _spellChecker;
    private GlobalMouseHook? _mouseHook;
    private IModuleContext? _context;
    private IntPtr _hook;
    private IntPtr _wordWindow;
    private bool _isWordSkipped;
    private long _inputSequence;
    private string _language = AutomaticLanguage;
    private int _minimumWordLength = MinimumWordLengthDefault;
    private HashSet<string> _excludedProcesses = new(StringComparer.OrdinalIgnoreCase);

    public AutoCorrectModule()
    {
        _callback = HandleKeyboard;
        _settings =
        [
            new SettingDescriptor
            {
                Key = "language",
                Label = "Düzeltme dili",
                Kind = SettingKind.Choice,
                Default = AutomaticLanguage,
                Options = [AutomaticLanguage, TurkishAndEnglish, .. LanguageTags.Keys],
                Description = "Klavye düzenine göre seçilirse etkin uygulamanın klavye dili kullanılır. Türkçe + English seçeneği iki dili birlikte denetler; her iki dil de Windows'ta yüklü olmalıdır."
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
    public string Description => "Yazım hatalı bir kelimeyi yazıp boşluğa bastığınızda Windows yazım denetimiyle otomatik olarak düzeltir (Android klavyesi gibi).";
    public string? Warning => "Tuş vuruşlarını izler ve yanlış yazılmış kelimeyi silip yerine düzeltmesini yazar. Şifre alanlarında ve oyunlarda kapalı tutun veya uygulamayı hariç bırakın.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        ReadSettings();
        _spellChecker = new WindowsSpellChecker();
        if (!_spellChecker.IsAvailable) throw new InvalidOperationException("Windows yazım denetimi kullanılamıyor.");
        _hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Otomatik düzeltme klavye kancası kurulamadı.");
        _mouseHook = new GlobalMouseHook();
        _mouseHook.LeftButtonDown += _ => ResetWord();
        _mouseHook.RightButtonDown += _ => ResetWord();
        _mouseHook.Install();
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
        ResetWord();
        _mouseHook?.Dispose();
        _mouseHook = null;
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _spellChecker = null;
    }

    public void Apply()
    {
        if (!IsActive) return;
        ReadSettings();
    }

    internal static string MatchCase(string original, string replacement, CultureInfo culture)
    {
        if (original.Length > 1 && original.All(char.IsUpper)) return replacement.ToUpper(culture);
        if (char.IsUpper(original[0])) return char.ToUpper(replacement[0], culture) + replacement[1..];
        return replacement;
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
        _excludedProcesses = QuickAccentModule.ParseExcludedProcesses(_context?.Settings.GetString("excluded-apps", "") ?? "");
    }

    private IntPtr HandleKeyboard(int code, IntPtr messagePointer, IntPtr dataPointer)
    {
        if (code >= 0 && IsActive)
        {
            try { TrackKey(messagePointer.ToInt32(), Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(dataPointer)); }
            catch (Exception exception) { _context?.Log.Error("Otomatik düzeltme klavye girdisi işlenemedi.", exception); }
        }
        return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
    }

    private void TrackKey(int message, KBDLLHOOKSTRUCT info)
    {
        if (message is not (Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)) return;
        if ((info.flags & InjectedFlag) != 0) return;
        _inputSequence++;
        var key = (int)info.vkCode;
        if (IsModifierKey(key)) return;

        var window = Native.GetForegroundWindow();
        if (_word.Length > 0 && window != _wordWindow) ResetWord();
        if (HasShortcutModifiers()) { ResetWord(); return; }

        if (key == KeyBackspace) { RemoveLastCharacter(); return; }
        if (key == KeySpace) { HandleWordEnd(window); return; }

        var character = TranslateCharacter(key, info.scanCode, window);
        if (char.IsLetter(character) && _word.Length < MaxWordLength) AppendCharacter(character, window);
        else MarkWordSkipped();
    }

    private void AppendCharacter(char character, IntPtr window)
    {
        if (_word.Length == 0) _wordWindow = window;
        _word.Append(character);
    }

    private void RemoveLastCharacter()
    {
        if (_word.Length > 0) _word.Length--;
        if (_word.Length == 0) _isWordSkipped = false;
    }

    private void MarkWordSkipped()
    {
        _word.Clear();
        _isWordSkipped = true;
    }

    private void ResetWord()
    {
        _word.Clear();
        _isWordSkipped = false;
        _wordWindow = IntPtr.Zero;
    }

    private void HandleWordEnd(IntPtr window)
    {
        var word = _word.ToString();
        var isEligible = !_isWordSkipped && word.Length >= _minimumWordLength && window == _wordWindow;
        ResetWord();
        if (!isEligible || IsExcludedWindow(window)) return;
        var sequence = _inputSequence;
        var keyboardTag = GetKeyboardLanguageTag(window);
        _dispatcher.BeginInvoke(DispatcherPriority.Input, () => ApplyCorrection(word, window, sequence, keyboardTag));
    }

    private void ApplyCorrection(string word, IntPtr window, long sequence, string keyboardTag)
    {
        try
        {
            if (!IsActive || _spellChecker is null) return;
            var languageTags = ResolveLanguageTags(_language, keyboardTag);
            var supportedTags = languageTags.Where(_spellChecker.IsLanguageSupported).ToArray();
            if (supportedTags.Length != languageTags.Count) return;
            if (supportedTags.Length > 1 && supportedTags.Any(tag => _spellChecker.IsCorrect(word, tag))) return;

            var preferredTag = supportedTags.Contains(keyboardTag, StringComparer.OrdinalIgnoreCase)
                ? keyboardTag
                : supportedTags[0];
            var orderedTags = supportedTags.OrderBy(tag => StringComparer.OrdinalIgnoreCase.Equals(tag, preferredTag) ? 0 : 1);
            var result = orderedTags
                .Select(tag => (Tag: tag, Correction: _spellChecker.FindCorrection(word, tag)))
                .FirstOrDefault(candidate => candidate.Correction is not null);
            if (result.Correction is null) return;
            if (sequence != _inputSequence || Native.GetForegroundWindow() != window) return;
            var replacement = MatchCase(word, result.Correction, WindowsSpellChecker.GetCulture(result.Tag));
            ReplaceTypedWord(word.Length + 1, replacement + " ");
            _context?.Log.Info($"Otomatik düzeltme: {word} → {replacement}");
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Otomatik düzeltme uygulanamadı.", exception);
        }
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

    private static bool HasShortcutModifiers() =>
        (IsKeyDown(KeyControl) && !IsKeyDown(KeyMenu)) || (IsKeyDown(KeyMenu) && !IsKeyDown(KeyControl)) || IsKeyDown(0x5B) || IsKeyDown(0x5C);

    private static bool IsModifierKey(int key) => key is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyboardState, StringBuilder buffer, int bufferLength, uint flags, IntPtr keyboardLayout);
}

