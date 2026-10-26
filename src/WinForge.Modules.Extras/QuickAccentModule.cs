using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;
using static WinForge.Modules.Extras.KeyboardInputSender;

namespace WinForge.Modules.Extras;

public sealed class QuickAccentModule : ITweakModule
{
    private const int InjectedFlag = 0x10;
    private const int KeyBackspace = 0x08;
    private const int KeyLeft = 0x25;
    private const int KeyUp = 0x26;
    private const int KeyRight = 0x27;
    private const int KeyDown = 0x28;
    private const int KeyEscape = 0x1B;
    private const int KeyReturn = 0x0D;
    private const int KeySpace = 0x20;
    private const uint InputKeyboard = 1;
    private const uint KeyeventUnicode = 0x0004;
    private const uint KeyeventKeyup = 0x0002;

    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly DispatcherTimer _holdTimer;
    private readonly SettingDescriptor[] _settings;
    private IModuleContext? _context;
    private IntPtr _hook;
    private int _heldKey;
    private char _baseCharacter;
    private IntPtr _targetWindow;
    private long _heldAt;
    private int _holdDuration = 500;
    private bool _disableFullscreen = true;
    private string _languageSet = "Tümü";
    private HashSet<string> _excludedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private QuickAccentPickerWindow? _picker;
    private IReadOnlyList<char> _choices = Array.Empty<char>();

    public QuickAccentModule()
    {
        _callback = HandleKeyboard;
        _holdTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
        _holdTimer.Tick += (_, _) => CheckHoldDuration();
        _settings =
        [
            new SettingDescriptor
            {
                Key = "hold-duration-ms",
                Label = "Harf basılı tutma süresi (ms)",
                Kind = SettingKind.Number,
                Default = 500,
                Min = 200,
                Max = 1500,
                Description = "Bu süre geçince aksan seçici açılır. Kısa dokunuş normal harfi yazar."
            },
            new SettingDescriptor
            {
                Key = "disable-fullscreen",
                Label = "Tam ekran uygulamalarda kapat",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Oyunlarda ve tam ekran uygulamalarda klavye girişine karışmaz."
            },
            new SettingDescriptor
            {
                Key = "language-set",
                Label = "Aksan karakter kümesi",
                Kind = SettingKind.Choice,
                Default = "Tümü",
                Options = ["Tümü", "Fransızca", "Almanca", "İspanyolca", "Türkçe", "İskandinav", "Orta Avrupa"],
                Description = "Seçicide gösterilecek aksanları dile göre filtreler."
            },
            new SettingDescriptor
            {
                Key = "excluded-apps",
                Label = "Quick Accent dışında bırakılan uygulamalar",
                Kind = SettingKind.Text,
                Default = "",
                Description = "İşlem adlarını noktalı virgülle ayırın (ör. game; terminal). .exe uzantısı ve büyük/küçük harf önemli değildir."
            }
        ];
    }

