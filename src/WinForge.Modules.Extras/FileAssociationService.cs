using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinForge.Modules.Extras;

internal sealed record FileAssociationRow(string Extension, string Program, string Executable);

internal static class FileAssociationService
{
    private const uint FriendlyAppName = 4;
    private const uint Executable = 2;
    private const int BufferLength = 32768;

    public static string NormalizeExtension(string value)
    {
        var extension = value.Trim().ToLowerInvariant();
        if (!extension.StartsWith('.')) extension = "." + extension;
        if (extension.Length is < 2 or > 64 || extension.Skip(1).Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '+')))
            throw new FormatException("Geçerli bir dosya uzantısı girin. Örnek: .pdf");
        return extension;
    }

    public static string[] ReadExtensions() => Registry.ClassesRoot.GetSubKeyNames()
        .Where(name => name.StartsWith('.') && name.Length > 1)
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static FileAssociationRow Read(string extension) => new(extension,
        Query(extension, FriendlyAppName), Query(extension, Executable));

    private static string Query(string extension, uint kind)
    {
        var text = new StringBuilder(BufferLength);
        uint length = BufferLength;
        return AssocQueryString(0, kind, extension, null, text, ref length) == 0 ? text.ToString() : "";
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, uint association, string associationName,
        string? extra, StringBuilder output, ref uint outputLength);
}
