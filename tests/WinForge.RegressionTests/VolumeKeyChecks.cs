using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WinForge.RegressionTests;

internal static class VolumeKeyChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const int mute = 0xAD;
        const int down = 0xAE;
        const int up = 0xAF;
        const int keyDown = 0x100;
        const int keyUp = 0x101;
        var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var type = extras.GetType("WinForge.Modules.Extras.LockKeysFlyoutHook", throwOnError: true)!;
        var received = new List<int>();
        var mediaCount = 0;
        using var hook = (IDisposable)Activator.CreateInstance(type,
            [false, false, false, (Action)(() => mediaCount++), (Action<int>)received.Add])!;
        var handle = type.GetMethod("HandleKeyboardEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var data = Marshal.AllocHGlobal(32);
        try
        {
            for (var offset = 0; offset < 32; offset += sizeof(int)) Marshal.WriteInt32(data, offset, 0);
            IntPtr Send(int key, int message)
            {
                Marshal.WriteInt32(data, key);
                return (IntPtr)handle.Invoke(hook, [0, (IntPtr)message, data])!;
            }

            foreach (var key in new[] { down, up, mute })
            {
                check(Send(key, keyDown) == (IntPtr)1 && Send(key, keyDown) == (IntPtr)1 &&
                      Send(key, keyUp) == (IntPtr)1,
                    $"Volume key {key:X} consumes press, repeat and release to suppress Windows OSD");
            }
            Send(mute, keyDown);
            Send(mute, keyUp);
            Send(0xB3, keyUp);
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            check(received.SequenceEqual(new[] { down, down, up, up, mute, mute }),
                "Volume keys preserve level repeats and toggle mute once per physical press");
            check(mediaCount == 1, "Playback hardware keys still open the media flyout");
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }
}
