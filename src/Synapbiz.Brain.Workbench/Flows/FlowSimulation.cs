using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// A session that walks one case through a flow on a virtual clock. It drives the pure interpreter only, the way the embedded
/// scenario player does: it answers a delivery window with the next opening in the zone the setup lists (or with a failure for a
/// zone it does not list), fires every due timer in due order, then id order, and never reads a real clock. The same setup and the
/// same commands give the same state text, history and log. A command the interpreter rejects or ignores changes nothing and gives
/// one log entry with the code. The session is not thread-safe.
/// </summary>
public sealed partial class FlowSimulation
{
    /// <summary>The source recorded for a command a person gave.</summary>
    public const string ManualSource = "Manual";

    /// <summary>The source recorded for a scripted answer, and for a replayed scenario.</summary>
    public const string ScriptedSource = "Scripted";

    /// <summary>The code of a command that carries a JSON text that cannot be used (not a JSON object).</summary>
    public const string ErrorJsonInvalid = "workbench.flow.json_invalid";

    /// <summary>The code of a duration that is not a positive whole number of seconds, is longer than 3660 days or would move the clock past the largest instant, and of an instant that is not after the clock.</summary>
    public const string ErrorDurationInvalid = "workbench.flow.duration_invalid";

    /// <summary>The code of a command given to a session whose first entry the interpreter rejected (see <see cref="Started"/>).</summary>
    public const string ErrorNotStarted = "workbench.flow.not_started";

    /// <summary>The code of an answer source that threw.</summary>
    public const string ErrorAnswerSourceFailed = "workbench.flow.answer_source_failed";

    private const string ZoneUnknown = "process.window.zone_unknown";
    private const string DefaultCancelReason = "workbench.flow.cancelled";
    private static readonly TimeSpan LongestAdvance = TimeSpan.FromDays(3660);

    private readonly BrainProcess process;
    private readonly ProcessOutline outline;
    private readonly FlowSimulationSetup setup;
    private readonly Dictionary<string, TimeZoneInfo> zones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> timers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> timerSteps = new(StringComparer.Ordinal);
    private readonly HashSet<string> windowTimers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EffectRequest> effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskRequest> tasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WaitRequest> waits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChildStartRequest> children = new(StringComparer.Ordinal);
    private readonly List<ProcessHistoryEntry> history = [];
    private readonly List<FlowSimulationEntry> log = [];
    private readonly ReadOnlyCollection<ProcessHistoryEntry> historyView;
    private readonly ReadOnlyCollection<FlowSimulationEntry> logView;
    private readonly List<JsonObject> scenarioEvents = [];
    private readonly string inputJson;
    private ProcessState state = null!;
    private DateTimeOffset now;
    private bool representable = true;

    private FlowSimulation(BrainProcess process, FlowSimulationSetup setup, string inputJson)
    {
        this.process = process;
        this.setup = setup;
        this.inputJson = inputJson;
        historyView = history.AsReadOnly();
        logView = log.AsReadOnly();
        outline = ProcessOutline.From(process);
        now = setup.At;
        foreach (var (id, offset) in setup.Zones)
        {
            zones[id] = TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
        }
    }

    /// <summary>A name for the run: the name of the scenario for a replay, else <c>simulation</c>.</summary>
    public string Name { get; private set; } = "simulation";

    /// <summary>
    /// False when the interpreter rejected the first entry (for example an input variable the document does not have or of the wrong
    /// type): the session then has no case, every command is refused with <see cref="ErrorNotStarted"/> and <see cref="StartCode"/> says why.
    /// </summary>
    public bool Started { get; private set; } = true;

    /// <summary>The interpreter's code for a rejected start (for example <c>process.input.unknown</c>); null when the case started.</summary>
    public string? StartCode { get; private set; }

    /// <summary>The process the session runs.</summary>
    public BrainProcess Process => process;

    /// <summary>The interpreter's state of the case.</summary>
    public ProcessState State => state;

    /// <summary>The virtual clock.</summary>
    public DateTimeOffset Now => now;

    /// <summary>The status of the case.</summary>
    public ProcessStatus Status => state.Status;

