using System.Diagnostics.CodeAnalysis;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>What one entry of the simulation log records.</summary>
public enum FlowSimulationEntryKind
{
    /// <summary>The case was started.</summary>
    Started,

    /// <summary>A port answered an effect with an output.</summary>
    EffectAnswered,

    /// <summary>A port answered an effect with a failure.</summary>
    EffectFailed,

    /// <summary>A person completed a task.</summary>
    TaskCompleted,

    /// <summary>An event was sent to the case.</summary>
    EventSent,

    /// <summary>The virtual clock moved forward.</summary>
    ClockAdvanced,

    /// <summary>A timer came due and was fired.</summary>
    TimerFired,

    /// <summary>A delivery window opened (the simulation answers a window request the way the scenario player does).</summary>
    WindowOpened,

    /// <summary>A delivery window could not be answered: the zone is not one the setup lists.</summary>
    WindowFailed,

    /// <summary>A child process ended with a result.</summary>
    ChildFinished,

    /// <summary>A child process failed.</summary>
    ChildFailed,

    /// <summary>The case was cancelled.</summary>
    Cancelled,

    /// <summary>A suspended case was resumed.</summary>
    Resumed,

    /// <summary>An answer source threw while it was asked for an answer.</summary>
    AnswerSourceFailed,
}

/// <summary>One thing the session did, with the virtual time, who answered and what the interpreter made of it.</summary>
/// <param name="Sequence">The position in the log, from 0.</param>
/// <param name="At">The virtual time.</param>
/// <param name="Kind">What was done.</param>
/// <param name="Source">Who answered: <see cref="FlowSimulation.ManualSource"/>, <see cref="FlowSimulation.ScriptedSource"/>, or the name an answer source gives.</param>
/// <param name="Step">The step concerned, or null.</param>
/// <param name="Subject">What was addressed: an effect id, task id, event name, timer id, child key or a duration; null when nothing is.</param>
/// <param name="Disposition">What the interpreter did with the trigger; <see cref="AdvanceDisposition.Applied"/> for an entry that is not a trigger.</param>
/// <param name="Code">The interpreter's code for a trigger that was ignored or rejected, or the code of a failed answer source; null otherwise.</param>
/// <param name="History">The history entries the interpreter appended because of this entry.</param>
public sealed record FlowSimulationEntry(
    int Sequence,
    DateTimeOffset At,
    FlowSimulationEntryKind Kind,
    string Source,
    string? Step,
    string? Subject,
    AdvanceDisposition Disposition,
    string? Code,
    IReadOnlyList<ProcessHistoryEntry> History);

/// <summary>What a case waits for right now.</summary>
/// <param name="Id">The id of the thing: an effect id, task id, wait id, timer id or child key.</param>
/// <param name="Step">The step that asked for it.</param>
[SuppressMessage(
    "Design",
    "S1694:An abstract class should have both abstract and concrete methods",
    Justification = "A closed, pattern-matchable hierarchy of sealed records is the point; an interface would lose record value-equality. Same reason as ProcessRequest in Synapbiz.Brain.Process.")]
public abstract record FlowOutstanding(string Id, string Step);

/// <summary>An effect the case waits for a port to answer.</summary>
/// <param name="Id">The effect id.</param>
/// <param name="Step">The step.</param>
/// <param name="Port">The port.</param>
/// <param name="StepKind">The kind of step: read, action or ai.</param>
/// <param name="InputJson">The canonical JSON the step built for the port.</param>
/// <param name="Attempt">The attempt number, from 1.</param>
public sealed record OutstandingEffect(string Id, string Step, string Port, OutlineStepKind StepKind, string InputJson, int Attempt) : FlowOutstanding(Id, Step);

/// <summary>A task the case waits for a person to complete.</summary>
/// <param name="Id">The task id.</param>
/// <param name="Step">The step.</param>
/// <param name="Queue">The queue.</param>
/// <param name="Outcomes">The outcomes the person may choose.</param>
/// <param name="ShowJson">The canonical JSON of what the person sees.</param>
public sealed record OutstandingTask(string Id, string Step, string Queue, IReadOnlyList<string> Outcomes, string ShowJson) : FlowOutstanding(Id, Step);

