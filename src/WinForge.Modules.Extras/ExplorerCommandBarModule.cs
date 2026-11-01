using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ExplorerCommandBarModule : ITweakModule, IAsyncTweakModule
{
    private readonly Dictionary<uint, ShellDiagnosticsSession> _sessions = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IModuleContext? _context;
    private DispatcherTimer? _timer;
    private string? _error;
    public string Id => "explorer-command-bar";
    public string Name => "Gezgin Komut Çubuğu";
    public string Category => "Gezgin";
    public string Description => "Kes, Kopyala, Yapıştır, Sırala ve Görünüm düğmelerinin bulunduğu üst komut çubuğunu gizler.";
    public string? Warning => _error;
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [
        new() { Key = "hide-command-bar", Label = "Üst komut çubuğunu gizle", Default = true,
            Description = "Windows 11 Dosya Gezgini için. Kapatıldığında çubuk geri gelir. Dosya işlemlerinin klavye kısayolları kullanılmaya devam eder." }
    ];
    internal static IReadOnlyList<ShellStyleRule> Rules(bool hidden) => hidden ?
    [
        new("FileExplorerExtensions.CommandBarControl", new Dictionary<string, string> { ["Visibility"] = "Collapsed" }),
        new("FileExplorerExtensions.CommandBarControl_Wave1", new Dictionary<string, string> { ["Visibility"] = "Collapsed" }),
        new("FileExplorerExtensions.NavigationBarControl", new Dictionary<string, string> { ["Grid.RowSpan"] = "2" })
    ] : [];

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() => ActivateAsync().GetAwaiter().GetResult();
    public async Task ActivateAsync()
    {
        if (IsActive) return;
        IsActive = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += OnTick;
        _timer.Start();
        try { await ApplyAsync().ConfigureAwait(false); }
        catch { Deactivate(); throw; }
    }
    private async void OnTick(object? sender, EventArgs e)
    {
        try { await ApplyAsync(); }
        catch (Exception error) { _error = error.Message; _context?.Log.Error("Gezgin komut çubuğu uygulanamadı.", error); }
    }
    public void Apply() => ApplyAsync().GetAwaiter().GetResult();
    public async Task ApplyAsync()
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (!IsActive) return;
            if (!_context!.Settings.GetBool("hide-command-bar", true)) { ClearSessions(); _error = null; return; }
            var processes = ExplorerProcesses();
            foreach (var process in _sessions.Keys.Where(id => !processes.Contains(id) || !_sessions[id].IsConnected).ToArray())
            {
                _sessions[process].Dispose(); _sessions.Remove(process);
            }
            foreach (var process in processes)
            {
                if (!_sessions.TryGetValue(process, out var session))
                {
                    session = await ShellDiagnosticsSession.ConnectAsync(process, Path.Combine(AppContext.BaseDirectory, "native"), winUi: true).ConfigureAwait(false);
                    _sessions.Add(process, session);
                    session.Apply(true, Rules(true));
                }
                if (session.Status.Errors > 0) throw new InvalidOperationException("Gezgin komut çubuğu değişikliği uygulanamadı: " + session.Status.LastErrorMessage);
            }
            _error = null;
        }
        finally { _gate.Release(); }
    }
    public void Deactivate()
    {
        IsActive = false;
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTick;
        _timer = null;
        _gate.Wait();
        try { ClearSessions(); _error = null; }
        finally { _gate.Release(); }
    }
    public Task DeactivateAsync() { Deactivate(); return Task.CompletedTask; }
    private void ClearSessions()
    {
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();
    }
    private static HashSet<uint> ExplorerProcesses()
    {
        var processes = new HashSet<uint>();
        EnumWindows((window, _) =>
        {
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "CabinetWClass")
            {
                GetWindowThreadProcessId(window, out var process);
                if (process != 0) processes.Add(process);
            }
            return true;
        }, 0);
        return processes;
    }
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
}
