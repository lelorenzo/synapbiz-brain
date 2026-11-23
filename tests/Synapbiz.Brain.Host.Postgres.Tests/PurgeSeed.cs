using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// Writes whole trees straight into the tables, so that a purge test controls the status, the parent link and the instants of every
/// row and knows the exact number of rows of every table. Everything here is synthetic.
/// </summary>
internal static class PurgeSeed
{
    internal const int Created = 0;
    internal const int Running = 1;
    internal const int Waiting = 2;
    internal const int Suspended = 3;
    internal const int Completed = 4;
    internal const int Cancelled = 5;

    internal static readonly DateTimeOffset T0 = new(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The tables that hold rows of an instance, in the order of <see cref="PurgeCounts"/> after the instances.</summary>
    internal static readonly string[] RowTables = ["process_history", "process_timer", "process_wait", "process_task", "process_effect", "process_inbox"];

    /// <summary>
    /// Writes a tree: the root <c>prefix</c>, its children <c>prefix-c1</c> and <c>prefix-c2</c> and the grandchild <c>prefix-g1</c> (a child
    /// of <c>prefix-c1</c>). The descendants are in <paramref name="childStatus"/>. Every instance has history, timers, waits, tasks,
    /// effects and inbox rows, a different number of each, so that a count that is wrong for one table or one instance shows.
    /// </summary>
    /// <returns>The exact number of rows written to every table.</returns>
    internal static async Task<PurgeCounts> TreeAsync(Rig rig, string tenant, string prefix, int rootStatus, DateTimeOffset updatedAt, int childStatus = Waiting, string process = "p")
    {
        var ids = new[] { prefix, prefix + "-c1", prefix + "-c2", prefix + "-g1" };
        var parents = new string?[] { null, prefix, prefix, prefix + "-c1" };
        var counts = PurgeCounts.None;
        for (var index = 0; index < ids.Length; index++)
        {
            var created = T0.AddMinutes(index);
            await InstanceAsync(rig, tenant, ids[index], process, "k-" + ids[index], index == 0 ? rootStatus : childStatus, parents[index], created, index == 0 ? updatedAt : created);
            counts = counts.Plus(await RowsAsync(rig, tenant, ids[index], index));
        }

        return counts with { Instances = ids.Length };
    }

    internal static Task InstanceAsync(Rig rig, string tenant, string id, string process, string key, int status, string? parent, DateTimeOffset created, DateTimeOffset updated) =>
        rig.ExecuteAsync(
            "insert into {schema}.process_instance (tenant_id, instance_id, process_id, process_version, process_hash, business_key, status, stage, current_step, result, problem_code, state, revision, parent_instance_id, parent_child_key, created_at, updated_at) "
            + "values (@t, @i, @p, 1, 'h', @k, @s, null, null, null, null, null, 1, @par, null, @c, @u)",
            ("t", tenant),
            ("i", id),
            ("p", process),
            ("k", key),
            ("s", (short)status),
            ("par", parent),
            ("c", created),
            ("u", updated));

    /// <summary>The rows of one instance: <c>index + 1</c> history rows, one timer, <c>index % 2 + 1</c> waits, two tasks (one open), <c>index + 2</c> effects and <c>index + 1</c> inbox rows.</summary>
    internal static async Task<PurgeCounts> RowsAsync(Rig rig, string tenant, string id, int index)
    {
        var history = index + 1;
        var waits = (index % 2) + 1;
        var effects = index + 2;
        var inbox = index + 1;
        await rig.ExecuteAsync(
            "insert into {schema}.process_history (tenant_id, instance_id, sequence, at, step, entry, attempt, kind, code, data) select @t, @i, g, @at, 'step', g, 1, 1, null, 'protected' from generate_series(1, @history) g; "
            + "insert into {schema}.process_timer (tenant_id, timer_id, instance_id, due_at) values (@t, @i || '-timer', @i, @at); "
            + "insert into {schema}.process_wait (tenant_id, wait_id, instance_id, event, correlation) select @t, @i || '-wait-' || g, @i, 'ev', null from generate_series(1, @waits) g; "
            + "insert into {schema}.process_task (tenant_id, task_id, instance_id, step, queue, outcomes, show, status, created_at) select @t, @i || '-task-' || g, @i, 'step', 'queue', '[\"go\"]', '{}', g, @at from generate_series(1, 2) g; "
            + "insert into {schema}.process_effect (tenant_id, effect_id, instance_id, step, idempotency_key, kind, port, input, attempt, delivery, model, prompt_id, prompt_version, status, due_at, lease_until, lease_owner) "
            + "select @t, @i || '-effect-' || g, @i, 'step', @i || '-key-' || g, 1, 't.read', '{}', 1, 1, null, null, null, ((g - 1) % 4) + 1, @at, null, null from generate_series(1, @effects) g; "
            + "insert into {schema}.process_inbox (tenant_id, instance_id, kind, payload, created_at, status) select @t, @i, 2, '{}', @at, ((g - 1) % 3) + 1 from generate_series(1, @inbox) g",
            ("t", tenant),
            ("i", id),
            ("at", T0),
            ("history", history),
            ("waits", waits),
            ("effects", effects),
            ("inbox", inbox));
        return new PurgeCounts(0, history, 1, waits, 2, effects, inbox);
    }

    /// <summary>The number of rows of a tenant in every table that a purge touches, as one statement sees them.</summary>
    internal static async Task<PurgeCounts> CountsAsync(Rig rig, string tenant) => new(
        (int)await rig.CountAsync("process_instance", "tenant_id = @t", ("t", tenant)),
        (int)await rig.CountAsync("process_history", "tenant_id = @t", ("t", tenant)),
        (int)await rig.CountAsync("process_timer", "tenant_id = @t", ("t", tenant)),
        (int)await rig.CountAsync("process_wait", "tenant_id = @t", ("t", tenant)),
        (int)await rig.CountAsync("process_task", "tenant_id = @t", ("t", tenant)),
        (int)await rig.CountAsync("process_effect", "tenant_id = @t", ("t", tenant)),
        (int)await rig.CountAsync("process_inbox", "tenant_id = @t", ("t", tenant)));

    /// <summary>The rows of every table that belong to the instances whose id starts with a prefix.</summary>
    internal static async Task<long> RowsOfAsync(Rig rig, string tenant, string prefix)
    {
        const string Match = "tenant_id = @t and (instance_id = @p or instance_id like @p || '-%')";
        var total = await rig.CountAsync("process_instance", Match, ("t", tenant), ("p", prefix));
        foreach (var table in RowTables)
        {
            total += await rig.CountAsync(table, Match, ("t", tenant), ("p", prefix));
        }

        return total;
    }
}
