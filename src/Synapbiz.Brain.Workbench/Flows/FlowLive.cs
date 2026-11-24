using System.Text.Json;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// The read-only view of the cases that are open now, which a host gives to the process workbench to draw its Live view. The workbench
/// library defines the contract and does no I/O of its own: a host implements it over whatever it keeps its cases in (the Postgres
/// host of Brain has an adapter, <c>PostgresFlowLiveSource</c> in the package <c>Synapbiz.Brain.Workbench.Postgres</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Case references are opaque and never personal data.</b> A reference is whatever the host chooses to name one case by (an instance
/// id is fine); it must not be a business key, a name, a number a person is known by or anything else that identifies a person, because
/// the workbench writes it on the screen and hands it to <c>OnOpenCase</c>. The host maps a reference to a person itself, if it ever has
/// to, and only after a person asks.
/// </para>
/// <para>
/// Nothing in this contract acts on a case: every member only reads. A member may throw; the screen then says in words that the source
/// failed and keeps what it had drawn. A member should honour the cancellation token.
/// </para>
/// </remarks>
public interface IFlowLiveSource
{
    /// <summary>The number of open cases of every process, by the step each is on, and how many of them wait for a person.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FlowLiveCounts> GetCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>One page of the references of the open cases of a process, optionally only those on one step, newest first.</summary>
    /// <param name="query">The process, the step, the page size and the cursor.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FlowLiveCasePage> ListCasesAsync(FlowLiveCaseQuery query, CancellationToken cancellationToken = default);

    /// <summary>One case with its history, the version of the process it runs on and what it waits for; null when the host has no such case (it may have just ended and been removed).</summary>
    /// <param name="caseRef">An opaque reference the source gave in a page of cases.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FlowLiveCase?> GetCaseAsync(string caseRef, CancellationToken cancellationToken = default);
}

/// <summary>The cases open on one step of one process.</summary>
/// <param name="ProcessId">The id of the process document.</param>
/// <param name="Step">The id of the step the cases are on; null for cases that have not entered a step yet.</param>
/// <param name="Open">How many cases are open on the step.</param>
/// <param name="Waiting">How many of them wait for a person (an open task for a person), at most <paramref name="Open"/>.</param>
public sealed record FlowLiveCount(string ProcessId, string? Step, int Open, int Waiting);

/// <summary>The open cases of every process by step.</summary>
/// <param name="Rows">One row for each process and step that has at least one open case.</param>
/// <param name="IsPartial">True when the source stopped counting at a limit of its own: the numbers are then at least what they say.</param>
public sealed record FlowLiveCounts(IReadOnlyList<FlowLiveCount> Rows, bool IsPartial = false)
{
    /// <summary>No open case at all.</summary>
    public static FlowLiveCounts None { get; } = new([]);
}

/// <summary>A request for a page of case references.</summary>
/// <param name="ProcessId">The process whose cases are wanted.</param>
/// <param name="Step">Only the cases on this step; null for every open case of the process.</param>
/// <param name="Limit">The page size, from 1 to 500.</param>
/// <param name="After">The <see cref="FlowLiveCasePage.Next"/> of the previous page; null for the first.</param>
public sealed record FlowLiveCaseQuery(string ProcessId, string? Step = null, int Limit = 50, string? After = null);

/// <summary>One open case in a list: an opaque reference and where it is.</summary>
/// <param name="CaseRef">The opaque reference; never personal data.</param>
/// <param name="ProcessVersion">The version of the process the case runs on.</param>
/// <param name="Step">The step it is on; null when it has not entered one.</param>
/// <param name="WaitsForPerson">True when it waits for a person.</param>
/// <param name="Since">When the case was created.</param>
public sealed record FlowLiveCaseLine(string CaseRef, long ProcessVersion, string? Step, bool WaitsForPerson, DateTimeOffset Since);

/// <summary>A page of case references.</summary>
/// <param name="Cases">The cases of the page.</param>
/// <param name="Next">The opaque cursor of the next page; null on the last page.</param>
public sealed record FlowLiveCasePage(IReadOnlyList<FlowLiveCaseLine> Cases, string? Next);

