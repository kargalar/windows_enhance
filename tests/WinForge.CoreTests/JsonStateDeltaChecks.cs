using System.Text.Json.Nodes;
using WinForge.Core;

internal static class JsonStateDeltaChecks
{
    public static void Run(Action<bool, string> check)
    {
        var before = JsonNode.Parse("""{"id":"context-menu","panel":{"entries":[{"id":"one","visible":true,"icon":"unchanged-large-image"}],"removed":"old"},"error":null}""")!;
        var after = JsonNode.Parse("""{"id":"context-menu","panel":{"entries":[{"id":"one","visible":false,"icon":"unchanged-large-image"}],"added":null},"error":"new"}""")!;
        var changes = JsonStateDelta.Between(before, after)!;
        check(changes.Count == 4 && changes.Any(change => change.Path.SequenceEqual(new[] { "panel", "entries", "0", "visible" }) && change.Value!.GetValue<bool>() == false),
            "JSON delta addresses only the changed field in a stable catalog array");
        check(changes.Any(change => change.Remove && change.Path.SequenceEqual(new[] { "panel", "removed" }))
            && changes.Any(change => !change.Remove && change.Value is null && change.Path.SequenceEqual(new[] { "panel", "added" })),
            "JSON delta distinguishes property removal from setting a JSON null");
        check(!System.Text.Json.JsonSerializer.Serialize(changes).Contains("unchanged-large-image"), "JSON delta does not retransmit an unchanged image");
        check(JsonStateDelta.Between(before, before)!.Count == 0, "An unchanged snapshot needs no module change payload");
        check(JsonStateDelta.Between(before, after, maximumChanges: 2) is null, "Excessive state changes fall back to a bounded full-module replacement");
        var resized = JsonStateDelta.Between(JsonNode.Parse("[1]")!, JsonNode.Parse("[1,2]")!)!;
        check(resized.Count == 1 && resized[0].Path.Length == 0 && resized[0].Value is JsonArray { Count: 2 }, "Array length changes use an atomic replacement instead of shifting element paths");
    }
}
