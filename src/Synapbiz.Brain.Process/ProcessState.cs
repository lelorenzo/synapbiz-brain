using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts;

namespace Synapbiz.Brain.Process;

/// <summary>The status of an instance. <see cref="Running"/> exists inside a run only and never appears in a returned state.</summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (the same documented CA1008 suppression as FactType in Synapbiz.Brain.Contracts): default(ProcessStatus) == 0 is deliberately not a valid status, so an uninitialized state is never read as running.")]
public enum ProcessStatus
{
    /// <summary>The interpreter is running the instance.</summary>
    Running = 1,

    /// <summary>The instance waits for a result, a timer, an event, a task or a child.</summary>
    Waiting = 2,

    /// <summary>The instance stopped on a problem and waits for an operator.</summary>
    Suspended = 3,

    /// <summary>The instance reached an <c>end</c> step.</summary>
    Completed = 4,

    /// <summary>The instance was cancelled.</summary>
    Cancelled = 5,
}

/// <summary>What an instance waits for.</summary>
public enum ProcessPendingKind
{
    /// <summary>The result of an effect.</summary>
    Effect,

    /// <summary>The delay before the next attempt of an effect.</summary>
    RetryDelay,

    /// <summary>The timer of a <c>timer</c> step.</summary>
    Timer,

    /// <summary>An external event.</summary>
    Wait,

    /// <summary>A human task.</summary>
    Task,

    /// <summary>A child instance.</summary>
    Child,
}

/// <summary>The work an instance waits for.</summary>
/// <param name="Kind">What kind of work.</param>
/// <param name="Id">The id of the work: the effect id, timer id, wait id, task id or child key.</param>
/// <param name="TimeoutTimerId">The id of the timeout timer of a wait or a task, if it has one.</param>
public sealed record ProcessPending(ProcessPendingKind Kind, string Id, string? TimeoutTimerId = null);

/// <summary>An event that arrived while no step waited for it and that the definition accepts.</summary>
/// <param name="Name">The event name.</param>
/// <param name="Correlation">The correlation value, if the event has one.</param>
/// <param name="PayloadJson">The canonical text of the payload object.</param>
public sealed record BufferedEvent(string Name, string? Correlation, string PayloadJson);

/// <summary>Why a suspended instance stopped.</summary>
/// <param name="Code">The stable code of the problem.</param>
/// <param name="Step">The step the instance is on.</param>
/// <param name="Detail">A short text for the operator, if there is one.</param>
public sealed record ProcessProblem(string Code, string Step, string? Detail = null);

/// <summary>
/// The complete state of one process instance: an immutable value that <see cref="BrainProcess.Advance"/> takes and
/// returns. It has value equality and a deterministic JSON form (<see cref="ToJson"/> and <see cref="FromJson"/>).
/// </summary>
public sealed class ProcessState : IEquatable<ProcessState>
{
    private const string Malformed = "process.state.malformed";
    private static readonly string[] Members =
    [
        "instanceId", "definitionId", "definitionVersion", "definitionHash", "status", "currentStep", "stage", "result",
        "variables", "loopCounters", "attempt", "entry", "stepCount", "sequence", "pending", "inbox", "problem",
    ];

    private static readonly JsonWriterOptions Compact = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal ProcessState()
    {
    }

    /// <summary>The id of the instance.</summary>
    public string InstanceId { get; internal init; } = "";

    /// <summary>The id of the process definition the instance is pinned to.</summary>
    public string DefinitionId { get; internal init; } = "";

    /// <summary>The version of the definition.</summary>
    public long DefinitionVersion { get; internal init; }

    /// <summary>The content hash of the definition.</summary>
    public string DefinitionHash { get; internal init; } = "";

    /// <summary>The status of the instance.</summary>
    public ProcessStatus Status { get; internal init; }

    /// <summary>The step the instance is on; null when completed or cancelled.</summary>
    public string? CurrentStep { get; internal init; }

    /// <summary>The stage of the last step that declared one.</summary>
    public string? Stage { get; internal init; }

    /// <summary>The result of the <c>end</c> step the instance completed with.</summary>
    public string? Result { get; internal init; }

    /// <summary>The variables that have a value: name to the canonical JSON text of the value.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; internal init; } = new Dictionary<string, string>();

    /// <summary>The number of arrivals at each loop step.</summary>
    public IReadOnlyDictionary<string, int> LoopCounters { get; internal init; } = new Dictionary<string, int>();

    /// <summary>The attempt of the current step, from 1.</summary>
    public int Attempt { get; internal init; }

    /// <summary>The value of <see cref="StepCount"/> when the current step was entered.</summary>
    public long Entry { get; internal init; }