/// <summary>
/// One entry of the history of a case, reduced to what an explanation reads. <see cref="DataJson"/> holds only the members the
/// workbench reads (<c>rule</c>, <c>version</c>, <c>decided</c>, <c>matchedRowKeys</c>, <c>outcome</c>, <c>event</c>, <c>dueAt</c>,
/// <c>result</c>, <c>kind</c>): never the facts a rule judged, never a value a person typed. <see cref="Of"/> makes such an entry from
/// the history the process library writes.
/// </summary>
/// <param name="Sequence">The sequence number, increasing by one.</param>
/// <param name="At">When it was written.</param>
/// <param name="Step">The step it belongs to; null outside a step.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Code">A stable code, for failures and suspensions.</param>
/// <param name="DataJson">The reduced data, a JSON object, or null.</param>
public sealed record FlowLiveEntry(long Sequence, DateTimeOffset At, string? Step, ProcessHistoryKind Kind, string? Code, string? DataJson)
{
    private static readonly string[] Kept = ["rule", "version", "decided", "matchedRowKeys", "outcome", "event", "dueAt", "result", "kind"];

    /// <summary>Reduces an entry of the history of a case to what the workbench may carry: the members named in the type's summary and nothing else.</summary>
    /// <param name="entry">The entry as the process library wrote it.</param>
    public static FlowLiveEntry Of(ProcessHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string? reduced = null;
        if (FlowJson.TryObject(entry.DataJson) is { } data)
        {
            var kept = new JsonObject();
            foreach (var name in Kept)
            {
                if (data[name] is { } value)
                {
                    kept[name] = value.DeepClone();
                }
            }

            reduced = kept.Count == 0 ? null : kept.ToJsonString();
        }

        return new FlowLiveEntry(entry.Sequence, entry.At, entry.Step, entry.Kind, entry.Code, reduced);
    }

    internal ProcessHistoryEntry ToHistory() => new(Sequence, At, Step, 0, 0, Kind, Code, DataJson);
}

/// <summary>What a case waits for, as the host knows it.</summary>
/// <param name="Queues">The queues of the open tasks for a person, ordered.</param>
/// <param name="Events">The names of the events it waits for, ordered; never a correlation value.</param>
/// <param name="Until">The earliest moment a timer of the case is due; null when it has no timer.</param>
public sealed record FlowLiveWait(IReadOnlyList<string> Queues, IReadOnlyList<string> Events, DateTimeOffset? Until)
{
    /// <summary>Waits for nothing.</summary>
    public static FlowLiveWait Nothing { get; } = new([], [], null);
}

/// <summary>One case with what the explanation needs.</summary>
/// <param name="CaseRef">The opaque reference.</param>
/// <param name="ProcessId">The id of the process the case runs.</param>
/// <param name="ProcessVersion">The version of that process the case is pinned to.</param>
/// <param name="Status">The status of the case.</param>
/// <param name="CurrentStep">The step the case is on; null when it has finished or has not entered one.</param>
/// <param name="Result">The result of the end step, when the case completed.</param>
/// <param name="ProblemCode">The code of the problem that suspended the case, if any.</param>
/// <param name="History">The entries in the order they were written (see <see cref="FlowLiveEntry"/>).</param>
/// <param name="Wait">What it waits for.</param>
public sealed record FlowLiveCase(
    string CaseRef,
    string ProcessId,
    long ProcessVersion,
    ProcessStatus Status,
    string? CurrentStep,
    string? Result,
    string? ProblemCode,
    IReadOnlyList<FlowLiveEntry> History,
    FlowLiveWait Wait);

