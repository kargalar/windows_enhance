using System.Net;
using System.Text.RegularExpressions;

namespace WinForge.Modules.Extras;

internal sealed class HostsFileDocument
{
    private readonly List<Line> _lines;
    private readonly string _lineEnding;

    private HostsFileDocument(List<Line> lines, string lineEnding)
    {
        _lines = lines;
        _lineEnding = lineEnding;
    }

    public IReadOnlyList<HostsFileEntry> Entries => _lines
        .Select((line, index) => line.Entry is null ? null : line.Entry with { LineIndex = index })
        .Where(entry => entry is not null)
        .Cast<HostsFileEntry>()
        .ToArray();

    public static HostsFileDocument Parse(string content)
    {
        var endings = Regex.Matches(content, "\\r\\n|\\n|\\r").Select(match => match.Value).ToArray();
        var lineEnding = endings.GroupBy(value => value, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault() ?? Environment.NewLine;
        var lines = new List<Line>();
        var position = 0;
        foreach (Match ending in Regex.Matches(content, "\\r\\n|\\n|\\r"))
        {
            var raw = content[position..ending.Index];
            lines.Add(ParseLine(raw, ending.Value));
            position = ending.Index + ending.Length;
        }
        if (position < content.Length) lines.Add(ParseLine(content[position..], ""));
        return new HostsFileDocument(lines, lineEnding);
    }

    public void Add(HostsFileEntry entry)
        => Add(entry, insertNearHeader: false);

    public void Add(HostsFileEntry entry, bool insertNearHeader)
    {
        Validate(entry);
        EnsureNoDuplicate(entry, null);
        if (!insertNearHeader)
        {
            if (_lines.Count > 0 && _lines[^1].Ending.Length == 0)
                _lines[^1] = _lines[^1] with { Ending = _lineEnding };
            _lines.Add(Format(entry, _lineEnding));
            return;
        }

        var insertionIndex = 0;
        while (insertionIndex < _lines.Count &&
               (string.IsNullOrWhiteSpace(_lines[insertionIndex].Raw) || _lines[insertionIndex].Raw.TrimStart().StartsWith('#')))
            insertionIndex++;
        _lines.Insert(insertionIndex, Format(entry, _lineEnding));
    }

    public void Update(HostsFileEntry entry)
    {
        Validate(entry);
        if (entry.LineIndex < 0 || entry.LineIndex >= _lines.Count || _lines[entry.LineIndex].Entry is null)
            throw new InvalidOperationException("Düzenlenecek hosts girdisi artık bulunmuyor.");
        EnsureNoDuplicate(entry, entry.LineIndex);
        _lines[entry.LineIndex] = Format(entry, _lines[entry.LineIndex].Ending);
    }

    public void Toggle(int lineIndex)
    {
        var current = GetEntry(lineIndex);
        _lines[lineIndex] = Format(current with { IsActive = !current.IsActive }, _lines[lineIndex].Ending);
    }

    public void Remove(int lineIndex)
    {
        _ = GetEntry(lineIndex);
        _lines.RemoveAt(lineIndex);
    }

    public string Render()
    {
        return string.Concat(_lines.Select(line => line.Raw + line.Ending));
    }

    public static void Validate(HostsFileEntry entry)
    {
        if (!IPAddress.TryParse(entry.Address.Trim(), out _))
            throw new ArgumentException("IP adresi IPv4 veya IPv6 biçiminde olmalıdır.");

        var hosts = SplitHostNames(entry.HostNames);
        if (hosts.Length == 0 || hosts.Any(host => !IsValidHostName(host)))
            throw new ArgumentException("En az bir geçerli ana bilgisayar adı girin.");
        if (entry.Comment.ContainsAny('\r', '\n', '\0'))
            throw new ArgumentException("Yorum tek satır olmalı.");
    }

    public static string[] SplitHostNames(string hostNames) => hostNames
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private HostsFileEntry GetEntry(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _lines.Count || _lines[lineIndex].Entry is not { } entry)
            throw new InvalidOperationException("Hosts girdisi artık bulunmuyor.");
        return entry with { LineIndex = lineIndex };
    }

    private void EnsureNoDuplicate(HostsFileEntry candidate, int? exceptLine)
    {
        if (!candidate.IsActive) return;
        var candidateAddress = IPAddress.Parse(candidate.Address.Trim());
        foreach (var existing in Entries)
        {
            if (!existing.IsActive || existing.LineIndex == exceptLine) continue;
            var existingAddress = IPAddress.Parse(existing.Address);
            foreach (var host in SplitHostNames(candidate.HostNames))
            {
                if (!SplitHostNames(existing.HostNames).Contains(host, StringComparer.OrdinalIgnoreCase)) continue;
                var duplicate = candidateAddress.Equals(existingAddress) ||
                                (IPAddress.IsLoopback(candidateAddress) && IPAddress.IsLoopback(existingAddress));
                if (duplicate)
                    throw new ArgumentException($"'{host}' için aynı veya çakışan etkin bir hosts girdisi zaten var.");
            }
        }
    }

    private static bool IsValidHostName(string host)
    {
        if (host.Length > 253 || host.StartsWith('.') || host.EndsWith('.') || host.Contains("..", StringComparison.Ordinal)) return false;
        return host.Split('.').All(label => label.Length is > 0 and <= 63 &&
            label[0] != '-' && label[^1] != '-' && label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }

    private static Line ParseLine(string raw, string ending)
    {
        var trimmed = raw.TrimStart();
        var active = !trimmed.StartsWith('#');
        var body = active ? trimmed : trimmed[1..].TrimStart();
        var commentIndex = body.IndexOf('#');
        var fields = (commentIndex >= 0 ? body[..commentIndex] : body)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < 2 || !IPAddress.TryParse(fields[0], out _)) return new Line(raw, ending, null);

        var comment = commentIndex >= 0 ? body[(commentIndex + 1)..].Trim() : "";
        return new Line(raw, ending, new HostsFileEntry(fields[0], string.Join(' ', fields.Skip(1)), comment, active, -1));
    }

    private static Line Format(HostsFileEntry entry, string ending)
    {
        var address = entry.Address.Trim();
        var names = string.Join(' ', SplitHostNames(entry.HostNames));
        var line = $"{address}\t{names}";
        if (!string.IsNullOrWhiteSpace(entry.Comment)) line += "\t# " + entry.Comment.Trim();
        if (!entry.IsActive) line = "# " + line;
        return new Line(line, ending, entry with { Address = address, HostNames = names, Comment = entry.Comment.Trim() });
    }

    private sealed record Line(string Raw, string Ending, HostsFileEntry? Entry);
}

internal sealed record HostsFileEntry(string Address, string HostNames, string Comment, bool IsActive, int LineIndex);

internal static class HostsFileTextExtensions
{
    public static bool ContainsAny(this string value, params char[] characters) => value.IndexOfAny(characters) >= 0;
}
