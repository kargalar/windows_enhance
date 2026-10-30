using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace WinForge.ClipboardLine;

internal sealed record LinkDetails(string Title, string Author, Uri? Thumbnail);

/// <summary>Bounded HTTPS metadata retrieval, with DNS pinning and no private-network access.</summary>
internal static partial class LinkMetadata
{
    private const int MaximumPageBytes = 512 * 1024;
    private const int MaximumImageBytes = 5 * 1024 * 1024;
    private const int MaximumRedirects = 3;
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.Ordinal)
    {
        "token", "accesstoken", "refreshtoken", "idtoken", "apikey", "auth", "authorization", "authentication", "authtoken", "code", "signature", "sig", "session", "sessionid", "state", "password", "secret", "clientsecret", "credential", "credentials", "accesskeyid", "key", "keypairid", "policy", "verifier", "jwt", "bearer"
    };

    internal static bool IsSafeUrl(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || uri.IsLoopback) return false;
        var host = uri.DnsSafeHost.TrimEnd('.').ToLowerInvariant();
        if (host == "localhost" || new[] { ".local", ".internal", ".home", ".localhost", ".test", ".invalid" }.Any(host.EndsWith)) return false;
        if (IPAddress.TryParse(host, out var address) && !IsPublicAddress(address)) return false;
        foreach (var entry in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(entry.Split('=', 2)[0]);
            var normalized = string.Concat(name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit));
            if (SensitiveKeys.Contains(normalized) || normalized.StartsWith("xamz", StringComparison.Ordinal) || normalized.StartsWith("xgoog", StringComparison.Ordinal) || normalized.StartsWith("oauth", StringComparison.Ordinal)) return false;
        }
        return true;
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            var a = bytes[0]; var b = bytes[1]; var c = bytes[2];
            return !(a is 0 or 10 or 127 || a >= 224 || a == 100 && b is >= 64 and <= 127 || a == 169 && b == 254 || a == 172 && b is >= 16 and <= 31 ||
                a == 192 && (b is 0 or 168 || b == 88 && c == 99) || a == 198 && (b is 18 or 19 || b == 51 && c == 100) || a == 203 && b == 0 && c == 113);
        }
        if (bytes.Length != 16) return false;
        var first = (bytes[0] << 8) | bytes[1]; var second = (bytes[2] << 8) | bytes[3];
        return (first & 0xe000) == 0x2000 && !(first == 0x2001 && ((second & 0xfe00) == 0 || second == 0x0db8)) && first != 0x2002 && !(first == 0x3fff && (second & 0xf000) == 0);
    }

    internal static async Task<LinkDetails> ReadAsync(Uri uri, CancellationToken token)
    {
        var (content, finalUri) = await FetchAsync(uri, MaximumPageBytes, token);
        return Parse(Encoding.UTF8.GetString(content), finalUri);
    }

    internal static async Task<byte[]> ReadThumbnailAsync(Uri uri, CancellationToken token) => (await FetchAsync(uri, MaximumImageBytes, token)).Bytes;

    internal static LinkDetails Parse(string html, Uri baseUri)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MetaTag().Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in Attributes().Matches(match.Value)) attributes.TryAdd(attribute.Groups[1].Value, WebUtility.HtmlDecode(attribute.Groups[3].Value));
            var name = attributes.GetValueOrDefault("property") ?? attributes.GetValueOrDefault("name");
            if (name is not null && attributes.TryGetValue("content", out var value) && !values.ContainsKey(name)) values[name] = value;
        }
        var title = values.GetValueOrDefault("og:title") ?? WebUtility.HtmlDecode(Title().Match(html).Groups[1].Value);
        var author = values.GetValueOrDefault("author") ?? values.GetValueOrDefault("article:author") ?? "";
        var image = values.GetValueOrDefault("og:image");
        Uri? thumbnail = null;
        if (!string.IsNullOrWhiteSpace(image) && Uri.TryCreate(baseUri, image, out var candidate) && IsSafeUrl(candidate)) thumbnail = candidate;
        static string Clean(string value) { var cleaned = string.Concat(value.Where(character => !char.IsControl(character))).Trim(); return cleaned[..Math.Min(500, cleaned.Length)]; }
        return new LinkDetails(Clean(title), Clean(author), thumbnail);
    }

    private static async Task<(byte[] Bytes, Uri Uri)> FetchAsync(Uri uri, int maximum, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectCallback = ConnectAsync };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinForge/1.0");
        for (var attempt = 0; attempt <= MaximumRedirects; attempt++)
        {
            if (!IsSafeUrl(uri)) throw new InvalidDataException("Otomatik bağlantı bilgisi yalnızca güvenli genel HTTPS adreslerinden alınabilir.");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Bağlantı yanıtı çok büyük.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream(); var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token); if (read == 0) break;
                if (output.Length + read > maximum) throw new InvalidDataException("Bağlantı yanıtı çok büyük.");
                output.Write(buffer, 0, read);
            }
            return (output.ToArray(), uri);
        }
        throw new InvalidDataException("Bağlantı çok fazla yönlendirme içeriyor.");
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address))) throw new InvalidDataException("Özel ağ adreslerine otomatik bağlantı engellendi.");
        var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try { await socket.ConnectAsync(addresses[0], context.DnsEndPoint.Port, token); return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }

    [GeneratedRegex("<meta\\b[^>]{0,8192}>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)] private static partial Regex MetaTag();
    [GeneratedRegex("([a-z_:][a-z0-9_:.\\-]*)\\s*=\\s*(['\"])(.*?)\\2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)] private static partial Regex Attributes();
    [GeneratedRegex("<title\\b[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 500)] private static partial Regex Title();
}
