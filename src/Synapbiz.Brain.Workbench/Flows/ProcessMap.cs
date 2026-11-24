using System.Collections.ObjectModel;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>What a process uses: the kinds of piece a map names inside a process.</summary>
public enum MapPieceKind
{
    /// <summary>A rule a <c>decide</c> or <c>poll</c> step judges with.</summary>
    Rule = 0,

    /// <summary>A connector to another system: the port of a <c>read</c>, <c>action</c> or <c>poll</c> step.</summary>
    Connector = 1,

    /// <summary>The queue of a task for a person.</summary>
    PersonTask = 2,

    /// <summary>A step that asks an AI model.</summary>
    Ai = 3,
}

/// <summary>One piece a process uses, with the label a person reads.</summary>
/// <param name="Kind">The kind of piece.</param>
/// <param name="Name">The name as the document writes it: a rule id, a port, a queue or (for an AI step) the step id.</param>
/// <param name="Label">The label: the document's own (<c>meta.display</c>), else the host's, else <paramref name="Name"/>.</param>
/// <param name="LabelIsName">True when no label was found and <paramref name="Label"/> is the raw name, which a screen shows as code.</param>
public sealed record MapUse(MapPieceKind Kind, string Name, string Label, bool LabelIsName)
{
    /// <summary>The key a host's label dictionary uses for this piece (<see cref="ProcessMap.LabelKey"/>).</summary>
    public string LabelKey => ProcessMap.LabelKey(Kind, Name);
}

/// <summary>The counts a card shows for a process.</summary>
/// <param name="Steps">The number of steps.</param>
/// <param name="Scenarios">The number of embedded scenarios (its tests).</param>
/// <param name="ScenariosPass">True when every scenario passes (also true when there are none).</param>
/// <param name="RoutesNotTried">The routes no scenario takes.</param>
public sealed record MapCounts(int Steps, int Scenarios, bool ScenariosPass, int RoutesNotTried);

/// <summary>A process of the map: what every card, frame and lane shows about it.</summary>
public sealed record MapProcess
{
    /// <summary>The id of the document.</summary>
    public required string Id { get; init; }

    /// <summary>The version of the document that the map describes (the highest of its id).</summary>
    public required long Version { get; init; }

    /// <summary>The title in the asked language.</summary>
    public required string Title { get; init; }

    /// <summary>The kind of the process.</summary>
    public required FlowKind Kind { get; init; }

    /// <summary>True for a main process the document gives no way to begin: no trigger and nothing starts it.</summary>
    public required bool NoStartDefined { get; init; }

    /// <summary>The counts.</summary>
    public required MapCounts Counts { get; init; }

    /// <summary>What it uses, deduplicated, in a fixed order: rules, connectors, queues of person tasks, AI steps; each group in document order.</summary>
    public required IReadOnlyList<MapUse> Uses { get; init; }

    /// <summary>What starts it, one sentence per trigger (<c>Starts when `case.opened` arrives, one case for each `case_ref`.</c>).</summary>
    public required IReadOnlyList<string> Triggers { get; init; }

    /// <summary>The ids of the processes that start it, other than itself, in ordinal order.</summary>
    public required IReadOnlyList<string> StartedBy { get; init; }

    /// <summary>The processes its steps start, in order of first use.</summary>
    public required IReadOnlyList<MapChildLink> Starts { get; init; }
}

/// <summary>A process another one starts.</summary>
/// <param name="ProcessId">The id as the step writes it.</param>
/// <param name="Title">The title of the child, or the id when the child is not loaded.</param>
/// <param name="IsMissing">True when no document of the set has that id.</param>
public sealed record MapChildLink(string ProcessId, string Title, bool IsMissing);

/// <summary>A subprocess card under a phase of a main process.</summary>
/// <param name="Process">The subprocess; for a child that is not loaded, a placeholder with the id as title and no counts.</param>
/// <param name="IsMissing">True when the child is not among the loaded processes.</param>
/// <param name="AlsoStartedBy">The titles of the other processes that start it (not this parent and not itself), in ordinal order of their ids: "also started by ...".</param>
public sealed record MapChild(MapProcess Process, bool IsMissing, IReadOnlyList<string> AlsoStartedBy);

