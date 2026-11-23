using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>A scenario a reviewer proposes, in the embedded scenario format (for example written by <see cref="FlowSimulation.ToScenarioJson"/>).</summary>
/// <param name="Name">The name of the scenario, or an empty text when the object has none.</param>
/// <param name="Json">The scenario object as canonical JSON (compact, members in ordinal order).</param>
public sealed record FlowProposedScenario(string Name, string Json);

/// <summary>
/// What a reader sends back about a flow: a state and a note per target, and scenarios that should be added. A review never changes a
/// document: applying a proposed scenario is the author's edit in the repository.
/// </summary>
public sealed record FlowReview
{
    /// <summary>The id of the reviewed document.</summary>
    public required string ProcessId { get; init; }

    /// <summary>The version of the reviewed document.</summary>
    public required long ProcessVersion { get; init; }

    /// <summary>The content hash of the reviewed document.</summary>
    public required string ProcessContentHash { get; init; }

    /// <summary>Who wrote the review.</summary>
    public required string Reviewer { get; init; }

    /// <summary>When the review was written, supplied by the caller.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The language the reader worked in; the texts of a check of the review use it.</summary>
    public required WorkbenchLocale Language { get; init; }

    /// <summary>
    /// The items. A target is <c>process</c>, <c>stage:&lt;id&gt;</c>, <c>step:&lt;id&gt;</c>, <c>route:&lt;step&gt;/&lt;label&gt;</c>,
    /// <c>trigger:&lt;id&gt;</c> (the id of a schedule, or the event name of an event trigger) or <c>scenario:&lt;name&gt;</c>.
    /// </summary>
    public required IReadOnlyList<ReviewItem> Items { get; init; }

    /// <summary>The scenarios the reviewer proposes.</summary>
    public required IReadOnlyList<FlowProposedScenario> ProposedScenarios { get; init; }
}

/// <summary>The result of reading a flow review document.</summary>
/// <param name="Review">The review, or null when there are errors.</param>
/// <param name="Errors">Every defect found, ordered by path.</param>
public sealed record FlowReviewParseResult(FlowReview? Review, IReadOnlyList<WorkbenchError> Errors)
{
    /// <summary>True when the document was read without a defect.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Writes and reads the <c>brain.process-review@1</c> document.</summary>
public static class FlowReviewJson
{
    /// <summary>The value of the <c>$spec</c> member.</summary>
    public const string Format = "brain.process-review@1";

    /// <summary>The text is not JSON.</summary>
    public const string ErrorJsonInvalid = "workbench.flow_review.json_invalid";

    /// <summary>The <c>$spec</c> member names another format.</summary>
    public const string ErrorUnknownFormat = "workbench.flow_review.unknown_format";

    /// <summary>A member is missing, of the wrong JSON kind or not readable.</summary>
    public const string ErrorMalformed = "workbench.flow_review.malformed";

    private const string CreatedAtFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    private static readonly string[] CreatedAtFormats = [CreatedAtFormat, "yyyy-MM-dd'T'HH:mm:ss'Z'"];

