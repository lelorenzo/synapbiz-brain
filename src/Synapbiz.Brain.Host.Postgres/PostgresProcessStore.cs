using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres;

/// <summary>
/// The PostgreSQL implementation of <see cref="IProcessStore"/>, and with the migrator the only code that knows Npgsql. Every
/// statement is a constant text with parameters; the only identifier that comes from configuration is the schema, validated
/// against a strict pattern and quoted.
/// </summary>
internal sealed partial class PostgresProcessStore : IProcessStore, IAsyncDisposable, IDisposable
{
    private const string InstanceColumns =
        "tenant_id, instance_id, process_id, process_version, process_hash, business_key, status, stage, current_step, result, problem_code, state, revision, parent_instance_id, parent_child_key, created_at, updated_at";

    private const string EffectColumns =
        "tenant_id, effect_id, instance_id, step, idempotency_key, kind, port, input, attempt, delivery, model, prompt_id, prompt_version, status, due_at, lease_until, lease_owner";

    private const string TaskColumns = "tenant_id, task_id, instance_id, step, queue, outcomes, show, status, created_at";

    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;
    private readonly ConcurrentDictionary<string, string> baked = new(StringComparer.Ordinal);

    internal PostgresProcessStore(PostgresProcessStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!PostgresProcessStoreOptionsValidator.IsValidSchema(options.Schema))
        {
            throw new ArgumentException("The schema name is not valid.", nameof(options));
        }

