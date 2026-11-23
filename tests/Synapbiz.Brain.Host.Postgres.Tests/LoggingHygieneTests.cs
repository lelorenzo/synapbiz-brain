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
