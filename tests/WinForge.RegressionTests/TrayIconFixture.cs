using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.RegressionTests;

/// <summary>Two disposable, activatable notification icons belonging only to this test run.</summary>
internal sealed class TrayIconFixture : IDisposable
{
    private const string RegistryPath = @"Control Panel\NotifyIconSettings";
    private const string PromotionValue = "IsPromoted";
    private const uint AddIcon = 0;
    private const uint DeleteIcon = 2;
    private const uint SetVersion = 4;
    private const uint IconVersion = 4;
    private const uint MessageFlag = 0x1;
    private const uint IconFlag = 0x2;
    private const uint TooltipFlag = 0x4;
    private const uint GuidFlag = 0x20;
    private const uint ShowTooltipFlag = 0x80;
    private const uint CallbackMessage = 0x8054;
    private const uint QuitMessage = 0x12;
    private const uint AbortHungWindow = 0x2;
    private const uint NotificationTimeoutMilliseconds = 1000;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RegistryPollInterval = TimeSpan.FromMilliseconds(40);
    private readonly IconIdentity[] _icons = [CreateIdentity("A"), CreateIdentity("B")];
    private readonly HashSet<string> _initialSubkeys;
    private readonly Dictionary<string, PromotionSnapshot> _promotions = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _iconGate = new();
    private readonly Thread _thread;
    private Exception? _workerFailure;
    private IntPtr _window;
    private uint _threadId;
    private int _disposed;

    internal sealed record IconIdentity(Guid Guid, string Tooltip);
    internal sealed record IconBounds(IconIdentity Identity, RECT Bounds);
    private sealed record PromotionSnapshot(bool Present, object? Value, RegistryValueKind Kind);

    internal IReadOnlyList<IconIdentity> Icons => _icons;

    internal TrayIconFixture()
    {
        using (var registry = Registry.CurrentUser.OpenSubKey(RegistryPath))
            _initialSubkeys = new HashSet<string>(registry?.GetSubKeyNames() ?? [], StringComparer.OrdinalIgnoreCase);
        _thread = new Thread(Run) { IsBackground = true, Name = "WinForge tray verification fixture" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        try { _startup.Task.WaitAsync(StartupTimeout).GetAwaiter().GetResult(); }
        catch (Exception startupFailure)
        {
            try { Dispose(); }
            catch (Exception cleanupFailure) { throw new AggregateException(startupFailure, cleanupFailure); }
            throw;
        }
    }

    private static IconIdentity CreateIdentity(string suffix)
    {
        var identity = Guid.NewGuid();
        return new(identity, $"WinForge tray verification {suffix} {identity:N}");
    }

    private void Run()
    {
        var added = new List<IconIdentity>();
        try
        {
            _threadId = GetCurrentThreadId();
            using var window = new NativeWindowFixture("WinForge.TrayFixture." + Guid.NewGuid().ToString("N"),
                "WinForge tray verification", new RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 }, 0x80000000, 0x08000080);
            _window = window.Handle;
            try
            {
                foreach (var icon in _icons)
                {
                    lock (_iconGate)
                    {
                        RegisterIcon(icon);
                        added.Add(icon);
                    }
                }
                _startup.TrySetResult();
                while (true)
                {
                    var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
                    if (result == 0) break;
                    if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            finally
            {
                lock (_iconGate)
                {
                    foreach (var icon in added)
                    {
                        var data = Data(icon);
                        if (!ShellNotifyIcon(DeleteIcon, ref data))
                            _workerFailure ??= new InvalidOperationException("Deleting own verification tray icon failed.");
                    }
                    _window = IntPtr.Zero;
                }
            }
        }
        catch (Exception exception)
        {
            _workerFailure ??= exception;
            _startup.TrySetException(exception);
        }
    }

    private NotifyIconData Data(IconIdentity icon) => new()
    {
        Size = checked((uint)Marshal.SizeOf<NotifyIconData>()),
        Window = _window,
        Id = icon == _icons[0] ? 1u : 2u,
        Flags = MessageFlag | IconFlag | TooltipFlag | GuidFlag | ShowTooltipFlag,
        Callback = CallbackMessage,
        Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)),
        Tooltip = icon.Tooltip,
        Info = "",
        InfoTitle = "",
        Guid = icon.Guid
    };

