using Npgsql;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres;

/// <summary>
/// The statements of the purge commands. The scans take no lock and the deletes of a tree run in the session of the engine, after it
/// holds the instance locks of the whole tree (taken with <c>skip locked</c>, never waiting); every statement filters by tenant first and takes values only as parameters.
/// </summary>
internal sealed partial class PostgresProcessStore
{
    // The finished roots in the order of the purge: the root update time, then the instance id bytewise. Status 4 is Completed and 5 is
    // Cancelled; the predicate is the one of the partial index of migration 0004.
    private const string FinishedRoots =
        "select instance_id, updated_at from {instance} "
        + "where tenant_id = @t and parent_instance_id is null and status in (4, 5) and updated_at < @before "
        + "and (@at is null or (updated_at, instance_id collate \"C\") > (@at, @id collate \"C\")) "
        + "order by updated_at, instance_id collate \"C\" limit @n";

    /// <inheritdoc />
    public async Task<IReadOnlyList<FinishedRoot>> ScanFinishedRootsAsync(string tenantId, DateTimeOffset before, int limit, FinishedRoot? after, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await QueryAsync(
                connection,
                null,
                Bake(FinishedRoots),
                [Text("t", tenantId), Time("before", before), Time("at", after?.UpdatedAt), Text("id", after?.InstanceId), Int("n", limit)],
                reader => new FinishedRoot(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<EventPurge> PurgeEventsAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var deleted = await ExecuteAsync(
                connection,
                null,
                Bake(
                    "delete from {event} where tenant_id = @t and event_id = any(array("
                    + "select event_id from {event} where tenant_id = @t and received_at < @before "
                    + "order by received_at, event_id collate \"C\" limit @n))"),
                [Text("t", tenantId), Time("before", before), Int("n", limit)],
                cancellationToken).ConfigureAwait(false);
            var more = await QueryAsync(
                connection,
                null,
                Bake("select exists (select 1 from {event} where tenant_id = @t and received_at < @before)"),
                [Text("t", tenantId), Time("before", before)],
                reader => reader.GetBoolean(0),
                cancellationToken).ConfigureAwait(false);
            return new EventPurge(deleted, more.Count > 0 && more[0]);
        }
    }

    private static NpgsqlParameter TextArray(string name, IReadOnlyCollection<string> values) =>
        new() { ParameterName = name, DataTypeName = "text[]", Value = values.ToArray() };
}