        schema = "\"" + options.Schema + "\"";
        dataSource = NpgsqlDataSource.Create(options.ConnectionString);
    }

    /// <inheritdoc />
    public void Dispose() => dataSource.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => dataSource.DisposeAsync();

    /// <inheritdoc />
    public async Task<IProcessSession> BeginAsync(CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var opened = false;
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            opened = true;
            return new Session(this, connection, transaction);
        }
        finally
        {
            if (!opened)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkRef>> ScanAsync(WorkKind kind, DateTimeOffset now, int limit, WorkRef? after, CancellationToken cancellationToken)
    {
        // Keyset paging: the cursor is the last item of the previous page, and the ordering key is total (time, tenant, id), so no
        // item is skipped or read twice by the pages of one pass. The inbox is ordered by its identity, which is unique on its own.
        var sql = kind switch
        {
            WorkKind.ExpiredLease => "select tenant_id, instance_id, effect_id, lease_until from {effect} where status = 2 and lease_until <= @now and (@at is null or (lease_until, tenant_id, effect_id) > (@at, @t, @id)) order by lease_until, tenant_id, effect_id limit @n",
            WorkKind.Inbox => "select i.tenant_id, i.instance_id, i.inbox_id::text, null::timestamptz from {inbox} i where i.status = 1 and i.inbox_id > @after and not exists (select 1 from {inbox} e where e.tenant_id = i.tenant_id and e.instance_id = i.instance_id and e.status = 1 and e.inbox_id < i.inbox_id) order by i.inbox_id limit @n",
            WorkKind.DueTimer => "select tenant_id, instance_id, timer_id, due_at from {timer} where due_at <= @now and (@at is null or (due_at, tenant_id, timer_id) > (@at, @t, @id)) order by due_at, tenant_id, timer_id limit @n",
            WorkKind.DueEffect => "select tenant_id, instance_id, effect_id, due_at from {effect} where status = 1 and due_at <= @now and (@at is null or (due_at, tenant_id, effect_id) > (@at, @t, @id)) order by due_at, tenant_id, effect_id limit @n",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind of work."),
        };
        NpgsqlParameter[] parameters;
        if (kind == WorkKind.Inbox)
        {
            var afterId = after is null ? 0 : long.Parse(after.ItemId, CultureInfo.InvariantCulture);
            parameters = [Big("after", afterId), Big("n", limit)];
        }
        else
        {
            parameters = [Time("now", now), Time("at", after?.SortAt), Text("t", after?.TenantId), Text("id", after?.ItemId), Big("n", limit)];
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await QueryAsync(
                connection,
                null,
                Bake(sql),
                parameters,
                reader => new WorkRef(reader.GetString(0), reader.GetString(1), reader.GetString(2), Moment(reader, 3)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<InstanceRecord?> GetInstanceAsync(string tenantId, string instanceId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await QueryAsync(
                connection,
                null,
                Bake("select " + InstanceColumns + " from {instance} where tenant_id = @t and instance_id = @i"),
                [Text("t", tenantId), Text("i", instanceId)],
                ReadInstance,
                cancellationToken).ConfigureAwait(false);
            return rows.Count == 0 ? null : rows[0];
        }
    }

    /// <inheritdoc />
    public async Task<InstanceRecord?> FindInstanceAsync(string tenantId, string processId, string businessKey, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await QueryAsync(
                connection,
                null,
                Bake("select " + InstanceColumns + " from {instance} where tenant_id = @t and process_id = @p and business_key = @k"),
                [Text("t", tenantId), Text("p", processId), Text("k", businessKey)],
                ReadInstance,
                cancellationToken).ConfigureAwait(false);
            return rows.Count == 0 ? null : rows[0];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(string tenantId, string instanceId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await QueryAsync(
                connection,
                null,
                Bake("select sequence, at, step, entry, attempt, kind, code, data from {history} where tenant_id = @t and instance_id = @i order by sequence"),
                [Text("t", tenantId), Text("i", instanceId)],
                reader => new HistoryRecord(
                    reader.GetInt64(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    Str(reader, 2),
                    reader.GetInt64(3),
                    reader.GetInt32(4),
                    (ProcessHistoryKind)reader.GetInt16(5),
                    Str(reader, 6),
                    Str(reader, 7)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskRecord>> ListOpenTasksAsync(string tenantId, string queue, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await QueryAsync(
                connection,
                null,
                Bake("select " + TaskColumns + " from {task} where tenant_id = @t and queue = @q and status = 1 order by created_at, task_id"),
                [Text("t", tenantId), Text("q", queue)],
                ReadTask,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<string?> FindTaskInstanceAsync(string tenantId, string taskId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await QueryAsync(
                connection,
                null,
                Bake("select instance_id from {task} where tenant_id = @t and task_id = @id"),
                [Text("t", tenantId), Text("id", taskId)],
                reader => reader.GetString(0),
                cancellationToken).ConfigureAwait(false);
            return rows.Count == 0 ? null : rows[0];
        }
    }

    /// <summary>Replaces the table tokens of a statement with the quoted, schema-qualified table names.</summary>
    private string Bake(string template) => baked.GetOrAdd(
        template,
        text => text
            .Replace("{instance}", schema + ".process_instance", StringComparison.Ordinal)
            .Replace("{history}", schema + ".process_history", StringComparison.Ordinal)
            .Replace("{timer}", schema + ".process_timer", StringComparison.Ordinal)
            .Replace("{wait}", schema + ".process_wait", StringComparison.Ordinal)
            .Replace("{task}", schema + ".process_task", StringComparison.Ordinal)
            .Replace("{effect}", schema + ".process_effect", StringComparison.Ordinal)
            .Replace("{event}", schema + ".process_event", StringComparison.Ordinal)
            .Replace("{inbox}", schema + ".process_inbox", StringComparison.Ordinal));

    private static NpgsqlBatch NewBatch(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, NpgsqlParameter[] parameters)
    {
        var batch = new NpgsqlBatch(connection, transaction);
        var command = new NpgsqlBatchCommand(sql);
        command.Parameters.AddRange(parameters);
        batch.BatchCommands.Add(command);
        return batch;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, NpgsqlParameter[] parameters, CancellationToken cancellationToken)
    {
        var batch = NewBatch(connection, transaction, sql, parameters);
        await using (batch.ConfigureAwait(false))
        {
            return await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<List<T>> QueryAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        NpgsqlParameter[] parameters,
        Func<NpgsqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var batch = NewBatch(connection, transaction, sql, parameters);
        await using (batch.ConfigureAwait(false))
        {
            var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                var rows = new List<T>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(map(reader));
                }

                return rows;
            }
        }
    }

    private static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Big(string name, long? value) => new(name, NpgsqlDbType.Bigint) { Value = value.HasValue ? value.Value : DBNull.Value };

    private static NpgsqlParameter Int(string name, int value) => new(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter Small(string name, int value) => new(name, NpgsqlDbType.Smallint) { Value = (short)value };

    private static NpgsqlParameter Time(string name, DateTimeOffset? value) =>
        new(name, NpgsqlDbType.TimestampTz) { Value = value.HasValue ? value.Value.ToUniversalTime() : DBNull.Value };

    private static string? Str(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long? Long(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static DateTimeOffset? Moment(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static InstanceRecord ReadInstance(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt64(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetInt16(6),
        Str(reader, 7),
        Str(reader, 8),
        Str(reader, 9),
        Str(reader, 10),
        Str(reader, 11),
        reader.GetInt64(12),
        Str(reader, 13),
        Str(reader, 14),
        reader.GetFieldValue<DateTimeOffset>(15),
        reader.GetFieldValue<DateTimeOffset>(16));

    private static TaskRecord ReadTask(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
        reader.GetString(6),
        (HumanTaskStatus)reader.GetInt16(7),
        reader.GetFieldValue<DateTimeOffset>(8));

    private static EffectRecord ReadEffect(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        (ProcessEffectKind)reader.GetInt16(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetInt32(8),
        (ProcessDelivery)reader.GetInt16(9),
        Str(reader, 10),
        Str(reader, 11),
        Long(reader, 12),
        (EffectStatus)reader.GetInt16(13),
        reader.GetFieldValue<DateTimeOffset>(14),
        Moment(reader, 15),
        Str(reader, 16));
}