/// <summary>Makes a <see cref="FlowLiveCase"/> from a simulation, for a demo or a test that scripts the cases a live source returns.</summary>
public static class FlowLiveScripts
{
    /// <summary>The case a simulation is in now: its history reduced by <see cref="FlowLiveEntry.Of"/>, its step, its status and what it waits for.</summary>
    /// <param name="caseRef">The opaque reference to give the case; never personal data.</param>
    /// <param name="session">The simulation (a replay of an embedded scenario, for example).</param>
    public static FlowLiveCase FromSimulation(string caseRef, FlowSimulation session)
    {
        ArgumentNullException.ThrowIfNull(caseRef);
        ArgumentNullException.ThrowIfNull(session);
        var outstanding = session.Outstanding;
        var due = outstanding.OfType<OutstandingTimer>().Select(item => (DateTimeOffset?)item.Due)
            .Concat(outstanding.OfType<OutstandingWindow>().Select(item => (DateTimeOffset?)item.OpensAt))
            .OrderBy(moment => moment).FirstOrDefault();
        var wait = new FlowLiveWait(
            [.. outstanding.OfType<OutstandingTask>().Select(item => item.Queue).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. outstanding.OfType<OutstandingWait>().Select(item => item.Event).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            due);
        return new FlowLiveCase(
            caseRef,
            session.State.DefinitionId,
            session.State.DefinitionVersion,
            session.Status,
            session.CurrentStep,
            session.State.Result,
            session.State.Problem?.Code,
            [.. session.History.Select(FlowLiveEntry.Of)],
            wait);
    }
}

/// <summary>The open cases of a map: per process, per step and, for a main process, per phase.</summary>
public sealed record LiveCount(int Open, int Waiting)
{
    /// <summary>No case.</summary>
    public static LiveCount Zero { get; } = new(0, 0);

    /// <summary>True when no case is counted.</summary>
    public bool IsZero => Open == 0;

    /// <summary>The sum of two counts.</summary>
    /// <param name="other">The other count.</param>
    public LiveCount Plus(LiveCount other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new LiveCount(Open + other.Open, Waiting + other.Waiting);
    }
}

/// <summary>
/// The counts of a live source laid over a <see cref="ProcessMap"/>: how many cases are open on each process, on each of its steps and
/// in each of its phases (the stage of the step a case is on, by the document of the highest version of the process). Pure: it only adds.
/// </summary>
public sealed class ProcessMapLive
{
    private readonly Dictionary<string, LiveCount> processes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Process, string Step), LiveCount> steps = [];
    private readonly Dictionary<(string Process, string? Stage), LiveCount> phases = [];

    private ProcessMapLive()
    {
    }

    /// <summary>The cases on a step that the document of its process (the highest version) does not have, or that have not entered a step: counted for the process, in no phase.</summary>
    public LiveCount Unplaced { get; private set; } = LiveCount.Zero;

    /// <summary>All the open cases of the map.</summary>
    public LiveCount Total { get; private set; } = LiveCount.Zero;

    /// <summary>True when the source said its numbers are at least what it reports.</summary>
    public bool IsPartial { get; private set; }

    /// <summary>The open cases of a process; zero for a process the source does not know.</summary>
    /// <param name="processId">The id of the process.</param>
    public LiveCount Process(string processId) => processes.GetValueOrDefault(processId ?? string.Empty, LiveCount.Zero);

    /// <summary>The open cases on a step.</summary>
    /// <param name="processId">The id of the process.</param>
    /// <param name="stepId">The id of the step.</param>
    public LiveCount Step(string processId, string stepId) => steps.GetValueOrDefault((processId ?? string.Empty, stepId ?? string.Empty), LiveCount.Zero);

    /// <summary>The open cases in a phase of a process: those on a step of that stage; with a null stage, those on a step that has none.</summary>
    /// <param name="processId">The id of the process.</param>
    /// <param name="stageId">The stage id, or null for the steps with no stage.</param>
    public LiveCount Phase(string processId, string? stageId) => phases.GetValueOrDefault((processId ?? string.Empty, stageId), LiveCount.Zero);

    /// <summary>Lays the counts over the documents of a map.</summary>
    /// <param name="documents">The loaded processes; the highest version of each id places its steps in phases.</param>
    /// <param name="counts">What the live source reported.</param>
    public static ProcessMapLive From(IEnumerable<BrainProcess> documents, FlowLiveCounts counts)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(counts);
        var outlines = documents.Select(ProcessOutline.From)
            .GroupBy(outline => outline.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(outline => outline.Version).ThenBy(outline => outline.ContentHash, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var live = new ProcessMapLive { IsPartial = counts.IsPartial };
        foreach (var row in counts.Rows.Where(row => row.Open > 0))
        {
            var count = new LiveCount(row.Open, Math.Min(row.Waiting, row.Open));
            live.Total = live.Total.Plus(count);
            live.processes[row.ProcessId] = live.Process(row.ProcessId).Plus(count);
            var step = row.Step is null || !outlines.TryGetValue(row.ProcessId, out var outline) ? null : outline.Step(row.Step);
            if (step is null)
            {
                live.Unplaced = live.Unplaced.Plus(count);
                continue;
            }

            live.steps[(row.ProcessId, step.Id)] = live.Step(row.ProcessId, step.Id).Plus(count);
            live.phases[(row.ProcessId, step.Stage)] = live.Phase(row.ProcessId, step.Stage).Plus(count);
        }

        return live;
    }
}

/// <summary>Why the history of a case cannot be explained against a document.</summary>
public enum LiveUnexplained
{
    /// <summary>The history is explained.</summary>
    None = 0,

