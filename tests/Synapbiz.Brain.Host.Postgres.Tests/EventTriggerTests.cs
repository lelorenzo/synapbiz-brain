using FluentAssertions;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>Event triggers: an event starts one instance per business key inside the de-duplication of its event id.</summary>
public sealed class EventTriggerTests
{
    private const string Process = "host.trigger_event";
    private const string Second = "host.trigger_event_second";

    private static async Task<Rig> ActivatedAsync(IPayloadProtector? protector = null)
    {
        var rig = await Rig.CreateAsync(protector);
        rig.Catalog.Add(Fixtures.Host("trigger-event"), Fixtures.Host("trigger-event-second"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, null, "{\"site\":\"s-1\"}"));
        return rig;
    }

    private static ProcessEvent Opened(string eventId, string caseRef = "c-1", string tenant = "t-1", string payload = "") =>
        new(tenant, eventId, "case.opened", null, payload.Length > 0 ? payload : "{\"case_ref\":\"" + caseRef + "\",\"visit_date\":\"2027-02-01\",\"ignored\":1}");

    [Fact]
    public async Task AnEventStartsOneInstanceWithTheActivationInputAndTheMappedPayloadMembers()
    {
        await using var rig = await ActivatedAsync();

        var published = await rig.Engine.PublishEventAsync(Opened("e-1"));
        var before = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1");
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        before!.Status.Should().Be(ProcessStatus.Running, "the start is applied by the pump, so until then the instance is reported as running without state");
        before.State.Should().BeNull();
        var info = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1");
        info!.Status.Should().Be(ProcessStatus.Waiting);
        info.ProcessVersion.Should().Be(1);
        info.State!.Variables["case_ref"].Should().Be("\"c-1\"");
        info.State.Variables["visit_date"].Should().Be("\"2027-02-01\"");
        info.State.Variables["site"].Should().Be("\"s-1\"");
        info.State.Variables.Should().NotContainKey("ignored");
        info.ParentInstanceId.Should().BeNull();
    }

    [Fact]
    public async Task PublishingTheSameEventIdTwiceStartsOneInstanceAndTheSecondIsADuplicate()
    {
        await using var rig = await ActivatedAsync();

        var first = await rig.Engine.PublishEventAsync(Opened("e-1"));
        var second = await rig.Engine.PublishEventAsync(Opened("e-1"));
        await rig.DrainAsync();

        first.Status.Should().Be(PublishStatus.Queued);
        second.Should().Be(new PublishResult(PublishStatus.Duplicate, 0));
        (await rig.CountAsync("process_instance")).Should().Be(1);
    }

