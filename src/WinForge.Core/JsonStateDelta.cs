using System.Text.Json.Nodes;

namespace WinForge.Core;

public sealed record JsonStateChange(string[] Path, JsonNode? Value, bool Remove = false);

/// <summary>Describes immutable JSON state changes without repeating unchanged catalogs or image strings.</summary>
public static class JsonStateDelta
{
    public static IReadOnlyList<JsonStateChange>? Between(JsonNode previous, JsonNode current, int maximumChanges = 4096)
    {
        var changes = new List<JsonStateChange>();
        Visit(previous, current, []);
        return changes.Count > maximumChanges ? null : changes;

        void Visit(JsonNode? before, JsonNode? after, string[] path)
        {
            if (changes.Count > maximumChanges || JsonNode.DeepEquals(before, after)) return;
            if (before is JsonObject oldObject && after is JsonObject newObject)
            {
                foreach (var (key, _) in oldObject)
                {
                    if (changes.Count > maximumChanges) return;
                    if (!newObject.ContainsKey(key)) changes.Add(new([.. path, key], null, true));
                }
                foreach (var (key, value) in newObject)
                {
                    if (changes.Count > maximumChanges) return;
                    if (oldObject.TryGetPropertyValue(key, out var oldValue)) Visit(oldValue, value, [.. path, key]);
                    else changes.Add(new([.. path, key], value));
                }
            }
            else if (before is JsonArray oldArray && after is JsonArray newArray && oldArray.Count == newArray.Count)
            {
                for (var i = 0; i < newArray.Count; i++)
                {
                    if (changes.Count > maximumChanges) return;
                    Visit(oldArray[i], newArray[i], [.. path, i.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                }
            }
            else changes.Add(new(path, after));
        }
    }
}
