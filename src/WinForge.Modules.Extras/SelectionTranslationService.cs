using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinForge.Modules.Extras;

internal sealed class SelectionTranslationService(HttpClient client)
{
    // Keep each escaped request comfortably below common URL limits. Bound the
    // total to avoid accidentally uploading an entire document or freezing the UI.
    internal const int MaxInputBytes = 30000;
    internal const int ChunkBytes = 2500;
    internal const int MyMemoryChunkBytes = 500;
    internal const int MaxResponseBytes = 1024 * 1024;

    internal static string Language(string value, bool allowAuto = false)
    {
        value = value.Trim();
        if (allowAuto && value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        if (value.Length > 35 || !Regex.IsMatch(value, "^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})*$"))
            throw new FormatException("Geçerli bir dil kodu girin (ör. en veya tr).");
        return value;
    }

    internal static Uri LibreEndpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new FormatException("LibreTranslate adresi HTTPS olmalı; yalnızca yerel sunucuda HTTP kullanılabilir. Adreste kullanıcı bilgisi, sorgu veya parça bulunmamalı.");
        var builder = new UriBuilder(uri);
        var path = builder.Path.TrimEnd('/');
        builder.Path = path.EndsWith("/translate", StringComparison.OrdinalIgnoreCase) ? path : path + "/translate";
        return builder.Uri;
    }

