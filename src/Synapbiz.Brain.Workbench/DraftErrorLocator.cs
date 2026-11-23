using System.Globalization;
using System.Text.Json.Nodes;
using static Synapbiz.Brain.Workbench.DraftText;

namespace Synapbiz.Brain.Workbench;

/// <summary>Where in the terms of a form an error of the loader is: the row, the fact of a condition, the outcome field or the test case.</summary>
/// <param name="RowKey">The <c>legacyKey</c> of the row the path is under; null for the default row and when the path is under no row.</param>
/// <param name="RowNumber">The 1-based position of that row; null for the default row and when the path is under no row.</param>
/// <param name="IsDefaultRow">True when the path is under the default row.</param>
/// <param name="Fact">The fact of the condition the path is under; null when it is under none.</param>
/// <param name="OutcomeField">The outcome field the path names; null when it names none.</param>
/// <param name="CaseName">The name of the test case the path is under; null when it is under none.</param>
public sealed record DraftLocation(string? RowKey, int? RowNumber, bool IsDefaultRow, string? Fact, string? OutcomeField, string? CaseName);

/// <summary>Turns the path of a loader error into the terms of a form, so a panel can mark the field.</summary>
public static class DraftErrorLocator
{
    private static readonly DraftLocation Nowhere = new(null, null, false, null, null, null);

    /// <summary>
    /// Locates an error. <c>/body/rows/{i}/...</c> gives the row's key and number <c>i + 1</c>; under it <c>conditions/{j}/...</c>
    /// gives that condition's fact and <c>outcome/{field}</c> the field. <c>/body/defaultRow/...</c> sets
    /// <see cref="DraftLocation.IsDefaultRow"/> and, like a row, the fact and the field. <c>/body/goldens/{k}/...</c> gives the
    /// case's name. Any other path, a path that does not resolve in the document and a document that is not JSON give a location
    /// with nothing set. It never throws on content.
    /// </summary>
    /// <param name="draftJson">The text of the draft the loader refused.</param>
    /// <param name="path">The path of the error, a JSON pointer into the document, such as <c>/body/rows/2/conditions/1/value</c>.</param>
    public static DraftLocation Locate(string draftJson, string path)
    {
        ArgumentNullException.ThrowIfNull(draftJson);
        ArgumentNullException.ThrowIfNull(path);

        var segments = Segments(path);
        if (segments is not ["body", var section, ..] || TryParse(draftJson) is not JsonObject { } document || document["body"] is not JsonObject body)
        {
            return Nowhere;
        }

        return section switch
        {
            "rows" => Row(body, segments),
            "defaultRow" => Fallback(body, segments),
            "goldens" => Case(body, segments),
            _ => Nowhere,
        };
    }

    private static DraftLocation Row(JsonObject body, string[] segments)
    {
        if (segments.Length < 3 || !TryIndex(segments[2], out var index) || (body["rows"] as JsonArray)?.ElementAtOrDefault(index) is not JsonObject row)
        {
            return Nowhere;
        }

        return Inside(row, segments, 3) is { } inside
            ? inside with { RowKey = Text(row["legacyKey"]), RowNumber = index + 1 }
            : Nowhere;
    }

    private static DraftLocation Fallback(JsonObject body, string[] segments) =>
        body["defaultRow"] is JsonObject row && Inside(row, segments, 2) is { } inside ? inside with { IsDefaultRow = true } : Nowhere;

    /// <summary>The fact and field named by the segments under a row, or null when a condition index does not resolve.</summary>
    private static DraftLocation? Inside(JsonObject row, string[] segments, int start)
    {
        if (segments.Length > start + 1 && segments[start] == "outcome")
        {
            return Nowhere with { OutcomeField = segments[start + 1] };
        }

        if (segments.Length > start + 1 && segments[start] == "conditions")
        {
            return TryIndex(segments[start + 1], out var index) && (row["conditions"] as JsonArray)?.ElementAtOrDefault(index) is JsonObject condition
                ? Nowhere with { Fact = Text(condition["fact"]) }
                : null;
        }

        return Nowhere;
    }

    private static DraftLocation Case(JsonObject body, string[] segments) =>
        segments.Length >= 3 && TryIndex(segments[2], out var index) && (body["goldens"] as JsonArray)?.ElementAtOrDefault(index) is JsonObject golden
            ? Nowhere with { CaseName = Text(golden["name"]) }
            : Nowhere;

    private static string[] Segments(string path) =>
        path.StartsWith('/')
            ? [.. path[1..].Split('/').Select(part => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))]
            : [];

    private static bool TryIndex(string segment, out int index) =>
        int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index);
}
