using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class EventTests
{
    private static ProcessEvent Reply(string eventId, string? correlation, string? instanceId = null, string tenant = "t-1", string name = "reply", string payload = "{\"text\":\"a-1\"}") =>
        new(tenant, eventId, name, correlation, payload, instanceId);

    [Fact]
    public async Task APublishedEventIsQueuedOnceAndAppliedByThePump()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var id = await rig.StartAsync("host.wait", "k-1");

        var first = await rig.Engine.PublishEventAsync(Reply("e-1", "c-1"));
        var again = await rig.Engine.PublishEventAsync(Reply("e-1", "c-1"));

        first.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        again.Should().Be(new PublishResult(PublishStatus.Duplicate, 0));
        (await rig.CountAsync("process_inbox")).Should().Be(1);
        (await rig.CountAsync("process_event")).Should().Be(1);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Waiting);

        await rig.DrainAsync();

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Completed);
        info.Result.Should().Be("replied");
        info.State!.Variables["answer"].Should().Be("\"a-1\"");
        (await rig.CountAsync("process_inbox", "status = 2 and code is null")).Should().Be(1);
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task AnEventIsRoutedByNameAndCorrelationToTheInstancesThatWaitForIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var one = await rig.StartAsync("host.wait", "k-1", "{\"case_ref\":\"c-1\"}");
        var two = await rig.StartAsync("host.wait", "k-2", "{\"case_ref\":\"c-2\"}");
        var three = await rig.StartAsync("host.wait", "k-3", "{\"case_ref\":\"c-3\"}");

        var result = await rig.Engine.PublishEventAsync(Reply("e-1", "c-2"));
        await rig.DrainAsync();

        result.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        (await rig.Engine.GetInstanceAsync(rig.Tenant, one))!.Status.Should().Be(ProcessStatus.Waiting);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, two))!.Result.Should().Be("replied");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, three))!.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public async Task AWaitWithoutACorrelationReceivesEveryEventOfItsNameAndItsCountIsReported()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-any"), Fixtures.Host("wait-reply"));
        var a = await rig.StartAsync("host.waitany", "k-1");
        var b = await rig.StartAsync("host.waitany", "k-2");
        var other = await rig.StartAsync("host.wait", "k-3");

        var result = await rig.Engine.PublishEventAsync(Reply("e-1", "x-9", name: "broadcast", payload: "{}"));
        await rig.DrainAsync();

        result.Should().Be(new PublishResult(PublishStatus.Queued, 2));
        (await rig.Engine.GetInstanceAsync(rig.Tenant, a))!.Result.Should().Be("heard");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, b))!.Result.Should().Be("heard");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, other))!.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public async Task AnEventWithoutACorrelationReachesOnlyWaitsWithoutOne()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        await rig.StartAsync("host.wait", "k-1");

        var result = await rig.Engine.PublishEventAsync(Reply("e-1", null));

        result.Status.Should().Be(PublishStatus.NoTarget);
    }

    [Fact]
    public async Task AnEventWithNoTargetIsKeptSoARetryOfTheSameIdIsADuplicate()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));

        var first = await rig.Engine.PublishEventAsync(Reply("e-1", "c-1"));
        var unknownInstance = await rig.Engine.PublishEventAsync(Reply("e-2", "c-1", instanceId: "nope"));
        var retry = await rig.Engine.PublishEventAsync(Reply("e-1", "c-1"));

        first.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        unknownInstance.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        retry.Should().Be(new PublishResult(PublishStatus.Duplicate, 0));
        (await rig.CountAsync("process_event")).Should().Be(2);
        (await rig.CountAsync("process_inbox")).Should().Be(0);
    }

    [Fact]
    public async Task AnAcceptedEventForAnInstanceThatDoesNotWaitYetIsBufferedAndConsumedLater()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("buffer"));
        var id = await rig.StartAsync("host.buffer", "k-1");

        var result = await rig.Engine.PublishEventAsync(Reply("e-1", null, instanceId: id, name: "early", payload: "{\"text\":\"first\"}"));
        await rig.Engine.PublishEventAsync(Reply("e-2", null, instanceId: id, name: "early", payload: "{\"text\":\"second\"}"));
        await rig.DrainAsync();

        result.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        var buffered = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        buffered!.CurrentStep.Should().Be("work");
        buffered.State!.Inbox.Select(item => item.PayloadJson).Should().Equal("{\"text\":\"first\"}", "{\"text\":\"second\"}");

        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "work_q")).Single();
        (await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "go", "{}")).Disposition.Should().Be(AdvanceDisposition.Applied);

        var done = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        done!.Result.Should().Be("consumed");
        done.State!.Variables["answer"].Should().Be("\"first\"");
        done.State.Inbox.Select(item => item.PayloadJson).Should().Equal("{\"text\":\"second\"}");
    }

    [Fact]
    public async Task AnEventThatIsNotAcceptedMarksItsInboxRowRejectedAndChangesNothing()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var id = await rig.StartAsync("host.wait", "k-1");
        var revision = await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", id));
        var history = await rig.CountAsync("process_history");

        var result = await rig.Engine.PublishEventAsync(Reply("e-1", null, instanceId: id, name: "bogus", payload: "{}"));
        await rig.DrainAsync();

        result.Status.Should().Be(PublishStatus.Queued);
        (await rig.CountAsync("process_inbox", "status = 3 and code = 'process.event.not_accepted'")).Should().Be(1);
        (await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", id))).Should().Be(revision);
        (await rig.CountAsync("process_history")).Should().Be(history);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Waiting);
        rig.Logs.Entries.Should().Contain(entry => entry.Message.StartsWith("process.inbox.refused", StringComparison.Ordinal) && entry.Message.Contains("process.event.not_accepted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEventForAFinishedInstanceIsClosedAsStale()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var id = await rig.StartAsync("host.wait", "k-1");
        await rig.Engine.PublishEventAsync(Reply("e-1", "c-1"));
        await rig.DrainAsync();

        await rig.Engine.PublishEventAsync(Reply("e-2", "c-1", instanceId: id));
        await rig.DrainAsync();

        (await rig.CountAsync("process_inbox", "status = 2 and code = 'process.trigger.stale'")).Should().Be(1);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("replied");
    }

    [Fact]
    public async Task TheTimeoutOfAWaitEndsItAndLaterEventsFindNoTarget()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var id = await rig.StartAsync("host.wait", "k-1");

        rig.Time.Advance(TimeSpan.FromHours(1));
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("expired");
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
        (await rig.Engine.PublishEventAsync(Reply("e-1", "c-1"))).Status.Should().Be(PublishStatus.NoTarget);
    }

    [Fact]
    public async Task AReferenceCorrelationInUpperCaseReachesTheWaitAndCompletesTheInstance()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-ref"));
        const string reference = "3f2b8c1e-0a4d-4c7e-9b1a-5d6e7f8091a2";
        var id = await rig.StartAsync("host.waitref", "k-1", "{\"case_ref\":\"" + reference + "\"}");

        var result = await rig.Engine.PublishEventAsync(Reply("e-1", reference.ToUpperInvariant()));
        await rig.DrainAsync();

        result.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Completed);
        info.Result.Should().Be("replied");
        (await rig.CountAsync("process_inbox", "status = 2 and code is null")).Should().Be(1);
    }

    [Fact]
    public async Task AReferenceStoredFromAnUpperCaseInputIsFoundByALowerCaseCorrelationToo()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-ref"));
        const string reference = "3f2b8c1e-0a4d-4c7e-9b1a-5d6e7f8091a2";
        var id = await rig.StartAsync("host.waitref", "k-1", "{\"case_ref\":\"" + reference.ToUpperInvariant() + "\"}");

        var result = await rig.Engine.PublishEventAsync(Reply("e-1", reference));
        await rig.DrainAsync();

        result.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("replied");
    }

    [Theory]
    [InlineData("wait-reply", "host.wait")]
    [InlineData("wait-accepts", "host.waitaccepts")]
    public async Task ACodeCorrelationThatDiffersOnlyInCaseIsNoTargetWhetherOrNotTheInstanceAcceptsTheEvent(string fixture, string processId)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host(fixture));
        var id = await rig.StartAsync(processId, "k-1", "{\"case_ref\":\"case-a\"}");
        var revision = await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", id));

        var result = await rig.Engine.PublishEventAsync(Reply("e-1", "CASE-A"));
        var again = await rig.Engine.PublishEventAsync(Reply("e-1", "CASE-A"));
        await rig.DrainAsync();

        result.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        again.Should().Be(new PublishResult(PublishStatus.Duplicate, 0), "the event id is consumed by the answer NoTarget");
        (await rig.CountAsync("process_inbox")).Should().Be(0);
        (await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", id))).Should().Be(revision);
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Waiting);
        info.State!.Inbox.Should().BeEmpty();
        (await rig.Engine.PublishEventAsync(Reply("e-2", "case-a"))).Should().Be(new PublishResult(PublishStatus.Queued, 1));
    }

    [Fact]
    public async Task ACandidateThatMatchesOnlyWithoutRegardToCaseIsAskedAndAnExactOneIsNot()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"), Fixtures.Host("wait-ref"));
        const string reference = "3f2b8c1e-0a4d-4c7e-9b1a-5d6e7f8091a2";
        var byReference = await rig.StartAsync("host.waitref", "k-1", "{\"case_ref\":\"" + reference + "\"}");
        var byCode = await rig.StartAsync("host.wait", "k-2", "{\"case_ref\":\"" + reference.ToUpperInvariant() + "\"}");

        var upper = await rig.Engine.PublishEventAsync(Reply("e-1", reference.ToUpperInvariant()));
        await rig.DrainAsync();

        upper.Should().Be(new PublishResult(PublishStatus.Queued, 2), "the Code wait holds the upper-case text as it was given, so it matches exactly, and the Reference wait matches by the interpreter's own rule");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, byReference))!.Result.Should().Be("replied");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, byCode))!.Result.Should().Be("replied");
    }
}