/// <summary>An event the case waits for.</summary>
/// <param name="Id">The wait id.</param>
/// <param name="Step">The step.</param>
/// <param name="Event">The event name.</param>
/// <param name="Correlation">The correlation the event must carry, or null.</param>
public sealed record OutstandingWait(string Id, string Step, string Event, string? Correlation) : FlowOutstanding(Id, Step);

/// <summary>A timer that has not come due.</summary>
/// <param name="Id">The timer id.</param>
/// <param name="Step">The step.</param>
/// <param name="Due">When it comes due.</param>
public sealed record OutstandingTimer(string Id, string Step, DateTimeOffset Due) : FlowOutstanding(Id, Step);

/// <summary>A delivery window that is not open yet.</summary>
/// <param name="Id">The timer id of the window.</param>
/// <param name="Step">The step.</param>
/// <param name="OpensAt">When the window next opens.</param>
public sealed record OutstandingWindow(string Id, string Step, DateTimeOffset OpensAt) : FlowOutstanding(Id, Step);

/// <summary>A child process the case waits for.</summary>
/// <param name="Id">The child key.</param>
/// <param name="Step">The step.</param>
/// <param name="ProcessId">The id of the child process.</param>
/// <param name="InputJson">The canonical JSON the step sends to the child.</param>
public sealed record OutstandingChild(string Id, string Step, string ProcessId, string InputJson) : FlowOutstanding(Id, Step);

/// <summary>What a simulation needs to start.</summary>
public sealed record FlowSimulationSetup
{
    /// <summary>The virtual instant the case starts at.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>The calendar date at <see cref="At"/>, as the practice sees it. The date moves by whole days of virtual time, as in an embedded scenario.</summary>
    public required DateOnly Today { get; init; }

    /// <summary>The input variables as a JSON object; empty or null means none.</summary>
    public string? InputJson { get; init; }

    /// <summary>The zones delivery windows may name: a zone id and a fixed UTC offset of at most 14 hours, in whole minutes, as in an embedded scenario.</summary>
    public IReadOnlyDictionary<string, TimeSpan> Zones { get; init; } = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);

    /// <summary>Who answers effects when <see cref="FlowSimulation.RunAnswersAsync"/> is called, or null.</summary>
    public IFlowAnswerSource? AnswerSource { get; init; }

    /// <summary>Child documents for <c>subprocess</c> steps, or null: <see cref="FlowSimulation.OpenChild"/> starts one.</summary>
    public IReadOnlyCollection<BrainProcess>? ChildProcesses { get; init; }
}

/// <summary>What an answer source is asked: one effect the case waits for.</summary>
/// <param name="ProcessId">The id of the process.</param>
/// <param name="StepId">The step.</param>
/// <param name="EffectId">The effect id.</param>
/// <param name="Port">The port.</param>
/// <param name="StepKind">The kind of step: read, action or ai.</param>
/// <param name="InputJson">The canonical JSON the step built for the port.</param>
/// <param name="Attempt">The attempt number, from 1.</param>
/// <param name="Model">For an <c>ai</c> step, the model it is pinned to.</param>
/// <param name="PromptId">For an <c>ai</c> step, the prompt it is pinned to.</param>
/// <param name="PromptVersion">For an <c>ai</c> step, the version of the prompt.</param>
/// <param name="OutputSchemaJson">For an <c>ai</c> step, the output schema as canonical JSON: what the answer must look like.</param>
public sealed record FlowEffectRequest(
    string ProcessId,
    string StepId,
    string EffectId,
    string Port,
    OutlineStepKind StepKind,
    string InputJson,
    int Attempt,
    string? Model = null,
    string? PromptId = null,
    long? PromptVersion = null,
    string? OutputSchemaJson = null);

/// <summary>An answer to an effect: an output, or a failure.</summary>
public sealed record FlowAnswer
{
    private FlowAnswer()
    {
    }

    /// <summary>The output as a JSON object, for an answer that is not a failure.</summary>
    public string? OutputJson { get; private init; }

    /// <summary>The code of a failure; null for an output.</summary>
    public string? Code { get; private init; }

    /// <summary>The kind of a failure.</summary>
    public ProcessErrorKind Kind { get; private init; } = ProcessErrorKind.Permanent;

