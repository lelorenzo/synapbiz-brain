using FluentAssertions;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// Section 10.1 of the W1-C specification: what makes a business key taken from an event payload unusable, and what that never
/// costs: the event id is recorded and the instances that wait for the event still get it. Also what else a payload can hold that
/// no store keeps or no reader can read, and how each case ends.
/// </summary>
public sealed class EventTriggerKeyTests
{
    private const string Process = "host.trigger_event";
    private const string Mapped = "host.trigger_event_mapped";

    // The JSON text of the key member, and a fragment of it that must never appear in a log record.
    public static TheoryData<string, string, string> BadKeys => new()
    {
        { "an unpaired high surrogate", "\"\\ud800\"", "ud800" },
        { "an unpaired low surrogate", "\"x\\udc00y\"", "udc00" },
        { "a pair in the wrong order", "\"\\udc00\\ud800\"", "udc00" },
        { "U+0000", "\"leakA\\u0000leakB\"", "leakA" },
        { "a tab", "\"leakA\\tleakB\"", "leakA" },
        { "a line feed", "\"leakA\\nleakB\"", "leakA" },
        { "a delete character", "\"leakA\\u007fleakB\"", "leakA" },
        { "a C1 control character", "\"leakA\\u0085leakB\"", "leakA" },
        { "only spaces", "\"     \"", "     " },
        { "only a no-break space", "\"\\u00a0\\u2003\"", "\u00a0" },
        { "nothing", "\"\"", "case_ref" },
        { "a leading @", "\"@schedule/hourly_skip/20270104T1430Z\"", "@schedule" },
        { "a leading @ after spaces", "\"   @x\"", "@x" },
        { "201 characters", "\"" + new string('k', 201) + "\"", "kkkkkkkkkk" },
    };

