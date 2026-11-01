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
    private UsageTracker? idleUsage;
    private ContextUsageTracker? contextUsage;
    private WindowEventObserver? foregroundObserver;
    private IDisposable? pauseHotkey;
    private CancellationTokenSource? lifetime;
    private Task? ocr;
    private bool locked, suspended, busy, paused;
    private string previous = "";
    private long lastCapture;
    private long lastContextRead;
    private nint contextWindow, contextReadWindow;
    private ActivityContext? currentContext;
    private Task<ActivityContext?>? contextRead;
    public override string Id => "screen-history";
    public override string Name => "Ekran Geçmişi";
    public override string Description => "Yerel ekran geçmişi, OCR araması, uygulama süreleri ve RecallClone kayıt aktarımı.";
    protected override string Kind => "recall";
    protected override IEnumerable<SettingDescriptor> ModuleSettings =>
    [
        new() { Key = "storage-directory", Label = "Görseller ve geçmiş klasörü", Kind = SettingKind.Text, Default = "",
            Description = "Görseller seçilen klasörün media alt klasörüne kaydedilir. Boş: varsayılan konum. Eski kayıtlar önceki klasörde korunur." },
        new() { Key = "choose-storage", Label = "Görseller ve geçmiş için klasör seç", Kind = SettingKind.Action, Execute = () =>
        {
            var picker = new OpenFolderDialog { Title = "Ekran geçmişi klasörünü seç", InitialDirectory = Store.DirectoryPath };
            if (picker.ShowDialog() != true) return;
            _ = new HistoryStore(picker.FolderName);
            Context.Settings.Set("storage-directory", picker.FolderName);
            Apply();
        } },
        new() { Key = "interval", Label = "Kayıt aralığı (saniye)", Kind = SettingKind.Number, Min = 1, Max = 3600, Default = 60 },
        new() { Key = "idle-seconds", Label = "Boşta kalınca dur (saniye)", Kind = SettingKind.Number, Min = 10, Max = 3600, Default = 60 },
        new() { Key = "image-format", Label = "Görsel biçimi", Kind = SettingKind.Choice, Default = "webp", Options = ["webp", "png"] },
        new() { Key = "image-quality", Label = "WebP kalitesi", Kind = SettingKind.Number, Min = 50, Max = 100, Default = 80 },
        new() { Key = "monitor", Label = "Monitör", Kind = SettingKind.Choice, Default = "cursor", Options = new[] { "cursor" }.Concat(DesktopDisplays.Enumerate().Select(d => d.DeviceName)).ToArray(), OptionLabels = new Dictionary<string,string> { ["cursor"] = "İmlecin bulunduğu ekran" } },
        new() { Key = "exclude", Label = "Dışlanan uygulama/pencere kalıpları", Kind = SettingKind.Text, Default = "", Description = "Noktalı virgülle ayırın; * ve ? desteklenir. Örnek: *KeePass*;*özel pencere*" },
        new() { Key = "track-context", Label = "Web sitesi ve klasör konumlarını kaydet", Default = true, Description = "Yalnız ön plandaki tarayıcının adres çubuğu veya Dosya Gezgini yolu okunur; arama sorgusu ve URL sorgu parametreleri saklanmaz." },
        new() { Key = "retention-days", Label = "Saklama süresi (gün; 0 = silme yok)", Kind = SettingKind.Number, Min = 0, Max = 3650, Default = 0 },
        new() { Key = "limit-gb", Label = "Depolama sınırı (GB; 0 = sınırsız)", Kind = SettingKind.Number, Min = 0, Max = 2000, Default = 0 },
        new() { Key = "pause-hotkey", Label = "Duraklatma kısayolu (isteğe bağlı)", Kind = SettingKind.Text, Default = "",
            Description = "Dolu olduğunda bu kısayol ekran geçmişi kaydını duraklatır veya sürdürür. Boş bırakarak devre dışı bırakın." },
        new() { Key = "pause", Label = "Kaydı duraklat / sürdür", Kind = SettingKind.Action, Execute = TogglePause }
    ];
    public override void Activate()
    {
        if (IsActive) return;
        store = Store; lifetime = new(); usage = new(store.AddUsage); idleUsage = new(store.AddUsage);
        contextUsage = new(store.AddContextUsage); previous = ""; lastCapture = 0;
        contextWindow = contextReadWindow = 0; lastContextRead = 0; currentContext = null; contextRead = null;
        paused = Context.Settings.GetBool("paused");
        IsActive = true;
        RegisterPauseHotkey();
        SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
        timer.Tick += Tick; timer.Start();
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
        IsActive = false;
        pauseHotkey?.Dispose(); pauseHotkey = null;
        timer.Stop(); timer.Tick -= Tick; SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
        foregroundObserver?.Dispose(); foregroundObserver = null;
        var now = DateTimeOffset.UtcNow; var tick = Environment.TickCount64;
        usage?.Observe(null, now, tick); idleUsage?.Observe(null, now, tick); lifetime?.Cancel();
        contextUsage?.Observe(null, null, null, now, tick);
        var stoppedOcr = ocr; var stoppedLifetime = lifetime;
        ocr = null; lifetime = null; store = null; usage = null; idleUsage = null; contextUsage = null; contextRead = null; base.Deactivate();
        _ = FinishOcrAsync(stoppedOcr, stoppedLifetime);
    }
    public override void Apply()
    {
        if (IsActive && store is not null && !string.Equals(store.DirectoryPath, Store.DirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            Deactivate(); Activate(); return;
        }
        if (IsActive) RegisterPauseHotkey();
    }
    private void RegisterPauseHotkey()
    {
        pauseHotkey?.Dispose(); pauseHotkey = null;
        var configured = Context.Settings.GetString("pause-hotkey").Trim();
        if (configured.Length == 0) return;
        try
        {
            var (modifiers, key) = ModuleShortcut.Parse(configured);
            pauseHotkey = Context.Hotkeys.Register(modifiers, key, TogglePause);
        }
        catch (Exception error) { Report(new InvalidOperationException("Ekran geçmişi duraklatma kısayolu kaydedilemedi.", error)); }
    }
    private async Task FinishOcrAsync(Task? stoppedOcr, CancellationTokenSource? stoppedLifetime)
    {
        try { if (stoppedOcr is not null) await stoppedOcr.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Context.Log.Error("OCR kuyruğu durdurulurken hata", error); }
        finally { stoppedLifetime?.Dispose(); }
    }
    private void TogglePause()
    {
        paused = !paused; Context.Settings.Set("paused", paused);
        if (paused)
        {
            var now = DateTimeOffset.UtcNow; var tick = Environment.TickCount64;
            usage?.Observe(null, now, tick); idleUsage?.Observe(null, now, tick); contextUsage?.Observe(null, null, null, now, tick);
        }
        Context.Notify(paused ? "Ekran geçmişi duraklatıldı." : "Ekran geçmişi sürdürülüyor.");
    }
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
            if (locked)
            {
                var now = DateTimeOffset.UtcNow; var tick = Environment.TickCount64;
                usage?.Observe(null, now, tick); idleUsage?.Observe(null, now, tick); contextUsage?.Observe(null, null, null, now, tick);
            }
        });
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        _ = timer.Dispatcher.InvokeAsync(() =>
        {
            if (e.Mode == PowerModes.Suspend) suspended = true;
            else if (e.Mode == PowerModes.Resume) suspended = false;
            if (suspended)
            {
                var now = DateTimeOffset.UtcNow; var tick = Environment.TickCount64;
                usage?.Observe(null, now, tick); idleUsage?.Observe(null, now, tick); contextUsage?.Observe(null, null, null, now, tick);
            }
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
            var idleThreshold = Math.Clamp(Context.Settings.GetInt("idle-seconds", 60), 10, 3600);
            var eligibleForIdle = !paused && !locked && !suspended && CaptureServices.IsDefaultInputDesktop &&
                foreground.Process != Environment.ProcessId && !CaptureServices.Excluded(foreground, Context.Settings.GetString("exclude"));
            var idleSeconds = CaptureServices.IdleSeconds;
            if (eligibleForIdle && idleSeconds >= idleThreshold)
            {
                var idleStartOffset = Math.Max(0, idleSeconds - idleThreshold);
                idleUsage?.Observe("System Idle", now.AddSeconds(-idleStartOffset), tick - (long)(idleStartOffset * 1000));
            }
            else idleUsage?.Observe(null, now, tick);
            if (activeApp is null)
            {
                contextUsage?.Observe(null, null, null, now, tick); currentContext = null; contextWindow = 0; contextRead = null;
                return;
            }
            usage?.Flush(now, tick);
            UpdateContext(foreground, activeApp, now, tick);
            var identity = foreground.Window + "|" + foreground.Title;
            var elapsed = tick - lastCapture;
            var interval = Math.Clamp(Context.Settings.GetInt("interval", 60), 1, 3600) * 1000L;
            if (elapsed < Math.Min(2000, interval) || (identity == previous && elapsed < interval)) return;
            busy = true;
            if (!CaptureServices.HasSpace(currentStore.DirectoryPath)) { paused = true; Context.Notify("Disk alanı az; ekran geçmişi durduruldu. Eski kayıtlar korunuyor."); return; }
            var area = CaptureServices.Monitor(Context.Settings.GetString("monitor", "cursor"));
            Native.GetCursorPos(out var cursor);
            var cursorMetadata = cursor.X >= area.X && cursor.X < area.X + area.Width && cursor.Y >= area.Y && cursor.Y < area.Y + area.Height
                ? System.Text.Json.JsonSerializer.Serialize(new { cursorX = cursor.X - area.X, cursorY = cursor.Y - area.Y }) : "";
            var image = CaptureServices.Capture(area);
            var webp = Context.Settings.GetString("image-format", "webp") == "webp";
            var quality = Context.Settings.GetInt("image-quality", 80);
            await Task.Run(() =>
            {
                lock (recordingGate)
                {
                    token.ThrowIfCancellationRequested(); var media = CaptureServices.Save(currentStore, image, webp, quality); token.ThrowIfCancellationRequested();
                    var icon = CaptureServices.SaveProcessIcon(currentStore, foreground.Process);
                    currentStore.Add("recall", now, foreground.App, foreground.Title, media: media, ocrState: "pending", icon: icon, metadata: cursorMetadata);
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
    private void UpdateContext(ForegroundInfo foreground, string activeApp, DateTimeOffset now, long tick)
    {
        if (!Context.Settings.GetBool("track-context", true))
        {
            currentContext = null; contextWindow = foreground.Window;
            contextUsage?.Observe(activeApp, null, null, now, tick); return;
        }
        if (contextWindow != foreground.Window)
        {
            currentContext = null; contextWindow = foreground.Window; contextRead = null; lastContextRead = 0;
        }
        if (contextRead is { IsCompleted: true } completed)
        {
            if (contextReadWindow == foreground.Window)
            {
                try { currentContext = completed.GetAwaiter().GetResult(); }
                catch (Exception error) { Context.Log.Error("Web sitesi veya klasör okunamadı", error); currentContext = null; }
            }
            contextRead = null;
        }
        if (contextRead is null && tick - lastContextRead >= 3000)
        {
            lastContextRead = tick; contextReadWindow = foreground.Window;
            var token = lifetime?.Token ?? CancellationToken.None;
            contextRead = Task.Run(() => ActivityContextReader.Read(foreground, token), token);
        }
        contextUsage?.Observe(activeApp, currentContext?.Type, currentContext?.Value, now, tick);
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