    public string Id => "quick-accent";
    public string Name => "Quick Accent · Hızlı Aksan";
    public string Category => "Verimlilik";
    public string Description => "Bir harfi basılı tutup dil kümesine göre filtrelenmiş aksanlı karakterini seçin; kısa basış normal yazımı korur."
        + " Oyunları ve diğer uygulamaları süreç adına göre dışarıda bırakabilirsiniz.";
    public string? Warning => "Seçici, etkin uygulamanın metin alanına Unicode karakter gönderir. Korumalı giriş ve bazı oyunlar tuş kancasını kullanmayabilir.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        _holdDuration = Math.Clamp(_context?.Settings.GetInt("hold-duration-ms", 500) ?? 500, 200, 1500);
        _disableFullscreen = _context?.Settings.GetBool("disable-fullscreen", true) ?? true;
        _languageSet = QuickAccentCharacters.NormalizeLanguageSet(_context?.Settings.GetString("language-set", "Tümü") ?? "Tümü");
        _excludedProcesses = ParseExcludedProcesses(_context?.Settings.GetString("excluded-apps", "") ?? "");
        _hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Quick Accent klavye kancası kurulamadı.");
        IsActive = true;
        _holdTimer.Start();
    }

    public void Deactivate()
    {
        IsActive = false;
        _holdTimer.Stop();
        ResetPending();
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public void Apply()
    {
        if (!IsActive) return;
        Deactivate();
        Activate();
    }

    internal static IReadOnlyList<char> GetChoices(char character) => QuickAccentCharacters.Get(character);
    internal static IReadOnlyList<char> GetChoices(char character, string languageSet) => QuickAccentCharacters.Get(character, languageSet);
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

    private IntPtr HandleKeyboard(int code, IntPtr messagePointer, IntPtr dataPointer)
    {
        if (code < 0 || !IsActive) return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
        try
        {
            var message = messagePointer.ToInt32();
            var keyDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            var keyUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            if (!keyDown && !keyUp) return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(dataPointer);
            if ((info.flags & InjectedFlag) != 0) return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
            var key = (int)info.vkCode;

            if (_picker?.IsVisible == true && HandlePickerKey(key, keyDown, keyUp)) return new IntPtr(1);
            if (keyDown && _heldKey != 0 && key != _heldKey && !IsModifier(key)) ResetPending();

            if (keyDown && key is >= 0x41 and <= 0x5A && !HasShortcutModifiers())
            {
                if (_disableFullscreen && IsFullscreenWindow(Native.GetForegroundWindow()))
                    return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
                if (IsExcludedWindow(Native.GetForegroundWindow(), _excludedProcesses))
                    return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
                if (_heldKey == key) return new IntPtr(1);
                if (_heldKey != 0) ResetPending();
                var character = GetForegroundCharacter(key, info.scanCode);
                if (character != '\0' && QuickAccentCharacters.Get(character, _languageSet).Count > 0)
                {
                    _heldKey = key;
                    _baseCharacter = character;
                    _targetWindow = Native.GetForegroundWindow();
                    _heldAt = Environment.TickCount64;
                    return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
                }
            }

            if (keyUp && key == _heldKey)
            {
                if (_picker?.IsVisible == true) CommitChoice();
                ResetPending();
                return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
            }

            if (keyDown && IsModifier(key) && _heldKey != 0) ResetPending();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Quick Accent klavye girdisi işlenemedi.", exception);
        }
        return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
    }

    private bool HandlePickerKey(int key, bool keyDown, bool keyUp)
    {
        if (!keyDown)
        {
            if (!keyUp || key != _heldKey) return false;
            CommitChoice();
            ResetPending();
            return true;
        }
        if (key == KeyLeft || key == KeyUp || key == KeySpace)
        {
            _picker!.MoveSelection(-1);
            return true;
        }
        if (key == KeyRight || key == KeyDown)
        {
            _picker!.MoveSelection(1);
            return true;
        }
        if (key is KeyEscape or KeyReturn)
        {
            if (key == KeyReturn) CommitChoice();
            else _picker?.Hide();
            _picker = null;
            _heldKey = 0;
            return true;
        }
        return false;
    }

    private void CheckHoldDuration()
    {
        if (_heldKey == 0 || _picker?.IsVisible == true || Environment.TickCount64 - _heldAt < _holdDuration) return;
        if (_targetWindow == IntPtr.Zero || Native.GetForegroundWindow() != _targetWindow)
        {
            ResetPending();
            return;
        }
        _choices = QuickAccentCharacters.Get(_baseCharacter, _languageSet);
        if (_choices.Count == 0) return;
        _picker = new QuickAccentPickerWindow(_choices);
        _picker.Show();
    }

    private void CommitChoice()
    {
        if (_picker is null || _choices.Count == 0) return;
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground != _targetWindow || !Native.IsWindow(foreground)) return;
        var selected = _choices[_picker.SelectedIndex];
        SendKey(KeyBackspace, keyUp: false);
        SendKey(KeyBackspace, keyUp: true);
        SendUnicode(selected);
        _picker.Hide();
        _picker = null;
    }

    private void ResetPending()
    {
        _picker?.Hide();
        _picker = null;
        _heldKey = 0;
        _baseCharacter = '\0';
        _targetWindow = IntPtr.Zero;
        _choices = Array.Empty<char>();
    }

    private static bool HasShortcutModifiers() =>
        IsKeyDown(0x11) || IsKeyDown(0x12) || IsKeyDown(0x5B) || IsKeyDown(0x5C);

    private static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static char GetForegroundCharacter(int key, uint scanCode)
    {
        var foreground = Native.GetForegroundWindow();
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        var layout = GetKeyboardLayout(thread);
        var state = new byte[256];
        if (!GetKeyboardState(state)) return '\0';
        var buffer = new StringBuilder(4);
        var count = ToUnicodeEx((uint)key, scanCode, state, buffer, buffer.Capacity, 0, layout);
        if (count != 1) return '\0';
        var character = buffer[0];
        return char.IsLetter(character) ? character : '\0';
    }

    private static bool IsFullscreenWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out var windowRect)) return false;
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        const int edgeTolerance = 3;
        return Math.Abs(windowRect.Left - info.Bounds.Left) <= edgeTolerance &&
               Math.Abs(windowRect.Top - info.Bounds.Top) <= edgeTolerance &&
               Math.Abs(windowRect.Right - info.Bounds.Right) <= edgeTolerance &&
               Math.Abs(windowRect.Bottom - info.Bounds.Bottom) <= edgeTolerance;
    }

    private static bool IsExcludedWindow(IntPtr window, IReadOnlySet<string> excludedProcesses)
    {
        if (window == IntPtr.Zero || excludedProcesses.Count == 0) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return excludedProcesses.Contains(process.ProcessName);
        }
        catch { return false; }
    }

    private static void SendKey(int key, bool keyUp)
    {
        var input = new INPUT
        {
            Type = InputKeyboard,
            Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = (ushort)key, Flags = keyUp ? KeyeventKeyup : 0 } }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendUnicode(char character)
    {
        INPUT Create(bool keyUp) => new()
        {
            Type = InputKeyboard,
            Union = new InputUnion { Keyboard = new KEYBDINPUT { ScanCode = character, Flags = KeyeventUnicode | (keyUp ? KeyeventKeyup : 0) } }
        };
        SendInput(2, [Create(false), Create(true)], Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyboardState, StringBuilder buffer, int bufferLength, uint flags, IntPtr keyboardLayout);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RECT Bounds;
        public RECT WorkArea;
        public uint Flags;
    }

}

