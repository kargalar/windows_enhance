using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;
using System.IO;
using WinForge.Modules.WindowControl;

namespace WinForge.RegressionTests;

internal static class KeyboardLatencyChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var type = extras.GetType("WinForge.Modules.Extras.AutoCorrectModule", true)!;
        var module = Activator.CreateInstance(type)!;
        void Require(bool condition, string message) => check(condition, message);
        var active = type.GetField("<IsActive>k__BackingField", flags)!;
        var generation = type.GetField("_hookGeneration", flags)!;
        var sequence = type.GetField("_inputSequence", flags)!;
        var current = type.GetProperty("InputIsCurrent", flags)!;
        var handler = type.GetMethod("HandleKeyboard", flags)!;
        active.SetValue(module, true);
        var data = Marshal.AllocHGlobal(Marshal.SizeOf<KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new KBDLLHOOKSTRUCT { vkCode = 0xA0 }, data, false);
            handler.Invoke(module, [0, (IntPtr)Native.WM_KEYDOWN, data]);
            Require((long)sequence.GetValue(module)! == 0, "Native autocorrect callback does not execute UI processing inline");
            Require(!(bool)current.GetValue(module)!, "New physical input invalidates pending corrections before the UI can run");
            Drain();
            Require((long)sequence.GetValue(module)! == 1 && (bool)current.GetValue(module)!, "Queued input is processed on the UI and restores freshness");
            handler.Invoke(module, [0, (IntPtr)Native.WM_KEYDOWN, data]);
            generation.SetValue(module, 1);
            Drain();
            Require((long)sequence.GetValue(module)! == 1, "Callbacks from a previous activation cannot update a restarted module");
            Marshal.StructureToPtr(new KBDLLHOOKSTRUCT { vkCode = 0xA0, flags = 0x10 }, data, false);
            var observed = type.GetField("_observedInput", flags)!;
            var before = observed.GetValue(module);
            handler.Invoke(module, [0, (IntPtr)Native.WM_KEYDOWN, data]);
            Require(Equals(before, observed.GetValue(module)), "Injected correction keystrokes do not invalidate their own result");
        }
        finally { active.SetValue(module, false); Marshal.FreeHGlobal(data); }

        var options = new WindowControlOptions("Alt", "Win", new Dictionary<string, WindowAction>(),
            false, false, "", 0, 0, 0, 50, 50, false, "", "");
        using var engine = new WindowControlEngine(options, error => throw error);
        engine.Start();
        var engineType = typeof(WindowControlEngine);
        var keyboard = (KeyboardHookThread)engineType.GetField("_keyboardHook", flags)!.GetValue(engine)!;
        var release = keyboard.Dispatcher.InvokeAsync(() =>
        {
            engineType.GetField("_nativeSpaceDown", flags)!.SetValue(engine, true);
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<KBDLLHOOKSTRUCT>());
            try
            {
                Marshal.StructureToPtr(new KBDLLHOOKSTRUCT { vkCode = 0x20 }, pointer, false);
                return (IntPtr)engineType.GetMethod("KeyboardHook", flags)!.Invoke(engine,
                    [0, (IntPtr)Native.WM_KEYUP, pointer])!;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        });
        Require(release.Task.Wait(TimeSpan.FromSeconds(3)), "Window-control keyboard processing does not wait for the blocked UI");
        Require(release.Task.Result == (IntPtr)1, "Window-control consumes the release of a captured Space key");
        Drain();
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
