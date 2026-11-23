using System.Text.Json;
using Npgsql;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres;

internal sealed partial class PostgresProcessStore
{
    /// <summary>One PostgreSQL transaction. It remembers which instance locks it holds and refuses to write an instance's rows without one.</summary>
    private sealed class Session : IProcessSession
    {
        private readonly PostgresProcessStore owner;
        private readonly NpgsqlConnection connection;
        private readonly NpgsqlTransaction transaction;
        private readonly HashSet<InstanceKey> held = [];
        private bool finished;

        internal Session(PostgresProcessStore owner, NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            this.owner = owner;
            this.connection = connection;
            this.transaction = transaction;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!finished)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            finished = true;
        }

        public async Task<InstanceRecord?> LockInstanceAsync(InstanceKey key, bool skipLocked, CancellationToken cancellationToken)
        {
            var sql = skipLocked
                ? "select " + InstanceColumns + " from {instance} where tenant_id = @t and instance_id = @i for update skip locked"
                : "select " + InstanceColumns + " from {instance} where tenant_id = @t and instance_id = @i for update";
            var rows = await QueryAsync(connection, transaction, owner.Bake(sql), [Text("t", key.TenantId), Text("i", key.InstanceId)], ReadInstance, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                return null;
            }

            held.Add(key);
            return rows[0];
        }

        public async Task<InsertedInstance> InsertInstanceAsync(NewInstance row, CancellationToken cancellationToken)
        {
            var inserted = await QueryAsync(
                connection,
                transaction,
                owner.Bake("insert into {instance} (tenant_id, instance_id, process_id, process_version, process_hash, business_key, status, revision, parent_instance_id, parent_child_key, created_at, updated_at) values (@t, @i, @p, @v, @h, @k, 0, 0, @pi, @pk, @now, @now) on conflict (tenant_id, process_id, business_key) do nothing returning instance_id"),
                [
                    Text("t", row.TenantId),
                    Text("i", row.InstanceId),
                    Text("p", row.ProcessId),
                    Big("v", row.ProcessVersion),
                    Text("h", row.ProcessHash),
                    Text("k", row.BusinessKey),
                    Text("pi", row.ParentInstanceId),
                    Text("pk", row.ParentChildKey),
                    Time("now", row.Now),
                ],
                reader => reader.GetString(0),
                cancellationToken).ConfigureAwait(false);
            if (inserted.Count > 0)
            {
                held.Add(new InstanceKey(row.TenantId, row.InstanceId));
                return new InsertedInstance(true, row.InstanceId);
            }

            var existing = await QueryAsync(
                connection,
                transaction,
                owner.Bake("select instance_id from {instance} where tenant_id = @t and process_id = @p and business_key = @k"),
                [Text("t", row.TenantId), Text("p", row.ProcessId), Text("k", row.BusinessKey)],
                reader => reader.GetString(0),
                cancellationToken).ConfigureAwait(false);
            return new InsertedInstance(false, existing.Count == 0 ? row.InstanceId : existing[0]);
        }

