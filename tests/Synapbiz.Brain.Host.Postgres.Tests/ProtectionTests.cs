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
}