internal static class QuickAccentCharacters
{
    private static readonly IReadOnlyDictionary<char, string> CharacterSets = new Dictionary<char, string>
    {
        ['a'] = "àáâãäåāăąǎǟǡǻȁȃȧạảấầẩẫậắằẳẵặæ",
        ['c'] = "çćĉċčƈȼ",
        ['d'] = "ďđðƌȡ",
        ['e'] = "èéêëēĕėęěȅȇȩẹẻẽếềểễệ",
        ['g'] = "ĝğġģǧǵɠ",
        ['h'] = "ĥħȟɦ",
        ['i'] = "ìíîïĩīĭįıǐȉȋịỉɨ",
        ['j'] = "ĵǰɉ",
        ['k'] = "ķǩƙ",
        ['l'] = "ĺļľŀłƚȴ",
        ['n'] = "ñńņňŉŋǹṅṇṋ",
        ['o'] = "òóôõöøōŏőǒǫǭȍȏȫȭȯȱọỏốồổỗộớờởỡợœ",
        ['r'] = "ŕŗřȑȓɍ",
        ['s'] = "śŝşšșßſ",
        ['t'] = "ţťŧƭȶþ",
        ['u'] = "ùúûüũūŭůűųǔǖǘǚǜȕȗụủứừửữự",
        ['w'] = "ŵẁẃẅẇẉ",
        ['y'] = "ýÿŷȳẏỳỵỷỹ",
        ['z'] = "źżžƶȥɀ"
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<char, HashSet<char>>> LanguageSets =
        new Dictionary<string, IReadOnlyDictionary<char, HashSet<char>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tümü"] = CharacterSets.ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet()),
            ["Fransızca"] = BuildLanguageSet(new Dictionary<char, string>
            {
                ['a'] = "àâäæ", ['c'] = "ç", ['e'] = "éèêë", ['i'] = "îï", ['o'] = "ô", ['u'] = "ùûü", ['y'] = "ÿ"
            }),
            ["Almanca"] = BuildLanguageSet(new Dictionary<char, string>
            {
                ['a'] = "ä", ['o'] = "ö", ['s'] = "ß", ['u'] = "ü"
            }),
            ["İspanyolca"] = BuildLanguageSet(new Dictionary<char, string>
            {
                ['a'] = "á", ['e'] = "é", ['i'] = "í", ['n'] = "ñ", ['o'] = "ó", ['u'] = "úü"
            }),
            ["Türkçe"] = BuildLanguageSet(new Dictionary<char, string>
            {
                ['c'] = "ç", ['g'] = "ğ", ['i'] = "ı", ['o'] = "ö", ['s'] = "ş", ['u'] = "ü"
            }),
            ["İskandinav"] = BuildLanguageSet(new Dictionary<char, string>
            {
                ['a'] = "åäæ", ['d'] = "ð", ['e'] = "æ", ['o'] = "øö", ['s'] = "š", ['t'] = "þ", ['u'] = "ü", ['y'] = "ý"
            }),
            ["Orta Avrupa"] = BuildLanguageSet(new Dictionary<char, string>
            {
                ['a'] = "áä", ['c'] = "ćč", ['d'] = "ď", ['e'] = "éě", ['i'] = "í", ['l'] = "ľ", ['n'] = "ńň",
                ['o'] = "óö", ['r'] = "ř", ['s'] = "śš", ['t'] = "ť", ['u'] = "úů", ['y'] = "ý", ['z'] = "źž"
            })
        };

