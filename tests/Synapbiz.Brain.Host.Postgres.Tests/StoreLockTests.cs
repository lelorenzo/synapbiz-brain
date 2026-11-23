using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>The lock discipline of the store session: nothing writes the rows of an instance before the instance row is locked in the same transaction.</summary>
public sealed class StoreLockTests
{
    private static IEnumerable<MethodInfo> Guarded =>
        typeof(IProcessSession).GetMethods().Where(method => method.GetCustomAttribute<RequiresInstanceLockAttribute>() is not null);

    [Fact]
    public void TheGuardedOperationsAreTheOnesThatWriteAnInstanceRowAndTakeTheKeyFirst()
    {
        var guarded = Guarded.ToList();

        guarded.Should().HaveCountGreaterThan(10);
        guarded.Should().OnlyContain(method => method.GetParameters()[0].ParameterType == typeof(InstanceKey));
        guarded.Select(method => method.Name).Should().Contain(
            [
                "SaveInstanceAsync",
                "AppendHistoryAsync",
                "InsertTimerAsync",
                "DeleteTimerAsync",
                "InsertWaitAsync",
                "DeleteWaitAsync",
                "InsertTaskAsync",
                "CloseTaskAsync",
                "InsertEffectAsync",
                "ClaimEffectAsync",
                "RequeueEffectAsync",
                "FinishEffectAsync",
                "ClearWorkAsync",
                "DiscardPendingEffectsAsync",
                "CompleteInboxAsync",
            ]);
    }

    [Fact]
    public void EveryOperationThatWritesWithAKeyIsGuardedExceptTheReadsAndThePureInserts()
    {
        var unguarded = typeof(IProcessSession).GetMethods()
            .Where(method => method.GetCustomAttribute<RequiresInstanceLockAttribute>() is null)
            .Where(method => method.GetParameters().Length > 0 && method.GetParameters()[0].ParameterType == typeof(InstanceKey))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal);

