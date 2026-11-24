using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>A live source over cases a test scripts from the replays of the reference documents; it counts its calls and can fail or wait on demand.</summary>
internal sealed class ScriptedLiveSource : IFlowLiveSource
{
    private static IReadOnlyList<BrainProcess> Children => [.. FlowDocs.Reference.Select(item => item.Process)];

    internal List<FlowLiveCase> Cases { get; } = [];

    internal int CountCalls { get; private set; }

    internal int ListCalls { get; private set; }

    internal int CaseCalls { get; private set; }

    internal bool FailCounts { get; set; }

    internal bool FailList { get; set; }

    internal bool FailCase { get; set; }

    /// <summary>When set, the counts wait for this task before they answer.</summary>
    internal Task? CountsGate { get; set; }

    internal int PageSize { get; set; } = 25;

    /// <summary>Every query the list was asked, in order.</summary>
    internal List<FlowLiveCaseQuery> Queries { get; } = [];

    /// <summary>Every open case the replays of the reference documents are in after half their commands, with an opaque reference of the form <c>case-&lt;n&gt;</c>.</summary>
    internal static ScriptedLiveSource Reference(int copies = 1)
    {
        var source = new ScriptedLiveSource();
        var number = 0;
        foreach (var id in FlowDocs.ReferenceIds)
        {
            var process = FlowDocs.Process(id);
            foreach (var scenario in ProcessOutline.From(process).Scenarios)
            {
                var total = FlowReplay.Start(process, scenario, null, Children).Total;
                for (var copy = 0; copy < copies; copy++)
                {
                    var replay = FlowReplay.Start(process, scenario, null, Children);
                    for (var played = 0; played < total / 2 && !replay.IsFinished; played++)
                    {
                        _ = replay.Next();
                    }

                    number++;
                    var live = FlowLiveScripts.FromSimulation("case-" + number.ToString("000", System.Globalization.CultureInfo.InvariantCulture), replay.Session);
                    if (live.Status is ProcessStatus.Running or ProcessStatus.Waiting or ProcessStatus.Suspended)
                    {
                        source.Cases.Add(live);
                    }
                }
            }
        }

        return source;
    }

    internal static bool IsOpen(FlowLiveCase live) => live.Status is ProcessStatus.Running or ProcessStatus.Waiting or ProcessStatus.Suspended;

    internal FlowLiveCounts CurrentCounts() => new(
        [.. Cases.Where(IsOpen)
            .GroupBy(item => (item.ProcessId, item.CurrentStep))
            .Select(group => new FlowLiveCount(group.Key.ProcessId, group.Key.CurrentStep, group.Count(), group.Count(item => item.Wait.Queues.Count > 0)))]);

    public async Task<FlowLiveCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        CountCalls++;
        var counts = CurrentCounts();
        if (CountsGate is { } gate)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return FailCounts ? throw new InvalidOperationException("scripted failure") : counts;
    }

    public Task<FlowLiveCasePage> ListCasesAsync(FlowLiveCaseQuery query, CancellationToken cancellationToken = default)
    {
        ListCalls++;
        Queries.Add(query);
        if (FailList)
        {
            throw new InvalidOperationException("scripted failure");
        }

        var all = Cases.Where(item => IsOpen(item) && item.ProcessId == query.ProcessId && (query.Step is null || item.CurrentStep == query.Step)).ToList();
        var from = query.After is null ? 0 : int.Parse(query.After, System.Globalization.CultureInfo.InvariantCulture);
        var size = Math.Min(query.Limit, PageSize);
        var page = all.Skip(from).Take(size).Select(item => new FlowLiveCaseLine(item.CaseRef, item.ProcessVersion, item.CurrentStep, item.Wait.Queues.Count > 0, new DateTimeOffset(2026, 3, 2, 15, 0, 0, TimeSpan.Zero))).ToList();
        var next = from + size < all.Count ? (from + size).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new FlowLiveCasePage(page, next));
    }

    public Task<FlowLiveCase?> GetCaseAsync(string caseRef, CancellationToken cancellationToken = default)
    {
        CaseCalls++;
        return FailCase ? throw new InvalidOperationException("scripted failure") : Task.FromResult(Cases.FirstOrDefault(item => item.CaseRef == caseRef));
    }
}
