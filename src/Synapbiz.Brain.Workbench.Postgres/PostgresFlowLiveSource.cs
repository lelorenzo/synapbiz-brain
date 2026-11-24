using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Postgres;

/// <summary>
/// The live source of the process workbench over the durable PostgreSQL host, for one tenant. It answers from the cockpit queries of
/// <see cref="IProcessEngine"/> and adds no query of its own; every call only reads. The case reference it gives is the instance id,
/// a random identifier the host generates: never the business key, which a clinic may well derive from a person.
/// </summary>
/// <remarks>
/// <para>
/// A case is <i>open</i> while its status is running, waiting or suspended; an instance that was created but not started yet counts as
/// running, as the engine reports it. Every instance counts as a case of its own process, a child instance too, so the numbers of a
/// subprocess are the cases running that subprocess and the numbers of its parent are the cases of the parent.
/// </para>
/// <para>
/// The counts are built by walking the open instances in pages (<see cref="ProcessInstanceQuery"/>, 500 at a time), so a call costs one
/// query for every 500 open cases; <see cref="MaxCountedCases"/> bounds it and the answer says when it stopped there. The history is
/// read with <c>GetHistoryAsync</c>, which decrypts the data the host protects; <see cref="FlowLiveEntry.Of"/> then drops everything but
/// the members an explanation reads, so no fact a rule judged leaves this class.
/// </para>
/// </remarks>
public sealed class PostgresFlowLiveSource : IFlowLiveSource
{
    /// <summary>The most open cases a counting call walks before it stops and says its numbers are partial.</summary>
    public const int MaxCountedCases = 20_000;

    /// <summary>The most pages a list call reads while it looks for the first page that holds a case on the wanted step.</summary>
    public const int MaxPagesScanned = 20;

    private const int DefaultPageSize = 500;

    private static readonly ProcessStatus[] OpenStatuses = [ProcessStatus.Running, ProcessStatus.Waiting, ProcessStatus.Suspended];

    private readonly IProcessEngine engine;
    private readonly string tenantId;
    private readonly int pageSize;
    private readonly int maxCounted;

    /// <summary>Creates the source of one tenant.</summary>
    /// <param name="engine">The engine of the host.</param>
    /// <param name="tenantId">The tenant whose cases are read; a source never reads another tenant's rows.</param>
    public PostgresFlowLiveSource(IProcessEngine engine, string tenantId)
        : this(engine, tenantId, DefaultPageSize, MaxCountedCases)
    {
    }

    internal PostgresFlowLiveSource(IProcessEngine engine, string tenantId, int pageSize, int maxCounted)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        this.engine = engine;
        this.tenantId = tenantId;
        this.pageSize = pageSize;
        this.maxCounted = maxCounted;
    }

    /// <inheritdoc />
    public async Task<FlowLiveCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new Dictionary<(string Process, string? Step), (int Open, int Waiting)>();
        var seen = 0;
        string? after = null;
        var partial = false;
        do
        {
            var page = await engine.ListInstancesAsync(new ProcessInstanceQuery(tenantId, Statuses: OpenStatuses, RootsOnly: false, Limit: pageSize, After: after), cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                var key = (item.ProcessId, item.CurrentStep);
                var (open, waiting) = rows.GetValueOrDefault(key);
                rows[key] = (open + 1, waiting + (item.OpenTasks > 0 ? 1 : 0));
            }

            seen += page.Items.Count;
            after = page.Next;
            partial = after is not null && seen >= maxCounted;
        }
        while (after is not null && !partial);

        return new FlowLiveCounts(
            [.. rows.OrderBy(pair => pair.Key.Process, StringComparer.Ordinal).ThenBy(pair => pair.Key.Step, StringComparer.Ordinal)
                .Select(pair => new FlowLiveCount(pair.Key.Process, pair.Key.Step, pair.Value.Open, pair.Value.Waiting))],
            partial);
    }

    /// <inheritdoc />
    public async Task<FlowLiveCasePage> ListCasesAsync(FlowLiveCaseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var found = new List<FlowLiveCaseLine>();
        var after = query.After;
        for (var scanned = 0; scanned < MaxPagesScanned; scanned++)
        {
            var page = await engine.ListInstancesAsync(
                new ProcessInstanceQuery(tenantId, ProcessId: query.ProcessId, Statuses: OpenStatuses, RootsOnly: false, Limit: query.Limit, After: after),
                cancellationToken).ConfigureAwait(false);
            found.AddRange(page.Items
                .Where(item => query.Step is null || string.Equals(item.CurrentStep, query.Step, StringComparison.Ordinal))
                .Select(item => new FlowLiveCaseLine(item.InstanceId, item.ProcessVersion, item.CurrentStep, item.OpenTasks > 0, item.CreatedAt)));
            after = page.Next;
            if (found.Count > 0 || after is null)
            {
                break;
            }
        }

        return new FlowLiveCasePage(found, after);
    }

    /// <inheritdoc />
    public async Task<FlowLiveCase?> GetCaseAsync(string caseRef, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(caseRef);
        var view = await engine.GetCaseAsync(tenantId, caseRef, cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return null;
        }

        var history = await engine.GetHistoryAsync(tenantId, caseRef, cancellationToken).ConfigureAwait(false);
        var instance = view.Instance;
        // The events and the timer of a case are read for its whole tree, so a case that has children does not claim them as its own.
        var alone = view.Descendants.Count == 0;
        var wait = new FlowLiveWait(
            [.. view.OpenTasks.Where(task => string.Equals(task.InstanceId, caseRef, StringComparison.Ordinal)).Select(task => task.Queue).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            alone ? view.WaitingForEvents : [],
            alone ? view.NextTimerDue : null);
        return new FlowLiveCase(
            instance.InstanceId,
            instance.ProcessId,
            instance.ProcessVersion,
            instance.Status,
            instance.CurrentStep,
            instance.Result,
            instance.ProblemCode,
            [.. history.Select(FlowLiveEntry.Of)],
            wait);
    }
}