    /// <summary>
    /// Writes the review as indented JSON with the members in a fixed order; the members of each proposed scenario are in ordinal order.
    /// <c>createdAt</c> is written to whole seconds. A proposed scenario whose text is not a JSON object (or repeats a member name)
    /// throws <see cref="JsonException"/>.
    /// </summary>
    /// <param name="review">The review.</param>
    public static string Serialize(FlowReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        var buffer = new ArrayBufferWriter<byte>();
        var options = new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();
            writer.WriteString("$spec", Format);
            writer.WriteStartObject("process");
            writer.WriteString("id", review.ProcessId);
            writer.WriteNumber("version", review.ProcessVersion);
            writer.WriteString("contentHash", review.ProcessContentHash);
            writer.WriteEndObject();
            writer.WriteString("reviewer", review.Reviewer);
            writer.WriteString("createdAt", review.CreatedAt.ToString(CreatedAtFormat, CultureInfo.InvariantCulture));
            writer.WriteString("language", LanguageName(review.Language));
            writer.WriteStartArray("items");
            foreach (var item in review.Items)
            {
                writer.WriteStartObject();
                writer.WriteString("target", item.Target);
                writer.WriteString("state", StateName(item.State));
                writer.WriteString("note", item.Note);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("proposedScenarios");
            foreach (var proposed in review.ProposedScenarios)
            {
                if (FlowJson.TryObject(proposed.Json) is not { } scenario)
                {
                    throw new JsonException("A proposed scenario must be a JSON object that repeats no member name.");
                }

                FlowJson.Sorted(scenario)!.WriteTo(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Reads a review document. Every defect is collected and ordered by path; with any error the review is null. Unknown members are
    /// ignored. Each proposed scenario is kept as canonical JSON, so a review that is written and read again has the same members, with
    /// <see cref="FlowReview.CreatedAt"/> to whole seconds.
    /// </summary>
    /// <param name="json">The document text. A text that repeats a member name is not valid JSON for this purpose.</param>
    public static FlowReviewParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = FlowJson.TryParse(json, out var valid);
        if (!valid)
        {
            return Failed(ErrorJsonInvalid, string.Empty, "The text is not valid JSON, or repeats a member name.");
        }

        if (root is not JsonObject document)
        {
            return Failed(ErrorMalformed, string.Empty, "The document must be a JSON object.");
        }

        return Read(document);
    }

    private static FlowReviewParseResult Read(JsonObject document)
    {
        var spec = document["$spec"];
        if (spec is JsonValue value && value.TryGetValue<string>(out var format) && !string.Equals(format, Format, StringComparison.Ordinal))
        {
            return Failed(ErrorUnknownFormat, "/$spec", $"The format '{format}' is not '{Format}'.");
        }

        var errors = new List<WorkbenchError>();
        if (Text(spec) is null)
        {
            errors.Add(Malformed("/$spec", "Expected the string '" + Format + "'."));
        }

        var process = document["process"] as JsonObject;
        if (process is null)
        {
            errors.Add(Malformed("/process", "Expected an object."));
        }

        var processId = process is null ? null : TextOf(process, "id", "/process", errors);
        var processVersion = process is null ? null : IntegerOf(process, "version", "/process", errors);
        var contentHash = process is null ? null : TextOf(process, "contentHash", "/process", errors);
        var reviewer = TextOf(document, "reviewer", string.Empty, errors);
        var createdAt = DateOf(document, errors);
        var language = LanguageOf(document, errors);
        var items = ReadItems(ArrayOf(document, "items", errors), errors);
        var scenarios = ReadScenarios(ArrayOf(document, "proposedScenarios", errors), errors);
        if (errors.Count > 0)
        {
            return new FlowReviewParseResult(null, [.. errors.OrderBy(error => error.Path, StringComparer.Ordinal)]);
        }

        return new FlowReviewParseResult(
            new FlowReview
            {
                ProcessId = processId!,
                ProcessVersion = processVersion!.Value,
                ProcessContentHash = contentHash!,
                Reviewer = reviewer!,
                CreatedAt = createdAt!.Value,
                Language = language!.Value,
                Items = new ReadOnlyCollection<ReviewItem>(items),
                ProposedScenarios = new ReadOnlyCollection<FlowProposedScenario>(scenarios),
            },
            []);
    }

    private static FlowReviewParseResult Failed(string code, string path, string message) => new(null, [new WorkbenchError(code, path, message)]);

    private static WorkbenchError Malformed(string path, string message) => new(ErrorMalformed, path, message);

    private static string Pointer(string parent, string key) =>
        parent + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string? TextOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        var text = Text(owner[name]);
        if (text is null)
        {
            errors.Add(Malformed(Pointer(path, name), "Expected a string."));
        }

        return text;
    }

    private static JsonArray? ArrayOf(JsonObject owner, string name, List<WorkbenchError> errors)
    {
        if (owner[name] is JsonArray child)
        {
            return child;
        }

        errors.Add(Malformed(Pointer(string.Empty, name), "Expected an array."));
        return null;
    }

    private static long? IntegerOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        if (owner[name] is JsonValue { } node && node.GetValueKind() == JsonValueKind.Number && node.TryGetValue<long>(out var number))
        {
            return number;
        }

        errors.Add(Malformed(Pointer(path, name), "Expected a whole number."));
        return null;
    }

    private static DateTimeOffset? DateOf(JsonObject owner, List<WorkbenchError> errors)
    {
        var text = Text(owner["createdAt"]);
        if (text is not null && DateTimeOffset.TryParseExact(text, CreatedAtFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment))
        {
            return moment;
        }

        errors.Add(Malformed("/createdAt", "Expected a date and time such as 2026-11-22T21:00:00+00:00."));
        return null;
    }

    private static WorkbenchLocale? LanguageOf(JsonObject owner, List<WorkbenchError> errors)
    {
        var locale = Text(owner["language"]) switch
        {
            "en" => (WorkbenchLocale?)WorkbenchLocale.En,
            "es" => WorkbenchLocale.Es,
            _ => null,
        };
        if (locale is null)
        {
            errors.Add(Malformed("/language", "Expected en or es."));
        }

        return locale;
    }

    private static string LanguageName(WorkbenchLocale locale) => locale switch
    {
        WorkbenchLocale.En => "en",
        WorkbenchLocale.Es => "es",
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unknown language."),
    };

    private static string StateName(ReviewState state) => state switch
    {
        ReviewState.Unreviewed => "unreviewed",
        ReviewState.Confirmed => "confirmed",
        ReviewState.NeedsFix => "needsFix",
        ReviewState.Question => "question",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown review state."),
    };

    private static ReviewState? StateOf(string name) => name switch
    {
        "unreviewed" => ReviewState.Unreviewed,
        "confirmed" => ReviewState.Confirmed,
        "needsFix" => ReviewState.NeedsFix,
        "question" => ReviewState.Question,
        _ => null,
    };

    private static List<ReviewItem> ReadItems(JsonArray? array, List<WorkbenchError> errors)
    {
        var items = new List<ReviewItem>();
        for (var index = 0; array is not null && index < array.Count; index++)
        {
            var path = "/items/" + index.ToString(CultureInfo.InvariantCulture);
            if (array[index] is not JsonObject item)
            {
                errors.Add(Malformed(path, "Expected an object."));
                continue;
            }

            var target = TextOf(item, "target", path, errors);
            var note = TextOf(item, "note", path, errors);
            var stateName = TextOf(item, "state", path, errors);
            var state = stateName is null ? null : StateOf(stateName);
            if (stateName is not null && state is null)
            {
                errors.Add(Malformed(Pointer(path, "state"), "Expected unreviewed, confirmed, needsFix or question."));
            }

            if (target is not null && note is not null && state is not null)
            {
                items.Add(new ReviewItem(target, state.Value, note));
            }
        }

        return items;
    }

    private static List<FlowProposedScenario> ReadScenarios(JsonArray? array, List<WorkbenchError> errors)
    {
        var scenarios = new List<FlowProposedScenario>();
        for (var index = 0; array is not null && index < array.Count; index++)
        {
            if (array[index] is not JsonObject scenario)
            {
                errors.Add(Malformed("/proposedScenarios/" + index.ToString(CultureInfo.InvariantCulture), "Expected an object."));
                continue;
            }

            scenarios.Add(new FlowProposedScenario(Text(scenario["name"]) ?? string.Empty, FlowJson.Canonical(scenario)));
        }

        return scenarios;
    }
}

/// <summary>What <see cref="FlowReviewCheck.Against"/> found out about one proposed scenario.</summary>
/// <param name="Index">The position of the scenario in the review.</param>
/// <param name="Name">The name of the scenario.</param>
/// <param name="IsValid">True when the scenario is valid for the document.</param>
/// <param name="Errors">The validator's own errors (code and path, relative to the scenario) of a scenario that is not valid.</param>
/// <param name="Passed">For a valid scenario, whether it runs to its own <c>expect</c>; null for one that is not valid.</param>
/// <param name="Failure">For a valid scenario that does not pass, what went wrong, in English; null otherwise.</param>
public sealed record FlowProposedScenarioResult(int Index, string Name, bool IsValid, IReadOnlyList<BrainSpecError> Errors, bool? Passed, string? Failure);

/// <summary>What a check of a review against a document found.</summary>
/// <param name="Findings">Every finding, ordered by path then code; the codes are <see cref="FlowReviewCheck.ErrorStale"/>, <see cref="FlowReviewCheck.ErrorTargetUnknown"/> and <see cref="FlowReviewCheck.ErrorScenarioInvalid"/>.</param>
/// <param name="Scenarios">One result per proposed scenario, in the order of the review.</param>
public sealed record FlowReviewReport(IReadOnlyList<WorkbenchError> Findings, IReadOnlyList<FlowProposedScenarioResult> Scenarios)
{
    /// <summary>True when the review has no finding.</summary>
    public bool IsClean => Findings.Count == 0;
}

/// <summary>Checks a review against the document it is about, without ever failing: whatever is wrong is a finding.</summary>
public static class FlowReviewCheck
{
    /// <summary>The content hash of the review is not the document's.</summary>
    public const string ErrorStale = "workbench.flow_review.stale";