        public async Task SaveInstanceAsync(InstanceKey key, InstanceWrite write, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("update {instance} set status = @s, stage = @st, current_step = @cs, result = @r, problem_code = @pc, state = @state, revision = revision + 1, updated_at = @now where tenant_id = @t and instance_id = @i"),
                [
                    Small("s", write.Status),
                    Text("st", write.Stage),
                    Text("cs", write.CurrentStep),
                    Text("r", write.Result),
                    Text("pc", write.ProblemCode),
                    Text("state", write.ProtectedState),
                    Time("now", write.Now),
                    Text("t", key.TenantId),
                    Text("i", key.InstanceId),
                ],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task AppendHistoryAsync(InstanceKey key, IReadOnlyList<HistoryRow> rows, CancellationToken cancellationToken)
        {
            RequireLock(key);
            var sql = owner.Bake("insert into {history} (tenant_id, instance_id, sequence, at, step, entry, attempt, kind, code, data) values (@t, @i, @seq, @at, @st, @en, @at2, @k, @c, @d)");
            var batch = new NpgsqlBatch(connection, transaction);
            await using (batch.ConfigureAwait(false))
            {
                foreach (var row in rows)
                {
                    var command = new NpgsqlBatchCommand(sql);
                    command.Parameters.AddRange(new[]
                    {
                        Text("t", key.TenantId),
                        Text("i", key.InstanceId),
                        Big("seq", row.Sequence),
                        Time("at", row.At),
                        Text("st", row.Step),
                        Big("en", row.Entry),
                        Int("at2", row.Attempt),
                        Small("k", (int)row.Kind),
                        Text("c", row.Code),
                        Text("d", row.ProtectedData),
                    });
                    batch.BatchCommands.Add(command);
                }

                await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<bool> DeleteTimerAsync(InstanceKey key, string timerId, CancellationToken cancellationToken)
        {
            RequireLock(key);
            var count = await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("delete from {timer} where tenant_id = @t and timer_id = @id and instance_id = @i"),
                [Text("t", key.TenantId), Text("id", timerId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
            return count > 0;
        }

        public async Task InsertTimerAsync(InstanceKey key, string timerId, DateTimeOffset dueAt, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("insert into {timer} (tenant_id, timer_id, instance_id, due_at) values (@t, @id, @i, @due)"),
                [Text("t", key.TenantId), Text("id", timerId), Text("i", key.InstanceId), Time("due", dueAt)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteWaitAsync(InstanceKey key, string waitId, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("delete from {wait} where tenant_id = @t and wait_id = @id and instance_id = @i"),
                [Text("t", key.TenantId), Text("id", waitId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task InsertWaitAsync(InstanceKey key, string waitId, string eventName, string? correlation, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("insert into {wait} (tenant_id, wait_id, instance_id, event, correlation) values (@t, @id, @i, @e, @c)"),
                [Text("t", key.TenantId), Text("id", waitId), Text("i", key.InstanceId), Text("e", eventName), Text("c", correlation)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task InsertTaskAsync(InstanceKey key, TaskRecord task, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("insert into {task} (tenant_id, task_id, instance_id, step, queue, outcomes, show, status, created_at) values (@t, @id, @i, @st, @q, @o, @sh, 1, @now)"),
                [
                    Text("t", key.TenantId),
                    Text("id", task.TaskId),
                    Text("i", key.InstanceId),
                    Text("st", task.Step),
                    Text("q", task.Queue),
                    Text("o", JsonSerializer.Serialize(task.Outcomes)),
                    Text("sh", task.ProtectedShow),
                    Time("now", task.CreatedAt),
                ],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<TaskRecord?> GetTaskAsync(InstanceKey key, string taskId, CancellationToken cancellationToken)
        {
            var rows = await QueryAsync(
                connection,
                transaction,
                owner.Bake("select " + TaskColumns + " from {task} where tenant_id = @t and task_id = @id and instance_id = @i"),
                [Text("t", key.TenantId), Text("id", taskId), Text("i", key.InstanceId)],
                ReadTask,
                cancellationToken).ConfigureAwait(false);
            return rows.Count == 0 ? null : rows[0];
        }

        public async Task<bool> CloseTaskAsync(InstanceKey key, string taskId, HumanTaskStatus status, DateTimeOffset now, CancellationToken cancellationToken)
        {
            RequireLock(key);
            var count = await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("update {task} set status = @s, closed_at = @now where tenant_id = @t and task_id = @id and instance_id = @i and status = 1"),
                [Small("s", (int)status), Time("now", now), Text("t", key.TenantId), Text("id", taskId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
            return count > 0;
        }

        public async Task InsertEffectAsync(InstanceKey key, EffectRecord effect, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("insert into {effect} (" + EffectColumns + ") values (@t, @id, @i, @st, @ik, @k, @p, @in, @a, @d, @m, @pid, @pv, 1, @due, null, null)"),
                [
                    Text("t", key.TenantId),
                    Text("id", effect.EffectId),
                    Text("i", key.InstanceId),
                    Text("st", effect.Step),
                    Text("ik", effect.IdempotencyKey),
                    Small("k", (int)effect.Kind),
                    Text("p", effect.Port),
                    Text("in", effect.ProtectedInput),
                    Int("a", effect.Attempt),
                    Small("d", (int)effect.Delivery),
                    Text("m", effect.Model),
                    Text("pid", effect.PromptId),
                    Big("pv", effect.PromptVersion),
                    Time("due", effect.DueAt),
                ],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<EffectRecord?> GetEffectAsync(InstanceKey key, string effectId, CancellationToken cancellationToken)
        {
            var rows = await QueryAsync(
                connection,
                transaction,
                owner.Bake("select " + EffectColumns + " from {effect} where tenant_id = @t and effect_id = @id and instance_id = @i"),
                [Text("t", key.TenantId), Text("id", effectId), Text("i", key.InstanceId)],
                ReadEffect,
                cancellationToken).ConfigureAwait(false);
            return rows.Count == 0 ? null : rows[0];
        }

        public async Task ClaimEffectAsync(InstanceKey key, string effectId, DateTimeOffset leaseUntil, string leaseOwner, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("update {effect} set status = 2, lease_until = @lu, lease_owner = @lo where tenant_id = @t and effect_id = @id and instance_id = @i and status = 1"),
                [Time("lu", leaseUntil), Text("lo", leaseOwner), Text("t", key.TenantId), Text("id", effectId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task RequeueEffectAsync(InstanceKey key, string effectId, DateTimeOffset dueAt, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("update {effect} set status = 1, due_at = @due, lease_until = null, lease_owner = null where tenant_id = @t and effect_id = @id and instance_id = @i and status = 2"),
                [Time("due", dueAt), Text("t", key.TenantId), Text("id", effectId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task FinishEffectAsync(InstanceKey key, string effectId, EffectStatus status, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("update {effect} set status = @s, lease_until = null, lease_owner = null where tenant_id = @t and effect_id = @id and instance_id = @i"),
                [Small("s", (int)status), Text("t", key.TenantId), Text("id", effectId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task ClearWorkAsync(InstanceKey key, DateTimeOffset now, CancellationToken cancellationToken)
        {
            RequireLock(key);
            NpgsqlParameter[] ByInstance() => [Text("t", key.TenantId), Text("i", key.InstanceId)];
            await ExecuteAsync(connection, transaction, owner.Bake("delete from {wait} where tenant_id = @t and instance_id = @i"), ByInstance(), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, owner.Bake("delete from {timer} where tenant_id = @t and instance_id = @i"), ByInstance(), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("update {task} set status = 3, closed_at = @now where tenant_id = @t and instance_id = @i and status = 1"),
                [Time("now", now), Text("t", key.TenantId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
            await DiscardPendingEffectsAsync(key, cancellationToken).ConfigureAwait(false);
        }

        public async Task DiscardPendingEffectsAsync(InstanceKey key, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("update {effect} set status = 4, lease_until = null, lease_owner = null where tenant_id = @t and instance_id = @i and status = 1"),
                [Text("t", key.TenantId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<InboxRecord?> GetInboxAsync(InstanceKey key, long inboxId, CancellationToken cancellationToken)
        {
            var rows = await QueryAsync(
                connection,
                transaction,
                owner.Bake("select tenant_id, inbox_id, instance_id, kind, payload, status, code from {inbox} where tenant_id = @t and inbox_id = @id and instance_id = @i"),
                [Text("t", key.TenantId), Big("id", inboxId), Text("i", key.InstanceId)],
                reader => new InboxRecord(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    (InboxKind)reader.GetInt16(3),
                    reader.GetString(4),
                    (InboxStatus)reader.GetInt16(5),
                    Str(reader, 6)),
                cancellationToken).ConfigureAwait(false);
            return rows.Count == 0 ? null : rows[0];
        }

        public async Task<long?> FirstPendingInboxAsync(InstanceKey key, CancellationToken cancellationToken)
        {
            var rows = await QueryAsync(
                connection,
                transaction,
                owner.Bake("select min(inbox_id) from {inbox} where tenant_id = @t and instance_id = @i and status = 1"),
                [Text("t", key.TenantId), Text("i", key.InstanceId)],
                reader => Long(reader, 0),
                cancellationToken).ConfigureAwait(false);
            return rows.Count == 0 ? null : rows[0];
        }

        public async Task CompleteInboxAsync(InstanceKey key, long inboxId, InboxStatus status, string? code, CancellationToken cancellationToken)
        {
            RequireLock(key);
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("update {inbox} set status = @s, code = @c where tenant_id = @t and inbox_id = @id and instance_id = @i"),
                [Small("s", (int)status), Text("c", code), Text("t", key.TenantId), Big("id", inboxId), Text("i", key.InstanceId)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task AppendInboxAsync(InstanceKey target, InboxKind kind, string protectedPayload, DateTimeOffset now, CancellationToken cancellationToken)
        {
            await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("insert into {inbox} (tenant_id, instance_id, kind, payload, created_at, status) values (@t, @i, @k, @p, @now, 1)"),
                [Text("t", target.TenantId), Text("i", target.InstanceId), Small("k", (int)kind), Text("p", protectedPayload), Time("now", now)],
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> ListLiveChildrenAsync(InstanceKey parent, CancellationToken cancellationToken) =>
            await QueryAsync(
                connection,
                transaction,
                owner.Bake("select instance_id from {instance} where tenant_id = @t and parent_instance_id = @i and status not in (4, 5) order by instance_id"),
                [Text("t", parent.TenantId), Text("i", parent.InstanceId)],
                reader => reader.GetString(0),
                cancellationToken).ConfigureAwait(false);

        public async Task<bool> TryInsertEventAsync(string tenantId, string eventId, string name, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var count = await ExecuteAsync(
                connection,
                transaction,
                owner.Bake("insert into {event} (tenant_id, event_id, name, received_at) values (@t, @id, @n, @now) on conflict do nothing"),
                [Text("t", tenantId), Text("id", eventId), Text("n", name), Time("now", now)],
                cancellationToken).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<IReadOnlyList<WaitCandidate>> FindWaitingInstancesAsync(string tenantId, string eventName, string? correlation, CancellationToken cancellationToken) =>
            await QueryAsync(
                connection,
                transaction,
                owner.Bake("select instance_id, bool_or(correlation is null or correlation = @c) from {wait} where tenant_id = @t and event = @e and (correlation is null or lower(correlation) = lower(@c)) group by instance_id order by instance_id"),
                [Text("t", tenantId), Text("e", eventName), Text("c", correlation)],
                reader => new WaitCandidate(reader.GetString(0), reader.GetBoolean(1)),
                cancellationToken).ConfigureAwait(false);

        private void RequireLock(InstanceKey key)
        {
            if (!held.Contains(key))
            {
                throw new InvalidOperationException("The instance lock of " + key.TenantId + "/" + key.InstanceId + " is not held by this transaction.");
            }
        }
    }
}
