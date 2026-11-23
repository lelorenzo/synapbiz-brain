using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class ProtectionTests
{
    private const string Secret = "secret-case-7";

    private static readonly (string Table, string Column)[] ProtectedColumns =
    [
        ("process_instance", "state"),
        ("process_history", "data"),
        ("process_effect", "input"),
        ("process_task", "show"),
        ("process_inbox", "payload"),
    ];

    [Fact]
    public async Task NoProtectedColumnHoldsAPlaintextValueAndEverythingRoundTrips()
    {
        await using var rig = await Rig.CreateAsync(new ReversingProtector());
        rig.Catalog.Add(Fixtures.Outreach());
        rig.Port("intake.status").Handler = (call, _) => Task.FromResult<PortResult>(new PortCompleted(
            call.Request.Step == "check" && call.Request.EffectId.Contains("/check/", StringComparison.Ordinal) ? "{\"missing_count\": 4}" : "{}"));
        rig.Port("messaging.notice").Handler = (_, _) => Task.FromResult<PortResult>(new PortFailed(ProcessErrorKind.Unknown, "send.lost"));
        var id = await rig.StartAsync("previsit.outreach", "case-1", "{\"case_ref\":\"" + Secret + "\"}");
        await rig.DrainAsync();
        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "front_desk")).Single();
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "form_submitted", Secret, "{\"note\":\"" + Secret + "\"}", id));
        await rig.DrainAsync();

        foreach (var (table, column) in ProtectedColumns)
        {
            var values = await rig.ColumnAsync<string>($"select {column} from {{schema}}.{table} where {column} is not null");
            values.Should().NotBeEmpty($"{table}.{column} must have been written");
            values.Should().OnlyContain(value => value.StartsWith(ReversingProtector.Marker, StringComparison.Ordinal), $"{table}.{column} is protected");
            values.Should().NotContain(value => value.Contains(Secret, StringComparison.Ordinal), $"{table}.{column} holds no plaintext value");
            values.Should().NotContain(value => value.Contains("case_ref", StringComparison.Ordinal), $"{table}.{column} holds no readable variable name");
        }

        task.ShowJson.Should().Contain(Secret);
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.State!.Variables["case_ref"].Should().Be("\"" + Secret + "\"");
        var history = await rig.Engine.GetHistoryAsync(rig.Tenant, id);
        history.Where(entry => entry.DataJson is not null).Should().NotBeEmpty();
        history.Should().Contain(entry => entry.DataJson != null && entry.DataJson.Contains("rule", StringComparison.Ordinal));
        (await rig.ColumnAsync<string>("select input from {schema}.process_effect")).Select(text => new string([.. text[(ReversingProtector.Marker.Length + rig.Tenant.Length + 1)..].Reverse()]))
            .Should().Contain(text => text.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProtectedPayloadIsUnprotectedForTheTenantItBelongsToOnly()
    {
        await using var rig = await Rig.CreateAsync(new ReversingProtector());
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = (await rig.Engine.StartAsync(new StartRequest("tenant-a", "host.task", "k-1", "{\"case_ref\":\"" + Secret + "\"}"))).InstanceId;

        (await rig.Engine.GetInstanceAsync("tenant-a", id))!.State!.Variables["case_ref"].Should().Contain(Secret);
        (await rig.Engine.GetInstanceAsync("tenant-b", id)).Should().BeNull();
        await rig.Engine.Invoking(engine => engine.ListOpenTasksAsync("tenant-a", "review_q")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task TheInboxRowsOfTheChildAndCancelPathsHoldNoPlaintextAndStillRoundTrip()
    {
        const string childValue = "v-secret-9";
        await using var rig = await Rig.CreateAsync(new ReversingProtector());
        rig.Catalog.Add(Fixtures.Host("parent"), Fixtures.Host("child"));
        var input = "{\"case_ref\":\"" + Secret + "\"}";

        // 1. a child that completes: Start for the child, ChildCompleted for the parent.
        var completing = await rig.StartAsync("host.parent", "k-1", input);
        await rig.DrainAsync();
        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "child_q")).Single();
        await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "finish", "{\"value\":\"" + childValue + "\"}");
        await rig.DrainAsync();

        // 2. a parent that is cancelled: Cancel for the child.
        var cancelledParent = await rig.StartAsync("host.parent", "k-2", input);
        await rig.DrainAsync();
        await rig.Engine.CancelAsync(rig.Tenant, cancelledParent, "operator");
        await rig.DrainAsync();

        // 3. a child that is cancelled: ChildFailed for the parent.
        var failingParent = await rig.StartAsync("host.parent", "k-3", input);
        await rig.DrainAsync();
        var child = (await rig.Engine.FindInstanceAsync(rig.Tenant, "host.child", failingParent + "/spawn/1/child"))!;
        await rig.Engine.CancelAsync(rig.Tenant, child.InstanceId, "operator");
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, completing))!.State!.Variables["child_value"].Should().Be("\"" + childValue + "\"");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, cancelledParent))!.Status.Should().Be(ProcessStatus.Cancelled);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, failingParent))!.Result.Should().Be("child_failed");
        (await rig.CountAsync("process_inbox", "status <> 2")).Should().Be(0, "every row was applied, so every protected payload was read back");

        var plaintexts = new Dictionary<int, string[]>
        {
            [1] = [Secret],
            [3] = [Secret, childValue, "finished"],
            [4] = ["process.child.cancelled"],
            [5] = ["process.parent.cancelled"],
        };
        foreach (var (kind, known) in plaintexts)
        {
            var payloads = await rig.ColumnAsync<string>("select payload from {schema}.process_inbox where kind = @k", ("k", (short)kind));
            payloads.Should().NotBeEmpty($"a row of kind {kind} was written");
            payloads.Should().OnlyContain(value => value.StartsWith(ReversingProtector.Marker, StringComparison.Ordinal), $"kind {kind} is protected");
            foreach (var text in known.Concat(["childKey", "\"reason\"", "\"code\"", "\"input\"", "\"output\""]))
            {
                payloads.Should().NotContain(value => value.Contains(text, StringComparison.Ordinal), $"kind {kind} holds no plaintext {text}");
            }
        }
    }

    [Fact]
    public async Task TheActivationInputAndTheWindowFailureOfATriggerFlowAreProtectedAndRoundTrip()
    {
        const string zone = "Zq/Secret-Zone-1";
        await using var rig = await Rig.CreateAsync(new ReversingProtector());
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"), Fixtures.Host("window-zone-variable"));
        var activation = new ProcessActivation(rig.Tenant, "host.trigger_schedule", "Test/Fixed", "{\"site\":\"" + Secret + "\"}");

        await rig.Engine.ActivateAsync(activation);
        (await rig.Engine.ActivateAsync(activation)).Changed.Should().BeFalse("the stored input is read back through the protector and compared with the new one");
        await rig.StartAsync("host.window_zone", "k-1", "{\"case_ref\":\"c-1\",\"practice_zone\":\"" + zone + "\"}");
        rig.Time.Advance(TimeSpan.FromMinutes(30));
        await rig.DrainAsync();

        var inputs = await rig.ColumnAsync<string>("select input from {schema}.process_activation");
        inputs.Should().ContainSingle().Which.Should().StartWith(ReversingProtector.Marker).And.NotContain(Secret).And.NotContain("site");
        foreach (var kind in new[] { 1, 6 })
        {
            var payloads = await rig.ColumnAsync<string>("select payload from {schema}.process_inbox where kind = @k", ("k", (short)kind));
            payloads.Should().NotBeEmpty($"a row of kind {kind} was written");
            payloads.Should().OnlyContain(value => value.StartsWith(ReversingProtector.Marker, StringComparison.Ordinal));
            payloads.Should().NotContain(value => value.Contains(Secret, StringComparison.Ordinal) || value.Contains("timerId", StringComparison.Ordinal) || value.Contains("\"input\"", StringComparison.Ordinal));
        }

        (await rig.CountAsync("process_inbox", "status <> 2")).Should().Be(0, "every row was applied, so every protected payload was read back");
        (await rig.Engine.FindInstanceAsync(rig.Tenant, "host.trigger_schedule", "hourly_skip/20270104T1430Z"))!.State!.Variables["site"].Should().Be("\"" + Secret + "\"");
        (await rig.Engine.FindInstanceAsync(rig.Tenant, "host.window_zone", "k-1"))!.Result.Should().Be("failed");
    }
}