    /// <summary>The number of steps entered so far.</summary>
    public long StepCount { get; internal init; }

    /// <summary>The sequence number of the last history entry.</summary>
    public long Sequence { get; internal init; }

    /// <summary>The work the instance waits for; null when it waits for nothing.</summary>
    public ProcessPending? Pending { get; internal init; }

    /// <summary>The buffered events, oldest first.</summary>
    public IReadOnlyList<BufferedEvent> Inbox { get; internal init; } = [];

    /// <summary>Why the instance is suspended; null otherwise.</summary>
    public ProcessProblem? Problem { get; internal init; }

    /// <summary>Writes the state as deterministic JSON text: members in a fixed order, dictionaries in ordinal key order.</summary>
    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Compact))
        {
            writer.WriteStartObject();
            writer.WriteString("instanceId", InstanceId);
            writer.WriteString("definitionId", DefinitionId);
            writer.WriteNumber("definitionVersion", DefinitionVersion);
            writer.WriteString("definitionHash", DefinitionHash);
            writer.WriteString("status", Status.ToString());
            writer.WriteString("currentStep", CurrentStep);
            writer.WriteString("stage", Stage);
            writer.WriteString("result", Result);
            writer.WriteStartObject("variables");
            foreach (var (name, value) in Variables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(name);
                writer.WriteRawValue(value);
            }

            writer.WriteEndObject();
            writer.WriteStartObject("loopCounters");
            foreach (var (name, count) in LoopCounters.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WriteNumber(name, count);
            }

            writer.WriteEndObject();
            writer.WriteNumber("attempt", Attempt);
            writer.WriteNumber("entry", Entry);
            writer.WriteNumber("stepCount", StepCount);
            writer.WriteNumber("sequence", Sequence);
            WritePending(writer);
            WriteInbox(writer);
            WriteProblem(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Reads a state written by <see cref="ToJson"/>. Throws a <see cref="BrainException"/> with the code
    /// <c>process.state.malformed</c> when the text is not a valid state.
    /// </summary>
    public static ProcessState FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!JsonText.TryParse(json, out var node) || node is not JsonObject root)
        {
            throw Fail("$", "the text is not a JSON object");
        }

        var unknown = root.Select(member => member.Key).FirstOrDefault(name => !Members.Contains(name, StringComparer.Ordinal));
        if (unknown is not null)
        {
            throw Fail("$." + unknown, "the member is not part of a state");
        }

        var missing = Members.FirstOrDefault(name => !root.ContainsKey(name));
        if (missing is not null)
        {
            throw Fail("$." + missing, "the member is required");
        }

        return new ProcessState
        {
            InstanceId = RequiredText(root, "instanceId"),
            DefinitionId = RequiredText(root, "definitionId"),
            DefinitionVersion = Integer(root, "definitionVersion", 1),
            DefinitionHash = RequiredText(root, "definitionHash"),
            Status = ReadStatus(root),
            CurrentStep = OptionalText(root, "currentStep"),
            Stage = OptionalText(root, "stage"),
            Result = OptionalText(root, "result"),
            Variables = ReadVariables(root["variables"]),
            LoopCounters = ReadCounters(root["loopCounters"]),
            Attempt = (int)Integer(root, "attempt", 0, int.MaxValue),
            Entry = Integer(root, "entry", 0),
            StepCount = Integer(root, "stepCount", 0),
            Sequence = Integer(root, "sequence", 0),
            Pending = ReadPending(root["pending"]),
            Inbox = ReadInbox(root["inbox"]),
            Problem = ReadProblem(root["problem"]),
        };
    }

    /// <inheritdoc />
    public bool Equals(ProcessState? other) =>
        other is not null
        && InstanceId == other.InstanceId
        && DefinitionId == other.DefinitionId
        && DefinitionVersion == other.DefinitionVersion
        && DefinitionHash == other.DefinitionHash
        && Status == other.Status
        && CurrentStep == other.CurrentStep
        && Stage == other.Stage
        && Result == other.Result
        && Attempt == other.Attempt
        && Entry == other.Entry
        && StepCount == other.StepCount
        && Sequence == other.Sequence
        && Pending == other.Pending
        && Problem == other.Problem
        && Variables.Count == other.Variables.Count
        && Variables.All(entry => other.Variables.TryGetValue(entry.Key, out var value) && value == entry.Value)
        && LoopCounters.Count == other.LoopCounters.Count
        && LoopCounters.All(entry => other.LoopCounters.TryGetValue(entry.Key, out var value) && value == entry.Value)
        && Inbox.SequenceEqual(other.Inbox);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ProcessState);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(InstanceId, DefinitionHash, Status, CurrentStep, StepCount, Sequence, Variables.Count, Inbox.Count);

    private void WritePending(Utf8JsonWriter writer)
    {
        if (Pending is null)
        {
            writer.WriteNull("pending");
            return;
        }

        writer.WriteStartObject("pending");
        writer.WriteString("kind", Pending.Kind.ToString());
        writer.WriteString("id", Pending.Id);
        writer.WriteString("timeoutTimerId", Pending.TimeoutTimerId);
        writer.WriteEndObject();
    }

    private void WriteInbox(Utf8JsonWriter writer)
    {
        writer.WriteStartArray("inbox");
        foreach (var buffered in Inbox)
        {
            writer.WriteStartObject();
            writer.WriteString("name", buffered.Name);
            writer.WriteString("correlation", buffered.Correlation);
            writer.WritePropertyName("payload");
            writer.WriteRawValue(buffered.PayloadJson);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private void WriteProblem(Utf8JsonWriter writer)
    {
        if (Problem is null)
        {
            writer.WriteNull("problem");
            return;
        }

        writer.WriteStartObject("problem");
        writer.WriteString("code", Problem.Code);
        writer.WriteString("step", Problem.Step);
        writer.WriteString("detail", Problem.Detail);
        writer.WriteEndObject();
    }

    private static DecisionEngineException Fail(string path, string problem) => new(
        Malformed,
        $"{path}: {problem}.",
        new Dictionary<string, object?> { ["path"] = path, ["problem"] = problem });

    private static string RequiredText(JsonObject owner, string name) =>
        JsonText.Text(owner[name]) is { Length: > 0 } text ? text : throw Fail("$." + name, "expected a non-empty string");

    private static string? OptionalText(JsonObject owner, string name) =>
        owner[name] is null ? null : JsonText.Text(owner[name]) ?? throw Fail("$." + name, "expected a string or null");

    private static long Integer(JsonObject owner, string name, long minimum, long maximum = long.MaxValue) =>
        owner[name] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<long>(out var number)
            && number >= minimum && number <= maximum
            ? number
            : throw Fail("$." + name, $"expected a whole number of at least {minimum.ToString(CultureInfo.InvariantCulture)}");

    private static ProcessStatus ReadStatus(JsonObject owner)
    {
        var text = RequiredText(owner, "status");
        return Enum.TryParse<ProcessStatus>(text, false, out var status) && status != ProcessStatus.Running && Enum.IsDefined(status) && text == status.ToString()
            ? status
            : throw Fail("$.status", "expected Waiting, Suspended, Completed or Cancelled");
    }

    private static Dictionary<string, string> ReadVariables(JsonNode? node)
    {
        var map = node as JsonObject ?? throw Fail("$.variables", "expected an object");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in map)
        {
            result[name] = value is null ? throw Fail("$.variables." + name, "a variable without a value is not written") : JsonText.Canonical(value);
        }

        return result;
    }

    private static Dictionary<string, int> ReadCounters(JsonNode? node)
    {
        var map = node as JsonObject ?? throw Fail("$.loopCounters", "expected an object");
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, value) in map)
        {
            result[name] = value is JsonValue number && value.GetValueKind() == JsonValueKind.Number && number.TryGetValue<int>(out var count) && count >= 0
                ? count
                : throw Fail("$.loopCounters." + name, "expected a non-negative whole number");
        }

        return result;
    }

    private static ProcessPending? ReadPending(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var map = node as JsonObject ?? throw Fail("$.pending", "expected an object or null");
        var kind = RequiredText(map, "kind");
        if (!Enum.TryParse<ProcessPendingKind>(kind, false, out var parsed) || !Enum.IsDefined(parsed) || kind != parsed.ToString())
        {
            throw Fail("$.pending.kind", "unknown kind");
        }

        return new ProcessPending(parsed, RequiredText(map, "id"), OptionalText(map, "timeoutTimerId"));
    }

    private static List<BufferedEvent> ReadInbox(JsonNode? node)
    {
        var array = node as JsonArray ?? throw Fail("$.inbox", "expected an array");
        var result = new List<BufferedEvent>();
        foreach (var item in array)
        {
            var map = item as JsonObject ?? throw Fail("$.inbox", "expected objects");
            var payload = map["payload"] as JsonObject ?? throw Fail("$.inbox.payload", "expected an object");
            result.Add(new BufferedEvent(RequiredText(map, "name"), OptionalText(map, "correlation"), JsonText.Canonical(payload)));
        }

        return result;
    }

    private static ProcessProblem? ReadProblem(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var map = node as JsonObject ?? throw Fail("$.problem", "expected an object or null");
        return new ProcessProblem(RequiredText(map, "code"), RequiredText(map, "step"), OptionalText(map, "detail"));
    }
}
