using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Threading;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Positions only positively identified Windows toast windows; never notification-center windows.</summary>
public sealed class NotificationsPlacementModule : ITweakModule
{
    private const uint NoZOrder = 0x0004;
    private const int DefaultGap = 12;
    private static readonly string[] Positions = ["Sağ alt", "Sol alt", "Sağ üst", "Sol üst", "Üst orta", "Alt orta", "Merkez"];
    private readonly Func<IntPtr, bool> _isToast;
    private readonly Dictionary<IntPtr, Placement> _placements = [];
    private WindowEventObserver? _observer;
    private DispatcherTimer? _timer;
    private IModuleContext? _context;
    private bool _positioning;
    internal const string RuntimeKey = @"Software\WinForge\NotificationsPlacement\Runtime";
    private readonly bool _useNative;
    private nint _library, _hook;
    private int _lastNativeError;
    private InstallHook? _install;
    private readonly Dictionary<uint, nint> _threadHooks = [];
    private const string NativeProperty = "WinForge.ToastPlacement.Native", ObserverProperty = "WinForge.ToastPlacement.Observer";

    public NotificationsPlacementModule() : this(IsWindowsToast) { _useNative = true; }
    internal NotificationsPlacementModule(Func<IntPtr, bool> isToast)
    {
        _isToast = isToast;
        Settings =
        [
            new() { Key = "position", Label = "Bildirim konumu", Kind = SettingKind.Choice, Options = Positions, Default = Positions[0] },
            new() { Key = "monitor", Label = "Ekran", Kind = SettingKind.Number, Default = 0, Min = 0, Max = 16,
                Description = "0: farenin ekranı, 1: ana ekran, 2 ve sonrası: diğer ekranlar." },
            new() { Key = "gap", Label = "Kenardan uzaklık (DIP)", Kind = SettingKind.Number, Default = DefaultGap, Min = 0, Max = 200 },
            new()
            {
                Key = "send-test-toast",
                Label = "Test bildirimi gönder",
                Kind = SettingKind.Action,
                Description = "Bildirim konumlandırmasını doğrulamak için bir Windows test bildirimi gönderir.",
                Execute = SendTestNotification
            }
        ];
    }