    private static async Task<Rig> ActivatedAsync(string fixture = "trigger-event", string process = Process)
    {
        var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host(fixture));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, process, null, process == Process ? "{\"site\":\"s-1\"}" : "{}"));
        return rig;
    }

    private static ProcessEvent Opened(string eventId, string payload, string? correlation = null) =>
        new("t-1", eventId, "case.opened", correlation, payload);

    private static string Payload(string key) => "{\"case_ref\":" + key + ",\"visit_date\":\"2027-02-01\"}";

    [Theory]
    [MemberData(nameof(BadKeys))]
    public async Task ABadKeyStartsNothingIsLoggedWithIdsOnlyAndStillRecordsTheEventAndWakesTheWaitingInstance(string why, string key, string leak)
    {
        await using var rig = await ActivatedAsync();
        await rig.Engine.PublishEventAsync(Opened("e-0", Payload("\"c-1\"")));
        await rig.DrainAsync();
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1"))!.Status.Should().Be(ProcessStatus.Waiting);

        var published = await rig.Engine.PublishEventAsync(Opened("e-bad", Payload(key), "c-1"));
        var again = await rig.Engine.PublishEventAsync(Opened("e-bad", Payload(key), "c-1"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1), why + ": nothing is started, the waiting instance is queued");
        again.Should().Be(new PublishResult(PublishStatus.Duplicate, 0), why + ": the event id was recorded");
        (await rig.CountAsync("process_event", "event_id = 'e-bad'")).Should().Be(1);
        (await rig.CountAsync("process_instance")).Should().Be(1);
        var warnings = rig.Logs.Entries.Where(entry => entry.EventId.Id == 13).ToList();
        warnings.Should().ContainSingle();
        warnings[0].Level.Should().Be(LogLevel.Warning);
        warnings[0].Message.Should().StartWith("process.trigger.no_business_key").And.Contain("e-bad").And.Contain("case.opened").And.Contain(Process).And.NotContain(leak);
        warnings[0].State.Select(pair => pair.Key).Should().BeEquivalentTo("TenantId", "EventId", "EventName", "ProcessId", "{OriginalFormat}");
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12, "no item of the pump failed");
        var waiting = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1");
        if (key.Contains("\\ud", StringComparison.Ordinal) || key.Contains("\\udc", StringComparison.Ordinal))
        {
            waiting!.Status.Should().Be(ProcessStatus.Suspended, why + ": the interpreter cannot read the payload, which is a type mismatch");
            waiting.Problem!.Code.Should().Be("process.variable.type_mismatch");
        }
        else
        {
            waiting!.Status.Should().Be(ProcessStatus.Completed, why + ": the wait took the event");
            waiting.Result.Should().Be("reopened");
        }

        (await rig.CountAsync("process_inbox", "status = 1")).Should().Be(0, "no inbox row is left pending");
    }

    [Theory]
    [MemberData(nameof(BadKeys))]
    public async Task ABadKeyWithNobodyWaitingIsNoTargetAndTheEventIdIsStillRecorded(string why, string key, string leak)
    {
        await using var rig = await ActivatedAsync();

        var published = await rig.Engine.PublishEventAsync(Opened("e-bad", Payload(key)));
        var again = await rig.Engine.PublishEventAsync(Opened("e-bad", Payload("\"c-1\"")));

        published.Should().Be(new PublishResult(PublishStatus.NoTarget, 0), why);
        again.Status.Should().Be(PublishStatus.Duplicate, why);
        (await rig.CountAsync("process_event", "event_id = 'e-bad'")).Should().Be(1);
        (await rig.CountAsync("process_instance")).Should().Be(0);
        rig.Logs.Entries.Single(entry => entry.EventId.Id == 13).Message.Should().NotContain(leak);
    }

    [Theory]
    [InlineData("c-1")]
    [InlineData("  padded key  ")]
    [InlineData("a b/c@d")]
    [InlineData("caf\u00e9 \ud83d\ude00")]
    public async Task AKeyThatIsUsableStartsAnInstanceAndIsStoredAsItWasWritten(string key)
    {
        await using var rig = await ActivatedAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(key);

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", Payload(json)));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, key))!.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public async Task AKeyOfExactlyTwoHundredCharactersIsUsable()
    {
        await using var rig = await ActivatedAsync();

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", Payload("\"" + new string('k', 200) + "\"")));

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1));
    }

    [Theory]
    [InlineData("{\"case_ref\":\"c-1\",\"visit_date\":\"2027-02-01\",\"other\":\"\\ud800\"}", "an unpaired surrogate in a value no trigger maps")]
    [InlineData("{\"case_ref\":\"c-1\",\"visit_date\":\"2027-02-01\",\"\\ud800\":1}", "an unpaired surrogate in a member name")]
    [InlineData("{\"case_ref\":\"c-1\",\"visit_date\":\"2027-02-01\",\"deep\":{\"list\":[1,\"\\udc00\"]}}", "an unpaired surrogate deep inside")]
    public async Task APayloadThatHoldsAnUnreadableStringAnywhereStartsNothingAndNeverThrows(string payload, string why)
    {
        await using var rig = await ActivatedAsync();

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", payload));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.NoTarget, 0), why);
        (await rig.CountAsync("process_event")).Should().Be(1);
        (await rig.CountAsync("process_instance")).Should().Be(0);
        rig.Logs.Entries.Should().ContainSingle(entry => entry.EventId.Id == 13);
    }

    [Fact]
    public async Task APayloadTextThatCannotBeEncodedIsRefusedBeforeAnythingIsWrittenAndTheEventIdIsNotConsumed()
    {
        await using var rig = await ActivatedAsync();
        var raw = "{\"case_ref\":\"c-\ud800\"}";

        var act = () => rig.Engine.PublishEventAsync(Opened("e-1", raw));

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("JSON object");
        (await rig.CountAsync("process_event")).Should().Be(0);
        var retry = await rig.Engine.PublishEventAsync(Opened("e-1", Payload("\"c-1\"")));
        retry.Should().Be(new PublishResult(PublishStatus.Queued, 1));
    }

    [Fact]
    public async Task AMappedValueWithU0000IsKeptAsDataAndTheInstanceStarts()
    {
        await using var rig = await ActivatedAsync("trigger-event-mapped", Mapped);

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", "{\"case_ref\":\"c-1\",\"ref_code\":\"r-1\",\"note\":\"a\\u0000b\"}"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        var info = await rig.Engine.FindInstanceAsync(rig.Tenant, Mapped, "c-1");
        info!.Status.Should().Be(ProcessStatus.Waiting);
        info.State!.Variables["note"].Should().Be("\"a\\u0000b\"");
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12);
    }

    [Fact]
    public async Task AMappedValueWithU0000ThatABeingWaitedCorrelationNeedsSuspendsTheInstanceInsteadOfPoisoningTheStartRow()
    {
        await using var rig = await ActivatedAsync("trigger-event-mapped", Mapped);

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", "{\"case_ref\":\"c-1\",\"ref_code\":\"r\\u0000-1\",\"note\":\"n\"}"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        var info = await rig.Engine.FindInstanceAsync(rig.Tenant, Mapped, "c-1");
        info!.Status.Should().Be(ProcessStatus.Suspended);
        info.Problem!.Code.Should().Be("process.wait.correlation_invalid");
        (await rig.CountAsync("process_inbox", "kind = 1 and status = 2")).Should().Be(1, "the start row is done, not pending and not failing on every pass");
        (await rig.CountAsync("process_wait")).Should().Be(0);
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12);
        (await rig.DrainAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnUnreadableStringInAMappedValueStartsNothing()
    {
        await using var rig = await ActivatedAsync("trigger-event-mapped", Mapped);

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", "{\"case_ref\":\"c-1\",\"ref_code\":\"r-1\",\"note\":\"\\ud800\"}"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        (await rig.CountAsync("process_event")).Should().Be(1);
        (await rig.CountAsync("process_instance")).Should().Be(0);
    }

    [Fact]
    public async Task APayloadKeyCanNeverEqualTheKeyOfAScheduleStart()
    {
        await using var rig = await ActivatedAsync();

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", Payload("\"@schedule/daily/20270105T1100Z\"")));

        published.Status.Should().Be(PublishStatus.NoTarget);
        (await rig.CountAsync("process_instance", "business_key like '@%'")).Should().Be(0);
    }
}
