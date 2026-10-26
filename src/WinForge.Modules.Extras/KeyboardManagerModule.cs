using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForge.Core;

using static WinForge.Modules.Extras.KeyboardInputSender;

namespace WinForge.Modules.Extras;

public sealed class KeyboardManagerModule : ITweakModule
{
    private const int LlkhfInjected = 0x10;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfExtendedkey = 0x0001;
    private const uint KeyeventfUnicode = 0x0004;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    internal const uint ModifierControl = 1;
    internal const uint ModifierAlt = 2;
    internal const uint ModifierShift = 4;
    internal const uint ModifierWin = 8;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly HashSet<int> _pressedKeys = new();
    private readonly HashSet<int> _suppressedKeyUps = new();
    private readonly Dictionary<int, int> _activeKeyRemaps = new();
    private readonly Dictionary<int, int> _keyMappings = new();
    private readonly List<ShortcutMapping> _shortcutMappings = new();
    private readonly List<ShortcutActionMapping> _shortcutActions = new();
    private List<ShortcutActionMapping> _pendingShortcutSequence = new();
    private int _pendingShortcutSequenceIndex;
    private DateTime _pendingShortcutSequenceExpires;
    private readonly List<TextMapping> _textMappings = new();
    private readonly Dictionary<string, ApplicationMappingSet> _applicationMappings = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _cachedForegroundWindow;
    private ApplicationMappingSet? _cachedApplicationMappings;
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private IntPtr _hook;

    public string Id => "keyboard-manager";
    public string Name => "Klavye Yöneticisi";
    public string Category => "Verimlilik";
    public string Description => "Tuşları ve kısayolları başka tuşlara, Unicode metne, uygulamalara veya URI'lere eşler; uygulama bazlı kurallar ve çok adımlı kısayolları destekler.";
    public string? Warning => "Yönetici olarak çalışan uygulamalarda WinForge'u da yönetici olarak çalıştırın. Ctrl+Alt+Del ve Win+L gibi Windows tarafından ayrılmış kısayollar değiştirilemez.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public KeyboardManagerModule()
    {
        _callback = HandleKeyboard;
        Settings = new[]
        {
            new SettingDescriptor
            {
                Key = "open-editor",
                Label = "Klavye eşlemelerini düzenle",
                Kind = SettingKind.Action,
                Description = "Genel/uygulama bazlı tuş eşlemelerini, metin dizilerini, ardışık chord kısayollarını ve uygulama/URI eylemlerini açıp kaydedin.",
                Execute = OpenEditor
            },
            new SettingDescriptor
            {
                Key = "hook-enabled",
                Label = "Eşlemeleri uygula",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Modül açıkken tanımlı eşlemeleri anında uygular."
            }
        };
    }

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        try
        {
            Apply();
            _context?.Log.Info("Klavye Yöneticisi etkin.");
        }
        catch
        {
            Deactivate();
            throw;
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _pressedKeys.Clear();
        _suppressedKeyUps.Clear();
        _activeKeyRemaps.Clear();
        ResetPendingShortcutSequence();
        _applicationMappings.Clear();
        _cachedForegroundWindow = IntPtr.Zero;
        _cachedApplicationMappings = null;
    }

    public void Apply()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        StopHook();
        _pressedKeys.Clear();
        _suppressedKeyUps.Clear();
        _activeKeyRemaps.Clear();
        ResetPendingShortcutSequence();
        _cachedForegroundWindow = IntPtr.Zero;
        _cachedApplicationMappings = null;
        LoadMappings();

        if (!IsActive || !(_context?.Settings.GetBool("hook-enabled", true) ?? true)) return;
        if (_keyMappings.Count == 0 && _shortcutMappings.Count == 0 && _shortcutActions.Count == 0 &&
            _textMappings.Count == 0 && _applicationMappings.Count == 0) return;