    /// <summary>A retry hint for a failure of kind <see cref="ProcessErrorKind.RateLimited"/>.</summary>
    public TimeSpan? RetryAfter { get; private init; }

    /// <summary>True for a failure.</summary>
    public bool IsFailure => Code is not null;

    /// <summary>True for a failure that is not worth trying again.</summary>
    public bool IsPermanent => IsFailure && Kind == ProcessErrorKind.Permanent;

    /// <summary>An answer with an output.</summary>
    /// <param name="outputJson">The output, a JSON object.</param>
    public static FlowAnswer Output(string outputJson)
    {
        ArgumentNullException.ThrowIfNull(outputJson);
        return new FlowAnswer { OutputJson = outputJson };
    }

    /// <summary>A failure that is permanent, or transient (the step may try again).</summary>
    /// <param name="code">The code of the failure.</param>
    /// <param name="isPermanent">True for <see cref="ProcessErrorKind.Permanent"/>, false for <see cref="ProcessErrorKind.Transient"/>.</param>
    public static FlowAnswer Failure(string code, bool isPermanent) =>
        Failure(isPermanent ? ProcessErrorKind.Permanent : ProcessErrorKind.Transient, code);

    /// <summary>A failure of any kind, for example not available or not entitled.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="code">The code of the failure.</param>
    /// <param name="retryAfter">A retry hint, for <see cref="ProcessErrorKind.RateLimited"/>.</param>
    public static FlowAnswer Failure(ProcessErrorKind kind, string code, TimeSpan? retryAfter = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        return new FlowAnswer { Code = code, Kind = kind, RetryAfter = retryAfter };
    }
}

/// <summary>
/// Whoever answers the effects of a simulated case: a scripted list, or the host's own AI account behind a port. Brain holds no model
/// vendor, key or network client: whatever the source does, its answer enters as an ordinary effect result, so the interpreter applies
/// the same schema check and the same judging rule.
/// </summary>
public interface IFlowAnswerSource
{
    /// <summary>The name recorded in the log next to every answer of this source.</summary>
    string Name { get; }

    /// <summary>Answers one effect, or returns null to leave it outstanding for a manual answer.</summary>
    /// <param name="request">The effect.</param>
    /// <param name="cancellationToken">The cancellation token of the run.</param>
    ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// An answer source that reads its answers from lists: for a port, or for a step (a step's list wins). Each request takes the next
/// answer of its list; when the list is empty or there is none, the source does not answer.
/// </summary>
public sealed class ScriptedAnswerSource : IFlowAnswerSource
{
    private readonly Dictionary<string, Queue<FlowAnswer>> ports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<FlowAnswer>> steps = new(StringComparer.Ordinal);

    /// <summary>The name recorded in the log: <see cref="FlowSimulation.ScriptedSource"/>.</summary>
    public string Name => FlowSimulation.ScriptedSource;

    /// <summary>Adds answers for the effects of a port, in order.</summary>
    /// <param name="port">The port.</param>
    /// <param name="answers">The answers.</param>
    public ScriptedAnswerSource ForPort(string port, params FlowAnswer[] answers) => Add(ports, port, answers);

    /// <summary>Adds answers for the effects of a step, in order; they are used before the answers of its port.</summary>
    /// <param name="step">The step id.</param>
    /// <param name="answers">The answers.</param>
    public ScriptedAnswerSource ForStep(string step, params FlowAnswer[] answers) => Add(steps, step, answers);

    /// <inheritdoc />
    public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValueTask.FromResult(Next(request.StepId, request.Port));
    }

    /// <summary>The next answer for a step, else for its port; null when there is none.</summary>
    internal FlowAnswer? Next(string step, string port)
    {
        if (steps.TryGetValue(step, out var own) && own.TryDequeue(out var answer))
        {
            return answer;
        }

        return ports.TryGetValue(port, out var shared) && shared.TryDequeue(out var fallback) ? fallback : null;
    }

    private ScriptedAnswerSource Add(Dictionary<string, Queue<FlowAnswer>> lists, string key, FlowAnswer[] answers)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(answers);
        if (!lists.TryGetValue(key, out var queue))
        {
            lists[key] = queue = new Queue<FlowAnswer>();
        }

        foreach (var answer in answers)
        {
            queue.Enqueue(answer);
        }

        return this;
    }
}