    /// <summary>The step the case is at, or null when it has ended.</summary>
    public string? CurrentStep => state.CurrentStep;

    /// <summary>The variables of the case: name to canonical JSON text.</summary>
    public IReadOnlyDictionary<string, string> Variables => state.Variables;

    /// <summary>Every history entry of the interpreter so far, in order: a read-only view that follows the session.</summary>
    public IReadOnlyList<ProcessHistoryEntry> History => historyView;

    /// <summary>One entry per thing the session did, in order: a read-only view that follows the session.</summary>
    public IReadOnlyList<FlowSimulationEntry> Log => logView;

    /// <summary>The answer source of the setup, or null.</summary>
    public IFlowAnswerSource? AnswerSource => setup.AnswerSource;

    /// <summary>What the case waits for now: effects, tasks, events, timers, delivery windows and children, as a read-only snapshot. A case that is not waiting has nothing outstanding.</summary>
    public IReadOnlyList<FlowOutstanding> Outstanding => ComputeOutstanding().AsReadOnly();

    /// <summary>
    /// Starts a case. The instant is cut to whole seconds (the scenario format and the commands work in whole seconds). When the
    /// interpreter rejects the first entry, the session is returned all the same, with <see cref="Started"/> false and the code in <see cref="StartCode"/>.
    /// </summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="setup">The instant, the date, the input, the zones and optionally an answer source and child documents.</param>
    /// <exception cref="ArgumentException">The input is not a JSON object (or repeats a member name), a zone id is not one an embedded scenario accepts, or a zone offset is more than 14 hours or not in whole minutes.</exception>
    public static FlowSimulation Start(BrainProcess process, FlowSimulationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return Begin(process, setup with { At = WholeSeconds(setup.At) }, "simulation", ManualSource);
    }

    /// <summary>Completes an effect the case waits for with an output.</summary>
    /// <param name="effectId">The id from <see cref="Outstanding"/>.</param>
    /// <param name="outputJson">The output, a JSON object.</param>
    public IReadOnlyList<FlowSimulationEntry> CompleteEffect(string effectId, string outputJson) =>
        CompleteEffect(effectId, outputJson, ManualSource);

    /// <summary>Fails an effect the case waits for, with a permanent or a transient failure.</summary>
    /// <param name="effectId">The id from <see cref="Outstanding"/>.</param>
    /// <param name="code">The code of the failure.</param>
    /// <param name="isPermanent">True for a failure that is not worth trying again.</param>
    public IReadOnlyList<FlowSimulationEntry> FailEffect(string effectId, string code, bool isPermanent) =>
        FailEffect(effectId, isPermanent ? ProcessErrorKind.Permanent : ProcessErrorKind.Transient, code, null, ManualSource);

    /// <summary>Fails an effect the case waits for, with a failure of any kind (for example not available or not entitled).</summary>
    /// <param name="effectId">The id from <see cref="Outstanding"/>.</param>
    /// <param name="kind">The kind of failure.</param>
    /// <param name="code">The code of the failure.</param>
    /// <param name="retryAfter">A retry hint for a rate limit: a positive whole number of seconds, at most 3660 days; any other value refuses the command with <see cref="ErrorDurationInvalid"/>.</param>
    public IReadOnlyList<FlowSimulationEntry> FailEffect(string effectId, ProcessErrorKind kind, string code, TimeSpan? retryAfter = null) =>
        FailEffect(effectId, kind, code, retryAfter, ManualSource);

    /// <summary>Completes a task the case waits for.</summary>
    /// <param name="taskId">The id from <see cref="Outstanding"/>.</param>
    /// <param name="outcome">The outcome the person chose.</param>
    /// <param name="dataJson">The data the person gave, a JSON object; null for none.</param>
    public IReadOnlyList<FlowSimulationEntry> CompleteTask(string taskId, string outcome, string? dataJson = null) =>
        CompleteTask(taskId, outcome, dataJson, ManualSource);

    /// <summary>Sends an event to the case.</summary>
    /// <param name="name">The name of the event.</param>
    /// <param name="correlation">The correlation of the event, or null.</param>
    /// <param name="payloadJson">The payload, a JSON object; null for none.</param>
    public IReadOnlyList<FlowSimulationEntry> SendEvent(string name, string? correlation = null, string? payloadJson = null) =>
        SendEvent(name, correlation, payloadJson, ManualSource);