        _hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Klavye eşleme kancası kurulamadı.");
    }

    private void StopHook()
    {
        foreach (var mappedKey in _activeKeyRemaps.Values.Distinct())
            SendKey(mappedKey, keyUp: true);
        _activeKeyRemaps.Clear();

        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private void LoadMappings()
    {
        _keyMappings.Clear();
        _shortcutMappings.Clear();
        _shortcutActions.Clear();
        _textMappings.Clear();
        _applicationMappings.Clear();
        var settings = _context?.Settings;
        var keyRows = Deserialize<List<KeyMapping>>(settings?.GetString("key-mappings", "") ?? "");
        var shortcutRows = Deserialize<List<ShortcutMapping>>(settings?.GetString("shortcut-mappings", "") ?? "");
        var actionRows = Deserialize<List<ShortcutActionMapping>>(settings?.GetString("shortcut-actions", "") ?? "");
        var textRows = Deserialize<List<TextMapping>>(settings?.GetString("text-mappings", "") ?? "");
        var appRows = Deserialize<List<AppSpecificMapping>>(settings?.GetString("app-specific-mappings", "") ?? "");

        foreach (var mapping in keyRows)
            if (mapping.From is > 0 and < 255 && mapping.To is > 0 and < 255 && mapping.From != mapping.To)
                _keyMappings.TryAdd(mapping.From, mapping.To);
        _shortcutMappings.AddRange(shortcutRows.Where(mapping =>
            mapping.FromKey is > 0 and < 255 && mapping.ToKey is > 0 and < 255 &&
            mapping.FromModifiers != 0 && !IsReserved(mapping.FromModifiers, mapping.FromKey) &&
            !IsReserved(mapping.ToModifiers, mapping.ToKey)));
        _shortcutActions.AddRange(actionRows.Where(IsValidShortcutAction));
        _textMappings.AddRange(textRows.Where(mapping =>
            mapping.FromKey is > 0 and < 255 &&
            (mapping.FromModifiers & ~(ModifierControl | ModifierAlt | ModifierShift | ModifierWin)) == 0 &&
            !IsReserved(mapping.FromModifiers, mapping.FromKey) && !string.IsNullOrEmpty(mapping.Text) && mapping.Text.Length <= 2048));
        foreach (var mapping in appRows.Where(IsValidAppSpecificMapping))
        {
            var processName = NormalizeProcessName(mapping.ProcessName);
            if (!_applicationMappings.TryGetValue(processName, out var mappings))
                _applicationMappings.Add(processName, mappings = new ApplicationMappingSet());
            if (mapping.IsShortcut)
                mappings.ShortcutMappings.TryAdd((mapping.FromModifiers, mapping.From), new ShortcutMapping
                {
                    FromModifiers = mapping.FromModifiers, FromKey = mapping.From,
                    ToModifiers = mapping.ToModifiers, ToKey = mapping.To
                });
            else
                mappings.KeyMappings.TryAdd(mapping.From, mapping.To);
        }
    }

    private static T Deserialize<T>(string json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T(); }
        catch { return new T(); }
    }

    private void OpenEditor()
    {
        var settings = _context?.Settings;
        if (settings is null) return;

        var currentKeys = Deserialize<List<KeyMapping>>(settings.GetString("key-mappings", ""));
        var currentShortcuts = Deserialize<List<ShortcutMapping>>(settings.GetString("shortcut-mappings", ""));
        var currentActions = Deserialize<List<ShortcutActionMapping>>(settings.GetString("shortcut-actions", ""));
        var currentTextMappings = Deserialize<List<TextMapping>>(settings.GetString("text-mappings", ""));
        var currentAppMappings = Deserialize<List<AppSpecificMapping>>(settings.GetString("app-specific-mappings", ""));
        var editor = new KeyboardMappingEditor(currentKeys, currentShortcuts, currentAppMappings, currentTextMappings, currentActions)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };

        if (editor.ShowDialog() != true) return;
        settings.Set("key-mappings", JsonSerializer.Serialize(editor.KeyMappings));
        settings.Set("shortcut-mappings", JsonSerializer.Serialize(editor.ShortcutMappings));
        settings.Set("shortcut-actions", JsonSerializer.Serialize(editor.ShortcutActions));
        settings.Set("text-mappings", JsonSerializer.Serialize(editor.TextMappings));
        settings.Set("app-specific-mappings", JsonSerializer.Serialize(editor.AppSpecificMappings));
        if (IsActive) Apply();
    }

    private IntPtr HandleKeyboard(int code, IntPtr messagePointer, IntPtr dataPointer)
    {
        if (code < 0 || !IsActive) return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);

        try
        {
            var message = messagePointer.ToInt32();
            var isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            var isUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            if (!isDown && !isUp) return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);

            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(dataPointer);
            var key = (int)info.vkCode;
            if ((info.flags & LlkhfInjected) != 0)
                return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);

            var isFirstKeyDown = isDown && _pressedKeys.Add(key);
            var appMappings = isDown ? GetForegroundApplicationMappings() : null;

            if (isUp && _activeKeyRemaps.Remove(key, out var remappedKey))
            {
                SendKey(remappedKey, keyUp: true);
                _pressedKeys.Remove(key);
                return new IntPtr(1);
            }

            if (isUp && _suppressedKeyUps.Remove(key))
            {
                _pressedKeys.Remove(key);
                return new IntPtr(1);
            }

            if (isDown && TryGetShortcut(key, appMappings, out var shortcut))
            {
                if (SendShortcut(shortcut, PressedModifierKeys()))
                {
                    _suppressedKeyUps.Add(key);
                    return new IntPtr(1);
                }
            }

            if (isDown && TryGetTextMapping(key, out var textMapping))
            {
                if (!isFirstKeyDown || SendText(textMapping.Text, PressedModifierKeys()))
                {
                    _suppressedKeyUps.Add(key);
                    return new IntPtr(1);
                }
            }

            if (isDown && TryGetShortcutAction(key, out var actionMapping))
            {
                if (!isFirstKeyDown || QueueShortcutAction(actionMapping))
                {
                    _suppressedKeyUps.Add(key);
                    return new IntPtr(1);
                }
            }

            if (isDown && (appMappings?.KeyMappings.TryGetValue(key, out remappedKey) == true ||
                           _keyMappings.TryGetValue(key, out remappedKey)))
            {
                if (SendKey(remappedKey, keyUp: false))
                {
                    _activeKeyRemaps[key] = remappedKey;
                    return new IntPtr(1);
                }
            }

            if (isUp) _pressedKeys.Remove(key);
            return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Klavye eşlemesi uygulanamadı.", exception);
            return Native.CallNextHookEx(_hook, code, messagePointer, dataPointer);
        }
    }

    private bool TryGetShortcut(int key, ApplicationMappingSet? appMappings, out ShortcutMapping mapping)
    {
        var modifiers = CurrentModifiers();
        if (appMappings?.ShortcutMappings.TryGetValue((modifiers, key), out var appMapping) == true)
        {
            mapping = appMapping;
            return true;
        }
        mapping = _shortcutMappings.FirstOrDefault(candidate =>
            candidate.FromKey == key && candidate.FromModifiers == modifiers)!;
        return mapping is not null;
    }

    private bool TryGetTextMapping(int key, out TextMapping mapping)
    {
        var modifiers = CurrentModifiers();
        mapping = _textMappings.FirstOrDefault(candidate =>
            candidate.FromKey == key && candidate.FromModifiers == modifiers)!;
        return mapping is not null;
    }

    private bool TryGetShortcutAction(int key, out ShortcutActionMapping mapping)
    {
        var stroke = new ShortcutStroke { Modifiers = CurrentModifiers(), Key = key };
        if (_pendingShortcutSequence.Count > 0 && DateTime.UtcNow < _pendingShortcutSequenceExpires)
        {
            var matches = _pendingShortcutSequence
                .Where(candidate => GetShortcutStrokes(candidate).Count > _pendingShortcutSequenceIndex &&
                    StrokesMatch(GetShortcutStrokes(candidate)[_pendingShortcutSequenceIndex], stroke))
                .ToList();
            if (matches.Count > 0)
            {
                var completed = matches.FirstOrDefault(candidate =>
                    GetShortcutStrokes(candidate).Count == _pendingShortcutSequenceIndex + 1);
                if (completed is not null)
                {
                    ResetPendingShortcutSequence();
                    mapping = completed;
                    return true;
                }
                _pendingShortcutSequence = matches;
                _pendingShortcutSequenceIndex++;
                _pendingShortcutSequenceExpires = DateTime.UtcNow.AddSeconds(2);
                mapping = null!;
                return false;
            }
            ResetPendingShortcutSequence();
        }
        else
        {
            ResetPendingShortcutSequence();
        }

        var immediate = _shortcutActions.FirstOrDefault(candidate =>
        {
            var strokes = GetShortcutStrokes(candidate);
            return strokes.Count == 1 && StrokesMatch(strokes[0], stroke);
        });
        if (immediate is not null)
        {
            mapping = immediate;
            return true;
        }

        var sequences = _shortcutActions.Where(candidate =>
        {
            var strokes = GetShortcutStrokes(candidate);
            return strokes.Count > 1 && StrokesMatch(strokes[0], stroke);
        }).ToList();
        if (sequences.Count > 0)
        {
            _pendingShortcutSequence = sequences;
            _pendingShortcutSequenceIndex = 1;
            _pendingShortcutSequenceExpires = DateTime.UtcNow.AddSeconds(2);
        }
        mapping = null!;
        return false;
    }

    private static IReadOnlyList<ShortcutStroke> GetShortcutStrokes(ShortcutActionMapping mapping) =>
        mapping.Sequence is { Count: > 0 } ? mapping.Sequence : [new ShortcutStroke { Modifiers = mapping.FromModifiers, Key = mapping.FromKey }];

    private static bool StrokesMatch(ShortcutStroke expected, ShortcutStroke actual) =>
        expected.Key == actual.Key && expected.Modifiers == actual.Modifiers;

    private void ResetPendingShortcutSequence()
    {
        _pendingShortcutSequence.Clear();
        _pendingShortcutSequenceIndex = 0;
        _pendingShortcutSequenceExpires = DateTime.MinValue;
    }

    private bool QueueShortcutAction(ShortcutActionMapping mapping)
    {
        try
        {
            _ = Task.Run(() => ExecuteShortcutAction(mapping));
            return true;
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Kısayol eylemi sıraya alınamadı.", exception);
            return false;
        }
    }

    private void ExecuteShortcutAction(ShortcutActionMapping mapping)
    {
        try
        {
            var target = Environment.ExpandEnvironmentVariables(mapping.Target);
            if (mapping.ActionType.Equals("uri", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }

            using var runningProcess = mapping.IfRunning == "new" ? null : FindRunningApplication(target);
            switch (mapping.IfRunning)
            {
                case "show" when runningProcess is not null:
                    ShowRunningApplication(runningProcess);
                    return;
                case "nothing" when runningProcess is not null:
                    return;
                case "close" when runningProcess is not null:
                    runningProcess.CloseMainWindow();
                    return;
                case "end-task" when runningProcess is not null:
                    runningProcess.Kill();
                    return;
            }

            var workingDirectory = string.IsNullOrWhiteSpace(mapping.WorkingDirectory)
                ? Environment.CurrentDirectory
                : Environment.ExpandEnvironmentVariables(mapping.WorkingDirectory);
            var useShellExecute = mapping.Elevation != "normal";
            var startInfo = new ProcessStartInfo(target)
            {
                Arguments = mapping.Arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = useShellExecute,
                WindowStyle = mapping.Visibility == "hidden" ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                CreateNoWindow = !useShellExecute && mapping.Visibility == "hidden"
            };
            if (useShellExecute)
                startInfo.Verb = mapping.Elevation == "elevated" ? "runas" : "runasuser";
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Klavye kısayoluyla uygulama veya URI açılamadı.", exception);
            Application.Current?.Dispatcher.BeginInvoke(() =>
                _context?.Notify("Kısayol eylemi çalıştırılamadı: " + exception.Message));
        }
    }

    private static Process? FindRunningApplication(string target)
    {
        var processName = Path.GetFileNameWithoutExtension(target);
        if (processName.Length == 0) return null;
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName(processName);
            var matching = processes
                .Where(process => !Path.IsPathRooted(target) || IsSameExecutable(process, target))
                .OrderByDescending(HasMainWindow)
                .ThenBy(process => process.Id)
                .FirstOrDefault();
            foreach (var process in processes)
                if (!ReferenceEquals(process, matching)) process.Dispose();
            return matching;
        }
        catch
        {
            foreach (var process in processes) process.Dispose();
            return null;
        }
    }

    private static bool IsSameExecutable(Process process, string target)
    {
        try
        {
            var executablePath = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(executablePath) &&
                   string.Equals(Path.GetFullPath(executablePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool HasMainWindow(Process process)
    {
        try { return process.MainWindowHandle != IntPtr.Zero; }
        catch { return false; }
    }

    private static void ShowRunningApplication(Process process)
    {
        var window = process.MainWindowHandle;
        if (window == IntPtr.Zero) return;
        ShowWindowAsync(window, SwRestore);
        SetForegroundWindow(window);
    }

    internal static bool IsValidShortcutAction(ShortcutActionMapping mapping)
    {
        var strokes = GetShortcutStrokes(mapping);
        if (strokes.Count is < 1 or > 4 || strokes.Any(stroke =>
                stroke.Key is <= 0 or >= 255 || stroke.Modifiers == 0 ||
                (stroke.Modifiers & ~(ModifierControl | ModifierAlt | ModifierShift | ModifierWin)) != 0 ||
                IsReserved(stroke.Modifiers, stroke.Key)) || string.IsNullOrWhiteSpace(mapping.Target)) return false;
        if (string.Equals(mapping.ActionType, "uri", StringComparison.OrdinalIgnoreCase))
            return TryValidateUri(mapping.Target);
        if (!string.Equals(mapping.ActionType, "app", StringComparison.OrdinalIgnoreCase)) return false;
        return mapping.Elevation is "normal" or "elevated" or "different-user" &&
               mapping.IfRunning is "new" or "show" or "nothing" or "close" or "end-task" &&
               mapping.Visibility is "visible" or "hidden";
    }

    private static bool TryValidateUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.IsWellFormedOriginalString() &&
               uri.Scheme is "http" or "https" or "mailto" or "ms-settings" or "shell" or "file";
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private ApplicationMappingSet? GetForegroundApplicationMappings()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == _cachedForegroundWindow) return _cachedApplicationMappings;
        _cachedForegroundWindow = foreground;
        _cachedApplicationMappings = null;
        if (foreground == IntPtr.Zero) return null;

        Native.GetWindowThreadProcessId(foreground, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            _applicationMappings.TryGetValue(process.ProcessName, out _cachedApplicationMappings);
        }
        catch { }
        return _cachedApplicationMappings;
    }

    internal static string NormalizeProcessName(string value)
    {
        var name = Path.GetFileNameWithoutExtension(value.Trim().Trim('"', '\''));
        return name.Trim();
    }

    private static bool IsValidAppSpecificMapping(AppSpecificMapping mapping) =>
        !string.IsNullOrWhiteSpace(mapping.ProcessName) && NormalizeProcessName(mapping.ProcessName).Length > 0 &&
        mapping.From is > 0 and < 255 && mapping.To is > 0 and < 255 &&
        (!mapping.IsShortcut || (mapping.FromModifiers != 0 &&
                                (mapping.FromModifiers & ~(ModifierControl | ModifierAlt | ModifierShift | ModifierWin)) == 0 &&
                                (mapping.ToModifiers & ~(ModifierControl | ModifierAlt | ModifierShift | ModifierWin)) == 0 &&
                                !IsReserved(mapping.FromModifiers, mapping.From) && !IsReserved(mapping.ToModifiers, mapping.To)));

    private uint CurrentModifiers()
    {
        uint modifiers = 0;
        if (IsAnyPressed(VkLControl, VkRControl, 0x11)) modifiers |= ModifierControl;
        if (IsAnyPressed(VkLMenu, VkRMenu, 0x12)) modifiers |= ModifierAlt;
        if (IsAnyPressed(VkLShift, VkRShift, 0x10)) modifiers |= ModifierShift;
        if (IsAnyPressed(VkLWin, VkRWin)) modifiers |= ModifierWin;
        return modifiers;
    }

    private bool IsAnyPressed(params int[] keys) => keys.Any(_pressedKeys.Contains);

    private int[] PressedModifierKeys() =>
    [
        .. _pressedKeys.Where(IsModifierKey)
    ];

    private static bool SendShortcut(ShortcutMapping mapping, IReadOnlyList<int> pressedModifiers)
    {
        var events = new List<INPUT>();
        var targetModifiers = ToModifierKeys(mapping.ToModifiers);
        var releaseSource = pressedModifiers
            .Where(key => (ModifierFlag(key) & mapping.ToModifiers) == 0)
            .ToArray();
        var sourceFlags = pressedModifiers.Aggregate(0u, (flags, key) => flags | ModifierFlag(key));
        var pressTarget = targetModifiers
            .Where(key => (sourceFlags & ModifierFlag(key)) == 0)
            .ToArray();

        foreach (var key in releaseSource) events.Add(KeyInput(key, true));
        foreach (var key in pressTarget) events.Add(KeyInput(key, false));
        events.Add(KeyInput(mapping.ToKey, false));
        events.Add(KeyInput(mapping.ToKey, true));
        foreach (var key in pressTarget.Reverse()) events.Add(KeyInput(key, true));
        foreach (var key in releaseSource.Reverse()) events.Add(KeyInput(key, false));

        return SendInput((uint)events.Count, events.ToArray(), Marshal.SizeOf<INPUT>()) == events.Count;
    }

    private static bool SendText(string text, IReadOnlyList<int> pressedModifiers)
    {
        var events = new List<INPUT>(pressedModifiers.Count * 2 + text.Length * 2);
        foreach (var key in pressedModifiers) events.Add(KeyInput(key, true));
        foreach (var character in text)
        {
            events.Add(UnicodeInput(character, keyUp: false));
            events.Add(UnicodeInput(character, keyUp: true));
        }
        foreach (var key in pressedModifiers.Reverse()) events.Add(KeyInput(key, false));
        return SendInput((uint)events.Count, events.ToArray(), Marshal.SizeOf<INPUT>()) == events.Count;
    }

    private static int[] ToModifierKeys(uint modifiers)
    {
        var keys = new List<int>(4);
        if ((modifiers & ModifierControl) != 0) keys.Add(VkLControl);
        if ((modifiers & ModifierAlt) != 0) keys.Add(VkLMenu);
        if ((modifiers & ModifierShift) != 0) keys.Add(VkLShift);
        if ((modifiers & ModifierWin) != 0) keys.Add(VkLWin);
        return keys.ToArray();
    }

    private static uint ModifierFlag(int key) => key switch
    {
        VkLControl or VkRControl or 0x11 => ModifierControl,
        VkLMenu or VkRMenu or 0x12 => ModifierAlt,
        VkLShift or VkRShift or 0x10 => ModifierShift,
        VkLWin or VkRWin => ModifierWin,
        _ => 0
    };

    private static bool IsModifierKey(int key) => ModifierFlag(key) != 0;

    private static bool SendKey(int key, bool keyUp) =>
        SendInput(1, [KeyInput(key, keyUp)], Marshal.SizeOf<INPUT>()) == 1;

    private static INPUT KeyInput(int key, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                VirtualKey = (ushort)key,
                Flags = (keyUp ? KeyeventfKeyup : 0) | (IsExtendedKey(key) ? KeyeventfExtendedkey : 0)
            }
        }
    };

    private static INPUT UnicodeInput(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                ScanCode = character,
                Flags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0)
            }
        }
    };

    private static bool IsExtendedKey(int key) => key is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0xA3 or 0xA5;

    internal static bool IsReserved(uint modifiers, int key) =>
        (modifiers == ModifierWin && key == 0x4C) ||
        (modifiers == (ModifierControl | ModifierAlt) && key == 0x2E) ||
        (modifiers == HotkeyModifiers.Alt && key is 0x46 or 0x51) ||
        (modifiers == (HotkeyModifiers.Alt | HotkeyModifiers.Shift) && key == 0x4C) ||
        (modifiers == (HotkeyModifiers.Win | HotkeyModifiers.Alt) && key is 0x20 or 0x43 or 0x50) ||
        (modifiers == (HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.Alt) && key == 0x56) ||
        (modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && key == 0x4D);

    internal sealed class KeyMapping
    {
        public KeyMapping() { }
        public int From { get; set; }
        public int To { get; set; }
    }

    internal sealed class ShortcutMapping
    {
        public ShortcutMapping() { }
        public uint FromModifiers { get; set; }
        public int FromKey { get; set; }
        public uint ToModifiers { get; set; }
        public int ToKey { get; set; }
    }

    internal sealed class TextMapping
    {
        public uint FromModifiers { get; set; }
        public int FromKey { get; set; }
        public string Text { get; set; } = "";
    }

    internal sealed class ShortcutActionMapping
    {
        public uint FromModifiers { get; set; }
        public int FromKey { get; set; }
        public List<ShortcutStroke> Sequence { get; set; } = new();
        public string ActionType { get; set; } = "uri";
        public string Target { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string Elevation { get; set; } = "normal";
        public string IfRunning { get; set; } = "new";
        public string Visibility { get; set; } = "visible";
    }

    internal sealed class ShortcutStroke
    {
        public uint Modifiers { get; set; }
        public int Key { get; set; }
    }

    internal sealed class AppSpecificMapping
    {
        public string ProcessName { get; set; } = "";
        public bool IsShortcut { get; set; }
        public int From { get; set; }
        public int To { get; set; }
        public uint FromModifiers { get; set; }
        public uint ToModifiers { get; set; }
    }

    private sealed class ApplicationMappingSet
    {
        public Dictionary<int, int> KeyMappings { get; } = new();
        public Dictionary<(uint Modifiers, int Key), ShortcutMapping> ShortcutMappings { get; } = new();
    }

}

