using System.Globalization;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Demo;

/// <summary>
/// A live source the demo scripts in memory: open cases made by replaying the embedded scenarios of the reference processes up to
/// different points. Every reference is synthetic (<c>demo-0001</c> and so on) and nothing in a case names or looks like a person. The
/// source only reads; nothing in the demo acts on a case.
/// </summary>
internal sealed class DemoLive : IFlowLiveSource
{
    private static readonly DateTimeOffset Origin = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly List<(FlowLiveCase Case, DateTimeOffset Since)> cases = [];

    public DemoLive(IReadOnlyDictionary<string, BrainProcess> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var known = processes.Values.ToList();
        var number = 0;
        foreach (var process in processes.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            foreach (var scenario in ProcessOutline.From(process).Scenarios)
            {
                var total = FlowReplay.Start(process, scenario, null, known).Total;
                foreach (var fraction in new[] { 2, 3 })
                {
                    var stop = Math.Max(1, total * (fraction - 1) / fraction);
                    for (var copy = 0; copy < (fraction == 2 ? 2 : 1); copy++)
                    {
                        var replay = FlowReplay.Start(process, scenario, null, known);
                        for (var played = 0; played < stop && !replay.IsFinished; played++)
                        {
                            _ = replay.Next();
                        }

                        number++;
                        var live = FlowLiveScripts.FromSimulation("demo-" + number.ToString("0000", CultureInfo.InvariantCulture), replay.Session);
                        if (live.Status is ProcessStatus.Running or ProcessStatus.Waiting or ProcessStatus.Suspended)
                        {
                            cases.Add((live, Origin.AddMinutes(number * 11)));
                        }
                    }
                }
            }
        }
    }

    private static bool IsOpen(FlowLiveCase live) => live.Status is ProcessStatus.Running or ProcessStatus.Waiting or ProcessStatus.Suspended;

    /// <inheritdoc />
    public Task<FlowLiveCounts> GetCountsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new FlowLiveCounts(
        [.. cases.Select(item => item.Case).Where(IsOpen)
            .GroupBy(item => (item.ProcessId, item.CurrentStep))
            .Select(group => new FlowLiveCount(group.Key.ProcessId, group.Key.CurrentStep, group.Count(), group.Count(item => item.Wait.Queues.Count > 0)))]));

    /// <inheritdoc />
    public Task<FlowLiveCasePage> ListCasesAsync(FlowLiveCaseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = cases.Where(item => item.Case.ProcessId == query.ProcessId && (query.Step is null || item.Case.CurrentStep == query.Step))
            .OrderByDescending(item => item.Since).ToList();
        var from = query.After is null ? 0 : int.Parse(query.After, CultureInfo.InvariantCulture);
        var page = all.Skip(from).Take(query.Limit)
            .Select(item => new FlowLiveCaseLine(item.Case.CaseRef, item.Case.ProcessVersion, item.Case.CurrentStep, item.Case.Wait.Queues.Count > 0, item.Since)).ToList();
        var next = from + query.Limit < all.Count ? (from + query.Limit).ToString(CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new FlowLiveCasePage(page, next));
    }

    /// <inheritdoc />
    public Task<FlowLiveCase?> GetCaseAsync(string caseRef, CancellationToken cancellationToken = default) =>
        Task.FromResult<FlowLiveCase?>(cases.Select(item => item.Case).FirstOrDefault(item => item.CaseRef == caseRef));
}