/// <summary>A phase of a main process: a stage of its document, or the steps that have none.</summary>
/// <param name="StageId">The stage id; null for the steps with no stage ("No phase").</param>
/// <param name="Label">The stage label; empty for the steps with no stage (the screen writes its own words).</param>
/// <param name="EndsWhen">What ends the phase, as a sentence, when the routes leaving the stage say it; null otherwise.</param>
/// <param name="Subprocesses">The subprocesses the steps of the phase start, in document order.</param>
public sealed record MapPhase(string? StageId, string Label, string? EndsWhen, IReadOnlyList<MapChild> Subprocesses)
{
    /// <summary>True for the steps with no stage.</summary>
    public bool IsLoose => StageId is null;
}

/// <summary>A main process with its starts, phases and ends.</summary>
/// <param name="Process">The process.</param>
/// <param name="Starts">What starts a case: one entry per event trigger (a manual start has none).</param>
/// <param name="Phases">The phases in the order of the stages, then "No phase" when a subprocess is started from a step with no stage.</param>
/// <param name="Ends">The labels of the end steps, each once, in document order.</param>
public sealed record MapMain(MapProcess Process, IReadOnlyList<string> Starts, IReadOnlyList<MapPhase> Phases, IReadOnlyList<string> Ends);

/// <summary>A scheduled task with its schedule sentences.</summary>
/// <param name="Process">The process.</param>
/// <param name="Schedules">One sentence per trigger.</param>
public sealed record MapScheduled(MapProcess Process, IReadOnlyList<string> Schedules);

/// <summary>A piece more than one process uses.</summary>
/// <param name="Use">The piece.</param>
/// <param name="UsedBy">The ids of the processes that use it, in ordinal order.</param>
public sealed record MapShared(MapUse Use, IReadOnlyList<string> UsedBy)
{
    /// <summary>How many processes use it.</summary>
    public int Count => UsedBy.Count;
}

/// <summary>
/// The structure of a clinic's processes, computed from the documents alone: main processes with their phases and the subprocesses
/// each phase starts, scheduled tasks in their own lane, what every process uses, the pieces shared between processes. Deterministic:
/// processes in the order of their ids, stages and steps in document order.
/// </summary>
public sealed record ProcessMap
{
    /// <summary>The main processes, in the order of their ids.</summary>
    public required IReadOnlyList<MapMain> Mains { get; init; }

    /// <summary>The scheduled tasks, in the order of their ids.</summary>
    public required IReadOnlyList<MapScheduled> Scheduled { get; init; }

    /// <summary>Every process by id (the highest version of each id), in the order of the ids.</summary>
    public required IReadOnlyList<MapProcess> Processes { get; init; }

    /// <summary>Pieces used by more than one process, rules first, then connectors, queues and AI steps, each in the order of the first process that uses them.</summary>
    public required IReadOnlyList<MapShared> Shared { get; init; }

    /// <summary>The groups of processes that start each other (a process that starts itself counts); each group lists its ids in ordinal order. Reported, never drawn twice.</summary>
    public required IReadOnlyList<IReadOnlyList<string>> Cycles { get; init; }

    /// <summary>The subprocesses no main process or scheduled task reaches (the processes of a cycle that nothing outside starts, for example), in the order of their ids.</summary>
    public required IReadOnlyList<MapProcess> Detached { get; init; }

    /// <summary>The number of distinct rules the processes use.</summary>
    public int RuleCount => Processes.SelectMany(process => process.Uses).Where(use => use.Kind == MapPieceKind.Rule).Select(use => use.Name).Distinct(StringComparer.Ordinal).Count();

    /// <summary>The number of subprocesses.</summary>
    public int SubprocessCount => Processes.Count(process => process.Kind == FlowKind.Subprocess);

    /// <summary>The process with an id; null when the map has none.</summary>
    /// <param name="id">The id of the document.</param>
    public MapProcess? Process(string id) => Processes.FirstOrDefault(process => string.Equals(process.Id, id, StringComparison.Ordinal));