    internal async Task<string> TranslateAsync(string input, string source, string target, CancellationToken token,
        string provider = "google", string endpoint = "", string apiKey = "")
    {
        source = Language(source, true);
        target = Language(target);
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Kaynak ve hedef dil farklı olmalı.");
        if (string.IsNullOrWhiteSpace(input)) throw new InvalidOperationException("Seçili metin boş.");
        if (Encoding.UTF8.GetByteCount(input) > MaxInputBytes) throw new InvalidOperationException("Seçim 30.000 UTF-8 baytını aşıyor; daha kısa bir metin seçin.");
        if (provider is not ("google" or "libretranslate" or "mymemory")) throw new FormatException("Bilinmeyen çeviri sağlayıcısı.");
        var libreUri = provider == "libretranslate" ? LibreEndpoint(endpoint) : null;
        var result = new StringBuilder();
        foreach (var (text, whitespace) in Split(input, provider == "mymemory" ? MyMemoryChunkBytes : ChunkBytes, provider == "mymemory"))
        {
            token.ThrowIfCancellationRequested();
            if (text.Length != 0)
            {
                using var request = provider switch
                {
                    "libretranslate" => new HttpRequestMessage(HttpMethod.Post, libreUri) { Content = JsonContent.Create(new { q = text, source, target, format = "text", api_key = apiKey }) },
                    "mymemory" => new HttpRequestMessage(HttpMethod.Get, "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) + "&langpair=" + Uri.EscapeDataString((source == "auto" ? "autodetect" : source) + "|" + target)),
                    _ => new HttpRequestMessage(HttpMethod.Get, "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&sl=" + Uri.EscapeDataString(source) + "&tl=" + Uri.EscapeDataString(target) + "&q=" + Uri.EscapeDataString(text))
                };
                // ResponseHeadersRead avoids buffering an unbounded server body.
                // Keep an explicit deadline through the body read as HttpClient's
                // timeout otherwise ends when the headers arrive.
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (client.Timeout != Timeout.InfiniteTimeSpan) deadline.CancelAfter(client.Timeout);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var message = response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.TooManyRequests => "Çeviri hizmetinin istek sınırına ulaşıldı. Daha sonra tekrar deneyin veya ayarlardan başka bir sağlayıcı seçin.",
                        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden when libreUri is not null => "LibreTranslate erişimi reddetti. Sunucu adresini ve gerekiyorsa API anahtarını kontrol edin.",
                        _ => $"Çeviri hizmeti isteği tamamlayamadı (HTTP {(int)response.StatusCode})."
                    };
                    throw new InvalidOperationException(message);
                }
                using var responseBody = await ReadBoundedResponseAsync(response.Content, deadline.Token);
                var responseBytes = responseBody.GetBuffer().AsMemory(0, checked((int)responseBody.Length));
                if (responseBytes.Span.StartsWith("\uFEFF"u8)) responseBytes = responseBytes[3..];
                using var json = JsonDocument.Parse(responseBytes);
                var root = json.RootElement;
                if (provider == "mymemory")
                {
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("responseStatus", out var status) ||
                        status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var statusCode) || statusCode != 200 ||
                        root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
                        throw new InvalidOperationException("MyMemory çeviriyi tamamlayamadı; dil çiftini ve hizmet kotasını kontrol edin.");
                    if (!root.TryGetProperty("responseData", out var responseData) || responseData.ValueKind != JsonValueKind.Object ||
                        !responseData.TryGetProperty("translatedText", out var translatedText) || translatedText.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(translatedText.GetString()))
                        throw new InvalidOperationException("MyMemory geçerli bir çeviri döndürmedi.");
                    var translated = System.Net.WebUtility.HtmlDecode(translatedText.GetString()) ?? "";
                    // Some service responses put quota warnings in the text
                    // field, even when their HTTP response was successful.
                    if (translated.TrimStart().StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("MyMemory çeviriyi tamamlayamadı; hizmet kotasını kontrol edin.");
                    result.Append(translated);
                    result.Append(whitespace);
                    continue;
                }
                if (libreUri is not null)
                {
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("translatedText", out var translated) ||
                        translated.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(translated.GetString()))
                        throw new InvalidOperationException("LibreTranslate geçerli bir çeviri döndürmedi.");
                    result.Append(translated.GetString());
                    result.Append(whitespace);
                    continue;
                }
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("Çeviri hizmeti beklenmeyen bir yanıt döndürdü.");
                var part = new StringBuilder();
                foreach (var segment in root[0].EnumerateArray())
                    if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                        part.Append(segment[0].GetString());
                if (part.Length == 0) throw new InvalidOperationException("Çeviri boş döndü.");
                result.Append(part);
            }
            result.Append(whitespace);
        }
        return result.ToString();
    }

    private static async Task<MemoryStream> ReadBoundedResponseAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidOperationException("Çeviri hizmetinin yanıtı çok büyük.");
        var body = new MemoryStream();
        try
        {
            await using var input = await content.ReadAsStreamAsync(token);
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), token);
                if (read == 0) break;
                if (body.Length + read > MaxResponseBytes) throw new InvalidOperationException("Çeviri hizmetinin yanıtı çok büyük.");
                body.Write(buffer, 0, read);
            }
            return body;
        }
        catch { body.Dispose(); throw; }
    }

    internal static IEnumerable<(string Text, string Whitespace)> Split(string input, int chunkBytes = ChunkBytes, bool separateLines = false)
    {
        if (chunkBytes < 4) throw new ArgumentOutOfRangeException(nameof(chunkBytes));
        var position = 0;
        while (position < input.Length)
        {
            if (char.IsWhiteSpace(input[position]))
            {
                var whitespaceStart = position;
                while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
                yield return ("", input[whitespaceStart..position]);
                continue;
            }
            var start = position;
            var bytes = 0;
            var lastWhitespace = -1;
            while (position < input.Length)
            {
                if (separateLines && (input[position] == '\r' || input[position] == '\n')) break;
                var length = char.IsHighSurrogate(input[position]) && position + 1 < input.Length && char.IsLowSurrogate(input[position + 1]) ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(input.AsSpan(position, length));
                if (bytes + size > chunkBytes) break;
                bytes += size;
                if (char.IsWhiteSpace(input[position])) lastWhitespace = position;
                position += length;
            }
            if (position < input.Length && lastWhitespace > start) position = lastWhitespace;
            var end = position;
            while (end > start && char.IsWhiteSpace(input[end - 1])) end--;
            position = end;
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            yield return (input[start..end], input[end..position]);
        }
    }
}
