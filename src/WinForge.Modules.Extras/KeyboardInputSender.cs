using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinForge.Modules.Extras;

/// <summary>Own Win32 keyboard input interop shared by remapping and paste features.</summary>
internal static class KeyboardInputSender
{
    private const uint KeyboardInputType = 1;
    private const uint KeyUp = 0x0002;
    private const ushort ControlKey = 0x11;
    private const ushort PasteKey = 0x56;

    internal static void PasteShortcut()
    {
        INPUT[] inputs = [Key(ControlKey, 0), Key(PasteKey, 0), Key(PasteKey, KeyUp), Key(ControlKey, KeyUp)];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Yapıştırma tuşları hedef uygulamaya gönderilemedi.");
    }

    private static INPUT Key(ushort key, uint flags) => new()
    {
        Type = KeyboardInputType,
        Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = flags } }
    };

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    // The native union reserves MOUSEINPUT (32 bytes on x64), even for keyboard events.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
