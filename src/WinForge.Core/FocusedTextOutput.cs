using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace WinForge.Core;

public sealed record TextTarget(nint Window, uint Process, string RuntimeId);
public enum TextInsertion { Inserted, TargetChanged, Blocked, UnsupportedLineBreak }

/// <summary>Checks field identity before each Unicode input batch, without changing clipboard contents.</summary>
public static class FocusedTextOutput
{
    private static readonly int[] Modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C];
    public static nint FocusedWindow(nint foreground)
    {
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        if (thread == 0) return 0;
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info) ? info.Focus : 0;
    }
    public static TextTarget? Capture()
    {
        try
        {
            var window = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(window, out var process);
            var field = AutomationElement.FocusedElement;
            if (field is null || process == Environment.ProcessId || field.Current.ProcessId != process || !IsEditable(field)) return null;
            return new(window, process, string.Join(",", field.GetRuntimeId()));
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException or InvalidOperationException) { return null; }
    }
    public static bool IsCurrent(TextTarget? target) => target is not null && Capture() == target;
    public static bool IsEditable(AutomationElement field)
    {
        if (field.Current.IsPassword || !field.Current.IsEnabled) return false;
        if (field.TryGetCurrentPattern(ValuePattern.Pattern, out var raw)) return !((ValuePattern)raw).Current.IsReadOnly;
        return field.TryGetCurrentPattern(TextPattern.Pattern, out var text) &&
            ((TextPattern)text).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false;
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
    public static async Task<TextInsertion> PasteAsync(TextTarget? target, uint clipboardSequence, CancellationToken token)
    {
        if (target is null || clipboardSequence == 0) return TextInsertion.Blocked;
        var deadline = Environment.TickCount64 + 5000;
        while (Modifiers.Any(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0))
        {
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(target)) return TextInsertion.TargetChanged;
            if (GetClipboardSequenceNumber() != clipboardSequence || Environment.TickCount64 >= deadline) return TextInsertion.Blocked;
            await Task.Delay(20, token);
        }
        token.ThrowIfCancellationRequested();
        if (!IsCurrent(target)) return TextInsertion.TargetChanged;
        if (GetClipboardSequenceNumber() != clipboardSequence || Modifiers.Any(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0)) return TextInsertion.Blocked;
        Input[] input = [Input.Virtual(0x11, false), Input.Virtual(0x56, false), Input.Virtual(0x56, true), Input.Virtual(0x11, true)];
        var sent = SendInput((uint)input.Length, input, Marshal.SizeOf<Input>());
        if (sent == input.Length) return TextInsertion.Inserted;
        // A partial injection can otherwise leave Ctrl or V logically held down.
        if (sent > 0)
        {
            Input[] release = [Input.Virtual(0x56, true), Input.Virtual(0x11, true)];
            SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
        }
        return TextInsertion.Blocked;
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort ScanCode;
        [FieldOffset(12)] public uint Flags;
        public static Input Key(char character, bool up) => new() { Type = 1, ScanCode = character, Flags = 4u | (up ? 2u : 0) };
        public static Input Virtual(ushort key, bool up) => new() { Type = 1, VirtualKey = key, Flags = up ? 2u : 0 };
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
}
