using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Extras;

internal enum AdvancedPasteAction
{
    PlainText,
    Markdown,
    Json,
    ImageToText,
    PasteAsTextFile,
    PasteAsHtmlFile,
    PasteAsPngFile,
    ConvertImageToJpeg,
    ConvertImageToBmp,
    ConvertImageToGif
}

internal sealed record ClipboardTextSnapshot(string Text, string? Html, BitmapSource? Image,
    int JpegQuality = AdvancedPasteArtifacts.DefaultJpegQuality);

internal static class AdvancedPasteTransforms
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Transform(AdvancedPasteAction action, ClipboardTextSnapshot input) => action switch
    {
        AdvancedPasteAction.PlainText => input.Text.Length > 0 ? input.Text : HtmlToText(input.Html ?? ""),
        AdvancedPasteAction.Markdown => input.Html is { Length: > 0 } html ? HtmlToMarkdown(html) : input.Text,
        AdvancedPasteAction.Json => ToJson(input.Text),
        AdvancedPasteAction.PasteAsTextFile => input.Text.Length > 0 ? input.Text : HtmlToText(input.Html ?? ""),
        AdvancedPasteAction.PasteAsHtmlFile => ToHtmlDocument(input.Html, input.Text),
        AdvancedPasteAction.PasteAsPngFile => input.Image is { } image
            ? $"PNG dosyası · {image.PixelWidth} × {image.PixelHeight} piksel"
            : throw new InvalidOperationException("Panoda görsel yok."),
        AdvancedPasteAction.ConvertImageToJpeg => $"{ImageConversionPreview("JPEG", input.Image)} · kalite {Math.Clamp(input.JpegQuality, 10, 100)}%",
        AdvancedPasteAction.ConvertImageToBmp => ImageConversionPreview("BMP", input.Image),
        AdvancedPasteAction.ConvertImageToGif => ImageConversionPreview("GIF", input.Image),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static string ImageConversionPreview(string format, BitmapSource? image) => image is null
        ? throw new InvalidOperationException("Panoda görsel yok.")
        : $"{format} görseli · {image.PixelWidth} × {image.PixelHeight} piksel";

    public static string HtmlToMarkdown(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        html = ExtractHtmlFragment(html);
        html = Regex.Replace(html, @"<(script|style|head)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase, RegexTimeout);
        html = Regex.Replace(html, @"<h([1-6])\b[^>]*>(.*?)</h\1\s*>", match =>
            $"\n{new string('#', int.Parse(match.Groups[1].Value))} {HtmlToText(match.Groups[2].Value)}\n", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        html = Regex.Replace(html, @"<a\b[^>]*href\s*=\s*(['""])(.*?)\1[^>]*>(.*?)</a\s*>", match =>
        {
            var label = HtmlToText(match.Groups[3].Value).Trim();
            var uri = WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
            return uri.Length == 0 ? label : $"[{label}]({uri})";
        }, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        html = Regex.Replace(html, @"<(strong|b)\b[^>]*>(.*?)</\1\s*>", "**$2**", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        html = Regex.Replace(html, @"<(em|i)\b[^>]*>(.*?)</\1\s*>", "*$2*", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        html = Regex.Replace(html, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase, RegexTimeout);
        html = Regex.Replace(html, @"</(p|div|section|article|ul|ol|li|blockquote|pre|tr)\s*>", "\n", RegexOptions.IgnoreCase, RegexTimeout);
        html = Regex.Replace(html, @"<blockquote\b[^>]*>", "\n> ", RegexOptions.IgnoreCase, RegexTimeout);
        html = Regex.Replace(html, @"<[^>]+>", " ", RegexOptions.Singleline, RegexTimeout);
        var result = WebUtility.HtmlDecode(html).Replace('\u00a0', ' ');
        result = Regex.Replace(result, @"[ \t]+\n", "\n", RegexOptions.None, RegexTimeout);
        result = Regex.Replace(result, @"\n[ \t]+", "\n", RegexOptions.None, RegexTimeout);
        result = Regex.Replace(result, @"\n{3,}", "\n\n", RegexOptions.None, RegexTimeout);
        return result.Trim();
    }

    public static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        html = ExtractHtmlFragment(html);
        html = Regex.Replace(html, @"<(script|style|head)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        html = Regex.Replace(html, @"<br\s*/?>|</(p|div|li|h[1-6]|tr)\s*>", "\n", RegexOptions.IgnoreCase, RegexTimeout);
        html = Regex.Replace(html, @"<[^>]+>", " ", RegexOptions.Singleline, RegexTimeout);
        var decoded = WebUtility.HtmlDecode(html).Replace('\u00a0', ' ');
        decoded = Regex.Replace(decoded, @"[ \t]+", " ", RegexOptions.None, RegexTimeout);
        decoded = Regex.Replace(decoded, @" *\n *", "\n", RegexOptions.None, RegexTimeout);
        return Regex.Replace(decoded, @"\n{3,}", "\n\n", RegexOptions.None, RegexTimeout).Trim();
    }

    public static string ToHtmlDocument(string? html, string text)
    {
        var body = string.IsNullOrWhiteSpace(html)
            ? $"<pre>{WebUtility.HtmlEncode(text)}</pre>"
            : ExtractHtmlFragment(html);
        return "<!doctype html>\n<html><head><meta charset=\"utf-8\"><title>WinForge clipboard</title></head><body>" + body + "</body></html>";
    }

    public static string ToJson(string text)
    {
        var input = text.Trim();
        if (input.Length == 0) return "[]";

        try
        {
            using var document = JsonDocument.Parse(input);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch (JsonException)
        {
        }

        object value;
        if (input.StartsWith('<'))
        {
            value = XmlToObject(XDocument.Parse(input, LoadOptions.None).Root ?? throw new FormatException("XML kök öğesi bulunamadı."));
        }
        else if (input.Contains(',') && input.Contains('\n'))
        {
            var csvRows = ParseCsv(input);
            value = csvRows.Count > 1 && csvRows[0].Count > 1 && csvRows.Skip(1).All(row => row.Count == csvRows[0].Count)
                ? CsvRowsToObjects(csvRows)
                : input.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => line.Length > 0).ToArray();
        }
        else if (input.Contains('=') && input.Split('\n').Any(line => line.TrimStart().StartsWith('[')))
        {
            value = IniToObject(input);
        }
        else
        {
            value = input.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => line.Length > 0).ToArray();
        }

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string ExtractHtmlFragment(string html)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
            return html[(start + startMarker.Length)..end];

        var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyStart < 0) return html;
        bodyStart = html.IndexOf('>', bodyStart);
        var bodyEnd = html.IndexOf("</body", bodyStart + 1, StringComparison.OrdinalIgnoreCase);
        return bodyEnd > bodyStart ? html[(bodyStart + 1)..bodyEnd] : html;
    }

    private static List<Dictionary<string, string>> CsvRowsToObjects(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count == 0) return new List<Dictionary<string, string>>();
        var headers = rows[0].Select((header, index) => string.IsNullOrWhiteSpace(header) ? $"Column{index + 1}" : header.Trim()).ToArray();
        var uniqueHeaders = new string[headers.Length];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            var name = headers[i];
            var suffix = 2;
            while (!used.Add(name)) name = $"{headers[i]}_{suffix++}";
            uniqueHeaders[i] = name;
        }

        return rows.Skip(1).Where(row => row.Any(cell => cell.Length > 0)).Select(row =>
        {
            var record = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < uniqueHeaders.Length; i++)
                record[uniqueHeaders[i]] = i < row.Count ? row[i] : "";
            return record;
        }).ToList();
    }

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (character == '"')
            {
                if (quoted && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (!quoted && character == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (!quoted && character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n') index++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else cell.Append(character);
        }

        if (quoted) throw new FormatException("CSV içinde kapanmamış tırnak var.");
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }

    private static object XmlToObject(XElement element)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes()) values["@" + attribute.Name.LocalName] = attribute.Value;
        foreach (var group in element.Elements().GroupBy(child => child.Name.LocalName, StringComparer.Ordinal))
        {
            var children = group.Select(XmlToObject).ToArray();
            values[group.Key] = children.Length == 1 ? children[0] : children;
        }
        var text = string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)).Trim();
        if (text.Length > 0) values["_text"] = text;
        return new Dictionary<string, object?> { [element.Name.LocalName] = values.Count == 0 ? element.Value : values };
    }

    private static Dictionary<string, object?> IniToObject(string text)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var current = result;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1].Trim();
                if (!result.TryGetValue(section, out var sectionObject) || sectionObject is not Dictionary<string, object?>)
                    result[section] = sectionObject = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current = (Dictionary<string, object?>)sectionObject;
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return result;
    }
}
