using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WinForge.Core;

namespace WinForge.RegressionTests;

/// <summary>Opt-in live OS test. Registers, shows and removes only its own local notification.</summary>
internal static class NotificationLiveChecks
{
    internal static void Run(string directory, Action<bool, string> check)
    {
        var id = "WinForge.Test.Toast." + Guid.NewGuid().ToString("N");
        var marker = "WinForge notification verification " + Guid.NewGuid().ToString("N");
        var activator = Guid.NewGuid();
        var protocol = "winforge-notification-test-" + Guid.NewGuid().ToString("N");
        var protocolRegistration = @"Software\Classes\" + protocol;
        var registration = @"Software\Classes\AppUserModelId\" + id;
        var preferences = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + id;
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), id + ".lnk");
        var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var type = extras.GetType("WinForge.Modules.Extras.NotificationsPlacementModule")!;
        var classifier = type.GetMethod("MatchesToastIdentity", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool IsToast(IntPtr window)
        {
            if (Native.GetAncestor(window, Native.GA_ROOT) != window) return false;
            Native.GetWindowThreadProcessId(window, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                return (bool)classifier.Invoke(null, [Native.ClassNameOf(window), Native.TitleOf(window), process.MainModule?.FileName ?? ""])!;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
        }

        ToastNotifier? notifier = null;
        ToastNotification? toast = null;
        ITweakModule? module = null;
        var registered = false;
        var shortcutCreated = false;
        var protocolRegistered = false;
        var failureCode = "none";
        var dismissed = "none";
        Exception? testFailure = null;
        var previousForeground = Native.GetForegroundWindow();
        var foregroundFixture = new System.Windows.Window
        {
            Title = "WinForge local notification verification", Width = 450, Height = 130,
            ShowInTaskbar = false, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
            Content = new System.Windows.Controls.TextBlock { Text = "WinForge bildirim testi", Margin = new System.Windows.Thickness(24) }
        };
        try
        {
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(id));
            foregroundFixture.Show();
            foregroundFixture.Activate();
            PumpFor(TimeSpan.FromMilliseconds(500));
            Console.WriteLine($"Notification live test process package identity: {ReadPackageIdentity()}.");
            using (var existing = Registry.CurrentUser.OpenSubKey(registration))
                if (existing is not null || File.Exists(shortcut)) throw new InvalidOperationException("The isolated notification identity unexpectedly exists.");
            using (var key = Registry.CurrentUser.CreateSubKey(registration))
            {
                registered = true;
                key.SetValue("DisplayName", "WinForge notification verification");
                key.SetValue("CustomActivator", activator.ToString("B"));
            }
            using (var existingProtocol = Registry.CurrentUser.OpenSubKey(protocolRegistration))
                if (existingProtocol is not null) throw new InvalidOperationException("The isolated notification protocol unexpectedly exists.");
            using (var key = Registry.CurrentUser.CreateSubKey(protocolRegistration))
            {
                protocolRegistered = true;
                key.SetValue("", "URL:WinForge own notification verification");
                key.SetValue("URL Protocol", "");
                using var command = key.CreateSubKey(@"shell\open\command");
                var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No test process path.");
                var assemblyArgument = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    ? $"\"{Assembly.GetExecutingAssembly().Location}\" " : "";
                command.SetValue("", $"\"{executable}\" {assemblyArgument}--notification-activation \"%1\"");
            }
            shortcutCreated = true;
            CreateShortcut(shortcut, id, activator);
            VerifyShortcut(shortcut, id, activator);
            SHChangeNotify(0x00000002, 0x0005, shortcut, IntPtr.Zero);
            HoldRegistrationProbe(id, activator, registration, shortcut);
            var indexWait = Stopwatch.StartNew();
            var indexResult = int.MinValue;
            string indexedName = "";
            while (indexWait.Elapsed < TimeSpan.FromSeconds(8))
            {
                indexResult = TryResolveIndexedApplication(id, out indexedName);
                if (indexResult >= 0) break;
                PumpFor(TimeSpan.FromMilliseconds(150));
            }
            Console.WriteLine($"Own AppsFolder registration: HRESULT=0x{indexResult:X8}; wait={indexWait.ElapsedMilliseconds}ms; displayName={indexedName}.");
            if (indexResult < 0)
                throw new InvalidOperationException($"Windows AppsFolder cannot resolve the exact own AUMID after shortcut registration (HRESULT0x{indexResult:X8}). Shortcut properties round-tripped correctly; no toast was sent. Verify visibility from Explorer's desktop context.");
            notifier = ToastNotificationManager.CreateToastNotifier(id);
            var xml = new XmlDocument();
            xml.LoadXml($"<toast duration='long' activationType='protocol' launch='{protocol}://dismiss'><visual><binding template='ToastGeneric'><text>{marker}</text><text>Local WinForge positioning verification. No action is required.</text></binding></visual><audio silent='true'/></toast>");
            toast = new ToastNotification(xml) { Tag = "position", Group = "verification", ExpirationTime = DateTimeOffset.Now.AddMinutes(1), SuppressPopup = false };
            toast.Activated += (_, _) => { };
            toast.Failed += (_, args) => failureCode = args.ErrorCode.ToString();
            toast.Dismissed += (_, args) => dismissed = args.Reason.ToString();
            notifier.Show(toast);
            SHQueryUserNotificationState(out var notificationState);
            Console.WriteLine($"Notification live: AUMID={id}; notificationState={notificationState}; no notification preferences changed.");
            Native.EnumWindows((candidate, _) =>
            {
                if (!Native.IsWindowVisible(candidate) || !Native.GetWindowRect(candidate, out var rectangle)) return true;
                var display = DesktopDisplays.ForWindow(candidate).Bounds;
                if (rectangle.Left <= display.Left && rectangle.Top <= display.Top && rectangle.Right >= display.Right && rectangle.Bottom >= display.Bottom)
                {
                    Native.GetWindowThreadProcessId(candidate, out var owner);
                    using var process = Process.GetProcessById((int)owner);
                    Console.WriteLine($"Fullscreen candidate: class={Native.ClassNameOf(candidate)} process={process.ProcessName} foreground={Native.GetForegroundWindow() == candidate}.");
                }
                return true;
            }, IntPtr.Zero);
            PumpFor(TimeSpan.FromSeconds(1));
            Console.WriteLine($"Notification live: own history entries={ToastNotificationManager.History.GetHistory(id).Count}.");

            IntPtr ownWindow = IntPtr.Zero;
            var observed = new HashSet<string>();
            var desktopScan = Task.Run(() => FindOwnMarkerHost(marker));
            PumpUntil(() => desktopScan.IsCompleted, TimeSpan.FromSeconds(5),
                "The desktop accessibility provider did not complete the exact own-marker search within five seconds. No window was moved.");
            var discovered = desktopScan.GetAwaiter().GetResult();
            Console.WriteLine($"Notification own-marker desktop discovery: {discovered.Diagnostic}");
            if (discovered.Window != IntPtr.Zero && Native.IsWindowVisible(discovered.Window) && IsToast(discovered.Window))
                ownWindow = discovered.Window;
            var deadline = Stopwatch.StartNew();
            while (ownWindow == IntPtr.Zero && deadline.Elapsed < TimeSpan.FromSeconds(3))
            {
                List<IntPtr> candidates = [];
                Native.EnumWindows((window, _) =>
                {
                    if (Native.IsWindowVisible(window) && IsToast(window)) candidates.Add(window);
                    return true;
                }, IntPtr.Zero);
                foreach (var candidate in candidates)
                {
                    observed.Add($"0x{candidate.ToInt64():X}:{Native.TitleOf(candidate)}");
                    var inspect = Task.Run(() => HasOwnMarker(candidate, marker));
                    PumpUntil(() => inspect.IsCompleted, TimeSpan.FromSeconds(2), "The Windows toast accessibility provider did not respond; no window was moved.");
                    if (inspect.GetAwaiter().GetResult()) { ownWindow = candidate; break; }
                }
                if (ownWindow == IntPtr.Zero) PumpFor(TimeSpan.FromMilliseconds(150));
            }
            if (ownWindow == IntPtr.Zero)
                throw new InvalidOperationException($"No positively identified own Windows toast host was found. Notifier.Setting={notifier.Setting}; Failed={failureCode}; Dismissed={dismissed}; exact own-marker discovery={discovered.Diagnostic}; visible recognized hosts=[{string.Join(", ", observed)}]. Do Not Disturb and user/account settings were not changed; no window was moved.");
            check(IsToast(ownWindow), "The actual Windows notification host contains the unique own toast text and passes system path/class/title classification");

            PumpFor(TimeSpan.FromMilliseconds(900));
            if (!Native.IsWindowVisible(ownWindow) || !Native.GetWindowRect(ownWindow, out var original))
                throw new InvalidOperationException("The own toast disappeared before its original position could be captured.");
            var foreground = Native.GetForegroundWindow();
            var context = new Context(directory);
            context.Settings.Set("monitor", 1);
            context.Settings.Set("position", "Sol üst");
            context.Settings.Set("gap", 23);
            // The injected guard narrows the production classifier to this proven own toast only.
            module = (ITweakModule)type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
                .Invoke([(Func<IntPtr, bool>)(window => window == ownWindow && IsToast(window))]);
            module.Initialize(context);
            module.Activate();
            PumpFor(TimeSpan.FromMilliseconds(750));
            if (!Native.GetWindowRect(ownWindow, out var moved)) throw new InvalidOperationException("The own toast disappeared during movement verification.");
            var area = DesktopDisplays.Enumerate().Single(display => display.IsPrimary).WorkArea;
            check(context.Errors.Count == 0 && moved.Left >= area.Left && moved.Top >= area.Top &&
                  moved.Left <= area.Left + 184 && moved.Top <= area.Top + 184 &&
                  moved.Right - moved.Left == original.Right - original.Left && moved.Bottom - moved.Top == original.Bottom - original.Top &&
                  (moved.Left != original.Left || moved.Top != original.Top),
                "The actual own Windows toast remains at the selected top-left location after shell layout with its original size");
            check(Native.GetForegroundWindow() == foreground, "Moving the actual own Windows toast preserves the active application");
            module.Deactivate();
            PumpFor(TimeSpan.FromMilliseconds(250));
            check(Native.GetWindowRect(ownWindow, out var restored) && Equal(original, restored),
                "Disabling notification placement restores the actual own Windows toast's exact original native bounds");
            Console.WriteLine($"Notification live HWND=0x{ownWindow.ToInt64():X}: original={Describe(original)}; moved={Describe(moved)}; restored={Describe(restored)}.");
        }
        catch (Exception exception) { testFailure = exception; throw; }
        finally
        {
            List<Exception> cleanupErrors = [];
            TryCleanup(() => foregroundFixture.Close(), cleanupErrors);
            if (Native.IsWindow(previousForeground)) Native.SetForegroundWindow(previousForeground);
            TryCleanup(() => module?.Deactivate(), cleanupErrors);
            TryCleanup(() => { if (toast is not null) notifier?.Hide(toast); }, cleanupErrors);
            TryCleanup(() => { if (registered) ToastNotificationManager.History.Clear(id); }, cleanupErrors);
            TryCleanup(() => { if (shortcutCreated && File.Exists(shortcut)) File.Delete(shortcut); }, cleanupErrors);
            TryCleanup(() => { if (registered) Registry.CurrentUser.DeleteSubKeyTree(registration, false); }, cleanupErrors);
            TryCleanup(() => { if (registered) Registry.CurrentUser.DeleteSubKeyTree(preferences, false); }, cleanupErrors);
            TryCleanup(() => { if (protocolRegistered) Registry.CurrentUser.DeleteSubKeyTree(protocolRegistration, false); }, cleanupErrors);
            if (cleanupErrors.Count > 0)
            {
                if (testFailure is not null) cleanupErrors.Insert(0, testFailure);
                throw new AggregateException("Own notification fixture cleanup failed; any original test failure is included.", cleanupErrors);
            }
        }
    }

    private static bool HasOwnMarker(IntPtr window, string marker)
    {
        var root = AutomationElement.FromHandle(window);
        return root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, marker)).Count == 1;
    }
    private static (IntPtr Window, string Diagnostic) FindOwnMarkerHost(string marker)
    {
        try
        {
            var ownText = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, marker));
            if (ownText is null) return (IntPtr.Zero, "the exact own marker is absent from the desktop accessibility tree");
            var isOffscreen = ownText.Current.IsOffscreen;
            var current = ownText;
            IntPtr native = IntPtr.Zero;
            for (var depth = 0; current is not null && depth < 32; depth++)
            {
                if (current.Current.NativeWindowHandle is var handle && handle != 0) { native = new(handle); break; }
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            if (native == IntPtr.Zero) return (native, $"the exact own marker exists (offscreen={isOffscreen}) but no native HWND ancestor was exposed");
            var root = Native.GetAncestor(native, Native.GA_ROOT);
            if (root == IntPtr.Zero) root = native;
            Native.GetWindowThreadProcessId(root, out var pid);
            using var process = Process.GetProcessById((int)pid);
            // Class/title/path are read only for the native ancestor of the unique own text.
            return (root, $"own marker offscreen={isOffscreen}; native=0x{native.ToInt64():X}; root=0x{root.ToInt64():X}; visible={Native.IsWindowVisible(root)}; class={Native.ClassNameOf(root)}; title={Native.TitleOf(root)}; process={process.MainModule?.FileName ?? "unavailable"}");
        }
        catch (Exception exception)
        {
            return (IntPtr.Zero, $"exact own-marker accessibility discovery failed: {exception.GetType().Name}, HRESULT=0x{exception.HResult:X8}");
        }
    }
    private static string ReadPackageIdentity()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        if (result != 122) return $"status={result}";
        var name = new StringBuilder(checked((int)length));
        result = GetCurrentPackageFullName(ref length, name);
        return result == 0 ? name.ToString() : $"status={result}";
    }
    private static void TryCleanup(Action action, List<Exception> failures) { try { action(); } catch (Exception exception) { failures.Add(exception); } }
    private static bool Equal(RECT a, RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    private static string Describe(RECT rectangle) => $"{rectangle.Left},{rectangle.Top},{rectangle.Right},{rectangle.Bottom}";
    private static void PumpUntil(Func<bool> ready, TimeSpan timeout, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!ready()) { if (watch.Elapsed >= timeout) throw new TimeoutException(message); PumpFor(TimeSpan.FromMilliseconds(20)); }
    }
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void CreateShortcut(string path, string appId, Guid activator)
    {
        object? linkObject = null;
        var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(appId) };
        var activatorValue = new PropVariant { Type = 72, Pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>()) };
        Marshal.StructureToPtr(activator, activatorValue.Pointer, false);
        try
        {
            linkObject = new ShellLink();
            Marshal.ThrowExceptionForHR(((IShellLink)linkObject).SetPath(Environment.ProcessPath ?? throw new InvalidOperationException("No test process path.")));
            var store = (IPropertyStore)linkObject;
            var key = new PropertyKey(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            key = new PropertyKey(key.Format, 26);
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref activatorValue));
            Marshal.ThrowExceptionForHR(store.Commit());
            ((IPersistFile)linkObject).Save(path, true);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
            Marshal.FreeCoTaskMem(activatorValue.Pointer);
            if (linkObject is not null && Marshal.IsComObject(linkObject)) Marshal.ReleaseComObject(linkObject);
        }
    }

    private static void VerifyShortcut(string path, string id, Guid activator)
    {
        object? link = null;
        PropVariant appId = default, classId = default;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(path, 0);
            var store = (IPropertyStore)link;
            var key = new PropertyKey(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out appId));
            key = new(key.Format, 26);
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out classId));
            if (appId.Type != 31 || Marshal.PtrToStringUni(appId.Pointer) != id ||
                classId.Type != 72 || classId.Pointer == IntPtr.Zero || Marshal.PtrToStructure<Guid>(classId.Pointer) != activator)
                throw new InvalidOperationException("The exact own shortcut's AUMID or toast activator did not persist correctly.");
            Console.WriteLine("Own shortcut AUMID and ToastActivatorCLSID persisted and verified.");
        }
        finally
        {
            PropVariantClear(ref appId);
            PropVariantClear(ref classId);
            if (link is not null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
        }
    }

    private static int TryResolveIndexedApplication(string id, out string displayName)
    {
        var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        IShellItem? item = null;
        IntPtr name = IntPtr.Zero;
        displayName = "";
        try
        {
            var result = SHCreateItemFromParsingName(@"shell:AppsFolder\" + id, IntPtr.Zero, ref iid, out item);
            if (result < 0) return result;
            result = item.GetDisplayName(0, out name);
            if (result >= 0) displayName = Marshal.PtrToStringUni(name) ?? "";
            return result;
        }
        finally
        {
            if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
            if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
        }
    }

    private static void HoldRegistrationProbe(string id, Guid activator, string registration, string shortcut)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("WINFORGE_NOTIFICATION_REGISTRATION_HOLD_MS"), out var milliseconds) || milliseconds <= 0) return;
        var file = Environment.GetEnvironmentVariable("WINFORGE_NOTIFICATION_REGISTRATION_PROBE");
        if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file))
            throw new InvalidOperationException("A registration hold requires an explicit absolute own probe artifact path.");
        var hold = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 1, 20000));
        var physicalPath = shortcut;
        using (var stream = File.Open(shortcut, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var name = new StringBuilder(32768);
            var count = GetFinalPathNameByHandle(stream.SafeFileHandle, name, (uint)name.Capacity, 0);
            if (count == 0 || count >= name.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            physicalPath = name.ToString();
        }
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            ProcessId = Environment.ProcessId, AppUserModelId = id, Activator = activator.ToString("B"),
            RegistryPath = registration, ShortcutPath = shortcut, ShortcutPhysicalPath = physicalPath,
            HoldUntilUtc = DateTimeOffset.UtcNow + hold
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Own notification registration probe held for {hold.TotalSeconds:F0}s: {file}");
        PumpFor(hold);
    }

    private sealed class Context(string directory) : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(directory, "notification-live.log"));
        public IHotkeyService Hotkeys => throw new NotSupportedException();
        public List<string> Errors { get; } = [];
        public void Notify(string message) => Errors.Add(message);
    }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint eventId, uint flags, string item, IntPtr second);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid id, out IShellItem item);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder? name);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr context, ref Guid handler, ref Guid id, out IntPtr value);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint kind, out IntPtr name);
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        [PreserveSig] int GetPath(IntPtr file, int count, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr list);
        [PreserveSig] int SetIDList(IntPtr list);
        [PreserveSig] int GetDescription(IntPtr text, int count);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetWorkingDirectory(IntPtr text, int count);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetArguments(IntPtr text, int count);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int command);
        [PreserveSig] int SetShowCmd(int command);
        [PreserveSig] int GetIconLocation(IntPtr text, int count, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string text, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr owner, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
