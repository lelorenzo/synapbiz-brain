using System.Text.Json.Nodes;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>A rule proposal as the authoring tools store it: the existing <c>brain.rule-proposal@1</c> plus the review decision and the change text.</summary>
/// <param name="Proposal">The proposal of the rule workbench.</param>
/// <param name="NeedsReview">True when our team must review it before it takes effect.</param>
/// <param name="Reasons">The <see cref="RuleReviewNeed"/> codes behind <paramref name="NeedsReview"/>.</param>
/// <param name="SummaryEn">What the draft changes, in English.</param>
/// <param name="SummaryEs">What the draft changes, in Spanish.</param>
public sealed record RuleProposalFile(RuleProposal Proposal, bool NeedsReview, IReadOnlyList<string> Reasons, string SummaryEn, string SummaryEs)
{
    /// <summary>The summary in the asked language.</summary>
    /// <param name="locale">The language.</param>
    public string Summary(WorkbenchLocale locale) => locale == WorkbenchLocale.Es ? SummaryEs : SummaryEn;
}

/// <summary>The result of reading a stored rule proposal.</summary>
/// <param name="File">The proposal; null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">What is wrong.</param>
public sealed record RuleProposalFileParseResult(RuleProposalFile? File, IReadOnlyList<WorkbenchError> Errors)
{
    /// <summary>True when the document was read.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Writes and reads a rule proposal with the review decision. The document is a <c>brain.rule-proposal@1</c> with three more members at the
/// end (<c>needsReview</c>, <c>needsReviewReasons</c> and <c>changes</c>); the parser of the rule workbench ignores unknown members, so the
/// existing reader still reads the file. A file without <c>needsReview</c> is read as needing review.
/// </summary>
public static class RuleProposalFiles
{
    /// <summary>Writes the proposal.</summary>
    /// <param name="file">The proposal with its decision.</param>
    public static string Serialize(RuleProposalFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var node = (JsonObject)JsonNode.Parse(RuleProposalJson.Serialize(file.Proposal))!;
        node["needsReview"] = file.NeedsReview;
        node["needsReviewReasons"] = AuthoringJson.Array(file.Reasons);
        node["changes"] = new JsonObject { ["en"] = file.SummaryEn, ["es"] = file.SummaryEs };
        return AuthoringJson.Format(node);
    }

    /// <summary>Reads the proposal without throwing on its content.</summary>
    /// <param name="json">The document text.</param>
    public static RuleProposalFileParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var inner = RuleProposalJson.Parse(json);
        if (inner.Proposal is not { } proposal)
        {
            return new RuleProposalFileParseResult(null, inner.Errors);
        }

        var node = AuthoringJson.TryObject(json)!;
        var needs = node["needsReview"] is not JsonValue value || !value.TryGetValue<bool>(out var flag) || flag;
        var reasons = (node["needsReviewReasons"] as JsonArray)?.Select(item => AuthoringJson.Text(item)).OfType<string>().ToList() ?? [];
        var changes = node["changes"] as JsonObject;
        return new RuleProposalFileParseResult(
            new RuleProposalFile(proposal, needs, reasons, AuthoringJson.Text(changes?["en"]) ?? string.Empty, AuthoringJson.Text(changes?["es"]) ?? string.Empty),
            []);
    }
}