    private void RegisterIcon(IconIdentity icon)
    {
        var data = Data(icon);
        if (!ShellNotifyIcon(AddIcon, ref data)) throw new Win32Exception("Adding own verification tray icon failed.");
        data.Version = IconVersion;
        if (!ShellNotifyIcon(SetVersion, ref data))
        {
            ShellNotifyIcon(DeleteIcon, ref data);
            throw new Win32Exception("Setting own verification tray icon version failed.");
        }
    }

    /// <summary>Re-registers only this fixture's two GUIDs so Explorer reads their own saved preference.</summary>
    internal void RefreshOwnIcons()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_iconGate)
        {
            if (_window == IntPtr.Zero) throw new InvalidOperationException("The own tray fixture window is no longer running.");
            foreach (var icon in _icons)
            {
                var data = Data(icon);
                if (!ShellNotifyIcon(DeleteIcon, ref data)) throw new Win32Exception("Refreshing own verification tray icon failed.");
                RegisterIcon(icon);
            }
        }
    }

    /// <summary>
    /// Requests promotion of these two icons only. Registry success is not a
    /// visibility assertion: callers must verify their real Explorer nodes.
    /// </summary>
    internal bool TryPromoteOwnIcons(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        var clock = Stopwatch.StartNew();
        do
        {
            var matched = new HashSet<Guid>();
            using (var root = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true))
            {
                if (root is not null)
                {
                    foreach (var name in root.GetSubKeyNames())
                    {
                        using var key = root.OpenSubKey(name, writable: true);
                        if (key is null || !TryMatch(key, out var identity)) continue;
                        if (_initialSubkeys.Contains(name))
                            throw new InvalidOperationException("A verification GUID unexpectedly matched a pre-existing tray registry entry.");
                        if (!_promotions.ContainsKey(name))
                        {
                            var present = key.GetValueNames().Contains(PromotionValue, StringComparer.OrdinalIgnoreCase);
                            _promotions.Add(name, new(present, present ? key.GetValue(PromotionValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null,
                                present ? key.GetValueKind(PromotionValue) : RegistryValueKind.Unknown));
                        }
                        key.SetValue(PromotionValue, 1, RegistryValueKind.DWord);
                        if (key.GetValue(PromotionValue) is int value && value == 1) matched.Add(identity.Guid);
                    }
                }
            }
            if (matched.Count == _icons.Length)
            {
                NotifyTraySettings();
                RefreshOwnIcons();
                return true;
            }
            if (clock.Elapsed >= timeout) break;
            Thread.Sleep(RegistryPollInterval);
        } while (true);
        Console.WriteLine("Tray fixture promotion diagnostic: " + DescribeDiagnostic());
        return false;
    }

    private bool TryMatch(RegistryKey key, out IconIdentity identity)
    {
        var value = key.GetValue("IconGuid");
        var tooltip = key.GetValue("InitialTooltip") as string;
        var guid = ReadGuid(value);
        identity = _icons.FirstOrDefault(icon => guid == icon.Guid ||
            string.Equals(tooltip, icon.Tooltip, StringComparison.Ordinal))!;
        return identity is not null;
    }

    private static Guid? ReadGuid(object? value) => value switch
    {
        Guid guid => guid,
        byte[] bytes when bytes.Length == 16 => new Guid(bytes),
        string text when Guid.TryParse(text, out var guid) => guid,
        _ => null
    };

    internal string DescribeDiagnostic()
    {
        var entries = new List<object>();
        using var root = Registry.CurrentUser.OpenSubKey(RegistryPath);
        var subkeys = root?.GetSubKeyNames() ?? [];
        foreach (var name in subkeys)
        {
            using var key = root!.OpenSubKey(name);
            if (key is null) continue;
            var guidValue = key.GetValue("IconGuid");
            var tooltip = key.GetValue("InitialTooltip") as string;
            var executable = key.GetValue("ExecutablePath") as string;
            var related = _icons.Any(icon => ReadGuid(guidValue) == icon.Guid ||
                string.Equals(tooltip, icon.Tooltip, StringComparison.Ordinal));
            var ownExecutable = executable is not null &&
                string.Equals(Path.GetFileName(executable), Path.GetFileName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase);
            if (!related && !ownExecutable) continue;
            entries.Add(new
            {
                Key = name,
                RelatedToOwnIcon = related,
                ExistedBeforeFixture = _initialSubkeys.Contains(name),
                IconGuidKind = key.GetValueNames().Contains("IconGuid") ? key.GetValueKind("IconGuid").ToString() : "Missing",
                IconGuidType = guidValue?.GetType().FullName,
                IconGuid = ReadGuid(guidValue)?.ToString("D"),
                InitialTooltip = tooltip,
                ExecutablePath = executable,
                IsPromoted = key.GetValue(PromotionValue)
            });
        }
        var native = _icons.Select(icon =>
        {
            var identifier = new NotifyIconIdentifier
            {
                Size = checked((uint)Marshal.SizeOf<NotifyIconIdentifier>()), Window = _window, Guid = icon.Guid
            };
            var result = ShellNotifyIconGetRect(ref identifier, out var bounds);
            return new { icon.Guid, icon.Tooltip, HResult = $"0x{result:X8}", Bounds = new { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom } };
        }).ToArray();
        Native.GetWindowThreadProcessId(_window, out var ownerProcess);
        return JsonSerializer.Serialize(new
        {
            Process = Environment.ProcessId,
            Executable = Environment.ProcessPath,
            Window = _window.ToInt64(),
            WindowOwnerProcess = ownerProcess,
            WorkerThread = _threadId,
            RegistryExists = root is not null,
            RegistrySubkeyCount = subkeys.Length,
            NativeIcons = native,
            OwnRegistryCandidates = entries
        });
    }

    internal IReadOnlyList<IconBounds> ReadBounds()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var result = new List<IconBounds>();
        foreach (var icon in _icons)
        {
            var identity = new NotifyIconIdentifier
            {
                Size = checked((uint)Marshal.SizeOf<NotifyIconIdentifier>()), Window = _window, Guid = icon.Guid
            };
            if (ShellNotifyIconGetRect(ref identity, out var bounds) >= 0) result.Add(new(icon, bounds));
        }
        return result;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var failures = new List<Exception>();
        if (_thread.IsAlive)
        {
            PostThreadMessage(_threadId, QuitMessage, IntPtr.Zero, IntPtr.Zero);
            if (!_thread.Join(ShutdownTimeout)) failures.Add(new TimeoutException("Own tray fixture thread did not shut down."));
        }
        if (_workerFailure is not null) failures.Add(_workerFailure);
        try { RestoreOwnRegistryEntries(); }
        catch (Exception exception) { failures.Add(exception); }
        if (failures.Count > 0) throw new AggregateException("Own tray fixture cleanup was incomplete.", failures);
    }

    private void RestoreOwnRegistryEntries()
    {
        using var root = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
        if (root is null) return;
        foreach (var name in root.GetSubKeyNames())
        {
            bool remove;
            using (var key = root.OpenSubKey(name, writable: true))
            {
                if (key is null || !TryMatch(key, out _)) continue;
                if (_promotions.TryGetValue(name, out var promotion))
                {
                    if (promotion.Present) key.SetValue(PromotionValue, promotion.Value!, promotion.Kind);
                    else key.DeleteValue(PromotionValue, throwOnMissingValue: false);
                }
                remove = !_initialSubkeys.Contains(name);
                if (remove && key.SubKeyCount != 0)
                    throw new InvalidOperationException("An own tray registry entry unexpectedly contains child keys; cleanup refused recursive deletion.");
            }
            if (remove) root.DeleteSubKey(name, throwOnMissingSubKey: false);
        }
        if (_promotions.Count > 0) NotifyTraySettings();
    }

    private static void NotifyTraySettings() => Native.SendMessageTimeout(Native.HWND_BROADCAST,
        Native.WM_SETTINGCHANGE, IntPtr.Zero, "TraySettings", AbortHungWindow, NotificationTimeoutMilliseconds, out _);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id;
        internal uint Flags;
        internal uint Callback;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tooltip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid Guid;
        internal IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id;
        internal Guid Guid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal IntPtr Window;
        internal uint Id;
        internal UIntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal POINT Point;
        internal uint Private;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    private static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier identity, out RECT bounds);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)] private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
}