    /// <summary>The key a host's label dictionary uses: <c>rule:</c>, <c>connector:</c>, <c>queue:</c> or <c>ai:</c> and the name.</summary>
    /// <param name="kind">The kind of piece.</param>
    /// <param name="name">The name as the document writes it.</param>
    public static string LabelKey(MapPieceKind kind, string name) => kind switch
    {
        MapPieceKind.Rule => "rule:" + name,
        MapPieceKind.Connector => "connector:" + name,
        MapPieceKind.PersonTask => "queue:" + name,
        MapPieceKind.Ai => "ai:" + name,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
    };

    /// <summary>Builds the map of a set of processes.</summary>
    /// <param name="processes">The loaded processes. When several documents share an id, the highest version (then the lowest content hash) describes it.</param>
    /// <param name="locale">The language of titles, labels and sentences.</param>
    /// <param name="labels">The host's labels by <see cref="LabelKey"/>, for the current language; null for none.</param>
    /// <param name="reports">The report of a document, so that no scenario is played twice; null plays them.</param>
    public static ProcessMap From(IEnumerable<BrainProcess> processes, WorkbenchLocale locale, IReadOnlyDictionary<string, string>? labels = null, Func<BrainProcess, FlowScenarioReport>? reports = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return new MapBuilder(processes, locale, labels, reports ?? (process => FlowScenarioReport.Of(process, locale))).Build();
    }

    private sealed class MapBuilder
    {
        private readonly WorkbenchLocale locale;
        private readonly IReadOnlyDictionary<string, string> labels;
        private readonly List<BrainProcess> set;
        private readonly Dictionary<string, FlowEntry> entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BrainProcess> byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProcessOutline> outlines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FlowWords> words = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MapProcess> built = new(StringComparer.Ordinal);

        internal MapBuilder(IEnumerable<BrainProcess> processes, WorkbenchLocale locale, IReadOnlyDictionary<string, string>? labels, Func<BrainProcess, FlowScenarioReport> reports)
        {
            this.locale = locale;
            this.labels = labels ?? new Dictionary<string, string>(0, StringComparer.Ordinal);
            var all = processes.ToList();
            set = [.. all
                .Select(process => (process, outline: ProcessOutline.From(process)))
                .OrderBy(item => item.outline.Id, StringComparer.Ordinal)
                .ThenByDescending(item => item.outline.Version)
                .ThenBy(item => item.outline.ContentHash, StringComparer.Ordinal)
                .GroupBy(item => item.outline.Id, StringComparer.Ordinal)
                .Select(group => group.First().process)];
            foreach (var entry in FlowLibrary.From(set, locale, reports))
            {
                entries[entry.Id] = entry;
            }

            foreach (var process in set)
            {
                var outline = ProcessOutline.From(process);
                byId[outline.Id] = process;
                outlines[outline.Id] = outline;
                words[outline.Id] = FlowWords.For(process, locale, set);
            }
        }

        internal ProcessMap Build()
        {
            var all = set.Select(process => Process(ProcessOutline.From(process).Id)).ToList();
            var mains = all.Where(process => process.Kind == FlowKind.Main).Select(MainOf).ToList();
            var scheduled = all.Where(process => process.Kind == FlowKind.Scheduled)
                .Select(process => new MapScheduled(process, process.Triggers)).ToList();
            var cycles = Cycles();
            var reached = Reached(all);
            return new ProcessMap
            {
                Mains = new ReadOnlyCollection<MapMain>(mains),
                Scheduled = new ReadOnlyCollection<MapScheduled>(scheduled),
                Processes = new ReadOnlyCollection<MapProcess>(all),
                Shared = Shared(all),
                Cycles = cycles,
                Detached = [.. all.Where(process => process.Kind == FlowKind.Subprocess && !reached.Contains(process.Id))],
            };
        }

        // ----- one process ------------------------------------------------------------------------------------------------

