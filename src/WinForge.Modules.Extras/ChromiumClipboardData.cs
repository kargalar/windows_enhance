using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WinForge.Modules.Extras;

/// <summary>Reads only VS Code selection metadata from Chromium's bounded custom-data pickle.</summary>
internal static class ChromiumClipboardData
{
    internal const string Format = "Chromium Web Custom MIME Data Format";
    internal const int MaximumBytes = 1024 * 1024;

    internal static bool IsVsCodeEmptySelection(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || data.Length > MaximumBytes) return false;
        var payload = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (payload > data.Length - 4) return false;
        var length = checked((int)payload + 4);
        var offset = 4;
        if (!ReadUInt(data, length, ref offset, out var count) || count > 128) return false;
        for (uint i = 0; i < count; i++)
        {
            if (!ReadString(data, length, ref offset, out var type) || !ReadString(data, length, ref offset, out var value)) return false;
            if (type != "vscode-editor-data") continue;
            try
            {
                using var json = JsonDocument.Parse(value);
                return json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var versionNumber) && versionNumber == 1 &&
                    json.RootElement.TryGetProperty("isFromEmptySelection", out var empty) && empty.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { return false; }
        }
        return false;
    }
    private static bool ReadUInt(ReadOnlySpan<byte> data, int end, ref int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset > end - 4) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        return true;
    }
    private static bool ReadString(ReadOnlySpan<byte> data, int end, ref int offset, out string value)
    {
        value = "";
        if (!ReadUInt(data, end, ref offset, out var chars) || chars > MaximumBytes / 2) return false;
        var bytes = checked((int)chars * 2);
        var padded = (bytes + 3) & ~3;
        if (offset > end - padded) return false;
        value = Encoding.Unicode.GetString(data.Slice(offset, bytes));
        offset += padded;
        return true;
    }
}