    [Fact]
    public async Task AnotherEventForTheSameBusinessKeyLeavesTheExistingInstanceAloneAndCountsNothingStarted()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-event"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, null, "{\"site\":\"s-1\"}"));
        await rig.Engine.PublishEventAsync(Opened("e-1"));
        var id = (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1"))!.InstanceId;

        // Before the pump has started it, nothing waits for the second event, so it starts nothing and has no target.
        var again = await rig.Engine.PublishEventAsync(Opened("e-2"));

        again.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        (await rig.CountAsync("process_instance")).Should().Be(1);
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1"))!.InstanceId.Should().Be(id);
    }

    [Fact]
    public async Task TheResultCountsStartedInstancesAndTheInstancesTheEventWasQueuedForTogether()
    {
        await using var rig = await ActivatedAsync();
        await rig.Engine.PublishEventAsync(Opened("e-1"));
        await rig.DrainAsync();

        // The instance of c-1 now waits for case.opened with the correlation c-1; c-2 is new.
        var waiting = await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-2", "case.opened", "c-1", "{\"case_ref\":\"c-1\",\"visit_date\":\"2027-02-01\"}"));
        var both = await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-3", "case.opened", "c-1", "{\"case_ref\":\"c-2\",\"visit_date\":\"2027-02-01\"}"));
        await rig.DrainAsync();

        waiting.Should().Be(new PublishResult(PublishStatus.Queued, 1), "queued for the waiting instance, nothing started because its key exists");
        both.Should().Be(new PublishResult(PublishStatus.Queued, 2), "queued for the waiting instance and started for the new key");
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1"))!.Result.Should().Be("reopened");
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-2"))!.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public async Task TwentyEventsForTheSameKeyPublishedAtOnceStartExactlyOneInstance()
    {
        await using var rig = await ActivatedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() => rig.Engine.PublishEventAsync(Opened("e-" + index)))));
        await rig.DrainAsync();

        results.Sum(result => result.Instances).Should().Be(1, "one publisher created the instance, the others found the key taken");
        results.Count(result => result.Status == PublishStatus.Queued).Should().Be(1);
        results.Count(result => result.Status == PublishStatus.NoTarget).Should().Be(19);
        (await rig.CountAsync("process_instance")).Should().Be(1);
        (await rig.CountAsync("process_inbox", "kind = 1")).Should().Be(1);
        (await rig.CountAsync("process_event")).Should().Be(20);
    }

    [Fact]
    public async Task ManyEventsForDifferentKeysPublishedAtOnceStartOneInstanceEach()
    {
        await using var rig = await ActivatedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(index => Task.Run(() => rig.Engine.PublishEventAsync(Opened("e-" + index, "c-" + (index % 10))))));
        await rig.DrainAsync();

        results.Sum(result => result.Instances).Should().Be(10);
        (await rig.CountAsync("process_instance")).Should().Be(10);
        (await rig.CountAsync("process_instance", "status = 2")).Should().Be(10, "every instance was started and waits");
    }

    [Theory]
    [InlineData("{\"visit_date\":\"2027-02-01\"}", "no member")]
    [InlineData("{\"case_ref\":\"\",\"visit_date\":\"2027-02-01\"}", "empty")]
    [InlineData("{\"case_ref\":17,\"visit_date\":\"2027-02-01\"}", "a number")]
    [InlineData("{\"case_ref\":null,\"visit_date\":\"2027-02-01\"}", "null")]
    [InlineData("{\"case_ref\":{\"x\":1},\"visit_date\":\"2027-02-01\"}", "an object")]
    [InlineData("{\"case_ref\":\"c-1\",\"case_ref\":\"c-2\",\"visit_date\":\"2027-02-01\"}", "a member that is written twice")]
    public async Task APayloadWithoutAUsableBusinessKeyStartsNothingAndIsLoggedWithIdsOnly(string payload, string why)
    {
        await using var rig = await ActivatedAsync();

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", payload: payload));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.NoTarget, 0), why);
        (await rig.CountAsync("process_instance")).Should().Be(0);
        var warning = rig.Logs.Entries.Single(entry => entry.EventId.Id == 13);
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain("e-1").And.Contain("case.opened").And.Contain(Process).And.StartWith("process.trigger.no_business_key");
    }

    [Fact]
    public async Task ABusinessKeyLongerThanTheLimitStartsNothing()
    {
        await using var rig = await ActivatedAsync();

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", new string('k', 201)));

        published.Status.Should().Be(PublishStatus.NoTarget);
        (await rig.CountAsync("process_instance")).Should().Be(0);
    }

    [Fact]
    public async Task TheEventIdIsConsumedEvenWhenThePayloadHadNoKey()
    {
        await using var rig = await ActivatedAsync();

        await rig.Engine.PublishEventAsync(Opened("e-1", payload: "{}"));
        var retry = await rig.Engine.PublishEventAsync(Opened("e-1"));

        retry.Status.Should().Be(PublishStatus.Duplicate);
        (await rig.CountAsync("process_instance")).Should().Be(0);
    }

    [Fact]
    public async Task OneEventStartsAnInstanceOfEveryActivatedProcessThatNamesItAndEachReadsItsOwnKeyMember()
    {
        await using var rig = await ActivatedAsync();
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Second));

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", payload: "{\"case_ref\":\"c-1\",\"visit_date\":\"2027-02-01\",\"file_no\":\"f-9\"}"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 2));
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1"))!.Status.Should().Be(ProcessStatus.Waiting);
        var second = await rig.Engine.FindInstanceAsync(rig.Tenant, Second, "f-9");
        second!.Result.Should().Be("filed");
        second.State!.Variables["case_ref"].Should().Be("\"c-1\"");
    }

    [Fact]
    public async Task OneTriggerWithoutAKeyDoesNotStopTheOthersOfTheSameEvent()
    {
        await using var rig = await ActivatedAsync();
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Second));

        var published = await rig.Engine.PublishEventAsync(Opened("e-1"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1), "the second process wants file_no, which the payload lacks");
        (await rig.CountAsync("process_instance", "process_id = @p", ("p", Process))).Should().Be(1);
        (await rig.CountAsync("process_instance", "process_id = @p", ("p", Second))).Should().Be(0);
    }

    [Fact]
    public async Task AnEventAddressedToAnInstanceStillRunsTheEventTriggersOfTheTenant()
    {
        await using var rig = await ActivatedAsync();
        await rig.Engine.PublishEventAsync(Opened("e-1"));
        await rig.DrainAsync();
        var waiting = (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1"))!.InstanceId;

        var published = await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-2", "case.opened", "c-1", "{\"case_ref\":\"c-5\",\"visit_date\":\"2027-02-01\"}", waiting));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 2));
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-5")).Should().NotBeNull();
    }

    [Fact]
    public async Task AnInputTheDefinitionRejectsCancelsTheStartedInstanceWithTheCodeInsteadOfFailingThePublisher()
    {
        await using var rig = await ActivatedAsync();

        var published = await rig.Engine.PublishEventAsync(Opened("e-1", payload: "{\"case_ref\":\"c-1\",\"visit_date\":\"not-a-date\"}"));
        await rig.DrainAsync();

        published.Status.Should().Be(PublishStatus.Queued);
        var info = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1");
        info!.Status.Should().Be(ProcessStatus.Cancelled);
        info.Problem!.Code.Should().Be("process.variable.type_mismatch");
        (await rig.CountAsync("process_inbox", "kind = 1 and status = 3 and code = 'process.variable.type_mismatch'")).Should().Be(1);
    }

    [Fact]
    public async Task TheStartIsKeptWhileTheCatalogLacksTheDefinitionAndAppliedWhenItIsBack()
    {
        await using var rig = await ActivatedAsync();
        var definition = Fixtures.Host("trigger-event");
        rig.Catalog.Remove(Process);

        var published = await rig.Engine.PublishEventAsync(Opened("e-1"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1), "publishing never needs the catalog: the activation holds what it needs");
        (await rig.CountAsync("process_inbox", "kind = 1 and status = 1")).Should().Be(1);
        rig.Logs.Entries.Should().Contain(entry => entry.EventId.Id == 1 && entry.Message.StartsWith("process.definition.unavailable", StringComparison.Ordinal));

        rig.Catalog.Add(definition);
        await rig.DrainAsync();
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "c-1"))!.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public async Task ADeactivatedProcessStartsNothing()
    {
        await using var rig = await ActivatedAsync();
        await rig.Engine.DeactivateAsync(rig.Tenant, Process);

        var published = await rig.Engine.PublishEventAsync(Opened("e-1"));

        published.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        (await rig.CountAsync("process_instance")).Should().Be(0);
    }

    [Fact]
    public async Task TheEventTriggersOfOneTenantNeverStartAnInstanceOfAnotherAndTheSameEventIdAndKeyDoNotCollide()
    {
        await using var rig = await ActivatedAsync();
        await rig.Engine.ActivateAsync(new ProcessActivation("t-2", Process, null, "{\"site\":\"s-2\"}"));

        var one = await rig.Engine.PublishEventAsync(Opened("e-1"));
        var other = await rig.Engine.PublishEventAsync(Opened("e-1", tenant: "t-2"));
        var nobody = await rig.Engine.PublishEventAsync(Opened("e-1", tenant: "t-3"));
        await rig.DrainAsync();

        one.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        other.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        nobody.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        (await rig.Engine.FindInstanceAsync("t-1", Process, "c-1"))!.State!.Variables["site"].Should().Be("\"s-1\"");
        (await rig.Engine.FindInstanceAsync("t-2", Process, "c-1"))!.State!.Variables["site"].Should().Be("\"s-2\"");
        (await rig.Engine.FindInstanceAsync("t-3", Process, "c-1")).Should().BeNull();
    }

    [Fact]
    public async Task ThePayloadInputAndTheKeyOfAStartAreStoredProtected()
    {
        var protector = new ReversingProtector();
        await using var rig = await ActivatedAsync(protector);

        await rig.Engine.PublishEventAsync(Opened("e-1", "case-secret-77"));

        var payload = await rig.ScalarAsync<string>("select payload from {schema}.process_inbox where kind = 1");
        payload.Should().StartWith(ReversingProtector.Marker).And.NotContain("case-secret-77");
        (await rig.ScalarAsync<string>("select input from {schema}.process_activation")).Should().StartWith(ReversingProtector.Marker).And.NotContain("s-1");
        await rig.DrainAsync();
        (await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "case-secret-77"))!.State!.Variables["case_ref"].Should().Be("\"case-secret-77\"");
        (await rig.ColumnAsync<string>("select state from {schema}.process_instance")).Should().OnlyContain(state => state.StartsWith(ReversingProtector.Marker, StringComparison.Ordinal));
    }
}