    public static string NormalizeLanguageSet(string languageSet) =>
        LanguageSets.ContainsKey(languageSet) ? languageSet : "Tümü";

    private static IReadOnlyDictionary<char, HashSet<char>> BuildLanguageSet(IReadOnlyDictionary<char, string> accents)
    {
        var result = new Dictionary<char, HashSet<char>>();
        foreach (var (letter, variants) in accents)
        {
            var lower = char.ToLowerInvariant(letter);
            if (!CharacterSets.TryGetValue(lower, out var choices)) continue;
            result[lower] = choices.Where(variants.Contains).ToHashSet();
        }
        return result;
    }

    public static IReadOnlyList<char> Get(char character) => Get(character, "Tümü");

    public static IReadOnlyList<char> Get(char character, string languageSet)
    {
        var lower = char.ToLowerInvariant(character);
        if (!CharacterSets.TryGetValue(lower, out var set)) return Array.Empty<char>();
        var normalizedLanguageSet = NormalizeLanguageSet(languageSet);
        if (!LanguageSets[normalizedLanguageSet].TryGetValue(lower, out var allowed)) return Array.Empty<char>();
        var result = set.Where(allowed.Contains).ToList();
        if (char.IsUpper(character))
        {
            for (var index = 0; index < result.Count; index++)
                result[index] = result[index] == 'ß' ? 'ẞ' : char.ToUpperInvariant(result[index]);
        }
        return result;
    }
}

internal sealed class QuickAccentPickerWindow : Window
{
    private readonly TextBlock[] _characters;
    public int SelectedIndex { get; private set; }

    public QuickAccentPickerWindow(IReadOnlyList<char> choices)
    {
        Width = Math.Max(280, Math.Min(720, choices.Count * 38 + 32));
        Height = 76;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 32, 38, 49)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(93, 119, 158)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = BuildChoices(choices, out _characters)
            }
        };
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 48;
        UpdateSelection();
    }

    public void MoveSelection(int delta)
    {
        SelectedIndex = (SelectedIndex + delta + _characters.Length) % _characters.Length;
        UpdateSelection();
    }

    private StackPanel BuildChoices(IReadOnlyList<char> choices, out TextBlock[] labels)
    {
        labels = new TextBlock[choices.Count];
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var index = 0; index < choices.Count; index++)
        {
            var label = new TextBlock
            {
                Text = choices[index].ToString(),
                FontSize = 24,
                Width = 34,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White,
                Padding = new Thickness(3)
            };
            labels[index] = label;
            row.Children.Add(new Border { Child = label, CornerRadius = new CornerRadius(5), Margin = new Thickness(1) });
        }
        return row;
    }

    private void UpdateSelection()
    {
        for (var index = 0; index < _characters.Length; index++)
        {
            var border = (Border)((StackPanel)((ScrollViewer)((Border)Content).Child).Content).Children[index];
            border.Background = index == SelectedIndex
                ? new SolidColorBrush(Color.FromRgb(59, 94, 145))
                : Brushes.Transparent;
        }
    }
}
