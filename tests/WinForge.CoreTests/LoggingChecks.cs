using System.IO;
using WinForge.Core;

internal static class LoggingChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "async.log");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var logger = new FileLogger(path, asynchronous: true, capacity: 4);
        var observed = 0;
        logger.Written += _ => throw new InvalidOperationException("Fixture observer failure");
        logger.Written += _ =>
        {
            if (Interlocked.Increment(ref observed) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
        };
        try
        {
            logger.Info("first");
            check(entered.Wait(TimeSpan.FromSeconds(5)), "The background logger reaches the writer without a caller flush");
            var produce = Task.Run(() => { for (var i = 0; i < 100; i++) logger.Info("burst-" + i); });
            check(produce.Wait(TimeSpan.FromSeconds(1)), "A stalled writer cannot block log producers");
            check(logger.DroppedMessages == 96, "A saturated queue counts rejected messages and stays within its configured capacity");
            check(!logger.FlushAsync().IsCompleted, "Flush waits for accepted writes and observers to finish");
        }
        finally { release.Set(); logger.Dispose(); }
        check(File.ReadAllLines(path).Length == 5 && observed == 5, "Dispose drains accepted messages and a failing observer cannot suppress other observers");
        var bytes = new FileInfo(path).Length;
        logger.Info("after-dispose");
        check(new FileInfo(path).Length == bytes, "Disposed loggers do not accept new writes");

        path = Path.Combine(root, "rotation.log");
        using (var rotating = new FileLogger(path, maximumFileBytes: 1024, retainedFiles: 2))
            for (var i = 0; i < 100; i++) rotating.Info($"row{i:000} " + new string('İ', 60));
        var files = Directory.GetFiles(root, "rotation.log*");
        check(files.Length == 3 && files.All(file => new FileInfo(file).Length <= 1024), "Rotation bounds the active UTF-8 file and retained archives by bytes");
        check(File.ReadAllText(path).Contains("row099"), "Rotation retains the newest log entries");

        path = Directory.CreateDirectory(Path.Combine(root, "log-error")).FullName;
        using (var failed = new FileLogger(path, asynchronous: true))
        {
            failed.Info("cannot write to a directory");
            failed.Flush();
            check(failed.FailedMessages == 1 && failed.LastError is not null, "A log disk error is observable without throwing through the caller");
            Directory.Delete(path);
            failed.Info("recovered");
            failed.Flush();
            check(failed.LastError is null && File.ReadAllText(path).Contains("recovered"), "The log writer recovers after a transient filesystem failure");
        }
    }
}
