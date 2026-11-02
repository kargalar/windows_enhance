using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinForge.Core;
using WinForge.History;
using WinForge.Modules.Capture;

internal static class HistoryRefreshChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var store = new HistoryStore(Path.Combine(root, "refresh"));
            var ui = new TestUi();
            var requests = new List<HistoryFilter>();
            var first = new TaskCompletionSource<IReadOnlyList<HistoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var last = new TaskCompletionSource<IReadOnlyList<HistoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new HistoryWindow(store, "recall", "Test", ui, filter =>
            {
                requests.Add(filter);
                return requests.Count == 1 ? first.Task : last.Task;
            });
            ui.Surface.Send("history.filter", new { search = "A" });
            ui.Surface.Send("history.filter", new { search = "B" });
            ui.Surface.Send("history.filter", new { search = "C" });
            ui.Surface.Send("history.page", new { delta = 200 });
            check(requests.Count == 1, "History queries are serialized while newer filters accumulate");
            first.SetResult([Item(1, "obsolete")]);
            PumpUntil(() => requests.Count == 2);
            check(requests[1].Query == "C" && requests[1].Offset == 200, "History coalesces changes into the latest filter and page snapshot");
            check(ui.Surface.Writes == 0, "Obsolete query results are never published");
            last.SetResult([Item(3, "latest")]);
            PumpUntil(() => ui.Surface.Writes > 0);
            check(ui.Surface.State.GetProperty("rows")[0].GetProperty("id").GetInt64() == 3,
                "Only the latest query result reaches history rows");
            check(ui.Surface.State.GetProperty("status").GetString()!.StartsWith("201–201"),
                "History status uses the same page snapshot as the result");
            window.Close();

            ui = new TestUi();
            first = new(TaskCreationOptions.RunContinuationsAsynchronously);
            window = new HistoryWindow(store, "recall", "Test", ui, _ => first.Task);
            ui.Surface.Send("history.filter", new { search = "close" });
            window.Close();
            first.SetResult([Item(4, "closed")]);
            PumpUntil(() => first.Task.IsCompleted);
            PumpOnce();
            check(ui.Surface.Disposed && ui.Surface.Writes == 0, "Closing history rejects in-flight query results and disposes its surface");

            ui = new TestUi();
            first = new(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Clear();
            window = new HistoryWindow(store, "recall", "Test", ui, filter =>
            {
                requests.Add(filter);
                return requests.Count == 1 ? first.Task : Task.FromResult<IReadOnlyList<HistoryItem>>([Item(5, "recovered")]);
            });
            ui.Surface.Send("history.filter", new { search = "failing" });
            ui.Surface.Send("history.filter", new { search = "recovery" });
            first.SetException(new IOException("obsolete failure"));
            PumpUntil(() => ui.Surface.Writes > 0);
            check(ui.Surface.State.GetProperty("rows")[0].GetProperty("id").GetInt64() == 5,
                "An obsolete query failure cannot discard the latest pending filter");
            window.Close();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static HistoryItem Item(long id, string text) => new(id, "recall", DateTimeOffset.UtcNow, "test", text, text, "", 0, false, "", "done", "", "");
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(5)) PumpOnce();
        if (!done()) throw new TimeoutException("History query did not complete.");
    }
    private static void PumpOnce()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private sealed class TestUi : IFeatureUiService
    {
        public TestSurface Surface { get; } = new();
        public IFeatureUiSurface CreateSurface(string view) => Surface;
        public IFeatureUiWindow Open(string key, string view, string title, FeatureUiWindowOptions options) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel) => throw new NotSupportedException();
        public Task ShowMessageAsync(string title, string message, string closeLabel) => throw new NotSupportedException();
    }
    private sealed class TestSurface : IFeatureUiSurface
    {
        public FrameworkElement Element { get; } = new Border();
        public event Action<string, JsonElement>? MessageReceived;
        public JsonElement State { get; private set; }
        public int Writes { get; private set; }
        public bool Disposed { get; private set; }
        public void Send(string command, object payload) => MessageReceived?.Invoke(command, JsonSerializer.SerializeToElement(payload));
        public void SetState(object state)
        {
            if (Disposed) throw new InvalidOperationException("State sent to a closed history surface.");
            State = JsonSerializer.SerializeToElement(state);
            Writes++;
        }
        public void Dispose() => Disposed = true;
    }
}