    /// <summary>The case belongs to another process than the document.</summary>
    OtherProcess = 1,

    /// <summary>The case runs on another version of the process than the document.</summary>
    OtherVersion = 2,

    /// <summary>The history names a step the document does not have.</summary>
    UnknownStep = 3,

    /// <summary>The history says the case went from one step to another by no route of the document, or by none the history picks out.</summary>
    ImpossibleRoute = 4,

    /// <summary>The case says it is on one step and its history ends on another.</summary>
    CurrentStepMismatch = 5,
}

/// <summary>A step a case has been on, in the order it first entered it.</summary>
/// <param name="Id">The step id.</param>
/// <param name="Label">The label of the step.</param>
/// <param name="Times">How many times the case entered it.</param>
/// <param name="IsCurrent">True for the step the case is on now.</param>
/// <param name="Left">How it left the step, as a route phrase; null for the current step and for a step it left in a way that is not a route.</param>
public sealed record LivePathStep(string Id, string Label, int Times, bool IsCurrent, string? Left);

/// <summary>A route a case took, in the order it took them.</summary>
/// <param name="From">The step it left.</param>
/// <param name="To">The step it went to.</param>
/// <param name="Label">The label of the route (<c>next</c>, <c>on:approved</c> and so on).</param>
/// <param name="Phrase">The route in words.</param>
public sealed record LivePathRoute(string From, string To, string Label, string Phrase);

/// <summary>The facts of the side column of a case.</summary>
/// <param name="CurrentStep">The label of the step the case is on; null when it is on none.</param>
/// <param name="Why">Why the case is here: the rule and row, the event or the choice that decided, or that the case started here; null when it has no step.</param>
/// <param name="Next">What can happen next, one route phrase for each way out of the current step; empty when it is on none.</param>
/// <param name="Version">The version of the process the case runs on.</param>
public sealed record LiveSideFacts(string? CurrentStep, string? Why, IReadOnlyList<string> Next, long Version);

/// <summary>
/// The path of one case over the document of the version it runs on: the steps it has been on, the one it is on, the routes it took, a
/// sentence for what it waits for and the facts of the side column. Built from the history with the same route reading that
/// <see cref="FlowCoverage"/> uses and the same words as <see cref="FlowDescriber"/>. A history the document cannot explain gives
/// <see cref="Unexplained"/> with a reason in words, never an exception.
/// </summary>
public sealed record LiveCasePath
{
    /// <summary>The empty path of a case that cannot be explained; <see cref="Unexplained"/> and <see cref="Reason"/> say why.</summary>
    private LiveCasePath()
    {
    }

    /// <summary>None when the history is explained; otherwise why it is not.</summary>
    public LiveUnexplained Unexplained { get; private init; }

    /// <summary>When <see cref="Unexplained"/> is not none, the reason in words in the asked language; otherwise null.</summary>
    public string? Reason { get; private init; }

    /// <summary>True when the history is explained against the document.</summary>
    public bool IsExplained => Unexplained == LiveUnexplained.None;

    /// <summary>The steps the case has been on, in the order it first entered each; empty when it is not explained.</summary>
    public IReadOnlyList<LivePathStep> Steps { get; private init; } = [];

    /// <summary>The routes the case took, in order; empty when it is not explained.</summary>
    public IReadOnlyList<LivePathRoute> Routes { get; private init; } = [];

    /// <summary>The id of the step the case is on; null when it is on none (it finished, or it has not started) or when it is not explained.</summary>
    public string? CurrentStep { get; private init; }

