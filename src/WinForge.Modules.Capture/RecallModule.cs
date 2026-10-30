using Microsoft.Win32;
using System.Windows.Threading;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

public sealed class RecallModule : CaptureModuleBase
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly object recordingGate = new();
    private HistoryStore? store;
    private UsageTracker? usage;
    private WindowEventObserver? foregroundObserver;
    private CancellationTokenSource? lifetime;
    private Task? ocr;
    private bool locked, suspended, busy, paused;
    private string previous = "";
    private long lastCapture;
    public override string Id => "screen-history";
    public override string Name => "Ekran Geçmişi";
    public override string Description => "Yerel ekran geçmişi, OCR araması, uygulama süreleri ve RecallClone kayıt aktarımı.";
    protected override string Kind => "recall";
    protected override IEnumerable<SettingDescriptor> ModuleSettings =>
    [
        new() { Key = "interval", Label = "Kayıt aralığı (saniye)", Kind = SettingKind.Number, Min = 1, Max = 3600, Default = 60 },
        new() { Key = "idle-seconds", Label = "Boşta kalınca dur (saniye)", Kind = SettingKind.Number, Min = 10, Max = 3600, Default = 60 },
        new() { Key = "image-format", Label = "Görsel biçimi", Kind = SettingKind.Choice, Default = "webp", Options = ["webp", "png"] },
        new() { Key = "image-quality", Label = "WebP kalitesi", Kind = SettingKind.Number, Min = 50, Max = 100, Default = 80 },
        new() { Key = "monitor", Label = "Monitör", Kind = SettingKind.Choice, Default = "cursor", Options = new[] { "cursor" }.Concat(DesktopDisplays.Enumerate().Select(d => d.DeviceName)).ToArray(), OptionLabels = new Dictionary<string,string> { ["cursor"] = "İmlecin bulunduğu ekran" } },
        new() { Key = "exclude", Label = "Dışlanan uygulama/pencere kalıpları", Kind = SettingKind.Text, Default = "", Description = "Noktalı virgülle ayırın; * ve ? desteklenir. Örnek: *KeePass*;*özel pencere*" },
        new() { Key = "retention-days", Label = "Saklama süresi (gün; 0 = silme yok)", Kind = SettingKind.Number, Min = 0, Max = 3650, Default = 0 },
        new() { Key = "limit-gb", Label = "Depolama sınırı (GB; 0 = sınırsız)", Kind = SettingKind.Number, Min = 0, Max = 2000, Default = 0 },
        new() { Key = "pause", Label = "Kaydı duraklat / sürdür", Kind = SettingKind.Action, Execute = TogglePause }
    ];
    public override void Activate()
    {
        if (IsActive) return;
        store = Store; lifetime = new(); usage = new(store.AddUsage); previous = ""; lastCapture = 0;
        paused = Context.Settings.GetBool("paused");
        SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
        timer.Tick += Tick; IsActive = true; timer.Start();
        foregroundObserver = new(WindowEventObserver.Foreground, WindowEventObserver.Foreground, _ =>
        {
            if (!IsActive) return;
            var info = CaptureServices.Foreground(); usage?.Observe(ActiveApplication(info), DateTimeOffset.UtcNow, Environment.TickCount64);
            Tick(this, EventArgs.Empty);
        }, error => Context.Log.Error("Etkin pencere takibi", error));
        var currentStore = store; var token = lifetime.Token; ocr = Task.Run(() => OcrLoopAsync(currentStore, token));
    }
    public override void Deactivate()
    {
        timer.Stop(); timer.Tick -= Tick; SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
        foregroundObserver?.Dispose(); foregroundObserver = null;
        usage?.Observe(null, DateTimeOffset.UtcNow, Environment.TickCount64); lifetime?.Cancel();
        lock (recordingGate) { }
        try { ocr?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Context.Log.Error("OCR kuyruğu durdurulurken hata", error); }
        lifetime?.Dispose(); lifetime = null; store = null; usage = null; base.Deactivate();
    }
    private void TogglePause() { paused = !paused; Context.Settings.Set("paused", paused); if (paused) usage?.Observe(null, DateTimeOffset.UtcNow, Environment.TickCount64); Context.Notify(paused ? "Ekran geçmişi duraklatıldı." : "Ekran geçmişi sürdürülüyor."); }
    private string? ActiveApplication(ForegroundInfo foreground) =>
        paused || locked || suspended || !CaptureServices.IsDefaultInputDesktop ||
        CaptureServices.IdleSeconds >= Context.Settings.GetInt("idle-seconds", 60) ||
        CaptureServices.Excluded(foreground, Context.Settings.GetString("exclude")) || foreground.Process == Environment.ProcessId ? null : foreground.App;
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        _ = timer.Dispatcher.InvokeAsync(() =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect) locked = true;
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect) locked = false;
            if (locked) usage?.Observe(null, DateTimeOffset.UtcNow, Environment.TickCount64);
        });
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        _ = timer.Dispatcher.InvokeAsync(() =>
        {
            if (e.Mode == PowerModes.Suspend) suspended = true;
            else if (e.Mode == PowerModes.Resume) suspended = false;
            if (suspended) usage?.Observe(null, DateTimeOffset.UtcNow, Environment.TickCount64);
        });
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (busy || store is null || lifetime is null) return;
        var currentStore = store; var token = lifetime.Token; var now = DateTimeOffset.UtcNow; var tick = Environment.TickCount64;
        try
        {
            var foreground = CaptureServices.Foreground();
            var activeApp = ActiveApplication(foreground);
            usage?.Observe(activeApp, now, tick);
            if (activeApp is null) return;
            usage?.Flush(now, tick);
            var identity = foreground.Window + "|" + foreground.Title;
            var elapsed = tick - lastCapture;
            var interval = Math.Clamp(Context.Settings.GetInt("interval", 60), 1, 3600) * 1000L;
            if (elapsed < Math.Min(2000, interval) || (identity == previous && elapsed < interval)) return;
            busy = true;
            if (!CaptureServices.HasSpace(currentStore.DirectoryPath)) { paused = true; Context.Notify("Disk alanı az; ekran geçmişi durduruldu. Eski kayıtlar korunuyor."); return; }
            var image = CaptureServices.Capture(CaptureServices.Monitor(Context.Settings.GetString("monitor", "cursor")));
            var webp = Context.Settings.GetString("image-format", "webp") == "webp";
            var quality = Context.Settings.GetInt("image-quality", 80);
            await Task.Run(() =>
            {
                lock (recordingGate)
                {
                    token.ThrowIfCancellationRequested(); var media = CaptureServices.Save(currentStore, image, webp, quality); token.ThrowIfCancellationRequested();
                    currentStore.Add("recall", now, foreground.App, foreground.Title, media: media, ocrState: "pending");
                }
            }, token);
            if (token.IsCancellationRequested) return;
            previous = identity; lastCapture = tick;
            var days = Context.Settings.GetInt("retention-days"); if (days > 0) currentStore.Delete("recall", until: now.AddDays(-days));
            var gigabytes = Context.Settings.GetInt("limit-gb");
            if (gigabytes > 0 && new DirectoryInfo(currentStore.MediaDirectory).EnumerateFiles().Sum(f => f.Length) > gigabytes * 1024L * 1024 * 1024)
            { paused = true; Context.Notify("Depolama sınırına ulaşıldı. Eski kayıtları temizleyin veya sınırı artırın."); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { paused = true; Report(error); }
        finally { busy = false; }
    }
    private async Task OcrLoopAsync(HistoryStore currentStore, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                foreach (var row in currentStore.PendingOcr())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var text = await OcrRecognitionService.RecognizeAsync(CaptureServices.Load(currentStore.ResolveMedia(row.Media)), token);
                        token.ThrowIfCancellationRequested(); currentStore.CompleteOcr(row.Id, text);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { currentStore.CompleteOcr(row.Id, "", true); Context.Log.Error("Ekran geçmişi OCR", error); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { Context.Log.Error("OCR kuyruğu yeniden denenecek", error); }
            await Task.Delay(1000, token);
        }
    }
    public override IEnumerable<QuickAction> GetQuickActions(string query) => base.GetQuickActions(query).Append(new(Id + ".pause", "Ekran geçmişini duraklat / sürdür", "recall kayıt", TogglePause));
}
