using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ExplorerFolderSizesModule : ITweakModule, IAsyncTweakModule
{
    private static readonly TimeSpan ConnectionInterval = TimeSpan.FromSeconds(3);
    private const int MaximumExplorerProcesses = 8;
    private const uint ShellUpdateDirectory = 0x00001000;
    private const uint ShellPathUnicode = 0x00000005;
    private const uint ShellFlushNoWait = 0x00002000;
    private readonly Dictionary<int, ExplorerFolderSizeSession> _sessions = [];
    private readonly Dictionary<int, DateTime> _retryAfter = [];
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private IModuleContext? _context;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _lifetime;
    private string? _error;

    public string Id => "explorer-folder-sizes";
    public string Name => "Gezgin Klasör Boyutları";
    public string Category => "Gezgin";
    public string Description => "Ayrıntılar görünümünün Boyut sütununda klasör boyutlarını gösterir. WinForge yerel diskleri arka planda tarar, sonuçları önbelleğe alır ve değişen klasörleri günceller.";
    public string? Warning => _error;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [
        new() { Key = "scan-seconds", Label = "Klasör başına tarama süresi (saniye)", Kind = SettingKind.Number, Default = 30, Min = 5, Max = 120,
            Description = "Süre sınırını aşan veya okunamayan klasörlerin boyutu boş kalır. Büyük klasörler için artırabilirsiniz." },
        new() { Key = "cache-seconds", Label = "Önbellek geçerlilik süresi (saniye)", Kind = SettingKind.Number, Default = 60, Min = 5, Max = 300,
            Description = "Disk değişiklikleri ayrıca izlenir. En fazla 256 klasör önbellekte tutulur; ağ sürücüleri ve klasör bağlantıları taranmaz." }
    ];

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate()
    {
        _operationGate.Wait();
        try
        {
            if (IsActive) return;
            _lifetime = new CancellationTokenSource();
            try
            {
                Task.Run(() => SynchronizeAsync(_lifetime.Token)).GetAwaiter().GetResult();
                _timer = new DispatcherTimer(ConnectionInterval, DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
                IsActive = true;
                RefreshExplorerViews();
            }
            catch { Release(); throw; }
        }
        finally { _operationGate.Release(); }
    }
    public void Apply()
    {
        _operationGate.Wait();
        try
        {
            if (!IsActive) return;
            Task.Run(StopSessionsAsync).GetAwaiter().GetResult();
            _retryAfter.Clear();
            Task.Run(() => SynchronizeAsync(_lifetime!.Token)).GetAwaiter().GetResult();
            RefreshExplorerViews();
        }
        finally { _operationGate.Release(); }
    }

    public async Task ActivateAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (IsActive) return;
            _lifetime = new CancellationTokenSource();
            try
            {
                await SynchronizeAsync(_lifetime.Token).ConfigureAwait(true);
                _timer = new DispatcherTimer(ConnectionInterval, DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
                IsActive = true;
                RefreshExplorerViews();
            }
            catch { Release(); throw; }
        }
        finally { _operationGate.Release(); }
    }

    public async Task ApplyAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!IsActive) return;
            await StopSessionsAsync().ConfigureAwait(true);
            _retryAfter.Clear();
            await SynchronizeAsync(_lifetime!.Token).ConfigureAwait(true);
            RefreshExplorerViews();
        }
        finally { _operationGate.Release(); }
    }

    private async void OnTick(object? sender, EventArgs arguments)
    {
        if (!IsActive || !await _operationGate.WaitAsync(0).ConfigureAwait(true)) return;
        try
        {
            await SynchronizeAsync(_lifetime!.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (_error != exception.Message) _context?.Log.Error("Gezgin klasör boyutu bağlantısı yenilenemedi.", exception);
            _error = exception.Message;
        }
        finally { _operationGate.Release(); }
    }

    private async Task SynchronizeAsync(CancellationToken cancellation)
    {
        var processes = Process.GetProcessesByName("explorer");
        try
        {
            using var current = Process.GetCurrentProcess();
            var visibleOwners = new HashSet<int>();
            Native.EnumWindows((window, _) =>
            {
                var className = Native.ClassNameOf(window);
                if (className is "CabinetWClass" or "ExploreWClass")
                {
                    Native.GetWindowThreadProcessId(window, out var owner);
                    if (owner != 0) visibleOwners.Add((int)owner);
                }
                return true;
            }, IntPtr.Zero);
            var ids = processes.Where(process => process.SessionId == current.SessionId)
                .Select(process => process.Id).Where(visibleOwners.Contains).Take(MaximumExplorerProcesses).ToHashSet();
            foreach (var id in _retryAfter.Keys.Where(id => !ids.Contains(id)).ToArray()) _retryAfter.Remove(id);
            foreach (var id in _sessions.Keys.Where(id => !ids.Contains(id) || !_sessions[id].IsConnected).ToArray())
            {
                _sessions[id].Dispose();
                _sessions.Remove(id);
            }
            Exception? failure = null;
            var connected = false;
            foreach (var id in ids.Where(id => !_sessions.ContainsKey(id)))
            {
                if (_retryAfter.TryGetValue(id, out var retry) && retry > DateTime.UtcNow) continue;
                try
                {
                    _sessions[id] = await ExplorerFolderSizeSession.ConnectAsync((uint)id, Path.Combine(AppContext.BaseDirectory, "native"),
                        _context!.Settings.GetInt("scan-seconds", 30), _context.Settings.GetInt("cache-seconds", 60), cancellation).ConfigureAwait(true);
                    _retryAfter.Remove(id);
                    connected = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { failure = exception; _retryAfter[id] = DateTime.UtcNow.AddSeconds(30); }
            }
            if (failure is not null && _sessions.Count == 0) throw failure;
            _error = failure?.Message;
            if (connected && IsActive) RefreshExplorerViews();
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public void Deactivate()
    {
        _lifetime?.Cancel();
        _operationGate.Wait();
        try { Release(); }
        finally { _operationGate.Release(); }
    }

    public async Task DeactivateAsync()
    {
        _lifetime?.Cancel();
        await _operationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            IsActive = false;
            if (_timer is not null) { _timer.Stop(); _timer.Tick -= OnTick; _timer = null; }
            try { await StopSessionsAsync().ConfigureAwait(true); }
            finally { Release(); }
        }
        finally { _operationGate.Release(); }
    }

    private async Task StopSessionsAsync()
    {
        Exception? failure = null;
        foreach (var session in _sessions.Values)
        {
            try { await session.StopAsync().ConfigureAwait(true); }
            catch (Exception exception) { failure ??= exception; }
        }
        _sessions.Clear();
        if (failure is not null) throw failure;
    }

    private static void RefreshExplorerViews()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return;
        try
        {
            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return;
            object windows = ((dynamic)shell).Windows();
            foreach (var candidate in (System.Collections.IEnumerable)windows)
            {
                try
                {
                    var location = (string)((dynamic)candidate).LocationURL;
                    if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile)
                        SHChangeNotify(ShellUpdateDirectory, ShellPathUnicode | ShellFlushNoWait, uri.LocalPath, IntPtr.Zero);
                }
                catch (Exception exception) when (exception is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string path, IntPtr item2);

    private void Release()
    {
        IsActive = false;
        if (_timer is not null) { _timer.Stop(); _timer.Tick -= OnTick; _timer = null; }
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();
        _retryAfter.Clear();
        _lifetime?.Dispose();
        _lifetime = null;
    }
}