    /// <summary>A target names something the document does not have.</summary>
    public const string ErrorTargetUnknown = "workbench.flow_review.target_unknown";

    /// <summary>A proposed scenario is not valid for the document.</summary>
    public const string ErrorScenarioInvalid = "workbench.flow_review.scenario_invalid";

    /// <summary>
    /// The text of a proposed scenario is not a JSON object (it is not JSON, or it repeats a member name): the code of the validator-style
    /// error that stands for it in <see cref="FlowProposedScenarioResult.Errors"/>.
    /// </summary>
    public const string ErrorScenarioNotObject = "workbench.flow_review.scenario_not_object";

    /// <summary>
    /// Reports a content hash that is not the document's, a target the document does not have, a proposed scenario that is not valid
    /// for the document (with the validator's own code and path) and, for each valid proposed scenario, whether it runs to its own
    /// <c>expect</c>. The texts are in the language of the review.
    /// </summary>
    /// <param name="review">The review.</param>
    /// <param name="process">The loaded process the review is about.</param>
    public static FlowReviewReport Against(FlowReview review, BrainProcess process)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(process);
        var outline = ProcessOutline.From(process);
        var locale = review.Language;
        var findings = new List<WorkbenchError>();
        if (!string.Equals(review.ProcessContentHash, outline.ContentHash, StringComparison.Ordinal)
            || !string.Equals(review.ProcessId, outline.Id, StringComparison.Ordinal)
            || review.ProcessVersion != outline.Version)
        {
            findings.Add(new WorkbenchError(ErrorStale, "/process/contentHash", FlowTemplates.Text(locale, "review.stale")));
        }

