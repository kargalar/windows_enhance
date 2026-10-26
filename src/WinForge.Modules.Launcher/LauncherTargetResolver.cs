using System.IO;

namespace WinForge.Modules.Launcher;

internal static class LauncherTargetResolver
{
    private static readonly HashSet<string> AllowedUriSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeMailto,
        "ms-settings",
        "shell"
    };

    public static bool TryResolve(string query, out string displayName, out string target)
    {
        displayName = string.Empty;
        target = string.Empty;

        var value = query.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Trim();
        if (value.Length < 3) return false;

        value = Environment.ExpandEnvironmentVariables(value);
        if (Path.IsPathRooted(value) && (File.Exists(value) || Directory.Exists(value)))
        {
            target = value;
            displayName = $"Aç: {value}";
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !AllowedUriSchemes.Contains(uri.Scheme))
            return false;

        if (uri.Scheme is "http" or "https" && string.IsNullOrWhiteSpace(uri.Host)) return false;

        target = value;
        displayName = $"Aç: {value}";
        return true;
    }
}