        private MapProcess Process(string id)
        {
            if (built.TryGetValue(id, out var known))
            {
                return known;
            }

            var entry = entries[id];
            var outline = outlines[id];
            var starts = outline.Steps.Select(step => step.Process).OfType<string>().Distinct(StringComparer.Ordinal)
                .Select(child => entries.TryGetValue(child, out var found) ? new MapChildLink(child, found.Title, false) : new MapChildLink(child, child, true))
                .ToList();
            var process = new MapProcess
            {
                Id = id,
                Version = outline.Version,
                Title = entry.Title,
                Kind = entry.Kind,
                NoStartDefined = entry.NoStartDefined,
                Counts = new MapCounts(outline.Steps.Count, entry.ScenarioCount, entry.ScenariosPass, entry.TotalRoutes - entry.CoveredRoutes),
                Uses = Uses(byId[id], outline),
                Triggers = entry.Triggers,
                StartedBy = [.. entry.StartedBy.Where(other => !string.Equals(other, id, StringComparison.Ordinal))],
                Starts = starts,
            };
            built[id] = process;
            return process;
        }

        private List<MapUse> Uses(BrainProcess process, ProcessOutline outline)
        {
            var display = ProcessDisplay.From(process);
            var language = locale == WorkbenchLocale.Es ? "es" : "en";
            var result = new List<MapUse>();
            var seen = new HashSet<(MapPieceKind, string)>();

            void Add(MapPieceKind kind, string? name, string? own)
            {
                if (name is not null && seen.Add((kind, name)))
                {
                    var label = own ?? (labels.TryGetValue(LabelKey(kind, name), out var host) && !string.IsNullOrWhiteSpace(host) ? host : null);
                    result.Add(new MapUse(kind, name, label ?? name, label is null));
                }
            }

            foreach (var step in outline.Steps.Where(step => step.Kind is OutlineStepKind.Decide or OutlineStepKind.Poll))
            {
                Add(MapPieceKind.Rule, step.Rule, null);
            }

            foreach (var step in outline.Steps.Where(step => step.Kind is OutlineStepKind.Read or OutlineStepKind.Action or OutlineStepKind.Poll))
            {
                Add(MapPieceKind.Connector, step.Port, null);
            }

            foreach (var queue in outline.Steps.Where(step => step.Kind == OutlineStepKind.HumanTask).Select(step => step.Queue))
            {
                Add(MapPieceKind.PersonTask, queue, queue is null ? null : display.Queue(language, queue));
            }

            foreach (var id in outline.Steps.Where(step => step.Kind == OutlineStepKind.Ai).Select(step => step.Id))
            {
                Add(MapPieceKind.Ai, id, display.Step(language, id));
            }

            return result;
        }

        // ----- main processes ---------------------------------------------------------------------------------------------

        private MapMain MainOf(MapProcess process)
        {
            var outline = outlines[process.Id];
            var wording = words[process.Id];
            var starts = outline.Triggers.OfType<EventTriggerDeclaration>().Select(trigger => FlowDescriber.DescribeTrigger(trigger, locale)).ToList();
            var phases = new List<MapPhase>();
            foreach (var stage in outline.Stages)
            {
                var steps = outline.Steps.Where(step => string.Equals(step.Stage, stage, StringComparison.Ordinal)).ToList();
                phases.Add(new MapPhase(stage, wording.Stage(stage), EndsWhen(outline, wording, stage, steps), Children(process, steps)));
            }

            var loose = Children(process, [.. outline.Steps.Where(step => step.Stage is null)]);
            if (loose.Count > 0)
            {
                phases.Add(new MapPhase(null, string.Empty, null, loose));
            }

            var ends = outline.Steps.Where(step => step.Kind == OutlineStepKind.End).Select(step => wording.Step(step.Id)).Distinct(StringComparer.Ordinal).ToList();
            return new MapMain(process, starts, phases, ends);
        }

        private List<MapChild> Children(MapProcess parent, List<OutlineStep> steps)
        {
            var result = new List<MapChild>();
            foreach (var id in steps.Where(step => step.Kind == OutlineStepKind.Subprocess).Select(step => step.Process!).Distinct(StringComparer.Ordinal))
            {
                if (!entries.ContainsKey(id))
                {
                    var missing = new MapProcess
                    {
                        Id = id,
                        Version = 0,
                        Title = id,
                        Kind = FlowKind.Subprocess,
                        NoStartDefined = false,
                        Counts = new MapCounts(0, 0, true, 0),
                        Uses = [],
                        Triggers = [],
                        StartedBy = [parent.Id],
                        Starts = [],
                    };
                    result.Add(new MapChild(missing, true, []));
                    continue;
                }

                var child = Process(id);
                var others = child.StartedBy.Where(other => !string.Equals(other, parent.Id, StringComparison.Ordinal)).Select(other => entries.TryGetValue(other, out var found) ? found.Title : other).ToList();
                result.Add(new MapChild(child, false, others));
            }

            return result;
        }

