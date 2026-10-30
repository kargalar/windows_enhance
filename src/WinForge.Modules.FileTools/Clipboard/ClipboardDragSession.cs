namespace WinForge.ClipboardLine;

/// <summary>Binds internal folder drops to the exact active drag without exporting item identities.</summary>
internal sealed class ClipboardDragSession
{
    internal const string Format = "WinForge.ClipboardLine.InternalDrag";
    private string? _token;
    private Guid[] _items = [];

    internal string Begin(IEnumerable<Guid> ids)
    {
        var items = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (items.Length is 0 or > 10_000) throw new ArgumentException("Sürükleme seçimi geçersiz.");
        _items = items; _token = Guid.NewGuid().ToString("N"); return _token;
    }

    internal IReadOnlyList<Guid> Resolve(object? value) => value is string token && _token is not null && token == _token ? _items : [];
    internal void End() { _token = null; _items = []; }
}
