using System.Data;
using Npgsql;
using NpgsqlTypes;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres;

/// <summary>
/// The read-only cockpit queries of <see cref="PostgresProcessStore"/>. Every statement filters by tenant first, takes values only
/// as parameters, and never selects the <c>state</c> column. A descendant walk is a recursive query bounded by depth 16. Every ordering and comparison on an instance id or task id is bytewise (<c>collate "C"</c>), so it does not depend on the locale of the database.
/// </summary>
internal sealed partial class PostgresProcessStore
{
    private const string SummaryColumns = "tenant_id, instance_id, process_id, process_version, business_key, status, stage, current_step, result, problem_code, parent_instance_id, created_at, updated_at";

    // The walk from every row of "roots" down the parent link, the suspended descendants and the open tasks of each root's tree.
    // Status 3 is Suspended; task status 1 is Open. The depth of a root is 0 and a root's descendants are counted to depth 16.
    private const string WalkCtes =
        "walk as (select r.instance_id as root_id, r.instance_id as id, 0 as depth from roots r "
        + "union all select w.root_id, ch.instance_id, w.depth + 1 from walk w join {instance} ch on ch.tenant_id = @t and ch.parent_instance_id = w.id where w.depth < 16), "
        + "susp as (select w.root_id, count(*) as n from walk w join {instance} d on d.tenant_id = @t and d.instance_id = w.id where w.depth > 0 and d.status = 3 group by w.root_id), "
        + "tree_tasks as (select w.root_id, count(*) as n from walk w join {task} tk on tk.tenant_id = @t and tk.instance_id = w.id and tk.status = 1 group by w.root_id) ";

    private const string SummarySelect =
        "select r.tenant_id, r.instance_id, r.process_id, r.process_version, r.business_key, r.status, r.stage, r.current_step, r.result, r.problem_code, r.parent_instance_id, r.created_at, r.updated_at, "
        + "(select count(*) from {task} o where o.tenant_id = @t and o.instance_id = r.instance_id and o.status = 1) as open_tasks, "
        + "coalesce(s.n, 0) as suspended_descendants "
        + "from roots r left join susp s on s.root_id = r.instance_id left join tree_tasks tt on tt.root_id = r.instance_id "
        + "where (not @attn or r.status = 3 or coalesce(s.n, 0) > 0 or coalesce(tt.n, 0) > 0) ";

    private const string CandidateCte =
        "cand as (select " + SummaryColumns + " from {instance} "
        + "where tenant_id = @t "
        + "and (@p is null or process_id = @p) "
        + "and (@statuses is null or (case when status = 0 then 1 else status end) = any(@statuses)) "
        + "and (@stage is null or stage = @stage) "
        + "and (not @roots or parent_instance_id is null) "
        + "and (@at is null or (created_at, instance_id collate \"C\") < (@at, @id collate \"C\"))) ";

    private const string ListOrder = "order by r.created_at desc, r.instance_id collate \"C\" desc limit @n";

    // Without the attention filter the page is cut first, so the walk runs only for the rows that are returned.
    private const string ListPlain =
        "with recursive " + CandidateCte + ", roots as (select * from cand order by created_at desc, instance_id collate \"C\" desc limit @n), " + WalkCtes + SummarySelect + ListOrder;

    // With it the filter needs the walk of every candidate before the page can be cut.
    private const string ListAttention =
        "with recursive " + CandidateCte + ", roots as (select * from cand), " + WalkCtes + SummarySelect + ListOrder;

    private const string CaseNodes =
        "nodes as (select instance_id, 0 as depth from {instance} where tenant_id = @t and instance_id = @i "
        + "union all select ch.instance_id, n.depth + 1 from nodes n join {instance} ch on ch.tenant_id = @t and ch.parent_instance_id = n.instance_id where n.depth < 16) ";

    private const string CaseSummaries =
        "with recursive " + CaseNodes + ", roots as (select " + SummaryColumns + " from {instance} where tenant_id = @t and instance_id in (select instance_id from nodes)), "
        + WalkCtes + SummarySelect + "order by r.created_at, r.instance_id collate \"C\"";

    private const string ChildSummaries =
        "with recursive roots as (select " + SummaryColumns + " from {instance} where tenant_id = @t and parent_instance_id = @i), "
        + WalkCtes + SummarySelect + "order by r.created_at, r.instance_id collate \"C\"";

