using System.Globalization;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>Writes and reads the <c>brain.process-proposal@1</c> document, in the manner of <c>brain.rule-proposal@1</c>.</summary>
public static class FlowProposalJson
{
    /// <summary>The value of <c>$spec</c>.</summary>
    public const string Spec = "brain.process-proposal@1";

    /// <summary>The text is not a JSON object.</summary>
    public const string ErrorJsonInvalid = "authoring.proposal.json_invalid";

    /// <summary>The <c>$spec</c> is another format.</summary>
    public const string ErrorUnknownFormat = "authoring.proposal.unknown_format";

    /// <summary>A member is missing or of the wrong kind.</summary>
    public const string ErrorMalformed = "authoring.proposal.malformed";

    private const string CreatedAtFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private static readonly string[] CreatedAtFormats = [CreatedAtFormat, "yyyy-MM-dd'T'HH:mm:ss'Z'"];

    /// <summary>
    /// The document text as the proposal holds it: the draft read as JSON and written again with two-space indentation and <c>\n</c> line
    /// ends. The hash of this text is <see cref="FlowProposal.DocumentHash"/>.
    /// </summary>
    /// <param name="documentJson">The text of a document.</param>
    /// <exception cref="ArgumentException">The text is not a JSON object.</exception>
    public static string FormatDocument(string documentJson)
    {
        ArgumentNullException.ThrowIfNull(documentJson);
        return AuthoringJson.TryObject(documentJson) is { } node
            ? AuthoringJson.Format(node)
            : throw new ArgumentException("The document must be a JSON object.", nameof(documentJson));
    }

    /// <summary>The hash of a formatted document text, as the library hashes a flow.</summary>
    /// <param name="formatted">The text.</param>
    public static string HashOf(string formatted) => AuthoringJson.Hash(DocumentKind.Flow, formatted);

    /// <summary>Writes the proposal as indented JSON with the members in a fixed order.</summary>
    /// <param name="proposal">The proposal.</param>
    /// <exception cref="ArgumentException">The document is not a JSON object.</exception>
    public static string Serialize(FlowProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var document = AuthoringJson.TryObject(proposal.DocumentJson) ?? throw new ArgumentException("The document must be a JSON object.", nameof(proposal));
        var node = new JsonObject
        {
            ["$spec"] = Spec,
            ["process"] = new JsonObject
            {
                ["id"] = proposal.ProcessId,
                ["version"] = proposal.Version,
                ["baseVersion"] = proposal.BaseVersion,
                ["baseContentHash"] = proposal.BaseContentHash,
            },
            ["author"] = proposal.Author,
            ["createdAt"] = proposal.CreatedAt.ToString(CreatedAtFormat, CultureInfo.InvariantCulture),
            ["language"] = AuthoringJson.Tag(proposal.Language),
            ["summary"] = proposal.Summary,
            ["needsReview"] = proposal.NeedsReview,
            ["documentHash"] = proposal.DocumentHash,
            ["document"] = document,
            ["changes"] = new JsonObject { ["en"] = Changes(proposal.ChangesEn), ["es"] = Changes(proposal.ChangesEs) },
            ["tests"] = new JsonArray([.. proposal.Tests.Select(test => (JsonNode?)new JsonObject { ["name"] = test.Name, ["passed"] = test.Passed, ["failure"] = test.Failure })]),
            ["coverage"] = new JsonObject
            {
                ["totalRoutes"] = proposal.TotalRoutes,
                ["coveredRoutes"] = proposal.CoveredRoutes,
                ["neverTaken"] = new JsonArray([.. proposal.NeverTaken.Select(route => (JsonNode?)new JsonObject { ["step"] = route.Step, ["route"] = route.Route })]),
            },
            ["knownGaps"] = new JsonArray([.. proposal.KnownGaps.Select(gap => (JsonNode?)new JsonObject { ["check"] = gap.Check, ["step"] = gap.Step, ["route"] = gap.Route, ["reason"] = gap.Reason })]),
        };
        return AuthoringJson.Format(node);
    }

    /// <summary>Reads a proposal document without throwing on its content; every defect is collected and ordered by path.</summary>
    /// <param name="json">The text.</param>
    public static FlowProposalParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (AuthoringJson.TryParse(json) is not JsonObject root)
        {
            return Failed(ErrorJsonInvalid, string.Empty, "The text is not a JSON object.");
        }

        if (AuthoringJson.Text(root["$spec"]) is not { } format || format != Spec)
        {
            return Failed(ErrorUnknownFormat, "/$spec", "The format is not " + Spec + ".");
        }

        var errors = new List<WorkbenchError>();
        var process = root["process"] as JsonObject;
        if (process is null)
        {
            errors.Add(Bad("/process", "Expected an object."));
        }

        var id = process is null ? null : TextOf(process, "id", "/process/id", errors);
        var version = process is null ? null : WholeOf(process, "version", "/process/version", errors);
        long? baseVersion = null;
        string? baseHash = null;
        if (process is not null)
        {
            baseVersion = process["baseVersion"] is null ? null : WholeOf(process, "baseVersion", "/process/baseVersion", errors);
            baseHash = process["baseContentHash"] is null ? null : TextOf(process, "baseContentHash", "/process/baseContentHash", errors);
        }

        var author = TextOf(root, "author", "/author", errors);
        var createdText = AuthoringJson.Text(root["createdAt"]);
        DateTimeOffset? created = createdText is not null && DateTimeOffset.TryParseExact(createdText, CreatedAtFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment) ? moment : null;
        if (created is null)
        {
            errors.Add(Bad("/createdAt", "Expected a date and time such as 2026-11-22T21:00:00+00:00."));
        }

