using System.Text.Json.Nodes;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>Says in words how one JSON schema (what an AI step is sent or may return) differs from another, by member.</summary>
internal static class SchemaWords
{
    internal static string Delta(string? before, string? after, WorkbenchLocale locale)
    {
        var old = AuthoringJson.TryObject(before ?? string.Empty) ?? [];
        var now = AuthoringJson.TryObject(after ?? string.Empty) ?? [];
        var parts = new List<string>();
        var earlier = old["properties"] as JsonObject ?? [];
        var later = now["properties"] as JsonObject ?? [];
        foreach (var name in later.Select(pair => pair.Key).Where(key => !earlier.ContainsKey(key)))
        {
            parts.Add(Say(locale, "schema.added", name));
        }

        foreach (var name in earlier.Select(pair => pair.Key).Where(key => !later.ContainsKey(key)))
        {
            parts.Add(Say(locale, "schema.removed", name));
        }

        foreach (var name in later.Select(pair => pair.Key).Where(key => earlier.ContainsKey(key) && !JsonNode.DeepEquals(earlier[key], later[key])))
        {
            parts.Add(Say(locale, "schema.changed", name));
        }

        var needed = Required(old);
        var needs = Required(now);
        foreach (var name in needs.Where(key => !needed.Contains(key)))
        {
            parts.Add(Say(locale, "schema.required", name));
        }

        foreach (var name in needed.Where(key => !needs.Contains(key)))
        {
            parts.Add(Say(locale, "schema.optional", name));
        }

        if (parts.Count == 0 || Rest(old) != Rest(now))
        {
            parts.Add(AuthoringText.Text(locale, "schema.rules"));
        }

        return AuthoringText.Join(locale, parts);
    }

    private static string Say(WorkbenchLocale locale, string key, string name) =>
        AuthoringText.Text(locale, key, ("name", FlowChangeDescriber.Quote(WorkbenchText.Humanize(name))));

    private static HashSet<string> Required(JsonObject schema) =>
        [.. (schema["required"] as JsonArray ?? []).Select(item => AuthoringJson.Text(item) ?? string.Empty)];

    private static string Rest(JsonObject schema)
    {
        var copy = (JsonObject)schema.DeepClone();
        _ = copy.Remove("properties");
        _ = copy.Remove("required");
        return AuthoringJson.CompactText(copy);
    }
}
