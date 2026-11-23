using FluentAssertions;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>What the host logs: ids, kinds and codes, never a variable, an input, an output, a payload or the message of an exception.</summary>
public sealed class LoggingHygieneTests
{
    private const string CaseValue = "zq-case-4417";
    private const string ReadOutput = "zq-read-8821";
    private const string SendOutput = "zq-send-9902";
    private const string ThrownMessage = "zq-thrown-3306";
    private const string EventPayload = "zq-event-5530";
    private const string TaskData = "zq-task-6612";
    private const string CancelReason = "zq-reason-7719";

    private static readonly string[] Sentinels = [CaseValue, ReadOutput, SendOutput, ThrownMessage, EventPayload, TaskData, CancelReason];

    [Fact]
    public async Task NoLogRecordOfAWholeOutreachFlowCarriesAValueThatWentThroughIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Outreach());
        var sends = 0;
        rig.Port("intake.status").Handler = (_, _) => Task.FromResult<PortResult>(new PortCompleted("{\"missing_count\": 2, \"note\": \"" + ReadOutput + "\"}"));
        rig.Port("messaging.notice").Handler = (_, _) => ++sends == 1
            ? throw new InvalidOperationException(ThrownMessage)
            : Task.FromResult<PortResult>(new PortCompleted("{\"receipt\": \"" + SendOutput + "\"}"));

        var id = await rig.StartAsync("previsit.outreach", "case-1", "{\"case_ref\":\"" + CaseValue + "\"}");
        await rig.DrainAsync();
        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "front_desk")).Single();
        (await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "resolved", "{\"note\":\"" + TaskData + "\"}")).Disposition.Should().Be(AdvanceDisposition.Applied);
        await rig.DrainAsync();
        (await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "form_submitted", CaseValue, "{\"text\":\"" + EventPayload + "\"}"))).Status.Should().Be(PublishStatus.Queued);
        (await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-2", "nobody_waits", null, "{\"text\":\"" + EventPayload + "\"}"))).Status.Should().Be(PublishStatus.NoTarget);
        await rig.DrainAsync();
        rig.Time.Advance(TimeSpan.FromHours(25));
        await rig.DrainAsync();
        (await rig.Engine.CancelAsync(rig.Tenant, id, CancelReason)).Disposition.Should().Be(AdvanceDisposition.Applied);
        await rig.DrainAsync();

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.State!.Variables["case_ref"].Should().Be("\"" + CaseValue + "\"");
        var entries = rig.Logs.Entries;
        entries.Should().Contain(entry => entry.Message.StartsWith("process.port.exception", StringComparison.Ordinal), "the flow did log, so the check below is not vacuous");
        entries.Should().Contain(entry => entry.Message.StartsWith("process.instance.finished", StringComparison.Ordinal));
        foreach (var entry in entries)
        {
            foreach (var sentinel in Sentinels)
            {
                entry.Message.Should().NotContain(sentinel);
                (entry.ExceptionText ?? string.Empty).Should().NotContain(sentinel);
                entry.State.Select(pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Should().NotContain(value => value.Contains(sentinel, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public async Task NoLogRecordOfActivationSchedulesEventTriggersAndWindowsCarriesAValueThatWentThroughThem()
    {
        const string SiteValue = "zq-site-5521";
        const string PayloadValue = "zq-visit-7783";
        const string KeyValue = "zq-key-3392";
        const string ZoneValue = "Zq/Secret-Zone-4410";
        var sentinels = new[] { SiteValue, PayloadValue, KeyValue, ZoneValue };
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"), Fixtures.Host("trigger-event"), Fixtures.Host("window-zone-variable"));
        rig.Port("t.send");

        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_schedule", "Test/Fixed", "{\"site\":\"" + SiteValue + "\"}"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_event", null, "{\"site\":\"" + SiteValue + "\"}"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_schedule", "Test/Fixed", "{\"site\":\"" + SiteValue + "\"}"));
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "case.opened", null, "{\"case_ref\":\"" + KeyValue + "\",\"visit_date\":\"2027-02-01\",\"note\":\"" + PayloadValue + "\"}"));
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-2", "case.opened", null, "{\"note\":\"" + PayloadValue + "\"}"));
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-3", "case.opened", null, "{\"case_ref\":\"" + KeyValue + "-2\",\"visit_date\":\"" + PayloadValue + "\"}"));
        await rig.StartAsync("host.window_zone", "k-w", "{\"case_ref\":\"" + KeyValue + "\",\"practice_zone\":\"" + ZoneValue + "\"}");
        rig.Time.Advance(TimeSpan.FromHours(10));
        await rig.DrainAsync();
        rig.Catalog.Remove("host.trigger_schedule");
        rig.Time.Advance(TimeSpan.FromHours(10));
        await rig.DrainAsync();
        rig.Zones.Clear();
        rig.Time.Advance(TimeSpan.FromHours(30));
        await rig.DrainAsync();
        await rig.Engine.DeactivateAsync(rig.Tenant, "host.trigger_schedule");

        var entries = rig.Logs.Entries;
        foreach (var expected in new[] { 13, 14, 15, 16, 17, 18, 19 })
        {
            entries.Should().Contain(entry => entry.EventId.Id == expected, "the flow produced log event " + expected + ", so the check below is not vacuous");
        }

        foreach (var entry in entries)
        {
            foreach (var sentinel in sentinels)
            {
                entry.Message.Should().NotContain(sentinel);
                (entry.ExceptionText ?? string.Empty).Should().NotContain(sentinel);
                entry.State.Select(pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Should().NotContain(value => value.Contains(sentinel, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public async Task EveryLogRecordOfTheHostCarriesAnEventIdAndAStableCodePrefix()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        rig.Port("t.send").Handler = (_, _) => throw new InvalidOperationException(ThrownMessage);
        await rig.StartAsync("host.repeat", "k-1", "{\"case_ref\":\"" + CaseValue + "\"}");
        await rig.DrainAsync();

        var hostEntries = rig.Logs.Entries.Where(entry => entry.Message.StartsWith("process.", StringComparison.Ordinal)).ToList();

        hostEntries.Should().NotBeEmpty();
        hostEntries.Should().OnlyContain(entry => entry.EventId.Id > 0);
        hostEntries.Where(entry => entry.Level >= LogLevel.Warning).Should().OnlyContain(entry => entry.Message.Contains(':', StringComparison.Ordinal));
    }
}