    /// <summary>Moves the clock forward and fires every timer that comes due, in due order, then id order.</summary>
    /// <param name="duration">A positive whole number of seconds, at most 3660 days.</param>
    public IReadOnlyList<FlowSimulationEntry> AdvanceBy(TimeSpan duration) => AdvanceBy(duration, ManualSource);

    /// <summary>
    /// Moves the clock to an instant after the present one, as <see cref="AdvanceBy(TimeSpan)"/> does. An instant with a fraction of a
    /// second is rounded up to the next whole second; an instant that is not after the clock is refused with <see cref="ErrorDurationInvalid"/>.
    /// </summary>
    /// <param name="instant">The instant.</param>
    public IReadOnlyList<FlowSimulationEntry> AdvanceTo(DateTimeOffset instant)
    {
        var span = instant - now;
        var whole = span > TimeSpan.Zero ? WholeSecondsUp(span) : span;
        return AdvanceBy(whole, ManualSource);
    }

    /// <summary>Completes a child process the case waits for.</summary>
    /// <param name="childKey">The key from <see cref="Outstanding"/>.</param>
    /// <param name="result">The result the child ended with.</param>
    /// <param name="outputJson">The output of the child, a JSON object; null for none.</param>
    public IReadOnlyList<FlowSimulationEntry> CompleteChild(string childKey, string result, string? outputJson = null) =>
        CompleteChild(childKey, result, outputJson, ManualSource);

    /// <summary>Fails a child process the case waits for.</summary>
    /// <param name="childKey">The key from <see cref="Outstanding"/>.</param>
    /// <param name="code">The code of the failure.</param>
    public IReadOnlyList<FlowSimulationEntry> FailChild(string childKey, string code) => FailChild(childKey, code, ManualSource);

    /// <summary>Cancels the case.</summary>
    public IReadOnlyList<FlowSimulationEntry> Cancel() => Cancel(DefaultCancelReason, ManualSource);

    /// <summary>Resumes a case that is suspended.</summary>
    public IReadOnlyList<FlowSimulationEntry> Resume() => Resume(ManualSource);

    /// <summary>
    /// Starts a child process the case waits for, as a session of its own: at the present virtual time, with the input the step sent,
    /// the same zones and the same answer source. Null when the key is not outstanding or the setup has no document for the child.
    /// The result goes back with <see cref="CompleteChild(string, string, string?)"/>.
    /// </summary>
    /// <param name="childKey">The key from <see cref="Outstanding"/>.</param>
    public FlowSimulation? OpenChild(string childKey)
    {
        ArgumentNullException.ThrowIfNull(childKey);
        if (!children.TryGetValue(childKey, out var request) || !ComputeOutstanding().OfType<OutstandingChild>().Any(item => item.Id == childKey))
        {
            return null;
        }

        var document = (setup.ChildProcesses ?? []).Where(child => string.Equals(child.Id, request.ProcessId, StringComparison.Ordinal))
            .OrderByDescending(child => child.Version).FirstOrDefault();
        if (document is null)
        {
            return null;
        }

        var child = Begin(
            document,
            setup with { At = now, Today = Clock().Today, InputJson = request.InputJson },
            "child",
            ManualSource);
        child.Name = "child:" + request.ProcessId;
        return child;
    }

