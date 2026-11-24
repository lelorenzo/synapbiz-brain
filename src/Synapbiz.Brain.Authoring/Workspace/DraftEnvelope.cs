using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Authoring;

/// <summary>The small <c>brain.draft@1</c> document that records, beside the text of a draft, what the draft came from and what its author accepted.</summary>
internal static class DraftEnvelope
{
    internal const string Spec = "brain.draft@1";

    internal static string Write(DocumentKind kind, string id, StoredDraft draft)
    {
        var node = new JsonObject
        {
            ["$spec"] = Spec,
            ["kind"] = kind == DocumentKind.Flow ? "flow" : "rule",
            ["id"] = id,
            ["base"] = draft.Base is null ? null : new JsonObject { ["id"] = draft.Base.Id, ["version"] = draft.Base.Version, ["contentHash"] = draft.Base.ContentHash },
            ["knownGaps"] = new JsonArray([.. draft.KnownGaps.Select(gap => (JsonNode?)new JsonObject
            {
                ["check"] = gap.Check,
                ["step"] = gap.Step,
                ["route"] = gap.Route,
                ["reason"] = gap.Reason,
            })]),
        };
        if (draft.EditsJson is not null && AuthoringJson.TryParse(draft.EditsJson) is { } edits)
        {
            node["edits"] = edits;
        }

        return AuthoringJson.Format(node);
    }

    /// <summary>Reads the envelope next to a draft text; an unreadable envelope gives a draft with no base and no gaps rather than losing the text.</summary>
    internal static StoredDraft Read(string text, string? envelope)
    {
        if (AuthoringJson.TryObject(envelope) is not { } node)
        {
            return new StoredDraft(text, null, []);
        }

        DraftBase? origin = null;
        if (node["base"] is JsonObject baseNode
            && AuthoringJson.Text(baseNode["id"]) is { } baseId
            && AuthoringJson.Whole(baseNode["version"]) is { } version
            && AuthoringJson.Text(baseNode["contentHash"]) is { } hash)
        {
            origin = new DraftBase(baseId, version, hash);
        }

        var gaps = new List<KnownGap>();
        if (node["knownGaps"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                if (AuthoringJson.Text(item["check"]) is { } check && AuthoringJson.Text(item["step"]) is { } step && AuthoringJson.Text(item["reason"]) is { } reason)
                {
                    gaps.Add(new KnownGap(check, step, AuthoringJson.Text(item["route"]), reason));
                }
            }
        }

        return new StoredDraft(text, origin, gaps, node["edits"] is JsonArray edits ? AuthoringJson.CompactText(edits) : null);
    }
}