        unguarded.Should().Equal(
            "AppendInboxAsync",
            "FirstPendingInboxAsync",
            "GetEffectAsync",
            "GetInboxAsync",
            "GetTaskAsync",
            "ListLiveChildrenAsync",
            "LockInstanceAsync");
    }

    [Fact]
    public async Task EveryGuardedOperationFailsWhenTheSessionHoldsNoLockForTheKey()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("quick"));
        var held = await rig.StartAsync("host.quick", "k-held");
        var other = await rig.StartAsync("host.quick", "k-other");
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        await using var session = await store.BeginAsync(CancellationToken.None);
        (await session.LockInstanceAsync(new InstanceKey(rig.Tenant, held), skipLocked: false, CancellationToken.None)).Should().NotBeNull();

        foreach (var method in Guarded)
        {
            foreach (var key in new[] { new InstanceKey(rig.Tenant, other), new InstanceKey("t-2", held) })
            {
                var arguments = Arguments(method, key);
                var call = (Task)method.Invoke(session, arguments)!;

                await FluentActions.Awaiting(() => call).Should().ThrowExactlyAsync<InvalidOperationException>(method.Name + " for " + key.TenantId + "/" + key.InstanceId);
            }
        }
    }

    [Fact]
    public async Task AnOperationThatWritesWorksOnceTheInstanceIsLockedAndTheLockEndsWithTheTransaction()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("quick"));
        var id = await rig.StartAsync("host.quick", "k-1");
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        var key = new InstanceKey(rig.Tenant, id);

        await using (var session = await store.BeginAsync(CancellationToken.None))
        {
            await FluentActions.Awaiting(() => session.AppendHistoryAsync(key, [], CancellationToken.None)).Should().ThrowExactlyAsync<InvalidOperationException>();
            (await session.LockInstanceAsync(key, skipLocked: false, CancellationToken.None)).Should().NotBeNull();
            await session.AppendHistoryAsync(key, [], CancellationToken.None);
        }

        await using (var next = await store.BeginAsync(CancellationToken.None))
        {
            await FluentActions.Awaiting(() => next.AppendHistoryAsync(key, [], CancellationToken.None)).Should().ThrowExactlyAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task ARowLockedByAnotherTransactionIsSkippedWhenAskedAndTheLockOfAMissingInstanceGivesNull()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("quick"));
        var id = await rig.StartAsync("host.quick", "k-1");
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        var key = new InstanceKey(rig.Tenant, id);

        await using var first = await store.BeginAsync(CancellationToken.None);
        await using var second = await store.BeginAsync(CancellationToken.None);
        (await first.LockInstanceAsync(key, skipLocked: false, CancellationToken.None)).Should().NotBeNull();

        (await second.LockInstanceAsync(key, skipLocked: true, CancellationToken.None)).Should().BeNull();
        (await second.LockInstanceAsync(new InstanceKey(rig.Tenant, "missing"), skipLocked: false, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void TheOperationsThatWriteAnActivationOrAScheduleAreGuardedByTheirOwnKeyAndNothingElseTakesAKey()
    {
        var methods = typeof(IProcessSession).GetMethods();

        methods.Where(method => method.GetCustomAttribute<RequiresActivationLockAttribute>() is not null).Select(method => method.Name).Order(StringComparer.Ordinal).Should().Equal(
            "DeleteActivationAsync",
            "DeleteSchedulesExceptAsync",
            "GetSchedulesForUpdateAsync",
            "ReplaceEventTriggersAsync",
            "SaveActivationAsync",
            "UpsertScheduleAsync");
        methods.Where(method => method.GetCustomAttribute<RequiresScheduleLockAttribute>() is not null).Select(method => method.Name).Should().Equal("SaveScheduleAsync");
        methods.Where(method => method.GetCustomAttribute<RequiresActivationLockAttribute>() is not null).Should().OnlyContain(method => method.GetParameters()[0].ParameterType == typeof(ActivationKey));
        methods.Where(method => method.GetCustomAttribute<RequiresScheduleLockAttribute>() is not null).Should().OnlyContain(method => method.GetParameters()[0].ParameterType == typeof(ScheduleKey));
        methods.Where(method => method.GetCustomAttribute<RequiresActivationLockAttribute>() is null && method.GetParameters().Length > 0 && method.GetParameters()[0].ParameterType == typeof(ActivationKey))
            .Select(method => method.Name).Order(StringComparer.Ordinal).Should().Equal("GetActivationAsync", "LockActivationAsync");
        methods.Where(method => method.GetCustomAttribute<RequiresScheduleLockAttribute>() is null && method.GetParameters().Length > 0 && method.GetParameters()[0].ParameterType == typeof(ScheduleKey))
            .Select(method => method.Name).Should().Equal("LockScheduleAsync");
    }

    [Fact]
    public async Task EveryGuardedActivationAndScheduleOperationFailsWithoutItsLockAndWorksOnceItIsHeld()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_schedule", "UTC"));
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        var activation = new ActivationKey(rig.Tenant, "host.trigger_schedule");
        var schedule = new ScheduleKey(rig.Tenant, "host.trigger_schedule", "daily");
        await using var session = await store.BeginAsync(CancellationToken.None);

        foreach (var method in typeof(IProcessSession).GetMethods().Where(method => method.GetCustomAttribute<RequiresActivationLockAttribute>() is not null))
        {
            var call = (Task)method.Invoke(session, Arguments(method, activation))!;
            await FluentActions.Awaiting(() => call).Should().ThrowExactlyAsync<InvalidOperationException>(method.Name);
        }

        var save = (Task)typeof(IProcessSession).GetMethod(nameof(IProcessSession.SaveScheduleAsync))!.Invoke(session, [schedule, Rig.Start, CancellationToken.None])!;
        await FluentActions.Awaiting(() => save).Should().ThrowExactlyAsync<InvalidOperationException>();

        await session.LockActivationAsync(activation, CancellationToken.None);
        (await session.GetSchedulesForUpdateAsync(activation, CancellationToken.None)).Should().HaveCount(3);
        (await session.LockScheduleAsync(schedule, skipLocked: false, CancellationToken.None)).Should().NotBeNull();
        await session.SaveScheduleAsync(schedule, Rig.Start, CancellationToken.None);
        await FluentActions.Awaiting(() => session.SaveScheduleAsync(new ScheduleKey(rig.Tenant, "host.trigger_schedule", "hourly_skip"), Rig.Start, CancellationToken.None)).Should().ThrowExactlyAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => session.DeleteActivationAsync(new ActivationKey("t-2", "host.trigger_schedule"), CancellationToken.None)).Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ADueScheduleLockedByAnotherTransactionIsSkippedWhenAskedAndAMissingOneGivesNull()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_schedule", "UTC"));
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        var key = new ScheduleKey(rig.Tenant, "host.trigger_schedule", "daily");
        await using var first = await store.BeginAsync(CancellationToken.None);
        await using var second = await store.BeginAsync(CancellationToken.None);
        var locked = await first.LockScheduleAsync(key, skipLocked: false, CancellationToken.None);

        (await second.LockScheduleAsync(key, skipLocked: true, CancellationToken.None)).Should().BeNull();
        (await second.LockScheduleAsync(new ScheduleKey(rig.Tenant, "host.trigger_schedule", "missing"), skipLocked: false, CancellationToken.None)).Should().BeNull();
        locked!.Row.Cron.Should().Be("0 6 * * *");
        locked.ProcessVersion.Should().Be(1);
    }

    private static object?[] Arguments(MethodInfo method, ActivationKey key)
    {
        var arguments = Arguments(method, new InstanceKey(key.TenantId, key.ProcessId));
        arguments[0] = key;
        return arguments;
    }

    private static object?[] Arguments(MethodInfo method, InstanceKey key)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        arguments[0] = key;
        for (var index = 1; index < parameters.Length; index++)
        {
            var type = parameters[index].ParameterType;
            if (type == typeof(CancellationToken))
            {
                arguments[index] = CancellationToken.None;
            }
            else if (type.IsValueType)
            {
                arguments[index] = Activator.CreateInstance(type);
            }
        }

        return arguments;
    }
}