    /// <summary>
    /// Asks the answer source of the setup for every outstanding effect, applies each answer through the same commands, and repeats
    /// until nothing outstanding gets an answer or the limit is reached. An exception from the source becomes a log entry with the
    /// code <c>workbench.flow.answer_source_failed</c>, and an output that is not usable JSON (not valid, or a repeated member name)
    /// one with the code <c>workbench.flow.json_invalid</c>; in both the effect stays outstanding and the session stays usable. A null
    /// answer leaves the effect for a manual answer. Time does not move: a retry that waits for a timer needs <see cref="AdvanceBy(TimeSpan)"/>.
    /// </summary>
    /// <param name="maxAnswers">The most answers to apply, at least 1.</param>
    /// <param name="cancellationToken">Cancels the run; the exception is thrown to the caller, and the answers applied so far stay.</param>
    /// <exception cref="InvalidOperationException">The setup has no answer source.</exception>
    public async Task<IReadOnlyList<FlowSimulationEntry>> RunAnswersAsync(int maxAnswers = 50, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAnswers, 1);
        var source = setup.AnswerSource ?? throw new InvalidOperationException("The setup has no answer source.");
        var before = log.Count;
        var declined = new HashSet<string>(StringComparer.Ordinal);
        var answered = 0;
        while (answered < maxAnswers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = ComputeOutstanding().OfType<OutstandingEffect>().FirstOrDefault(effect => !declined.Contains(effect.Id));
            if (next is null)
            {
                break;
            }

            var answer = await AskAsync(source, next, declined, cancellationToken).ConfigureAwait(false);
            if (answer is null)
            {
                continue;
            }

            if (!answer.IsFailure)
            {
                FlowJson.TryParse(answer.OutputJson, out var usable);
                if (!usable)
                {
                    declined.Add(next.Id);
                    log.Add(new FlowSimulationEntry(log.Count, now, FlowSimulationEntryKind.AnswerSourceFailed, source.Name, next.Step, next.Id, AdvanceDisposition.Rejected, ErrorJsonInvalid, []));
                    continue;
                }
            }

            _ = answer.IsFailure
                ? FailEffect(next.Id, answer.Kind, answer.Code!, answer.RetryAfter, source.Name)
                : CompleteEffect(next.Id, answer.OutputJson!, source.Name);
            answered++;
        }

        return Since(before);
    }

    /// <summary>
    /// Writes the session so far as an embedded scenario of the format: the start, the zones, the commands that the interpreter applied
    /// (a command it refused is left out) and an <c>expect</c> taken from the present status, result, trace and variables. Added to the
    /// document, the scenario passes <see cref="BrainProcess.RunScenarios"/>.
    /// </summary>
    /// <param name="name">The name of the scenario: a name the scenario format accepts (not empty).</param>
    /// <exception cref="ArgumentException">The name is not one an embedded scenario accepts.</exception>
    /// <exception cref="InvalidOperationException">The session never started, or a command carried a JSON text that is not an object, which the scenario format cannot hold.</exception>
    public string ToScenarioJson(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!NameIsValid(process, name))
        {
            throw new ArgumentException("The name of a scenario must be a text the scenario format accepts (not empty).", nameof(name));
        }

        if (!Started)
        {
            throw new InvalidOperationException("The session never started: the interpreter rejected the first entry, so there is no scenario to write.");
        }

        if (!representable)
        {
            throw new InvalidOperationException("A command of this session carried a text that is not a JSON object, which an embedded scenario cannot hold.");
        }

        var start = new JsonObject
        {
            ["at"] = setup.At.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
            ["today"] = setup.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        if (FlowJson.TryObject(inputJson) is { Count: > 0 } input)
        {
            start["input"] = FlowJson.Sorted(input);
        }

        var scenario = new JsonObject { ["name"] = name, ["start"] = start };
        if (setup.Zones.Count > 0)
        {
            var offsets = new JsonObject();
            foreach (var (id, offset) in setup.Zones.OrderBy(zone => zone.Key, StringComparer.Ordinal))
            {
                offsets[id] = (offset < TimeSpan.Zero ? "-" : "+") + Math.Abs(offset.Hours).ToString("00", CultureInfo.InvariantCulture) + ":" + Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture);
            }

            scenario["zones"] = offsets;
        }

        scenario["events"] = new JsonArray([.. scenarioEvents.Select(item => (JsonNode?)item.DeepClone())]);
        var expect = new JsonObject { ["status"] = state.Status.ToString() };
        if (state.Result is { } result)
        {
            expect["result"] = result;
        }

        expect["trace"] = new JsonArray([.. history.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => (JsonNode?)JsonValue.Create(entry.Step))]);
        var variables = new JsonObject();
        foreach (var (variable, text) in state.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            variables[variable] = FlowJson.Sorted(JsonNode.Parse(text));
        }

        expect["variables"] = variables;
        scenario["expect"] = expect;
        return scenario.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
