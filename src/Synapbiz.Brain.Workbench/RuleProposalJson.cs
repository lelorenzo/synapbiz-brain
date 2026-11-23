using System.Globalization;
using System.Text.Json.Nodes;
using static Synapbiz.Brain.Workbench.DraftText;

namespace Synapbiz.Brain.Workbench;

/// <summary>Writes and reads the <c>brain.rule-proposal@1</c> document.</summary>
public static class RuleProposalJson
{
    /// <summary>The value of the <c>$spec</c> member.</summary>
    public const string Spec = "brain.rule-proposal@1";

    private const string NotJson = "workbench.proposal.notJson";
    private const string SpecNotSupported = "workbench.proposal.specNotSupported";
    private const string FieldMissing = "workbench.proposal.fieldMissing";
    private const string FieldNotValid = "workbench.proposal.fieldNotValid";

    private const string CreatedAtFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    private static readonly string[] CreatedAtFormats = [CreatedAtFormat, "yyyy-MM-dd'T'HH:mm:ss'Z'"];

    /// <summary>
    /// Writes the proposal as indented JSON with the members in a fixed order. The draft is written as a JSON object, not as a
    /// string, in the formatting of <see cref="RuleDrafter"/>; <c>createdAt</c> is written to whole seconds with its offset.
    /// </summary>
    /// <param name="proposal">The proposal.</param>
    /// <exception cref="ArgumentException">The draft text is not a JSON object.</exception>
    public static string Serialize(RuleProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        if (TryParse(proposal.DraftJson) is not JsonObject draft)
        {
            throw new ArgumentException("The draft must be a JSON object.", nameof(proposal));
        }

        var document = new JsonObject
        {
            ["$spec"] = Spec,
            ["rule"] = new JsonObject
            {
                ["id"] = proposal.RuleId,
                ["baseVersion"] = proposal.BaseVersion,
                ["baseContentHash"] = proposal.BaseContentHash,
            },
            ["author"] = proposal.Author,
            ["createdAt"] = proposal.CreatedAt.ToString(CreatedAtFormat, CultureInfo.InvariantCulture),
            ["note"] = proposal.Note,
            ["draft"] = draft,
        };
        return Format(document);
    }

    /// <summary>
    /// Reads a proposal document without throwing on its content. Every defect is collected and ordered by path; with any error
    /// the proposal is null. Unknown members are ignored. <see cref="RuleProposal.DraftJson"/> is the draft object written
    /// again in the formatting of <see cref="RuleDrafter"/>.
    /// </summary>
    /// <param name="json">The document text.</param>
    public static RuleProposalParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (TryParse(json) is not JsonObject document)
        {
            return Failed(NotJson, string.Empty, "The text is not a JSON object.");
        }

        if (!document.TryGetPropertyValue("$spec", out var spec))
        {
            return Failed(FieldMissing, "/$spec", "The member is missing.");
        }

        if (Text(spec) is not { } format)
        {
            return Failed(FieldNotValid, "/$spec", "Expected a string.");
        }

        if (!string.Equals(format, Spec, StringComparison.Ordinal))
        {
            return Failed(SpecNotSupported, "/$spec", $"The format '{format}' is not '{Spec}'.");
        }

        var errors = new List<WorkbenchError>();
        var rule = document.TryGetPropertyValue("rule", out var ruleNode) && ruleNode is JsonObject ruleObject ? ruleObject : null;
        if (rule is null)
        {
            errors.Add(Problem(document, "rule", "/rule", "Expected an object."));
        }

        var id = rule is null ? null : TextOf(rule, "id", "/rule/id", errors);
        var baseVersion = rule is null ? null : WholeOf(rule, "baseVersion", "/rule/baseVersion", errors);
        var hash = rule is null ? null : TextOf(rule, "baseContentHash", "/rule/baseContentHash", errors);
        var author = TextOf(document, "author", "/author", errors);
        var createdAt = DateOf(document, errors);
        var note = TextOf(document, "note", "/note", errors);
        var draft = document.TryGetPropertyValue("draft", out var draftNode) && draftNode is JsonObject draftObject ? draftObject : null;
        if (draft is null)
        {
            errors.Add(Problem(document, "draft", "/draft", "Expected an object."));
        }

        if (errors.Count > 0)
        {
            return new RuleProposalParseResult(null, [.. errors.OrderBy(error => error.Path, StringComparer.Ordinal)]);
        }

        return new RuleProposalParseResult(
            new RuleProposal(id!, baseVersion!.Value, hash!, author!, createdAt!.Value, note!, Format(draft!)),
            []);
    }

    private static RuleProposalParseResult Failed(string code, string path, string message) =>
        new(null, [new WorkbenchError(code, path, message)]);

    /// <summary>The error for a member that is absent (missing) or present and of the wrong kind (not valid).</summary>
    private static WorkbenchError Problem(JsonObject owner, string name, string path, string expected) =>
        owner.ContainsKey(name)
            ? new WorkbenchError(FieldNotValid, path, expected)
            : new WorkbenchError(FieldMissing, path, "The member is missing.");

    private static string? TextOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        var text = Text(owner[name]);
        if (text is null)
        {
            errors.Add(Problem(owner, name, path, "Expected a string."));
        }

        return text;
    }

    private static long? WholeOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        var number = Whole(owner[name]);
        if (number is null)
        {
            errors.Add(Problem(owner, name, path, "Expected a whole number."));
        }

        return number;
    }

    private static DateTimeOffset? DateOf(JsonObject owner, List<WorkbenchError> errors)
    {
        if (Text(owner["createdAt"]) is { } text
            && DateTimeOffset.TryParseExact(text, CreatedAtFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment))
        {
            return moment;
        }

        errors.Add(Problem(owner, "createdAt", "/createdAt", "Expected a date and time such as 2026-11-22T21:00:00+00:00."));
        return null;
    }
}
