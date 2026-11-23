using System.Collections.Concurrent;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;
using Xunit.Abstractions;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>The two tests that use real parallel tasks. Neither assumes any timing: they check what must hold whatever the interleaving.</summary>
public sealed class ConcurrencyTests
{
    private const int Instances = 50;

    private readonly ITestOutputHelper output;

    public ConcurrencyTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task FourPumpsInParallelCompleteEveryInstanceAndInvokeEveryEffectOnce()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("read-timer-action"));
        var invoked = new ConcurrentBag<string>();
        rig.Port("t.read").Handler = (call, _) =>
        {
            invoked.Add(call.Request.EffectId);
            return Task.FromResult<PortResult>(new PortCompleted("{\"amount\": 5}"));
        };
        rig.Port("t.act").Handler = (call, _) =>
        {
            invoked.Add(call.Request.EffectId);
            return Task.FromResult<PortResult>(new PortCompleted("{}"));
        };
        for (var index = 0; index < Instances; index++)
        {
            await rig.StartAsync("host.rta", "k-" + index, "{\"case_ref\":\"c-" + index + "\"}");
        }

        var pumps = new[] { rig.Pump, rig.NewPump(), rig.NewPump(), rig.NewPump() };
        var finished = 0;
        await Task.WhenAll(pumps.Select(pump => Task.Run(async () =>
        {
            for (var pass = 0; pass < 100000 && Volatile.Read(ref finished) == 0; pass++)
            {
                if (await pump.RunOnceAsync() > 0)
                {
                    continue;
                }

                // Idle: either everything is done, or the instances wait for their 30 second timers.
                if (await rig.CountAsync("process_instance", "status = 4") == Instances)
                {
                    Volatile.Write(ref finished, 1);
                }
                else
                {
                    rig.Time.Advance(TimeSpan.FromSeconds(31));
                }
            }
        })));

        Volatile.Read(ref finished).Should().Be(1, "every instance must complete");
        (await rig.CountAsync("process_instance", "status = 4 and result = 'ok'")).Should().Be(Instances);
        invoked.Should().HaveCount(Instances * 2);
        invoked.Should().OnlyHaveUniqueItems("every effect is invoked exactly once per attempt");
        (await rig.CountAsync("process_effect", "status = 3")).Should().Be(Instances * 2);
        (await rig.CountAsync("process_effect", "status <> 3")).Should().Be(0);
        (await rig.CountAsync("process_timer")).Should().Be(0);
        (await rig.ColumnAsync<string>("select instance_id from {schema}.process_history group by instance_id having min(sequence) <> 1 or max(sequence) <> count(*) or count(distinct sequence) <> count(*)"))
            .Should().BeEmpty("history sequences are contiguous from 1 with no duplicates");
        (await rig.ColumnAsync<long>("select count(*) from {schema}.process_history group by instance_id")).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task CompletingATaskWhilePumpsFireItsTimeoutNeverDeadlocksAndEndsInExactlyOneOutcome()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var approved = 0;
        var late = 0;
        var pumps = new[] { rig.Pump, rig.NewPump() };
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var id = await rig.StartAsync("host.task", "k-" + iteration);
            var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Single(candidate => candidate.InstanceId == id);
            rig.Time.Advance(TimeSpan.FromHours(1));
            var gate = new TaskCompletionSource();
            var pump = pumps[iteration % 2];

            var lead = iteration % 40;
            var complete = Task.Run(async () =>
            {
                await gate.Task;
                for (var step = 0; step < lead * 25; step++)
                {
                    await Task.Yield();
                }

                return await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "approve", "{}");
            });
            var fire = Task.Run(async () =>
            {
                await gate.Task;
                return await pump.RunOnceAsync();
            });
            gate.SetResult();
            var completed = await complete.WaitAsync(TimeSpan.FromMinutes(2));
            await fire.WaitAsync(TimeSpan.FromMinutes(2));
            await rig.DrainAsync();

            var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
            info!.Status.Should().Be(ProcessStatus.Completed);
            (await rig.OutstandingWorkAsync(id)).Should().Be(0);
            var taskStatus = await rig.ScalarAsync<short>("select status from {schema}.process_task where task_id = @t", ("t", task.TaskId));
            if (info.Result == "approved")
            {
                completed.Disposition.Should().Be(AdvanceDisposition.Applied);
                taskStatus.Should().Be(2);
                approved++;
            }
            else
            {
                info.Result.Should().Be("late");
                completed.Should().Be(new CommandResult(AdvanceDisposition.Ignored, "process.trigger.stale"));
                taskStatus.Should().Be(3);
                late++;
            }
        }

        output.WriteLine($"approved by the person: {approved}, timed out first: {late}");
        (approved + late).Should().Be(200, "a PostgreSQL deadlock (40P01) or any other error would have thrown above");
    }
}