        for (var index = 0; index < review.Items.Count; index++)
        {
            if (!TargetExists(outline, review.Items[index].Target) && !IsOwnProposal(review, review.Items[index].Target))
            {
                findings.Add(new WorkbenchError(
                    ErrorTargetUnknown,
                    "/items/" + FlowText.Number(index) + "/target",
                    FlowTemplates.Text(locale, "review.target_unknown", ("target", review.Items[index].Target))));
            }
        }

        var results = new List<FlowProposedScenarioResult>();
        for (var index = 0; index < review.ProposedScenarios.Count; index++)
        {
            results.Add(CheckScenario(process, review.ProposedScenarios[index], index, locale, findings));
        }

        return new FlowReviewReport(
            new ReadOnlyCollection<WorkbenchError>([.. findings.OrderBy(finding => finding.Path, StringComparer.Ordinal).ThenBy(finding => finding.Code, StringComparer.Ordinal)]),
            new ReadOnlyCollection<FlowProposedScenarioResult>(results));
    }

    private static FlowProposedScenarioResult CheckScenario(BrainProcess process, FlowProposedScenario proposed, int index, WorkbenchLocale locale, List<WorkbenchError> findings)
    {
        var path = "/proposedScenarios/" + FlowText.Number(index);
        if (FlowJson.TryObject(proposed.Json) is not { } scenario)
        {
            var unreadable = new BrainSpecError(
                ErrorScenarioNotObject,
                string.Empty,
                FlowTemplates.Text(WorkbenchLocale.En, "review.scenario_not_object"),
                FlowTemplates.Text(WorkbenchLocale.Es, "review.scenario_not_object"),
                string.Empty);
            findings.Add(new WorkbenchError(ErrorScenarioInvalid, path, FlowTemplates.Text(locale, "review.scenario_invalid", ("code", unreadable.Code), ("path", "/"))));
            return new FlowProposedScenarioResult(index, proposed.Name, false, [unreadable], null, null);
        }

        var errors = ProcessOutline.CheckScenario(process, scenario);
        foreach (var error in errors)
        {
            findings.Add(new WorkbenchError(
                ErrorScenarioInvalid,
                path + error.Path,
                FlowTemplates.Text(locale, "review.scenario_invalid", ("code", error.Code), ("path", error.Path.Length == 0 ? "/" : error.Path))));
        }

        if (errors.Count > 0)
        {
            return new FlowProposedScenarioResult(index, proposed.Name, false, errors, null, null);
        }

        var run = FlowSimulation.Play(process, scenario);
        return new FlowProposedScenarioResult(index, proposed.Name, true, [], run.Failure is null, run.Failure);
    }

    // A mark on one of the review's own proposed scenarios names something the review carries, so it is not an unknown target.
    private static bool IsOwnProposal(FlowReview review, string target) =>
        target.StartsWith("scenario:", StringComparison.Ordinal) && review.ProposedScenarios.Any(proposed => string.Equals(proposed.Name, target["scenario:".Length..], StringComparison.Ordinal));

    private static bool TargetExists(ProcessOutline outline, string target)
    {
        if (string.Equals(target, "process", StringComparison.Ordinal))
        {
            return true;
        }

        var colon = target.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return false;
        }

        var kind = target[..colon];
        var name = target[(colon + 1)..];
        return kind switch
        {
            "stage" => outline.Stages.Contains(name, StringComparer.Ordinal),
            "step" => outline.Step(name) is not null,
            "route" => RouteExists(outline, name),
            "trigger" => outline.Triggers.Any(trigger => TriggerId(trigger) == name),
            "scenario" => outline.Scenarios.Contains(name, StringComparer.Ordinal),
            _ => false,
        };
    }

    private static bool RouteExists(ProcessOutline outline, string name)
    {
        var slash = name.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && outline.Step(name[..slash]) is { } step && step.Routes.Any(route => route.Label == name[(slash + 1)..]);
    }

    private static string TriggerId(ProcessTriggerDeclaration trigger) => trigger switch
    {
        ScheduleTriggerDeclaration schedule => schedule.Id,
        EventTriggerDeclaration declared => declared.Event,
        _ => string.Empty,
    };
}
