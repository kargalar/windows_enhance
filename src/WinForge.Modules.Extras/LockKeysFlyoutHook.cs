using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed class LockKeysFlyoutHook : IDisposable
{
    internal const int VolumeMuteKey = 0xAD;
    internal const int VolumeDownKey = 0xAE;
    internal const int VolumeUpKey = 0xAF;
    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly Action? _showMediaFlyout;
    private readonly Action<int>? _handleVolumeKey;
    private bool _muteKeyDown;
    private readonly bool _showCapsLock;
    private readonly bool _showNumLock;
    private readonly bool _showScrollLock;
    private readonly IntPtr _hook;

    public LockKeysFlyoutHook(bool showCapsLock, bool showNumLock, bool showScrollLock, Action? showMediaFlyout,
        Action<int>? handleVolumeKey)
    {
        _showCapsLock = showCapsLock;
        _showNumLock = showNumLock;
        _showScrollLock = showScrollLock;
        _showMediaFlyout = showMediaFlyout;
        _handleVolumeKey = handleVolumeKey;
        _callback = HandleKeyboardEvent;
        _hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Kilit tuşları klavye izleyicisi başlatılamadı.");
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
    }

    private IntPtr HandleKeyboardEvent(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var isKeyUp = message == (IntPtr)Native.WM_KEYUP || message == (IntPtr)Native.WM_SYSKEYUP;
            var isKeyDown = message == (IntPtr)Native.WM_KEYDOWN || message == (IntPtr)Native.WM_SYSKEYDOWN;
            if (isKeyUp || isKeyDown)
            {
                var key = (int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data).vkCode;
                if (IsVolumeHardwareKey(key) && _handleVolumeKey is not null &&
                    Application.Current?.Dispatcher is { HasShutdownStarted: false } dispatcher)
                {
                    // Consume both edges so Windows cannot open its own volume OSD.
                    // Repeated volume presses still adjust the level; mute toggles once per press.
                    if (isKeyDown && (key != VolumeMuteKey || !_muteKeyDown))
                        dispatcher.BeginInvoke(() => _handleVolumeKey(key));
                    if (key == VolumeMuteKey) _muteKeyDown = isKeyDown;
                    return (IntPtr)1;
                }
                if (isKeyUp)
                {
                    var label = GetEnabledLockKeyLabel(key, _showCapsLock, _showNumLock, _showScrollLock);
                    if (label is not null)
                    {
                        var enabled = (GetKeyState(key) & 1) != 0;
                        Application.Current?.Dispatcher.BeginInvoke(() => LockKeysStatusWindow.Show(label, enabled));
                    }
                }
                if (isKeyUp && IsMediaFlyoutKey(key) && !IsVolumeHardwareKey(key) && _showMediaFlyout is not null)
                    Application.Current?.Dispatcher.BeginInvoke(_showMediaFlyout);
            }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    internal static bool IsMediaFlyoutKey(int key) => key is >= 0xAD and <= 0xB3;

    private static bool IsVolumeHardwareKey(int key) => key is >= VolumeMuteKey and <= VolumeUpKey;

    internal static string? GetEnabledLockKeyLabel(int key, bool showCapsLock, bool showNumLock, bool showScrollLock) => key switch
    {
        0x14 when showCapsLock => "Caps Lock",
        0x90 when showNumLock => "Num Lock",
        0x91 when showScrollLock => "Scroll Lock",
        _ => null
    };

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
