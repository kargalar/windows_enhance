using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Capture;

internal static class ImageUploadService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static async Task<Uri> UploadAsync(BitmapSource image, string endpoint, CancellationToken token)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
            throw new ArgumentException("Yükleme adresi http:// veya https:// ile başlamalı.", nameof(endpoint));

        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var data = new MemoryStream(); encoder.Save(data);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(data.ToArray()); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "WinForge.png");
        using var response = await Client.PostAsync(address, content, token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(token);
        return ParseResult(body, response.Headers.Location, address);
    }

    internal static Uri ParseResult(string body, Uri? location, Uri endpoint)
    {
        var value = location?.ToString() ?? ExtractUrl(body);
        if (!Uri.TryCreate(value.Trim().Trim('"'), UriKind.RelativeOrAbsolute, out var parsed))
            throw new InvalidDataException("Yükleme sunucusu geçerli bir görsel bağlantısı döndürmedi.");
        var result = parsed.IsAbsoluteUri ? parsed : new Uri(endpoint, parsed);
        if (result.Scheme is not ("http" or "https")) throw new InvalidDataException("Yükleme sunucusu http/https bağlantısı döndürmeli.");
        return result;
    }

    private static string ExtractUrl(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(trimmed);
                foreach (var name in new[] { "url", "link", "direct_url" })
                    if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString()!;
                if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("url", out var nested))
                    return nested.GetString() ?? "";
            }
            catch (JsonException) { }
        }
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return trimmed;
        throw new InvalidDataException("Yükleme yanıtı URL veya url/link alanı içeren JSON olmalı.");
    }
}