    /// <summary>What the case waits for, or how it ended, in one sentence (code-styled ids between backticks); null when it is not explained.</summary>
    public string? Waiting { get; private init; }

    /// <summary>The facts of the side column; null when it is not explained.</summary>
    public LiveSideFacts? Side { get; private init; }

    /// <summary>The ids of the steps the case has been on, for a diagram to mark.</summary>
    public IReadOnlySet<string> VisitedIds => Steps.Select(step => step.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>The pairs (from, to) of the routes the case took, for a diagram to mark.</summary>
    public IReadOnlySet<(string From, string To)> TakenPairs => Routes.Select(route => (route.From, route.To)).ToHashSet();

    /// <summary>Explains the history of a case against a document.</summary>
    /// <param name="process">The document of the version the case runs on.</param>
    /// <param name="live">The case as the source gave it.</param>
    /// <param name="locale">The language of every sentence.</param>
    /// <param name="knownProcesses">Other documents the caller knows, to word a child result by the child's own labels; null for none.</param>
    public static LiveCasePath From(BrainProcess process, FlowLiveCase live, WorkbenchLocale locale, IEnumerable<BrainProcess>? knownProcesses = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(live);
        var outline = ProcessOutline.From(process);
        if (!string.Equals(outline.Id, live.ProcessId, StringComparison.Ordinal))
        {
            return Refuse(locale, LiveUnexplained.OtherProcess, "live.unexplained.otherProcess", ("found", live.ProcessId), ("expected", outline.Id));
        }

        if (outline.Version != live.ProcessVersion)
        {
            return Refuse(locale, LiveUnexplained.OtherVersion, "live.unexplained.otherVersion", ("found", FlowText.Number(live.ProcessVersion)), ("expected", FlowText.Number(outline.Version)));
        }

        var history = live.History.Select(entry => entry.ToHistory()).ToList();
        var entered = history.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step ?? string.Empty).ToList();
        if (entered.FirstOrDefault(id => outline.Step(id) is null) is { } unknown)
        {
            return Refuse(locale, LiveUnexplained.UnknownStep, "live.unexplained.unknownStep", ("step", unknown));
        }

        if (live.CurrentStep is { } declared && outline.Step(declared) is null)
        {
            return Refuse(locale, LiveUnexplained.UnknownStep, "live.unexplained.unknownStep", ("step", declared));
        }

        var read = RouteReader.Taken(outline, history);
        if (read.Problem is { } problem)
        {
            return Refuse(locale, LiveUnexplained.ImpossibleRoute, "error.route_unexplained", ("step", problem.Step), ("to", problem.To));
        }

        var open = live.Status is ProcessStatus.Running or ProcessStatus.Waiting or ProcessStatus.Suspended;
        var current = open ? live.CurrentStep ?? entered.LastOrDefault() : null;
        if (open && live.CurrentStep is { } here && entered.Count > 0 && !string.Equals(entered[^1], here, StringComparison.Ordinal))
        {
            return Refuse(locale, LiveUnexplained.CurrentStepMismatch, "live.unexplained.current", ("step", here), ("last", entered[^1]));
        }

        return new Builder(process, outline, history, entered, read, live, current, locale, knownProcesses).Build();
    }

    private static LiveCasePath Refuse(WorkbenchLocale locale, LiveUnexplained reason, string key, params (string Name, string Value)[] values) =>
        new() { Unexplained = reason, Reason = FlowTemplates.Text(locale, key, values) };