        private string? EndsWhen(ProcessOutline outline, FlowWords wording, string stage, List<OutlineStep> steps)
        {
            var exits = steps
                .SelectMany(step => step.Routes.Select(route => outline.Step(route.Target)))
                .Where(target => target?.Stage is not null && !string.Equals(target.Stage, stage, StringComparison.Ordinal))
                .Select(target => target!.Stage!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(other => outline.Stages.ToList().IndexOf(other))
                .Select(wording.Stage)
                .ToList();
            if (exits.Count > 0)
            {
                return FlowTemplates.Text(locale, "map.phase.exits", ("stages", FlowText.Join(locale, exits)));
            }

            var results = steps.Where(step => step.Kind == OutlineStepKind.End && step.Result is not null).Select(step => wording.Result(step.Result!)).Distinct(StringComparer.Ordinal).ToList();
            return results.Count > 0 ? FlowTemplates.Text(locale, "map.phase.ends", ("results", FlowText.Join(locale, results))) : null;
        }

        // ----- shared pieces, cycles, reach -------------------------------------------------------------------------------

        private static ReadOnlyCollection<MapShared> Shared(List<MapProcess> all) =>
            new([.. all
                .SelectMany(process => process.Uses.Select(use => (process, use)))
                .GroupBy(item => (item.use.Kind, item.use.Name))
                .Where(group => group.Select(item => item.process.Id).Distinct(StringComparer.Ordinal).Count() > 1)
                .OrderBy(group => group.Key.Kind)
                .Select(group => new MapShared(group.First().use, [.. group.Select(item => item.process.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]))]);

        // Strongly connected groups of the "starts" relation (Tarjan), each in ordinal order, ordered by their first id.
        private ReadOnlyCollection<IReadOnlyList<string>> Cycles()
        {
            var ids = outlines.Keys.Order(StringComparer.Ordinal).ToList();
            var edges = ids.ToDictionary(id => id, id => outlines[id].Steps.Select(step => step.Process).OfType<string>().Where(outlines.ContainsKey).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var low = new Dictionary<string, int>(StringComparer.Ordinal);
            var onStack = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            var groups = new List<IReadOnlyList<string>>();
            var counter = 0;

            void Visit(string id)
            {
                index[id] = low[id] = counter++;
                stack.Push(id);
                onStack.Add(id);
                foreach (var next in edges[id])
                {
                    if (!index.TryGetValue(next, out var seenAt))
                    {
                        Visit(next);
                        low[id] = Math.Min(low[id], low[next]);
                    }
                    else if (onStack.Contains(next))
                    {
                        low[id] = Math.Min(low[id], seenAt);
                    }
                }

                if (low[id] != index[id])
                {
                    return;
                }

                var group = new List<string>();
                string member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    group.Add(member);
                }
                while (!string.Equals(member, id, StringComparison.Ordinal));
                if (group.Count > 1 || edges[id].Contains(id, StringComparer.Ordinal))
                {
                    groups.Add([.. group.Order(StringComparer.Ordinal)]);
                }
            }

            foreach (var id in ids.Where(id => !index.ContainsKey(id)))
            {
                Visit(id);
            }

            return new ReadOnlyCollection<IReadOnlyList<string>>([.. groups.OrderBy(group => group[0], StringComparer.Ordinal)]);
        }

        // The processes a main process or a scheduled task reaches by following "starts".
        private HashSet<string> Reached(List<MapProcess> all)
        {
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(all.Where(process => process.Kind != FlowKind.Subprocess).Select(process => process.Id));
            foreach (var id in queue)
            {
                reached.Add(id);
            }

            while (queue.TryDequeue(out var id))
            {
                foreach (var child in Process(id).Starts.Where(link => !link.IsMissing).Select(link => link.ProcessId).Where(reached.Add))
                {
                    queue.Enqueue(child);
                }
            }

            return reached;
        }
    }
}
