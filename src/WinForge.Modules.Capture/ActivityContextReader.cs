using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinForge.Core;

namespace WinForge.Modules.Capture;

internal sealed record ActivityContext(string Type, string Value);

/// <summary>Reads only the foreground browser address or Explorer folder from its accessible UI.</summary>
internal static class ActivityContextReader
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "waterfox", "arc" };

    public static ActivityContext? Read(ForegroundInfo foreground, CancellationToken token)
    {
        var processName = Path.GetFileNameWithoutExtension(foreground.App);
        var isBrowser = Browsers.Contains(processName);
        if (!isBrowser && !processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var root = AutomationElement.FromHandle(foreground.Window);
            var addressControls = root.FindAll(TreeScope.Descendants, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)));
            string? folder = null;
            for (var index = 0; index < Math.Min(addressControls.Count, 80); index++)
            {
                if (token.IsCancellationRequested) return null;
                var element = addressControls[index];
                if (element.Current.IsPassword) continue;
                var automationId = element.Current.AutomationId;
                var name = element.Current.Name;
                var value = ReadValue(element);
                if (isBrowser)
                {
                    var url = NormalizeUrl(value) ?? NormalizeUrl(name);
                    if (url is not null && (automationId.Contains("url", StringComparison.OrdinalIgnoreCase) ||
                        automationId.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                        automationId.Contains("omnibox", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("adres", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("arama", StringComparison.OrdinalIgnoreCase))) return new("website", url);
                    continue;
                }
                folder ??= ExistingDirectory(value) ?? ExistingDirectory(name);
            }
            return folder is null ? null : new("folder", folder);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    private static string? ReadValue(AutomationElement element)
    {
        try { return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ? ((ValuePattern)pattern).Current.Value : null; }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or COMException) { return null; }
    }

    internal static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal) && candidate.Contains('.') && !candidate.Contains(' ')) candidate = "https://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host)) return null;
        var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string? ExistingDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim();
        if (path.StartsWith("Address: ", StringComparison.OrdinalIgnoreCase)) path = path[9..].Trim();
        if (!Path.IsPathRooted(path)) return null;
        try { return Directory.Exists(path) ? Path.GetFullPath(path) : null; }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }
}