        var locale = AuthoringJson.Locale(root["language"] ?? JsonValue.Create("en"));
        if (locale is null)
        {
            errors.Add(Bad("/language", "Expected en or es."));
        }

        var summary = TextOf(root, "summary", "/summary", errors);
        var review = root["needsReview"] is JsonValue flag && flag.TryGetValue<bool>(out var needs) ? needs : (bool?)null;
        if (review is null)
        {
            errors.Add(Bad("/needsReview", "Expected true or false."));
        }

        var hash = TextOf(root, "documentHash", "/documentHash", errors);
        var document = root["document"] as JsonObject;
        if (document is null)
        {
            errors.Add(Bad("/document", "Expected an object."));
        }

        var changes = root["changes"] as JsonObject;
        var en = ChangesOf(changes, "en", errors);
        var es = ChangesOf(changes, "es", errors);
        var tests = ReadList(root, "tests", errors, (item, path) => new FlowProposalTest(
            TextOf(item, "name", path + "/name", errors) ?? string.Empty,
            item["passed"] is JsonValue passed && passed.TryGetValue<bool>(out var ok) && ok,
            AuthoringJson.Text(item["failure"])));
        var coverage = root["coverage"] as JsonObject;
        if (coverage is null)
        {
            errors.Add(Bad("/coverage", "Expected an object."));
        }

        var total = coverage is null ? null : WholeOf(coverage, "totalRoutes", "/coverage/totalRoutes", errors);
        var covered = coverage is null ? null : WholeOf(coverage, "coveredRoutes", "/coverage/coveredRoutes", errors);
        var never = coverage is null
            ? []
            : ReadList(coverage, "neverTaken", errors, (item, path) => new FlowProposalRoute(TextOf(item, "step", path + "/step", errors) ?? string.Empty, TextOf(item, "route", path + "/route", errors) ?? string.Empty), "/coverage");
        var gaps = ReadList(root, "knownGaps", errors, (item, path) => new KnownGap(
            TextOf(item, "check", path + "/check", errors) ?? string.Empty,
            TextOf(item, "step", path + "/step", errors) ?? string.Empty,
            AuthoringJson.Text(item["route"]),
            TextOf(item, "reason", path + "/reason", errors) ?? string.Empty));

        if (errors.Count > 0)
        {
            return new FlowProposalParseResult(null, [.. errors.OrderBy(error => error.Path, StringComparer.Ordinal)]);
        }

        var formatted = AuthoringJson.Format(document);
        return new FlowProposalParseResult(
            new FlowProposal
            {
                ProcessId = id!,
                Version = version!.Value,
                BaseVersion = baseVersion,
                BaseContentHash = baseHash,
                Author = author!,
                CreatedAt = created!.Value,
                Language = locale!.Value,
                Summary = summary!,
                NeedsReview = review!.Value,
                DocumentJson = formatted,
                DocumentHash = hash!,
                ChangesEn = en!,
                ChangesEs = es!,
                Tests = tests,
                TotalRoutes = (int)total!.Value,
                CoveredRoutes = (int)covered!.Value,
                NeverTaken = never,
                KnownGaps = gaps,
            },
            []);
    }

    private static FlowProposalParseResult Failed(string code, string path, string message) => new(null, [new WorkbenchError(code, path, message)]);

    private static WorkbenchError Bad(string path, string message) => new(ErrorMalformed, path, message);

    private static JsonObject Changes(FlowProposalChanges changes) => new()
    {
        ["summary"] = changes.Summary,
        ["items"] = AuthoringJson.Array(changes.Items),
        ["behaviour"] = AuthoringJson.Array(changes.Behaviour),
    };

    private static FlowProposalChanges? ChangesOf(JsonObject? changes, string language, List<WorkbenchError> errors)
    {
        var path = "/changes/" + language;
        if (changes?[language] is not JsonObject node)
        {
            errors.Add(Bad(path, "Expected an object."));
            return null;
        }

        var summary = TextOf(node, "summary", path + "/summary", errors);
        var items = Strings(node["items"], path + "/items", errors);
        var behaviour = Strings(node["behaviour"], path + "/behaviour", errors);
        return summary is null ? null : new FlowProposalChanges(summary, items, behaviour);
    }

    private static List<string> Strings(JsonNode? node, string path, List<WorkbenchError> errors)
    {
        if (node is not JsonArray items || items.Any(item => AuthoringJson.Text(item) is null))
        {
            errors.Add(Bad(path, "Expected an array of texts."));
            return [];
        }

        return [.. items.Select(item => AuthoringJson.Text(item)!)];
    }

    private static List<T> ReadList<T>(JsonObject owner, string name, List<WorkbenchError> errors, Func<JsonObject, string, T> read, string prefix = "")
    {
        var path = AuthoringJson.Pointer(prefix, name);
        if (owner[name] is not JsonArray items)
        {
            errors.Add(Bad(path, "Expected an array."));
            return [];
        }

        var list = new List<T>();
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is JsonObject item)
            {
                list.Add(read(item, AuthoringJson.Pointer(path, index)));
            }
            else
            {
                errors.Add(Bad(AuthoringJson.Pointer(path, index), "Expected an object."));
            }
        }

        return list;
    }

    private static string? TextOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        var text = AuthoringJson.Text(owner[name]);
        if (text is null)
        {
            errors.Add(Bad(path, owner.ContainsKey(name) ? "Expected a text." : "The member is missing."));
        }

        return text;
    }

    private static long? WholeOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        var number = AuthoringJson.Whole(owner[name]);
        if (number is null)
        {
            errors.Add(Bad(path, owner.ContainsKey(name) ? "Expected a whole number." : "The member is missing."));
        }

        return number;
    }
}