internal sealed class KeyboardMappingEditor : Window
{
    private readonly TextBox _keyMappings;
    private readonly TextBox _shortcutMappings;
    private readonly TextBox _appSpecificMappings;
    private readonly TextBox _textMappings;
    private readonly TextBox _shortcutActions;
    private readonly TextBlock _error;

    public List<object> KeyMappings { get; private set; } = new();
    public List<object> ShortcutMappings { get; private set; } = new();
    public List<object> AppSpecificMappings { get; private set; } = new();
    public List<object> TextMappings { get; private set; } = new();
    public List<object> ShortcutActions { get; private set; } = new();

    public KeyboardMappingEditor(IEnumerable<object> keys, IEnumerable<object> shortcuts, IEnumerable<object> appSpecificMappings,
        IEnumerable<object> textMappings, IEnumerable<object> shortcutActions)
    {
        Title = "Klavye eşlemeleri";
        Width = 760;
        Height = 640;
        MinWidth = 600;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(27, 31, 40));
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(18) };
        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 12) };
        _keyMappings = CreateEditor();
        _shortcutMappings = CreateEditor();
        _appSpecificMappings = CreateEditor();
        _textMappings = CreateEditor();
        _shortcutActions = CreateEditor();
        tabs.Items.Add(new TabItem
        {
            Header = "Tuş eşlemeleri",
            Content = EditorPage("Her satırda kaynak tuş = gönderilecek tuş. Örnek: A = B", _keyMappings)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Kısayol eşlemeleri",
            Content = EditorPage("Her satırda kaynak kısayol = gönderilecek kısayol. Örnek: Ctrl+C = Ctrl+Insert", _shortcutMappings)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Uygulamaya özel",
            Content = EditorPage("Her satırda işlem.exe: kaynak = hedef. Örnek: msedge.exe: Ctrl+F = Ctrl+H veya notepad.exe: A = B", _appSpecificMappings)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Metin dizileri",
            Content = EditorPage("Tuş veya kısayol = yazdırılacak metin biçimini kullanın. Örnek: H = Hello! veya Ctrl+Alt+T = Toplantı notları:", _textMappings)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Uygulama veya URI aç",
            Content = EditorPage("URI: Ctrl+Alt+W = uri|https://example.com. Ardışık kısayol: Ctrl+K, Ctrl+C = uri|https://example.com (en fazla dört adım, 2 saniye içinde; öncü kısayol normal şekilde uygulamaya iletilir). Uygulama: Ctrl+Alt+N = app|notepad.exe|argümanlar|çalışma klasörü|normal|new|visible. Son üç alan yetki (normal/elevated/different-user), zaten açıksa davranış (new/show/nothing/close/end-task) ve görünürlük (visible/hidden) seçenekleridir.", _shortcutActions)
        });

        _error = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(255, 130, 130)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var save = new Button { Content = "Kaydet ve uygula", Padding = new Thickness(14, 8, 14, 8), MinWidth = 130 };
        save.Click += (_, _) => SaveMappings();
        var cancel = new Button { Content = "İptal", Padding = new Thickness(14, 8, 14, 8), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        var footer = new StackPanel();
        footer.Children.Add(_error);
        footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(tabs);
        Content = root;

        _keyMappings.Text = string.Join(Environment.NewLine, keys.Select(FormatKeyMapping));
        _shortcutMappings.Text = string.Join(Environment.NewLine, shortcuts.Select(FormatShortcutMapping));
        _appSpecificMappings.Text = string.Join(Environment.NewLine, appSpecificMappings.Select(FormatAppSpecificMapping));
        _textMappings.Text = string.Join(Environment.NewLine, textMappings.Select(FormatTextMapping));
        _shortcutActions.Text = string.Join(Environment.NewLine, shortcutActions.Select(FormatShortcutAction));
    }

    private static TextBox CreateEditor() => new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Cascadia Code, Consolas"),
        FontSize = 13,
        TextWrapping = TextWrapping.NoWrap,
        Background = new SolidColorBrush(Color.FromRgb(35, 41, 52)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(72, 84, 103)),
        Padding = new Thickness(10)
    };

    private static UIElement EditorPage(string help, TextBox editor)
    {
        var panel = new DockPanel { Margin = new Thickness(10) };
        var instructions = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(instructions, Dock.Top);
        panel.Children.Add(instructions);
        panel.Children.Add(editor);
        return panel;
    }

    private void SaveMappings()
    {
        try
        {
            var keyMappings = ParseKeyMappings(_keyMappings.Text);
            var shortcutMappings = ParseShortcutMappings(_shortcutMappings.Text);
            var appMappings = ParseAppSpecificMappings(_appSpecificMappings.Text);
            var textMappings = ParseTextMappings(_textMappings.Text);
            var shortcutActions = ParseShortcutActions(_shortcutActions.Text);
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in keyMappings)
                if (!sources.Add("key:" + mapping.From)) throw new FormatException("Aynı kaynak tuş birden fazla kez eşlenemez.");
            foreach (var mapping in shortcutMappings)
            {
                if (!sources.Add($"shortcut:{mapping.FromModifiers}:{mapping.FromKey}"))
                    throw new FormatException("Aynı kaynak kısayol birden fazla kez eşlenemez.");
                if (keyMappings.Any(key => key.From == mapping.FromKey))
                    throw new FormatException("Kısayolun son tuşu ayrıca tek tuş olarak eşlenmiş; bu çakışma kısayolu çalışmaz hale getirir.");
            }
            foreach (var mapping in textMappings)
            {
                var source = mapping.FromModifiers == 0
                    ? $"key:{mapping.FromKey}"
                    : $"shortcut:{mapping.FromModifiers}:{mapping.FromKey}";
                if (!sources.Add(source))
                    throw new FormatException("Aynı kaynak tuş veya kısayol birden fazla kez eşlenemez.");
                if (keyMappings.Any(key => key.From == mapping.FromKey))
                    throw new FormatException("Metin kısayolunun son tuşu ayrıca tek tuş olarak eşlenmiş; bu çakışma kısayolu çalışmaz hale getirir.");
            }
            foreach (var mapping in shortcutActions)
            {
                var source = string.Join(',', mapping.Sequence.Select(stroke => $"{stroke.Modifiers}:{stroke.Key}"));
                var sourcePrefix = mapping.Sequence.Count == 1 ? "shortcut:" : "sequence:";
                if (!sources.Add(sourcePrefix + source))
                    throw new FormatException("Aynı kaynak kısayol birden fazla kez eşlenemez.");
                if (mapping.Sequence.Any(stroke => keyMappings.Any(key => key.From == stroke.Key)))
                    throw new FormatException("Eylem kısayolunun son tuşu ayrıca tek tuş olarak eşlenmiş.");
            }
            foreach (var mapping in shortcutActions.Where(action => action.Sequence.Count > 1))
            {
                if (shortcutActions.Any(other => !ReferenceEquals(other, mapping) && other.Sequence.Count == 1 &&
                        SameStroke(other.Sequence[0], mapping.Sequence[0])) ||
                    mapping.Sequence.Any(stroke =>
                        shortcutMappings.Any(other => other.FromModifiers == stroke.Modifiers && other.FromKey == stroke.Key) ||
                        textMappings.Any(other => other.FromModifiers == stroke.Modifiers && other.FromKey == stroke.Key)))
                    throw new FormatException("Ardışık kısayol adımı başka bir kısayol/metin eşlemesiyle çakışıyor.");
            }
            for (var firstIndex = 0; firstIndex < shortcutActions.Count; firstIndex++)
            for (var secondIndex = firstIndex + 1; secondIndex < shortcutActions.Count; secondIndex++)
            {
                var first = shortcutActions[firstIndex].Sequence;
                var second = shortcutActions[secondIndex].Sequence;
                var shorter = first.Count <= second.Count ? first : second;
                var longer = first.Count <= second.Count ? second : first;
                if (shorter.Count < longer.Count && shorter.Select((stroke, index) => SameStroke(stroke, longer[index])).All(matches => matches))
                    throw new FormatException("Bir ardışık kısayol başka bir kısayolun tamamlanmadan önceki başlangıcı olamaz.");
            }
            var appSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in appMappings)
            {
                var source = mapping.IsShortcut
                    ? $"{mapping.ProcessName}:shortcut:{mapping.FromModifiers}:{mapping.From}"
                    : $"{mapping.ProcessName}:key:{mapping.From}";
                if (!appSources.Add(source)) throw new FormatException("Aynı uygulamada aynı kaynak tuş veya kısayol birden fazla kez eşlenemez.");
                if (mapping.IsShortcut && appMappings.Any(key => !key.IsShortcut &&
                    key.ProcessName.Equals(mapping.ProcessName, StringComparison.OrdinalIgnoreCase) && key.From == mapping.From))
                    throw new FormatException("Uygulama kısayolunun son tuşu aynı uygulamada ayrıca tek tuş olarak eşlenmiş.");
            }

            KeyMappings = keyMappings.Cast<object>().ToList();
            ShortcutMappings = shortcutMappings.Cast<object>().ToList();
            AppSpecificMappings = appMappings.Cast<object>().ToList();
            TextMappings = textMappings.Cast<object>().ToList();
            ShortcutActions = shortcutActions.Cast<object>().ToList();
            DialogResult = true;
        }
        catch (FormatException exception)
        {
            _error.Text = exception.Message;
        }
    }

    private static bool SameStroke(KeyboardManagerModule.ShortcutStroke first, KeyboardManagerModule.ShortcutStroke second) =>
        first.Modifiers == second.Modifiers && first.Key == second.Key;

    internal static List<KeyboardManagerModule.KeyMapping> ParseKeyMappings(string text)
    {
        var result = new List<KeyboardManagerModule.KeyMapping>();
        foreach (var (line, lineNumber) in ReadLines(text))
        {
            var parts = SplitMapping(line, lineNumber);
            var from = ParseKey(parts.From, lineNumber, allowModifiers: false);
            var to = ParseKey(parts.To, lineNumber, allowModifiers: false);
            if (from == to) continue;
            result.Add(new KeyboardManagerModule.KeyMapping { From = from, To = to });
        }
        return result;
    }

    internal static List<KeyboardManagerModule.ShortcutMapping> ParseShortcutMappings(string text)
    {
        var result = new List<KeyboardManagerModule.ShortcutMapping>();
        foreach (var (line, lineNumber) in ReadLines(text))
        {
            var parts = SplitMapping(line, lineNumber);
            var from = ParseShortcut(parts.From, lineNumber, requireModifier: true);
            var to = ParseShortcut(parts.To, lineNumber, requireModifier: false);
            if (KeyboardManagerModule.IsReserved(from.Modifiers, from.Key))
                throw new FormatException($"{lineNumber}. satır: Windows tarafından ayrılmış kısayol eşlenemez.");
            if (KeyboardManagerModule.IsReserved(to.Modifiers, to.Key))
                throw new FormatException($"{lineNumber}. satır: Windows tarafından ayrılmış bir kısayol gönderilemez.");
            result.Add(new KeyboardManagerModule.ShortcutMapping
            {
                FromModifiers = from.Modifiers,
                FromKey = from.Key,
                ToModifiers = to.Modifiers,
                ToKey = to.Key
            });
        }
        return result;
    }

    internal static List<KeyboardManagerModule.TextMapping> ParseTextMappings(string text)
    {
        var result = new List<KeyboardManagerModule.TextMapping>();
        foreach (var (line, lineNumber) in ReadLines(text))
        {
            var parts = SplitMapping(line, lineNumber, allowEqualsInTarget: true);
            var from = ParseShortcut(parts.From, lineNumber, requireModifier: false);
            if (KeyboardManagerModule.IsReserved(from.Modifiers, from.Key))
                throw new FormatException($"{lineNumber}. satır: Windows tarafından ayrılmış kısayol eşlenemez.");
            if (parts.To.Length > 2048)
                throw new FormatException($"{lineNumber}. satır: Metin dizisi 2048 karakterden uzun olamaz.");
            result.Add(new KeyboardManagerModule.TextMapping
            {
                FromModifiers = from.Modifiers,
                FromKey = from.Key,
                Text = parts.To
            });
        }
        return result;
    }

    internal static List<KeyboardManagerModule.ShortcutActionMapping> ParseShortcutActions(string text)
    {
        var result = new List<KeyboardManagerModule.ShortcutActionMapping>();
        foreach (var (line, lineNumber) in ReadLines(text))
        {
            var parts = SplitMapping(line, lineNumber, allowEqualsInTarget: true);
            var sourceParts = parts.From.Split(',', StringSplitOptions.TrimEntries);
            if (sourceParts.Length is < 1 or > 4 || sourceParts.Any(string.IsNullOrWhiteSpace))
                throw new FormatException($"{lineNumber}. satır: Ardışık kısayol bir ile dört adımdan oluşmalıdır.");
            var strokes = sourceParts.Select(sourcePart => ParseShortcut(sourcePart, lineNumber, requireModifier: true)).ToArray();
            if (strokes.Any(source => KeyboardManagerModule.IsReserved(source.Modifiers, source.Key)))
                throw new FormatException($"{lineNumber}. satır: Windows tarafından ayrılmış kısayol kullanılamaz.");

            var fields = parts.To.Split('|', StringSplitOptions.TrimEntries);
            if (fields.Length < 2) throw new FormatException($"{lineNumber}. satır: Eylem türü ve hedef eksik.");
            var actionType = fields[0].ToLowerInvariant();
            if (actionType is not ("app" or "uri"))
                throw new FormatException($"{lineNumber}. satır: Eylem türü app veya uri olmalıdır.");

            var target = Unquote(fields[1], lineNumber);
            if (string.IsNullOrWhiteSpace(target)) throw new FormatException($"{lineNumber}. satır: Hedef boş olamaz.");
            var arguments = "";
            var workingDirectory = "";
            var elevation = "normal";
            var ifRunning = "new";
            var visibility = "visible";
            if (actionType == "uri" && fields.Length != 2)
                throw new FormatException($"{lineNumber}. satır: URI eylemi yalnızca tek hedef alır.");
            if (actionType == "app" && fields.Length > 7)
                throw new FormatException($"{lineNumber}. satır: app hedefi yedi alanı aşamaz.");
            if (actionType == "app" && fields.Length >= 3) arguments = Unquote(fields[2], lineNumber, allowEmpty: true);
            if (actionType == "app" && fields.Length >= 4) workingDirectory = Unquote(fields[3], lineNumber, allowEmpty: true);
            if (actionType == "app" && fields.Length >= 5) elevation = fields[4].ToLowerInvariant();
            if (actionType == "app" && fields.Length >= 6) ifRunning = fields[5].ToLowerInvariant();
            if (actionType == "app" && fields.Length >= 7) visibility = fields[6].ToLowerInvariant();
            var mapping = new KeyboardManagerModule.ShortcutActionMapping
            {
                FromModifiers = strokes[0].Modifiers,
                FromKey = strokes[0].Key,
                Sequence = strokes.Select(source => new KeyboardManagerModule.ShortcutStroke
                {
                    Modifiers = source.Modifiers,
                    Key = source.Key
                }).ToList(),
                ActionType = actionType,
                Target = target,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                Elevation = elevation,
                IfRunning = ifRunning,
                Visibility = visibility
            };
            if (!KeyboardManagerModule.IsValidShortcutAction(mapping))
                throw new FormatException($"{lineNumber}. satır: Hedef veya URI biçimi desteklenmiyor.");
            if (result.Any(existing => IsAmbiguousSequencePrefix(existing.Sequence, mapping.Sequence)))
                throw new FormatException($"{lineNumber}. satır: Bir ardışık kısayol başka bir kısayolun tamamlanmadan önceki başlangıcı olamaz.");
            result.Add(mapping);
        }
        return result;
    }

    private static bool IsAmbiguousSequencePrefix(
        IReadOnlyList<KeyboardManagerModule.ShortcutStroke> first,
        IReadOnlyList<KeyboardManagerModule.ShortcutStroke> second)
    {
        var shorter = first.Count <= second.Count ? first : second;
        var longer = first.Count <= second.Count ? second : first;
        return shorter.Count <= longer.Count &&
               shorter.Select((stroke, index) => SameStroke(stroke, longer[index])).All(matches => matches);
    }

    private static string Unquote(string value, int lineNumber, bool allowEmpty = false)
    {
        if (allowEmpty && value.Length == 0) return "";
        var startsQuoted = value.StartsWith('"');
        var endsQuoted = value.EndsWith('"');
        if (startsQuoted != endsQuoted) throw new FormatException($"{lineNumber}. satır: Tırnak çiftini tamamlayın.");
        return startsQuoted ? value[1..^1] : value;
    }

    internal static List<KeyboardManagerModule.AppSpecificMapping> ParseAppSpecificMappings(string text)
    {
        var result = new List<KeyboardManagerModule.AppSpecificMapping>();
        foreach (var (line, lineNumber) in ReadLines(text))
        {
            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0) throw new FormatException($"{lineNumber}. satır: işlem.exe: eşleme biçimini kullanın.");
            var processName = KeyboardManagerModule.NormalizeProcessName(line[..separator]);
            if (processName.Length == 0 || line[..separator].Contains('/') || line[..separator].Contains('\\'))
                throw new FormatException($"{lineNumber}. satır: Yalnızca uygulamanın işlem adını yazın (ör. msedge.exe). Dosya yolu kullanmayın.");
            var parts = SplitMapping(line[(separator + 1)..].Trim(), lineNumber);
            if (parts.From.Contains('+'))
            {
                var from = ParseShortcut(parts.From, lineNumber, requireModifier: true);
                var to = ParseShortcut(parts.To, lineNumber, requireModifier: false);
                if (KeyboardManagerModule.IsReserved(from.Modifiers, from.Key) || KeyboardManagerModule.IsReserved(to.Modifiers, to.Key))
                    throw new FormatException($"{lineNumber}. satır: Windows tarafından ayrılmış kısayol eşlenemez.");
                result.Add(new KeyboardManagerModule.AppSpecificMapping
                {
                    ProcessName = processName, IsShortcut = true,
                    From = from.Key, FromModifiers = from.Modifiers,
                    To = to.Key, ToModifiers = to.Modifiers
                });
            }
            else
            {
                result.Add(new KeyboardManagerModule.AppSpecificMapping
                {
                    ProcessName = processName,
                    From = ParseKey(parts.From, lineNumber, allowModifiers: false),
                    To = ParseKey(parts.To, lineNumber, allowModifiers: false)
                });
            }
        }
        return result;
    }

    private static IEnumerable<(string Line, int Number)> ReadLines(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            yield return (line, index + 1);
        }
    }

    private static (string From, string To) SplitMapping(string line, int lineNumber, bool allowEqualsInTarget = false)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0 || (!allowEqualsInTarget && separator != line.LastIndexOf('=')) || separator == line.Length - 1)
            throw new FormatException($"{lineNumber}. satır: Kaynak = hedef biçimi kullanın.");
        return (line[..separator].Trim(), line[(separator + 1)..].Trim());
    }

    private static int ParseKey(string value, int lineNumber, bool allowModifiers)
    {
        int virtualKey;
        if (value.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value.AsSpan(3), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var explicitVirtualKey))
        {
            virtualKey = explicitVirtualKey;
        }
        else
        {
            if (!Enum.TryParse<Key>(value, true, out var key) || key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed or Key.System)
                throw new FormatException($"{lineNumber}. satır: '{value}' tanınan bir tuş değil.");
            virtualKey = KeyInterop.VirtualKeyFromKey(key);
        }

        if (virtualKey is < 1 or > 254)
            throw new FormatException($"{lineNumber}. satır: '{value}' eşlenebilir bir tuş değil.");
        if (!allowModifiers && IsModifier(virtualKey))
            throw new FormatException($"{lineNumber}. satır: Ctrl, Alt, Shift ve Win tek tuş eşlemesinde desteklenmiyor; kısayol sekmesini kullanın.");
        return virtualKey;
    }

    private static (uint Modifiers, int Key) ParseShortcut(string value, int lineNumber, bool requireModifier)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace))
            throw new FormatException($"{lineNumber}. satır: Kısayolda işlem tuşu eksik veya + işareti hatalı.");
        uint modifiers = 0;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = parts[index].ToLowerInvariant() switch
            {
                "ctrl" or "control" => KeyboardManagerModule.ModifierControl,
                "alt" => KeyboardManagerModule.ModifierAlt,
                "shift" => KeyboardManagerModule.ModifierShift,
                "win" or "windows" => KeyboardManagerModule.ModifierWin,
                _ => throw new FormatException($"{lineNumber}. satır: '{parts[index]}' tanınan bir değiştirici değil.")
            };
            if ((modifiers & modifier) != 0) throw new FormatException($"{lineNumber}. satır: Aynı değiştirici birden fazla yazılmış.");
            modifiers |= modifier;
        }

        if (requireModifier && modifiers == 0)
            throw new FormatException($"{lineNumber}. satır: Kaynak kısayol Ctrl, Alt, Shift veya Win ile başlamalı.");
        var key = ParseKey(parts[^1], lineNumber, allowModifiers: false);
        return (modifiers, key);
    }

    private static bool IsModifier(int virtualKey) => virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    private static string FormatKeyMapping(object mapping)
    {
        if (mapping is not KeyboardManagerModule.KeyMapping key) return string.Empty;
        return $"{KeyFromVirtualKey(key.From)} = {KeyFromVirtualKey(key.To)}";
    }

    private static string FormatShortcutMapping(object mapping)
    {
        if (mapping is not KeyboardManagerModule.ShortcutMapping shortcut) return string.Empty;
        return $"{FormatShortcut(shortcut.FromModifiers, shortcut.FromKey)} = {FormatShortcut(shortcut.ToModifiers, shortcut.ToKey)}";
    }

    private static string FormatAppSpecificMapping(object mapping)
    {
        if (mapping is not KeyboardManagerModule.AppSpecificMapping appMapping) return string.Empty;
        var from = appMapping.IsShortcut ? FormatShortcut(appMapping.FromModifiers, appMapping.From) : KeyFromVirtualKey(appMapping.From);
        var to = appMapping.IsShortcut ? FormatShortcut(appMapping.ToModifiers, appMapping.To) : KeyFromVirtualKey(appMapping.To);
        return $"{appMapping.ProcessName}.exe: {from} = {to}";
    }

    private static string FormatTextMapping(object mapping)
    {
        if (mapping is not KeyboardManagerModule.TextMapping textMapping) return string.Empty;
        return $"{FormatShortcut(textMapping.FromModifiers, textMapping.FromKey)} = {textMapping.Text}";
    }

    private static string FormatShortcutAction(object mapping)
    {
        if (mapping is not KeyboardManagerModule.ShortcutActionMapping action) return string.Empty;
        var sequence = action.Sequence is { Count: > 0 }
            ? action.Sequence
            : [new KeyboardManagerModule.ShortcutStroke { Modifiers = action.FromModifiers, Key = action.FromKey }];
        var source = string.Join(", ", sequence.Select(stroke => FormatShortcut(stroke.Modifiers, stroke.Key)));
        if (action.ActionType.Equals("app", StringComparison.OrdinalIgnoreCase))
        {
            var fields = new List<string> { "app", QuoteIfNeeded(action.Target) };
            var hasLaunchOptions = action.Elevation != "normal" || action.IfRunning != "new" || action.Visibility != "visible";
            if (action.Arguments.Length > 0 || action.WorkingDirectory.Length > 0 || hasLaunchOptions)
                fields.Add(QuoteIfNeeded(action.Arguments));
            if (action.WorkingDirectory.Length > 0 || hasLaunchOptions) fields.Add(QuoteIfNeeded(action.WorkingDirectory));
            if (action.Elevation != "normal" || action.IfRunning != "new" || action.Visibility != "visible")
                fields.Add(action.Elevation);
            if (action.IfRunning != "new" || action.Visibility != "visible") fields.Add(action.IfRunning);
            if (action.Visibility != "visible") fields.Add(action.Visibility);
            return $"{source} = {string.Join('|', fields)}";
        }
        return $"{source} = uri|{QuoteIfNeeded(action.Target)}";
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains('|') || value.Contains(' ') ? $"\"{value}\"" : value;

    private static string FormatShortcut(uint modifiers, int key)
    {
        var parts = new List<string>();
        if ((modifiers & KeyboardManagerModule.ModifierControl) != 0) parts.Add("Ctrl");
        if ((modifiers & KeyboardManagerModule.ModifierAlt) != 0) parts.Add("Alt");
        if ((modifiers & KeyboardManagerModule.ModifierShift) != 0) parts.Add("Shift");
        if ((modifiers & KeyboardManagerModule.ModifierWin) != 0) parts.Add("Win");
        parts.Add(KeyFromVirtualKey(key));
        return string.Join('+', parts);
    }

    private static string KeyFromVirtualKey(int virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key == Key.None ? $"VK_{virtualKey:X2}" : key.ToString();
    }
}