    private sealed class Builder(
        BrainProcess process,
        ProcessOutline outline,
        List<ProcessHistoryEntry> history,
        List<string> entered,
        RouteReader.Reading read,
        FlowLiveCase live,
        string? current,
        WorkbenchLocale locale,
        IEnumerable<BrainProcess>? knownProcesses)
    {
        private readonly FlowLabels labels = new(process, locale, knownProcesses);

        internal LiveCasePath Build()
        {
            var view = FlowDescriber.Describe(process, locale, knownProcesses);
            var phrases = new Dictionary<(string Step, string Label), string>();
            foreach (var each in outline.Steps)
            {
                foreach (var route in each.Routes)
                {
                    phrases[(each.Id, route.Label)] = FlowDescriber.RouteView(each, route, labels, locale).Phrase;
                }
            }

            var routes = new List<LivePathRoute>();
            // A step the case entered again after a suspension has no route out of it; the reader skips it, so the hops pair up with the entries that moved.
            var moves = Moves();
            for (var index = 0; index < read.Routes.Count && index < moves.Count; index++)
            {
                var (origin, label) = read.Routes[index];
                routes.Add(new LivePathRoute(origin, moves[index].To, label, phrases[(origin, label)]));
            }

            var steps = new List<LivePathStep>();
            foreach (var id in entered.Distinct(StringComparer.Ordinal))
            {
                var left = routes.LastOrDefault(route => string.Equals(route.From, id, StringComparison.Ordinal))?.Phrase;
                var isCurrent = string.Equals(id, current, StringComparison.Ordinal);
                steps.Add(new LivePathStep(id, labels.Step(id), entered.Count(other => string.Equals(other, id, StringComparison.Ordinal)), isCurrent, isCurrent ? null : left));
            }

            return new LiveCasePath
            {
                Unexplained = LiveUnexplained.None,
                Steps = steps,
                Routes = routes,
                CurrentStep = current,
                Waiting = WaitingSentence(),
                Side = new LiveSideFacts(
                    current is null ? null : labels.Step(current),
                    current is null ? null : Why(routes, phrases),
                    current is null || outline.Step(current) is not { } step ? [] : [.. view.Steps.First(item => item.Id == step.Id).Routes.Select(route => route.Phrase)],
                    live.ProcessVersion),
            };
        }

        // The moves of the case: each pair of consecutive step entries that RouteReader read a route for (it skips a step re-entered after a pause).
        private List<(string From, string To)> Moves()
        {
            var moves = new List<(string From, string To)>();
            var positions = Enumerable.Range(0, history.Count).Where(index => history[index].Kind == ProcessHistoryKind.StepEntered).ToList();
            for (var position = 0; position + 1 < positions.Count; position++)
            {
                var from = history[positions[position]].Step!;
                var to = history[positions[position + 1]].Step!;
                var segment = history.Skip(positions[position] + 1).Take(positions[position + 1] - positions[position] - 1);
                if (string.Equals(from, to, StringComparison.Ordinal) && segment.Any(entry => entry.Kind is ProcessHistoryKind.Suspended or ProcessHistoryKind.Resumed))
                {
                    continue;
                }

                moves.Add((from, to));
            }

            return moves;
        }

        private string? Why(List<LivePathRoute> routes, Dictionary<(string Step, string Label), string> phrases)
        {
            var positions = Enumerable.Range(0, history.Count).Where(index => history[index].Kind == ProcessHistoryKind.StepEntered).ToList();
            if (positions.Count < 2)
            {
                return FlowTemplates.Text(locale, "live.why.start");
            }

            var from = history[positions[^2]].Step!;
            var segment = history.Skip(positions[^2] + 1).Take(positions[^1] - positions[^2] - 1).ToList();
            if (string.Equals(from, history[positions[^1]].Step, StringComparison.Ordinal) && segment.Any(entry => entry.Kind is ProcessHistoryKind.Suspended or ProcessHistoryKind.Resumed))
            {
                return FlowTemplates.Text(locale, "live.why.resumed");
            }

            var step = outline.Step(from)!;
            var decided = segment.LastOrDefault(entry => entry.Kind == ProcessHistoryKind.Decided);
            if (step.Kind == OutlineStepKind.Decide && decided is not null && decided.Code is null && FlowJson.TryObject(decided.DataJson) is { } data)
            {
                return DecisionWhy(step, data);
            }

            if (segment.LastOrDefault(entry => entry.Kind == ProcessHistoryKind.TaskCompleted) is { } task && FlowJson.TryObject(task.DataJson)?["outcome"] is JsonValue { } outcome && outcome.GetValueKind() == JsonValueKind.String)
            {
                return FlowTemplates.Text(locale, "live.why.person", ("outcome", labels.Outcome(outcome.GetValue<string>())));
            }

            if (segment.Any(entry => entry.Kind == ProcessHistoryKind.TimedOut))
            {
                return FlowTemplates.Text(locale, "live.why.timeout");
            }

            if (segment.LastOrDefault(entry => entry.Kind == ProcessHistoryKind.EventConsumed) is { } consumed && Text(FlowJson.TryObject(consumed.DataJson)?["event"]) is { } name)
            {
                return FlowTemplates.Text(locale, "live.why.event", ("event", name));
            }

            if (segment.Any(entry => entry.Kind == ProcessHistoryKind.TimerFired))
            {
                return FlowTemplates.Text(locale, "live.why.timer");
            }

            var route = routes.LastOrDefault(item => string.Equals(item.From, from, StringComparison.Ordinal));
            return route is null ? null : FlowTemplates.Text(locale, "live.why.route", ("step", labels.Step(from)), ("phrase", phrases[(route.From, route.Label)]));
        }

        private string DecisionWhy(OutlineStep step, JsonObject data)
        {
            var rule = Text(data["rule"]) ?? step.Rule ?? string.Empty;
            if (data["decided"] is JsonValue flag && flag.TryGetValue<bool>(out var isDecided) && !isDecided)
            {
                return FlowTemplates.Text(locale, "live.why.rule.none", ("rule", rule));
            }

            var rows = data["matchedRowKeys"] is JsonArray array ? [.. array.Select(Text).OfType<string>()] : new List<string>();
            return rows.Count == 0
                ? FlowTemplates.Text(locale, "live.why.rule", ("rule", rule))
                : FlowTemplates.Text(locale, "live.why.rule.row", ("rule", rule), ("rows", FlowText.Codes(locale, rows)));
        }

        private string? WaitingSentence()
        {
            switch (live.Status)
            {
                case ProcessStatus.Completed:
                    return live.Result is { } result
                        ? FlowTemplates.Text(locale, "live.wait.completed", ("result", labels.Result(result)))
                        : FlowTemplates.Text(locale, "live.wait.completed.plain");
                case ProcessStatus.Cancelled:
                    return FlowTemplates.Text(locale, "live.wait.cancelled");
                case ProcessStatus.Suspended:
                    return live.ProblemCode is { } code
                        ? FlowTemplates.Text(locale, "live.wait.suspended", ("code", code))
                        : FlowTemplates.Text(locale, "live.wait.suspended.plain");
                default:
                    break;
            }

            if (current is null || outline.Step(current) is not { } step)
            {
                return FlowTemplates.Text(locale, "live.wait.notStarted");
            }

            var time = live.Wait.Until is { } moment
                ? moment.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC"
                : null;
            var until = time is null ? string.Empty : FlowTemplates.Text(locale, "live.wait.until", ("time", time));
            return step.Kind switch
            {
                OutlineStepKind.HumanTask => FlowTemplates.Text(locale, "live.wait.person", ("queue", labels.Queue((live.Wait.Queues.Count > 0 ? live.Wait.Queues[0] : step.Queue) ?? string.Empty))) + until,
                OutlineStepKind.Wait => FlowTemplates.Text(locale, "live.wait.event", ("event", (live.Wait.Events.Count > 0 ? live.Wait.Events[0] : step.Event) ?? string.Empty)) + until,
                OutlineStepKind.Timer => time is null ? FlowTemplates.Text(locale, "live.wait.running") : FlowTemplates.Text(locale, "live.wait.timer", ("time", time)),
                OutlineStepKind.Subprocess => FlowTemplates.Text(locale, "live.wait.child", ("process", ChildTitle(step.Process))),
                OutlineStepKind.Read or OutlineStepKind.Action or OutlineStepKind.Ai or OutlineStepKind.Poll when live.Status == ProcessStatus.Waiting =>
                    FlowTemplates.Text(locale, "live.wait.effect", ("port", step.Port ?? labels.Step(step.Id))) + until,
                _ => FlowTemplates.Text(locale, "live.wait.running") + until,
            };
        }

        // The title the child's own document gives (the highest version among the known documents); the id as code when the child is not known.
        private string ChildTitle(string? processId)
        {
            var child = (knownProcesses ?? []).Where(candidate => string.Equals(candidate.Id, processId, StringComparison.Ordinal))
                .OrderByDescending(candidate => ProcessOutline.From(candidate).Version).FirstOrDefault();
            return child is null ? FlowText.Code(processId ?? string.Empty) : new FlowLabels(child, locale).Title();
        }

        private static string? Text(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
    }
}
