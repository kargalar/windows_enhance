using System.Windows.Threading;

namespace WinForge.Core;

/// <summary>Coalesces text edits and flushes pending changes when an editor closes.</summary>
public sealed class AutomaticSettingsSave : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Action _save;
    private bool _pending;
    public AutomaticSettingsSave(Action save) { _save = save; _timer.Tick += OnTick; }
    private void OnTick(object? sender, EventArgs args) => Flush();
    public void Schedule() { _pending = true; _timer.Stop(); _timer.Start(); }
    public void Flush() { _timer.Stop(); if (!_pending) return; _pending = false; _save(); }
    public void Dispose() { Flush(); _timer.Tick -= OnTick; }
}
