namespace WinForge.Core;

/// <summary>Shares one diagnostics consumer per shell process across WinForge modules.</summary>
public static class ShellDiagnosticsBroker
{
    private static readonly object Gate = new();
    private static readonly Dictionary<uint, Entry> Entries = [];

    public static Lease Acquire(uint processId, string nativeDirectory)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(processId, out var entry) || !entry.Session.IsConnected)
            {
                var session = Task.Run(() => ShellDiagnosticsSession.ConnectAsync(processId, nativeDirectory)).GetAwaiter().GetResult();
                entry = new Entry(session);
                Entries[processId] = entry;
            }
            entry.References++;
            return new Lease(processId, entry);
        }
    }

    internal sealed class Entry(ShellDiagnosticsSession session)
    {
        internal ShellDiagnosticsSession Session { get; } = session;
        internal Dictionary<Guid, IReadOnlyList<ShellStyleRule>> Rules { get; } = [];
        internal int References { get; set; }
        internal int Apply() => Session.Apply(true, Rules.Values.SelectMany(rules => rules).ToArray());
    }

    public sealed class Lease : IDisposable
    {
        private readonly uint _processId;
        private readonly Entry _entry;
        private readonly Guid _owner = Guid.NewGuid();
        private bool _disposed;

        internal Lease(uint processId, Entry entry) { _processId = processId; _entry = entry; }
        public ShellDiagnosticsSession Session => _entry.Session;

        public int Apply(IReadOnlyList<ShellStyleRule> rules)
        {
            lock (Gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_entry.Session.IsConnected) throw new InvalidOperationException("Windows kabuk bağlantısı kesildi; özellik yeniden bağlanmalı.");
                _entry.Rules[_owner] = rules;
                return _entry.Apply();
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed) return;
                _disposed = true;
                _entry.Rules.Remove(_owner);
                if (--_entry.References == 0)
                {
                    if (Entries.TryGetValue(_processId, out var current) && ReferenceEquals(current, _entry))
                        Entries.Remove(_processId);
                    _entry.Session.Dispose();
                }
                else if (_entry.Session.IsConnected) _entry.Apply();
            }
        }
    }
}