    public string Id => "notifications-placement";
    public string Name => "Bildirim Konumu";
    public string Category => "Görünüm";
    public string Description => "Windows bildirimlerini seçilen ekranın köşesine veya ortasına taşır; kapatıldığında özgün konumu geri yükler.";
    public string? Warning => "Türkçe ve İngilizce Windows bildirim pencereleri tanınır. Windows'un taşımaya izin vermediği sürümlerde hata gösterilir; bildirim merkezi taşınmaz.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        if (_context is null) throw new InvalidOperationException("Modül başlatılmadı.");
        try
        {
            if (_useNative)
            {
                IsActive = true;
                ApplyNative();
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _timer.Tick += OnNativeHeartbeat;
                _timer.Start();
            }
            _observer = new(WindowEventObserver.ObjectDestroy, WindowEventObserver.ObjectLocationChange, OnWindowEvent, Report);
            IsActive = true;
            Apply();
        }
        catch { Deactivate(); throw; }
    }

    public void Apply()
    {
        if (!IsActive) return;
        if (_useNative) ApplyNative();
        Native.EnumWindows((window, _) => { Track(window); return true; }, IntPtr.Zero);
        foreach (var placement in _placements.Values)
        {
            placement.Blocked = false;
            placement.Attempts = 0;
            placement.Moves.Restart();
        }
        PositionAll();
    }

    public void Deactivate()
    {
        IsActive = false;
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnNativeHeartbeat;
        _timer = null;
        if (_useNative)
        {
            using (var runtime = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RuntimeKey)) runtime.SetValue("active", 0);
            if (_hook != 0) UnhookWindowsHookEx(_hook);
            _hook = 0;
            foreach (var hook in _threadHooks.Values) UnhookWindowsHookEx(hook);
            _threadHooks.Clear();
            WakeNativeWindows();
        }
        try { _observer?.Dispose(); }
        finally
        {
            _observer = null;
            List<Exception> errors = [];
            foreach (var pair in _placements.ToArray())
            {
                if (!SameProcess(pair.Key, pair.Value) || !_isToast(pair.Key)) { Forget(pair.Key, pair.Value); continue; }
                try { MoveVerified(pair.Key, pair.Value.Original); _placements.Remove(pair.Key); }
                catch (Exception exception) { errors.Add(exception); }
                finally { if (_useNative) RemoveProp(pair.Key, ObserverProperty); }
            }
            if (errors.Count != 0) throw new AggregateException("Bildirimlerin özgün konumu geri yüklenemedi.", errors);
        }
    }

    private void OnWindowEvent(WindowEventObserver.WindowEvent change)
    {
        if (!IsActive) return;
        if (change.Kind == WindowEventObserver.ObjectDestroy) { if (_placements.Remove(change.Window)) PositionAll(); return; }
        if (_positioning) return;
        if (!_placements.ContainsKey(change.Window) && !Track(change.Window)) return;
        // Accessibility events are already delivered after the window changes.
        // Do not add another frame of delay in the observer/test engine.
        PositionAll();
    }

    private void ApplyNative()
    {
        if (_library == 0)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "native", "WinForge.NotificationsPlacementTap.dll");
            using var source = File.OpenRead(path);
            var digest = SHA256.HashData(source);
            var directory = Path.Combine(AppData.DirectoryPath, "Notifications", Convert.ToHexString(digest)[..16]);
            Directory.CreateDirectory(directory);
            var cachedPath = Path.Combine(directory, Path.GetFileName(path));
            if (!File.Exists(cachedPath)) File.Copy(path, cachedPath);
            using var cached = File.OpenRead(cachedPath);
            if (!CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(cached))) throw new InvalidDataException("Bildirim bileşeninin önbellek doğrulaması başarısız.");
            _library = NativeLibrary.Load(cachedPath);
        }
        using (var runtime = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RuntimeKey))
        {
            runtime.SetValue("anchor", Math.Max(0, Array.IndexOf(Positions, _context!.Settings.GetString("position", Positions[0]))));
            runtime.SetValue("monitor", Math.Clamp(_context.Settings.GetInt("monitor", 0), 0, 16));
            runtime.SetValue("gap", Math.Clamp(_context.Settings.GetInt("gap", DefaultGap), 0, 200));
            runtime.SetValue("heartbeat", Environment.TickCount64, Microsoft.Win32.RegistryValueKind.QWord);
            runtime.SetValue("last-error", 0);
            runtime.SetValue("active", 1);
        }
        _lastNativeError = 0;
        if (_hook == 0)
        {
            _install = Marshal.GetDelegateForFunctionPointer<InstallHook>(NativeLibrary.GetExport(_library, "InstallNotificationsHook"));
            _hook = _install(0);
            if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Bildirim yerleşim kancası kurulamadı.");
        }
        RefreshNativeThreads();
        WakeNativeWindows();
    }

    private void RefreshNativeThreads()
    {
        // Packaged shell hosts may reject desktop-wide DLL hooks. Install on
        // their existing GUI threads as well, before a toast HWND is created.
        var live = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("ShellExperienceHost"))
        using (process)
        {
            try
            {
                if (!IsSystemHostPath(process.MainModule?.FileName ?? "")) continue;
                foreach (ProcessThread thread in process.Threads)
                using (thread)
                {
                    var id = (uint)thread.Id;
                    var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
                    if (!GetGUIThreadInfo(id, ref info)) continue;
                    live.Add(id);
                    if (_threadHooks.ContainsKey(id)) continue;
                    var hook = _install!(id);
                    if (hook != 0) _threadHooks.Add(id, hook);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception) { }
        }
        foreach (var id in _threadHooks.Keys.Where(id => !live.Contains(id)).ToArray())
        {
            UnhookWindowsHookEx(_threadHooks[id]);
            _threadHooks.Remove(id);
        }
    }

    private void OnNativeHeartbeat(object? sender, EventArgs args)
    {
        try
        {
            using var runtime = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RuntimeKey);
            runtime.SetValue("heartbeat", Environment.TickCount64, Microsoft.Win32.RegistryValueKind.QWord);
            var error = runtime.GetValue("last-error") is int value ? value : 0;
            if (error != 0 && error != _lastNativeError) { _lastNativeError = error; Report(new Win32Exception(error)); }
            RefreshNativeThreads();
        }
        catch (Exception exception) { Report(exception); }
    }

    private void WakeNativeWindows() => Native.EnumWindows((window, _) =>
    {
        if (_isToast(window)) SendMessageTimeout(window, 0, 0, 0, 2, 100, out _);
        return true;
    }, IntPtr.Zero);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate nint InstallHook(uint thread);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint window, uint message, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(nint window, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint RemoveProp(nint window, string name);
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretBounds;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    private bool Track(IntPtr window)
    {
        if (_placements.ContainsKey(window)) return true;
        if (_useNative && GetProp(window, NativeProperty) != 0) return false;
        if (!Native.IsWindowVisible(window) || !_isToast(window) || !Native.GetWindowRect(window, out var bounds)) return false;
        Native.GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var placement = new Placement(pid, process.StartTime.ToUniversalTime().Ticks, bounds);
            if (_useNative) SetProp(window, ObserverProperty, 1);
            _placements.Add(window, placement);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    private void PositionAll()
    {
        if (!IsActive || _positioning) return;
        _positioning = true;
        try
        {
            var displays = DesktopDisplays.Enumerate().OrderByDescending(display => display.IsPrimary).ThenBy(display => display.DeviceName, StringComparer.Ordinal).ToArray();
            if (displays.Length == 0) throw new InvalidOperationException("Bildirimler için etkin ekran bulunamadı.");
            var monitor = Math.Clamp(_context!.Settings.GetInt("monitor", 0), 0, displays.Length);
            var display = monitor == 0 && Native.GetCursorPos(out var cursor) ? DesktopDisplays.ForPoint(cursor) : displays[Math.Max(0, monitor - 1)];
            var anchor = Array.IndexOf(Positions, _context.Settings.GetString("position", Positions[0]));
            var gap = Math.Clamp(_context.Settings.GetInt("gap", DefaultGap), 0, 200);
            var stack = 0;
            foreach (var pair in _placements.OrderByDescending(pair => pair.Value.Original.Top).ToArray())
            {
                var window = pair.Key;
                var placement = pair.Value;
                if (!SameProcess(window, placement) || !_isToast(window)) { Forget(window, placement); continue; }
                if (!Native.IsWindowVisible(window) || placement.Blocked || !Native.GetWindowRect(window, out var current)) continue;
                try
                {
                    var target = CalculateBounds(display.Bounds, current, anchor, gap, DisplayDpi(display.Handle), stack);
                    stack = checked(stack + current.Bottom - current.Top + DefaultGap);
                    if (Equal(current, target)) continue;
                    if (placement.Moves.Elapsed > TimeSpan.FromSeconds(3)) { placement.Moves.Restart(); placement.Attempts = 0; }
                    if (++placement.Attempts > 6) throw new InvalidOperationException("Windows bildirim konumunu sürekli sıfırlıyor; taşıma durduruldu.");
                    MoveVerified(window, target);
                }
                catch (Exception exception) { placement.Blocked = true; Report(exception); }
            }
        }
        finally { _positioning = false; }
    }

    internal static RECT CalculateBounds(RECT work, RECT source, int anchor, int gapDip, uint dpi, int stack = 0)
    {
        var width = (long)source.Right - source.Left;
        var height = (long)source.Bottom - source.Top;
        var workWidth = (long)work.Right - work.Left;
        var workHeight = (long)work.Bottom - work.Top;
        if (width <= 0 || height <= 0 || width > workWidth || height > workHeight || stack < 0)
            throw new ArgumentOutOfRangeException(nameof(source), "Bildirim boyutları ekran çalışma alanına sığmıyor.");
        var gap = (long)Math.Round(Math.Clamp(gapDip, 0, 200) * Math.Clamp(dpi == 0 ? 96u : dpi, 48u, 768u) / 96d);
        var left = anchor is 1 or 3 ? work.Left + gap : anchor is 4 or 5 or 6 ? work.Left + (workWidth - width) / 2 : work.Right - width - gap;
        var top = anchor is 2 or 3 or 4 ? work.Top + gap + stack : anchor == 6 ? work.Top + (workHeight - height) / 2 + stack : work.Bottom - height - gap - stack;
        if (stack > workHeight - height) throw new ArgumentOutOfRangeException(nameof(stack), "Bildirim yığını ekran çalışma alanına sığmıyor.");
        if (stack > 0 && (top < work.Top || top + height > work.Bottom))
            throw new ArgumentOutOfRangeException(nameof(stack), "Bildirim yığını kenar boşluğuyla birlikte ekran çalışma alanına sığmıyor.");
        left = Math.Clamp(left, work.Left, work.Right - width);
        top = Math.Clamp(top, work.Top, work.Bottom - height);
        return new() { Left = checked((int)left), Top = checked((int)top), Right = checked((int)(left + width)), Bottom = checked((int)(top + height)) };
    }

    private static void MoveVerified(IntPtr window, RECT bounds)
    {
        if (!Native.SetWindowPos(window, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, NoZOrder | Native.SWP_NOACTIVATE))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        if (!Native.GetWindowRect(window, out var actual) || !Equal(actual, bounds))
            throw new InvalidOperationException("Windows bildirim penceresinin istenen konumunu uygulamadı.");
    }

    private static bool Equal(RECT a, RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    private void Forget(nint window, Placement placement)
    {
        if (_useNative && SameProcess(window, placement)) RemoveProp(window, ObserverProperty);
        _placements.Remove(window);
    }

    private static bool SameProcess(IntPtr window, Placement placement)
    {
        if (!Native.IsWindow(window)) return false;
        Native.GetWindowThreadProcessId(window, out var pid);
        if (pid != placement.ProcessId) return false;
        try { using var process = Process.GetProcessById((int)pid); return process.StartTime.ToUniversalTime().Ticks == placement.ProcessStart; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    internal static bool MatchesToastIdentity(string className, string title, string executablePath)
    {
        return className == "Windows.UI.Core.CoreWindow" && title is "Yeni bildirim" or "New notification" &&
               IsSystemHostPath(executablePath);
    }

    private static bool IsSystemHostPath(string path) => string.Equals(path,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps", "ShellExperienceHost_cw5n1h2txyewy", "ShellExperienceHost.exe"),
        StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsToast(IntPtr window)
    {
        if (Native.GetAncestor(window, Native.GA_ROOT) != window || Native.ClassNameOf(window) != "Windows.UI.Core.CoreWindow") return false;
        Native.GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            // Never synchronously ask unrelated (including hidden own-process)
            // windows for their text while their UI thread may be starting.
            return IsSystemHostPath(process.MainModule?.FileName ?? "") && Native.TitleOf(window) is "Yeni bildirim" or "New notification";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    private void SendTestNotification()
    {
        if (!IsActive)
        {
            _context?.Notify("Bildirim konumu etkin değil; test bildirimi Windows varsayılan konumunda gösterilebilir.");
        }

        try
        {
            var xml = new XmlDocument();
            xml.LoadXml(
                "<toast duration='short'>" +
                "<visual>" +
                "<binding template='ToastGeneric'>" +
                "<text>WinForge Bildirim Testi</text>" +
                "<text>Bildirim konumu başarıyla test ediliyor.</text>" +
                "</binding>" +
                "</visual>" +
                "</toast>");

            var toast = new ToastNotification(xml)
            {
                ExpirationTime = DateTimeOffset.Now.AddSeconds(10)
            };

            const string aumid = "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe";
            ToastNotifier notifier;
            try
            {
                notifier = ToastNotificationManager.CreateToastNotifier(aumid);
            }
            catch
            {
                notifier = ToastNotificationManager.CreateToastNotifier();
            }

            notifier.Show(toast);
        }
        catch (Exception exception)
        {
            SendTestNotificationViaPowerShell(exception);
        }
    }

    private void SendTestNotificationViaPowerShell(Exception? primaryException = null)
    {
        try
        {
            const string script =
                "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null\n" +
                "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlContent, ContentType = WindowsRuntime] | Out-Null\n" +
                "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument\n" +
                "$xml.LoadXml('<toast duration=\"short\"><visual><binding template=\"ToastGeneric\"><text>WinForge Bildirim Testi</text><text>Bildirim konumu başarıyla test ediliyor.</text></binding></visual></toast>')\n" +
                "$toast = New-Object Windows.UI.Notifications.ToastNotification $xml\n" +
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe').Show($toast)\n";

            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(startInfo);
        }
        catch (Exception fallbackException)
        {
            _context?.Log.Error("Test bildirimi gönderilemedi.", fallbackException);
            _context?.Notify("Test bildirimi gönderilemedi: " + (primaryException?.Message ?? fallbackException.Message));
        }
    }

    private void Report(Exception exception)
    {
        _context?.Log.Error("Bildirim konumu uygulanamadı.", exception);
        _context?.Notify("Bildirim konumu uygulanamadı: " + exception.Message);
    }

    private static uint DisplayDpi(IntPtr monitor)
    {
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(GetDpiForMonitor(monitor, 0, out var x, out _));
        return x;
    }

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    private sealed class Placement(uint processId, long processStart, RECT original)
    {
        public uint ProcessId { get; } = processId;
        public long ProcessStart { get; } = processStart;
        public RECT Original { get; } = original;
        public Stopwatch Moves { get; } = Stopwatch.StartNew();
        public int Attempts { get; set; }
        public bool Blocked { get; set; }
    }
}
