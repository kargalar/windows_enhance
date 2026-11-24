using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace WinForge.Core;

public sealed record TextTarget(nint Window, uint Process, string RuntimeId)
{
    public nint FocusWindow { get; init; }
}
public enum TextInsertion { Inserted, TargetChanged, Blocked, UnsupportedLineBreak }

/// <summary>Sends text input only to the captured editable field.</summary>
public static class FocusedTextOutput
{
    private static readonly int[] Modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C];
    private static readonly TimeSpan AutomationTimeout = TimeSpan.FromMilliseconds(800);
    public static nint FocusedWindow(nint foreground)
    {
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        if (thread == 0) return 0;
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info) ? info.Focus : 0;
    }
    public static TextTarget? Capture()
    {
        var window = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(window, out var process);
        if (process == 0 || process == Environment.ProcessId) return null;
        var focus = FocusedWindow(window);
        var runtimeId = ReadFieldId(window, process, focus);
        return runtimeId is not null && Native.GetForegroundWindow() == window && FocusedWindow(window) == focus
            ? new TextTarget(window, process, runtimeId) { FocusWindow = focus }
            : null;
    }

    private static string? ReadFieldId(nint window, uint process, nint focus)
    {
        // Standard edit controls have their own HWND and expose editability through window styles.
        if (focus != 0 && Native.GetAncestor(focus, Native.GA_ROOT) == window && IsNativeEdit(focus))
        {
            const int styleIndex = -16, password = 0x20, readOnly = 0x800;
            var style = (long)Native.GetWindowLongPtr(focus, styleIndex);
            return (style & (password | readOnly)) == 0 && IsWindowEnabled(focus) ? "native" : null;
        }

        try
        {
            var field = AutomationElement.FocusedElement;
            if (field is null || !BelongsToWindow(field, window, process, focus) || !IsEditable(field)) return null;
            return field.GetRuntimeId() is { Length: > 0 } id ? string.Join(",", id) : null;
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException) { return null; }
    }

    private static bool IsNativeEdit(nint focus)
    {
        var className = new StringBuilder(32);
        Native.GetClassName(focus, className, className.Capacity);
        return className.ToString().ToUpperInvariant() is "EDIT" or "RICHEDIT" or "RICHEDIT20A" or "RICHEDIT20W" or "RICHEDIT50W";
    }

    private static bool BelongsToWindow(AutomationElement field, nint window, uint process, nint focus)
    {
        var info = field.Current;
        if (info.ProcessId == process)
        {
            var handle = (nint)info.NativeWindowHandle;
            return handle == 0 || Native.GetAncestor(handle, Native.GA_ROOT) == window;
        }
        if (focus == 0 || !info.HasKeyboardFocus || info.ProcessId == Environment.ProcessId) return false;

        // Embedded editors can expose UI Automation from a different process than their host.
        for (AutomationElement? element = field; element is not null; element = TreeWalker.RawViewWalker.GetParent(element))
        {
            var handle = (nint)element.Current.NativeWindowHandle;
            if (handle == 0) continue;
            return Native.GetAncestor(handle, Native.GA_ROOT) == window &&
                (handle == focus || IsChild(handle, focus) || IsChild(focus, handle));
        }
        return false;
    }

    public static bool IsCurrent(TextTarget? target) => target is not null && Capture() == target;

    public static bool IsEditable(AutomationElement field)
    {
        if (field.Current.IsPassword || !field.Current.IsEnabled) return false;
        if (field.TryGetCurrentPattern(ValuePattern.Pattern, out var raw)) return !((ValuePattern)raw).Current.IsReadOnly;
        if (!field.TryGetCurrentPattern(TextPattern.Pattern, out var text)) return false;
        var pattern = (TextPattern)text;
        var readOnly = pattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        if (readOnly is bool wholeDocument) return !wholeDocument;

        // The whole document can mix editable and protected text. Check the active selection or caret.
        if (pattern.SupportedTextSelection == SupportedTextSelection.None) return false;
        var selection = pattern.GetSelection();
        return selection.Length > 0 && selection.All(range => range.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false);
    }
    public static async Task<TextInsertion> InsertAsync(TextTarget? target, string text, CancellationToken token)
    {
        if (target is null) return TextInsertion.Blocked;
        // KEYEVENTF_UNICODE does not reliably insert line breaks. A physical Enter
        // could submit a message, so keep the full result available for manual paste.
        if (text.Contains('\r') || text.Contains('\n')) return TextInsertion.UnsupportedLineBreak;
        var deadline = Environment.TickCount64 + 5000;
        while (Modifiers.Any(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0))
        {
            if (!IsCurrent(target)) return TextInsertion.TargetChanged;
            if (Environment.TickCount64 >= deadline) return TextInsertion.Blocked;
            await Task.Delay(20, token);
        }
        foreach (var chunk in text.Chunk(32))
        {
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(target)) return TextInsertion.TargetChanged;
            if (Modifiers.Any(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0)) return TextInsertion.Blocked;
            var input = chunk.SelectMany(character => new[] { Input.Key(character, false), Input.Key(character, true) }).ToArray();
            if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) != input.Length) return TextInsertion.Blocked;
            await Task.Delay(1, token);
        }
        return TextInsertion.Inserted;
    }
    public static bool CanPasteImmediately(bool allowHeldShift = false) =>
        !Modifiers.Any(key => (!allowHeldShift || key != 0x10) && IsKeyDown(key));

    private static bool HasSameNativeFocus(TextTarget target) =>
        Native.GetForegroundWindow() == target.Window && Native.GetWindowThreadProcessId(target.Window, out var process) != 0 &&
        process == target.Process && FocusedWindow(target.Window) == target.FocusWindow;

    private static async Task<TextInsertion?> ValidateTargetAsync(TextTarget target, CancellationToken token)
    {
        if (!HasSameNativeFocus(target)) return TextInsertion.TargetChanged;
        TextTarget? current;
        try
        {
            current = await Task.Run(async () =>
            {
                var captured = Capture();
                if (captured is not null || !HasSameNativeFocus(target)) return captured;
                await Task.Delay(40, token);
                return HasSameNativeFocus(target) ? Capture() : null;
            }, token).WaitAsync(AutomationTimeout, token);
        }
        catch (TimeoutException) { current = null; }
        if (!HasSameNativeFocus(target)) return TextInsertion.TargetChanged;
        if (current is null) return TextInsertion.Blocked;
        return current == target ? null : TextInsertion.TargetChanged;
    }

    public static Task<TextInsertion> PasteAsync(TextTarget? target, uint clipboardSequence, CancellationToken token) =>
        clipboardSequence == 0 ? Task.FromResult(TextInsertion.Blocked) :
            PasteCoreAsync(target, () => clipboardSequence, token, false, true, clipboardSequence);

    public static Task<TextInsertion> PasteAsync(TextTarget? target, Func<uint> prepareClipboard, CancellationToken token,
        bool useShiftInsert = false, bool waitForModifiers = true) =>
        PasteCoreAsync(target, prepareClipboard, token, useShiftInsert, waitForModifiers, 0);

    private static async Task<TextInsertion> PasteCoreAsync(TextTarget? target, Func<uint> prepareClipboard, CancellationToken token,
        bool useShiftInsert, bool waitForModifiers, uint expectedClipboardSequence)
    {
        if (target is null) return TextInsertion.Blocked;
        var allowHeldShift = useShiftInsert && !waitForModifiers;
        var deadline = Environment.TickCount64 + 5000;
        while (!CanPasteImmediately(allowHeldShift))
        {
            token.ThrowIfCancellationRequested();
            if (!waitForModifiers) return TextInsertion.Blocked;
            if (!HasSameNativeFocus(target)) return TextInsertion.TargetChanged;
            if (Environment.TickCount64 >= deadline ||
                (expectedClipboardSequence != 0 && GetClipboardSequenceNumber() != expectedClipboardSequence)) return TextInsertion.Blocked;
            await Task.Delay(20, token);
        }
        token.ThrowIfCancellationRequested();
        if (await ValidateTargetAsync(target, token) is { } failure) return failure;
        token.ThrowIfCancellationRequested();
        var clipboardSequence = prepareClipboard();
        token.ThrowIfCancellationRequested();
        if (expectedClipboardSequence == 0 && await ValidateTargetAsync(target, token) is { } changed) return changed;
        token.ThrowIfCancellationRequested();
        if (!HasSameNativeFocus(target)) return TextInsertion.TargetChanged;
        if (clipboardSequence == 0 || GetClipboardSequenceNumber() != clipboardSequence || !CanPasteImmediately(allowHeldShift))
            return TextInsertion.Blocked;
        ushort modifier = useShiftInsert ? (ushort)0x10 : (ushort)0x11;
        ushort key = useShiftInsert ? (ushort)0x2D : (ushort)0x56;
        var alreadyHeld = allowHeldShift && IsKeyDown(modifier);
        Input[] input = alreadyHeld ? [Input.Virtual(key, false), Input.Virtual(key, true)] :
            [Input.Virtual(modifier, false), Input.Virtual(key, false), Input.Virtual(key, true), Input.Virtual(modifier, true)];
        var sent = SendInput((uint)input.Length, input, Marshal.SizeOf<Input>());
        if (sent == input.Length) return TextInsertion.Inserted;
        // A partial injection can leave the injected keys logically held down.
        if (sent > 0)
        {
            Input[] release = alreadyHeld ? [Input.Virtual(key, true)] : [Input.Virtual(key, true), Input.Virtual(modifier, true)];
            SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
            if (alreadyHeld || sent >= 2) return TextInsertion.Inserted;
        }
        return TextInsertion.Blocked;
    }
    internal static void MaskWindowsKeyRelease()
    {
        // A consumed Win chord must not appear to the shell as a lone Win press.
        SendInput(1, [Input.Virtual(0xff, true)], Marshal.SizeOf<Input>());
    }

    private static bool IsKeyDown(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort ScanCode;
        [FieldOffset(12)] public uint Flags;
        public static Input Key(char character, bool up) => new() { Type = 1, ScanCode = character, Flags = 4u | (up ? 2u : 0) };
        public static Input Virtual(ushort key, bool up) => new()
        {
            Type = 1, VirtualKey = key, Flags = (key == 0x2D ? 1u : 0) | (up ? 2u : 0)
        };
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
}