    /// <summary>
    /// A seam for tests, null in production: runs inside the transaction of <see cref="GetCaseAsync"/>, after its first read and
    /// before the others, so a test can change the data from another connection or look at the transaction itself.
    /// </summary>
    internal Func<NpgsqlConnection, NpgsqlTransaction, Task>? AfterCaseSummaries { get; set; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstanceSummaryRecord>> ListSummariesAsync(InstanceListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var statuses = new NpgsqlParameter { ParameterName = "statuses", DataTypeName = "integer[]", Value = filter.Statuses is { Count: > 0 } ? filter.Statuses.ToArray() : DBNull.Value };
        NpgsqlParameter[] parameters =
        [
            Text("t", filter.TenantId),
            Text("p", filter.ProcessId),
            statuses,
            Text("stage", filter.Stage),
            Bool("roots", filter.RootsOnly),
            Bool("attn", filter.NeedsAttention),
            Time("at", filter.After?.CreatedAt),
            Text("id", filter.After?.InstanceId),
            Int("n", filter.Limit),
        ];
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await QueryAsync(connection, null, Bake(filter.NeedsAttention ? ListAttention : ListPlain), parameters, ReadSummary, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstanceCountRecord>> CountInstancesAsync(string tenantId, string? processId, bool rootsOnly, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await QueryAsync(
                connection,
                null,
                Bake(
                    "select process_id, case when status = 0 then 1 else status end as reported, stage, count(*) from {instance} "
                    + "where tenant_id = @t and (@p is null or process_id = @p) and (not @roots or parent_instance_id is null) "
                    + "group by process_id, case when status = 0 then 1 else status end, stage "
                    + "order by process_id collate \"C\", reported, stage collate \"C\" nulls first"),
                [Text("t", tenantId), Text("p", processId), Bool("roots", rootsOnly)],
                reader => new InstanceCountRecord(reader.GetString(0), reader.GetInt32(1), Str(reader, 2), checked((int)reader.GetInt64(3))),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<CaseRecord?> GetCaseAsync(string tenantId, string instanceId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await ExecuteAsync(connection, transaction, "set transaction read only", [], cancellationToken).ConfigureAwait(false);
                var withNodes = "with recursive " + CaseNodes;
                NpgsqlParameter[] Key() => [Text("t", tenantId), Text("i", instanceId)];
                var summaries = await QueryAsync(connection, transaction, Bake(CaseSummaries), [Text("t", tenantId), Text("i", instanceId), Bool("attn", false)], ReadSummary, cancellationToken).ConfigureAwait(false);
                if (AfterCaseSummaries is { } afterSummaries)
                {
                    await afterSummaries(connection, transaction).ConfigureAwait(false);
                }

                var root = summaries.FirstOrDefault(row => string.Equals(row.InstanceId, instanceId, StringComparison.Ordinal));
                if (root is null)
                {
                    return null;
                }

                var tasks = await QueryAsync(
                    connection,
                    transaction,
                    Bake(withNodes + "select " + TaskColumns + " from {task} where tenant_id = @t and status = 1 and instance_id in (select instance_id from nodes) order by created_at, task_id collate \"C\""),
                    Key(),
                    ReadTask,
                    cancellationToken).ConfigureAwait(false);
                var timers = await QueryAsync(
                    connection,
                    transaction,
                    Bake(withNodes + "select min(due_at) from {timer} where tenant_id = @t and instance_id in (select instance_id from nodes)"),
                    Key(),
                    reader => Moment(reader, 0),
                    cancellationToken).ConfigureAwait(false);
                var events = await QueryAsync(
                    connection,
                    transaction,
                    Bake(withNodes + "select event from {wait} where tenant_id = @t and instance_id in (select instance_id from nodes) group by event order by event collate \"C\""),
                    Key(),
                    reader => reader.GetString(0),
                    cancellationToken).ConfigureAwait(false);
                return new CaseRecord(
                    root,
                    [.. summaries.Where(row => !ReferenceEquals(row, root))],
                    tasks,
                    timers.Count == 0 ? null : timers[0],
                    events);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstanceSummaryRecord>> ListChildrenAsync(string tenantId, string instanceId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await QueryAsync(connection, null, Bake(ChildSummaries), [Text("t", tenantId), Text("i", instanceId), Bool("attn", false)], ReadSummary, cancellationToken).ConfigureAwait(false);
        }
    }

    private static NpgsqlParameter Bool(string name, bool value) => new(name, NpgsqlDbType.Boolean) { Value = value };

    private static InstanceSummaryRecord ReadSummary(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt64(3),
        reader.GetString(4),
        reader.GetInt16(5),
        Str(reader, 6),
        Str(reader, 7),
        Str(reader, 8),
        Str(reader, 9),
        Str(reader, 10),
        reader.GetFieldValue<DateTimeOffset>(11),
        reader.GetFieldValue<DateTimeOffset>(12),
        checked((int)reader.GetInt64(13)),
        checked((int)reader.GetInt64(14)));
}
