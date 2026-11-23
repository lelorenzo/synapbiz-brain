using System.Globalization;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// The reference lifecycle end to end on the durable host: <c>case.lifecycle</c> started by the event <c>case.opened</c>, its four
/// child processes, a fake clock, fake ports and a catalog that holds all six reference documents. The history of every instance is
/// compared with what the pure interpreter writes for the same events.
/// </summary>
public sealed class LifecycleFlowTests
{
    private const string Tenant = "t-1";
    private const string CaseRef = "c-1001";

    private static readonly DateTimeOffset FridayEvening = Ev.At("2027-01-08T23:00:00Z");
    private static readonly DateTimeOffset MondayOpening = Ev.At("2027-01-11T14:00:00Z");

    public static TheoryData<string, int, string, string> ScheduleZones => new()
    {
        { "America/New_York", -5, "2027-01-05T11:00:00Z", "2027-01-05" },
        { "Pacific/Test", 14, "2027-01-04T16:00:00Z", "2027-01-05" },
    };

    [Fact]
    public async Task ACaseRunsFromTheOpeningEventUntilTheMoneyIsCollected()
    {
        await using var world = await CaseWorld.CreateAsync(new ReversingProtector());

        var run = await RunHappyPathAsync(world, CaseRef, Quiet);

        var root = run.Root;
        root.Status.Should().Be(ProcessStatus.Completed);
        root.Result.Should().Be("collected");
        root.Stage.Should().Be("collection");
        root.ParentInstanceId.Should().BeNull();
        run.Stages.Should().Equal("previsit", "visit", "postvisit", "collection");
        run.Children.Keys.Should().BeEquivalentTo("previsit.outreach", "visit.note_wait", "postvisit.coding_review", "collection.followup");
        run.Children.Values.Should().OnlyContain(child => child.Status == ProcessStatus.Completed && child.ParentInstanceId == root.InstanceId);
        run.Children.Values.ToDictionary(child => child.ProcessId, child => child.Result).Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["previsit.outreach"] = "previsit_closed",
            ["visit.note_wait"] = "note_signed",
            ["postvisit.coding_review"] = "coding_approved",
            ["collection.followup"] = "collected",
        });
        (await world.Rig.CountAsync("process_instance")).Should().Be(5);
        foreach (var id in run.Children.Values.Select(child => child.InstanceId).Append(root.InstanceId))
        {
            (await world.Rig.OutstandingWorkAsync(id)).Should().Be(0);
        }

        (await world.Rig.CountAsync("process_inbox", "status = 1")).Should().Be(0);
    }

    [Fact]
    public async Task TheContactIsNotSentOutsideItsWindowAndIsSentAtTheOpening()
    {
        await using var world = await CaseWorld.CreateAsync();
        world.Rig.Time.SetUtcNow(FridayEvening);
        await world.ActivateLifecycleAsync();
        await world.OpenAsync(Tenant, CaseRef);
        await world.Rig.DrainAsync();

        var previsit = (await world.ChildrenAsync(Tenant, (await world.RootAsync(Tenant, CaseRef)).InstanceId)).Single().Value;
        previsit.State!.Pending!.Kind.Should().Be(ProcessPendingKind.Window, "18:00 on a Friday is the closing instant of the contact hours");
        world.Contacts.Should().BeEmpty();
        foreach (var moment in new[] { "2027-01-09T14:00:00Z", "2027-01-10T14:00:00Z", "2027-01-11T13:59:59Z" })
        {
            world.Rig.Time.SetUtcNow(Ev.At(moment));
            await world.Rig.DrainAsync();
            world.Contacts.Should().BeEmpty("the window opens on Monday at 09:00 local time, not before");
        }

        world.Rig.Time.SetUtcNow(MondayOpening);
        await world.Rig.DrainAsync();

        var contact = world.Contacts.Should().ContainSingle().Subject;
        contact.At.Should().Be(MondayOpening);
        var local = TimeZoneInfo.ConvertTime(contact.At, TestZoneResolver.Eastern);
        local.DayOfWeek.Should().Be(DayOfWeek.Monday);
        local.TimeOfDay.Should().Be(TimeSpan.FromHours(9));
        (await world.Rig.Engine.GetInstanceAsync(Tenant, previsit.InstanceId))!.CurrentStep.Should().Be("reply");
    }

    [Fact]
    public async Task ARejectedCodingClosesTheCaseWithoutBillingAndNothingReachesTheClaimsPort()
    {
        await using var world = await CaseWorld.CreateAsync();
        world.MissingFirst = _ => 0;
        world.DraftReads = 0;
        await OpenAsync(world, CaseRef);

        await world.CompleteAsync(await world.TaskAsync(Tenant, "physician_review"), "rejected");

        var root = await world.RootAsync(Tenant, CaseRef);
        root.Result.Should().Be("closed_not_billed");
        root.Stage.Should().Be("postvisit");
        (await world.ChildrenAsync(Tenant, root.InstanceId)).Keys.Should().BeEquivalentTo("previsit.outreach", "visit.note_wait", "postvisit.coding_review");
        (await world.Rig.CountAsync("process_instance", "process_id = 'collection.followup'")).Should().Be(0);
        world.ClaimStatus.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AVisitThatDidNotHappenClosesTheCaseWithoutAVisit()
    {
        await using var world = await CaseWorld.CreateAsync();
        world.MissingFirst = _ => 0;
        world.NoteStatus.Handler = (_, _) => CaseWorld.Failed(ProcessErrorKind.NotAvailable, "records.no_connector");
        await OpenAsync(world, CaseRef);

        await world.CompleteAsync(await world.TaskAsync(Tenant, "note_confirmation"), "no_visit");

        var root = await world.RootAsync(Tenant, CaseRef);
        root.Result.Should().Be("closed_no_visit");
        root.Stage.Should().Be("visit");
        var children = await world.ChildrenAsync(Tenant, root.InstanceId);
        children.Keys.Should().BeEquivalentTo("previsit.outreach", "visit.note_wait");
        children["visit.note_wait"].Result.Should().Be("visit_cancelled");
        world.NoteFacts.Calls.Should().BeEmpty();
        world.Suggest.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ADeniedClaimThatAPersonWritesOffEndsTheCaseAsWrittenOff()
    {
        await using var world = await CaseWorld.CreateAsync();
        world.MissingFirst = _ => 0;
        world.DraftReads = 0;
        world.ReviewReads = 0;
        world.FinalClaimStatus = "denied";
        await OpenAsync(world, CaseRef);
        await world.CompleteAsync(await world.TaskAsync(Tenant, "physician_review"), "approved");
        await world.PublishAsync(Tenant, "claim-1", "claim.submitted", CaseRef);
        await world.Rig.DrainAsync();

        await world.CompleteAsync(await world.TaskAsync(Tenant, "denials"), "written_off");

        var root = await world.RootAsync(Tenant, CaseRef);
        root.Result.Should().Be("written_off");
        (await world.ChildrenAsync(Tenant, root.InstanceId))["collection.followup"].Result.Should().Be("written_off");
    }

    [Fact]
    public async Task AClaimNobodySubmittedRaisesATaskAfterTenDaysAndAPersonClosesTheCase()
    {
        await using var world = await CaseWorld.CreateAsync();
        world.MissingFirst = _ => 0;
        world.DraftReads = 0;
        await OpenAsync(world, CaseRef);
        await world.CompleteAsync(await world.TaskAsync(Tenant, "physician_review"), "approved");
        world.Rig.Time.Advance(TimeSpan.FromDays(10) - TimeSpan.FromSeconds(1));
        await world.Rig.DrainAsync();
        (await world.Rig.Engine.ListOpenTasksAsync(Tenant, "billing_followup")).Should().BeEmpty();

        world.Rig.Time.Advance(TimeSpan.FromSeconds(1));
        await world.Rig.DrainAsync();
        await world.CompleteAsync(await world.TaskAsync(Tenant, "billing_followup"), "manual_close");

        var root = await world.RootAsync(Tenant, CaseRef);
        root.Result.Should().Be("manual_close");
        (await world.ChildrenAsync(Tenant, root.InstanceId))["collection.followup"].Result.Should().Be("manual_close");
        world.ClaimStatus.Calls.Should().BeEmpty("the claim was never submitted, so nothing was read");
    }

    [Fact]
    public async Task AChildThatIsCancelledRaisesTheOperationsTaskOfItsStage()
    {
        await using var world = await CaseWorld.CreateAsync();
        await OpenAsync(world, CaseRef);
        var root = await world.RootAsync(Tenant, CaseRef);
        var previsit = (await world.ChildrenAsync(Tenant, root.InstanceId)).Single().Value;

        (await world.Rig.Engine.CancelAsync(Tenant, previsit.InstanceId, "operator")).Disposition.Should().Be(AdvanceDisposition.Applied);
        await world.Rig.DrainAsync();

        var task = await world.TaskAsync(Tenant, "operations");
        task.Step.Should().Be("ops_previsit");
        task.Outcomes.Should().Equal("continue", "close");
        await world.CompleteAsync(task, "close");
        (await world.RootAsync(Tenant, CaseRef)).Result.Should().Be("manual_close");
    }

    [Theory]
    [MemberData(nameof(ScheduleZones))]
    public async Task TheDailyRefreshIsFiredOnceByTheScheduleWithTheLocalDateOfTheZone(string zoneId, int offsetHours, string fireAt, string localDate)
    {
        await using var world = await CaseWorld.CreateAsync();
        world.Rig.Zones.Add("Pacific/Test", TimeZoneInfo.CreateCustomTimeZone("Pacific/Test", TimeSpan.FromHours(offsetHours), "Test", "Test"));
        var fire = Ev.At(fireAt);
        var activation = await world.Rig.Engine.ActivateAsync(new ProcessActivation(Tenant, "practice.daily_refresh", zoneId));
        activation.Changed.Should().BeTrue();
        await world.ActivateLifecycleAsync();
        await world.Rig.DrainAsync();
        (await world.Rig.CountAsync("process_instance")).Should().Be(0);

        world.Rig.Time.SetUtcNow(fire - TimeSpan.FromSeconds(1));
        await world.Rig.DrainAsync();
        (await world.Rig.CountAsync("process_instance")).Should().Be(0, "06:00 local time has not come");

        world.Rig.Time.SetUtcNow(fire);
        await world.Rig.DrainAsync();
        world.Rig.Time.Advance(TimeSpan.FromMinutes(1));
        await world.Rig.DrainAsync();

        var key = "@schedule/daily/" + fire.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
        var refresh = (await world.Rig.Engine.FindInstanceAsync(Tenant, "practice.daily_refresh", key))!;
        (await world.Rig.CountAsync("process_instance")).Should().Be(1, "one occurrence is one instance, however many passes run");
        refresh.Status.Should().Be(ProcessStatus.Completed);
        refresh.Result.Should().Be("refreshed");
        refresh.State!.Variables["run_for"].Should().Be("\"" + localDate + "\"");
        world.CaseListInputs.Should().ContainSingle().Which.Should().Be("{\"day\":\"" + localDate + "\"}");

        // What the consumer does with the list: it opens one case per entry, by publishing the event, so one lifecycle starts per case.
        var cases = JsonNode.Parse(refresh.State.Variables["cases"])!.AsArray().Select(node => node!["case_ref"]!.GetValue<string>()).ToList();
        cases.Should().Equal("c-1001", "c-1002");
        foreach (var caseRef in cases)
        {
            (await world.OpenAsync(Tenant, caseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        }

        await world.Rig.DrainAsync();
        (await world.Rig.CountAsync("process_instance", "process_id = 'case.lifecycle'")).Should().Be(2);
        (await world.StageAsync(Tenant, "c-1002")).Should().Be("previsit");

        world.Rig.Time.SetUtcNow(fire + TimeSpan.FromDays(1));
        await world.Rig.DrainAsync();
        (await world.Rig.CountAsync("process_instance", "process_id = 'practice.daily_refresh'")).Should().Be(2);
        world.CaseListInputs.Last().Should().Be("{\"day\":\"" + DateOnly.ParseExact(localDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\"}");
    }

    [Fact]
    public async Task TheOperationsTaskOfTheVisitStageShowsHowManyItemsThePrevisitStageLeftMissing()
    {
        await using var world = await CaseWorld.CreateAsync();
        await OpenAsync(world, CaseRef);
        (await world.PublishAsync(Tenant, "reply-1", "case.reply_received", CaseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await world.Rig.DrainAsync();
        var root = await world.RootAsync(Tenant, CaseRef);
        root.Stage.Should().Be("visit");
        var visit = (await world.ChildrenAsync(Tenant, root.InstanceId))["visit.note_wait"];

        await world.Rig.Engine.CancelAsync(Tenant, visit.InstanceId, "operator");
        await world.Rig.DrainAsync();

        var task = await world.TaskAsync(Tenant, "operations");
        task.Step.Should().Be("ops_visit");
        var shown = JsonNode.Parse(task.ShowJson)!.AsObject();
        shown["case_ref"]!.GetValue<string>().Should().Be(CaseRef);
        shown["missing_at_close"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task ACaseOpenedWithoutItsZoneIsHandedToOperationsBeforeAnyReminderAndContinuesAtTheVisit()
    {
        await using var world = await CaseWorld.CreateAsync();
        (await world.Rig.Engine.ActivateAsync(new ProcessActivation(Tenant, "case.lifecycle", null, "{\"assist_enabled\":true}"))).Changed.Should().BeTrue();
        (await world.OpenAsync(Tenant, CaseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await world.Rig.DrainAsync();

        var root = await world.RootAsync(Tenant, CaseRef);
        root.Status.Should().Be(ProcessStatus.Waiting);
        root.CurrentStep.Should().Be("ops_zone");
        (await world.ChildrenAsync(Tenant, root.InstanceId)).Should().BeEmpty("no stage starts for a case that has no zone");
        world.Contacts.Should().BeEmpty();
        world.Requirements.Calls.Should().BeEmpty();
        var task = await world.TaskAsync(Tenant, "operations");
        task.Step.Should().Be("ops_zone");
        task.Outcomes.Should().Equal("continue", "close");
        JsonNode.Parse(task.ShowJson)!["case_ref"]!.GetValue<string>().Should().Be(CaseRef);

        await world.CompleteAsync(task, "continue");

        (await world.StageAsync(Tenant, CaseRef)).Should().Be("visit");
        (await world.ChildrenAsync(Tenant, root.InstanceId)).Keys.Should().BeEquivalentTo("visit.note_wait");
    }

    [Fact]
    public async Task ACaseThatReachedItsReminderWithoutAZoneIsSuspendedInSilenceAndCancellingTheChildIsTheWayOut()
    {
        // The reference lifecycle checks the zone first. This copy checks another variable, so that it lets a case without a zone through,
        // as a copy made before the check existed would.
        var processes = ReferenceFlows.All("case.lifecycle", text => text.Replace("\"facts\": { \"practice_zone\": \"practice_zone\" }", "\"facts\": { \"practice_zone\": \"case_ref\" }", StringComparison.Ordinal));
        await using var world = await CaseWorld.CreateAsync(null, processes);
        await world.Rig.Engine.ActivateAsync(new ProcessActivation(Tenant, "case.lifecycle", null, "{\"assist_enabled\":true}"));
        await world.OpenAsync(Tenant, CaseRef);
        await world.Rig.DrainAsync();

        var root = await world.RootAsync(Tenant, CaseRef);
        var child = (await world.ChildrenAsync(Tenant, root.InstanceId)).Single().Value;
        child.Status.Should().Be(ProcessStatus.Suspended);
        child.Problem.Should().Be(new ProcessProblem("process.window.zone_missing", "send"));
        root.Status.Should().Be(ProcessStatus.Waiting, "a suspended child is not a failed child: the parent is told nothing");
        (await world.Rig.Engine.ListOpenTasksAsync(Tenant, "operations")).Should().BeEmpty("nothing routes a suspension, so nobody is asked");
        world.Contacts.Should().BeEmpty();
        world.Rig.Logs.Entries.Should().Contain(entry => entry.Message.StartsWith("process.instance.suspended", StringComparison.Ordinal) && entry.Level == LogLevel.Warning);

        (await world.Rig.Engine.ResumeAsync(Tenant, child.InstanceId)).Disposition.Should().Be(AdvanceDisposition.Applied);
        await world.Rig.DrainAsync();
        var resumed = (await world.Rig.Engine.GetInstanceAsync(Tenant, child.InstanceId))!;
        resumed.Status.Should().Be(ProcessStatus.Suspended, "resuming runs the step again and the zone is still missing");
        resumed.Problem!.Code.Should().Be("process.window.zone_missing");
        (await world.Rig.Engine.GetHistoryAsync(Tenant, child.InstanceId)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.Resumed);
        (await world.Rig.Engine.ListOpenTasksAsync(Tenant, "operations")).Should().BeEmpty();

        (await world.Rig.Engine.CancelAsync(Tenant, child.InstanceId, "operator.zone_missing")).Disposition.Should().Be(AdvanceDisposition.Applied);
        await world.Rig.DrainAsync();

        var task = await world.TaskAsync(Tenant, "operations");
        task.Step.Should().Be("ops_previsit");
        await world.CompleteAsync(task, "continue");
        (await world.StageAsync(Tenant, CaseRef)).Should().Be("visit");
        await world.Rig.Engine.PublishEventAsync(new ProcessEvent(Tenant, "open-again", "case.opened", null, "{\"case_ref\":\"" + CaseRef + "\",\"practice_zone\":\"" + CaseWorld.Zone + "\"}"));
        await world.Rig.DrainAsync();
        // The case cannot be opened again with its zone: its business key is taken, so the second event starts nothing.
        (await world.Rig.CountAsync("process_instance", "process_id = 'case.lifecycle'")).Should().Be(1);
    }

    [Fact]
    public async Task AFailingCaseListRaisesTheOperationsTaskAndTheRefreshEndsOnceAPersonHandledIt()
    {
        await using var world = await CaseWorld.CreateAsync();
        world.CaseList.Handler = (_, _) => CaseWorld.Failed(ProcessErrorKind.Permanent, "schedule.gone");
        await world.Rig.Engine.ActivateAsync(new ProcessActivation(Tenant, "practice.daily_refresh", CaseWorld.Zone));
        world.Rig.Time.SetUtcNow(Ev.At("2027-01-05T11:00:00Z"));
        await world.Rig.DrainAsync();
        world.Rig.Time.Advance(TimeSpan.FromMinutes(1));
        await world.Rig.DrainAsync();

        var refresh = (await world.Rig.ColumnAsync<string>("select instance_id from {schema}.process_instance where process_id = 'practice.daily_refresh'")).Should().ContainSingle().Subject;
        var waiting = (await world.Rig.Engine.GetInstanceAsync(Tenant, refresh))!;
        waiting.Status.Should().Be(ProcessStatus.Waiting, "the refresh does not end silently as failed");
        var task = await world.TaskAsync(Tenant, "operations");
        task.Step.Should().Be("refresh_attention");
        task.Outcomes.Should().Equal("handled");
        JsonNode.Parse(task.ShowJson)!["run_for"]!.GetValue<string>().Should().Be("2027-01-05");

        await world.CompleteAsync(task, "handled");

        var done = (await world.Rig.Engine.GetInstanceAsync(Tenant, refresh))!;
        done.Status.Should().Be(ProcessStatus.Completed);
        done.Result.Should().Be("refresh_failed");
    }

    [Fact]
    public async Task TwoTenantsRunningTheSameCaseReferenceDoNotInterfere()
    {
        const string Other = "t-2";
        await using var world = await CaseWorld.CreateAsync();
        await world.ActivateLifecycleAsync(Tenant);
        await world.ActivateLifecycleAsync(Other);
        (await world.OpenAsync(Tenant, CaseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        (await world.OpenAsync(Other, CaseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await world.Rig.DrainAsync();

        var first = await world.RootAsync(Tenant, CaseRef);
        var second = await world.RootAsync(Other, CaseRef);
        first.InstanceId.Should().NotBe(second.InstanceId);
        first.BusinessKey.Should().Be(second.BusinessKey);
        (await world.Rig.Engine.GetInstanceAsync(Other, first.InstanceId)).Should().BeNull();
        world.Contacts.Select(contact => contact.Tenant).Should().BeEquivalentTo(Tenant, Other);

        // A reply of one tenant moves that tenant's case on and leaves the other tenant's wait where it was.
        (await world.PublishAsync(Tenant, "reply-1", "case.reply_received", CaseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await world.Rig.DrainAsync();
        (await world.StageAsync(Tenant, CaseRef)).Should().Be("visit");
        (await world.StageAsync(Other, CaseRef)).Should().Be("previsit");
        (await world.Rig.CountAsync("process_wait", "tenant_id = @t", ("t", Other))).Should().Be(1);

        // The other tenant replies too, before its own wait of a day runs out, so that both cases reach their reviews together.
        (await world.PublishAsync(Other, "reply-2", "case.reply_received", CaseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await world.Rig.DrainAsync();
        world.Rig.Time.Advance(TimeSpan.FromHours(1));
        await world.Rig.DrainAsync();
        var review = await world.TaskAsync(Tenant, "physician_review");
        var otherReview = await world.TaskAsync(Other, "physician_review");
        otherReview.TaskId.Should().NotBe(review.TaskId);
        (await world.Rig.Engine.CompleteTaskAsync(Other, review.TaskId, "approved", "{}")).Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.instance.not_found"));
        (await world.Rig.Engine.ListOpenTasksAsync(Tenant, "physician_review")).Should().ContainSingle().Which.TaskId.Should().Be(review.TaskId);
        await world.CompleteAsync(review, "approved");
        (await world.PublishAsync(Other, "claim-other", "claim.submitted", CaseRef)).Status.Should().Be(PublishStatus.NoTarget);
        (await world.PublishAsync(Tenant, "claim-1", "claim.submitted", CaseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await world.Rig.DrainAsync();
        world.Rig.Time.Advance(TimeSpan.FromDays(1));
        await world.Rig.DrainAsync();
        (await world.RootAsync(Tenant, CaseRef)).Result.Should().Be("collected");
        (await world.StageAsync(Other, CaseRef)).Should().Be("postvisit", "the other tenant's case did not move with the clock or with the claim of the first");

        // The other tenant's case ends differently: its claim is denied and a person writes it off.
        world.FinalClaimStatus = "denied";
        await world.CompleteAsync(otherReview, "approved");
        (await world.PublishAsync(Other, "claim-2", "claim.submitted", CaseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await world.Rig.DrainAsync();
        world.Rig.Time.Advance(TimeSpan.FromDays(1));
        await world.Rig.DrainAsync();
        await world.CompleteAsync(await world.TaskAsync(Other, "denials"), "written_off");

        (await world.RootAsync(Tenant, CaseRef)).Result.Should().Be("collected");
        (await world.RootAsync(Other, CaseRef)).Result.Should().Be("written_off");
        (await world.Rig.CountAsync("process_instance", "tenant_id = @t", ("t", Tenant))).Should().Be(5);
        (await world.Rig.CountAsync("process_instance", "tenant_id = @t", ("t", Other))).Should().Be(5);
        var owned = (await world.Rig.ColumnAsync<string>("select tenant_id || '/' || instance_id from {schema}.process_instance")).ToHashSet();
        world.Rig.Ports.SelectMany(port => port.Calls).Select(call => call.TenantId + "/" + call.InstanceId).Should().OnlyContain(call => owned.Contains(call));
        world.Rig.Ports.SelectMany(port => port.Calls).Select(call => call.TenantId).Distinct().Should().BeEquivalentTo(Tenant, Other);
    }

    [Fact]
    public async Task NoLogRecordOfTheWholeCaseCarriesAValueThatWentThroughIt()
    {
        var sentinels = new Noise("zq-open-5531", "zq-reply-6620", "zq-noise-7712", "zq-code-8803");
        const string SentinelCase = "zq-case-4417";
        await using var world = await CaseWorld.CreateAsync(new ReversingProtector());

        await RunHappyPathAsync(world, SentinelCase, sentinels);

        string[] all = [SentinelCase, sentinels.Opened!, sentinels.Reply!, sentinels.Output!, sentinels.Code!];
        var entries = world.Rig.Logs.Entries;
        entries.Count(entry => entry.Message.StartsWith("process.instance.finished", StringComparison.Ordinal)).Should().Be(5, "the flow did log, so the check below is not vacuous");
        entries.Should().Contain(entry => entry.Message.StartsWith("process.activation.saved", StringComparison.Ordinal));
        foreach (var entry in entries)
        {
            foreach (var sentinel in all)
            {
                entry.Message.Should().NotContain(sentinel);
                (entry.ExceptionText ?? string.Empty).Should().NotContain(sentinel);
                entry.State.Select(pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty).Should().NotContain(value => value.Contains(sentinel, StringComparison.Ordinal));
            }
        }

        entries.Where(entry => entry.Level >= LogLevel.Warning).Should().BeEmpty("a case that goes well warns about nothing");
        foreach (var sentinel in all)
        {
            foreach (var column in new[] { "process_instance.state", "process_history.data", "process_effect.input", "process_task.show", "process_inbox.payload" })
            {
                var parts = column.Split('.');
                (await world.Rig.CountAsync(parts[0], parts[1] + " like @s", ("s", "%" + sentinel + "%"))).Should().Be(0, sentinel + " in " + column);
            }
        }

        (await world.Rig.CountAsync("process_instance", "business_key = @k", ("k", SentinelCase))).Should().Be(1, "the business key is stored in clear, so it must be an opaque reference");
    }

    private static Noise Quiet => new(null, null, null, null);

    private static async Task OpenAsync(CaseWorld world, string caseRef)
    {
        await world.ActivateLifecycleAsync();
        (await world.OpenAsync(Tenant, caseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await world.Rig.DrainAsync();
    }

    /// <summary>
    /// A case from its opening on a Friday evening, outside the contact hours, until its claim is paid, with the assertions that hold
    /// at every point of the way. The history of the five instances is compared with the pure interpreter's.
    /// </summary>
    private static async Task<HappyRun> RunHappyPathAsync(CaseWorld world, string caseRef, Noise noise)
    {
        var rig = world.Rig;
        world.Noise = noise.Output;
        var stages = new List<string>();

        async Task Stage()
        {
            var stage = (await world.StageAsync(Tenant, caseRef))!;
            if (stages.Count == 0 || stages[^1] != stage)
            {
                stages.Add(stage);
            }
        }

        rig.Time.SetUtcNow(FridayEvening);
        await world.ActivateLifecycleAsync();
        (await world.OpenAsync(Tenant, caseRef, noise.Opened)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await rig.DrainAsync();
        await Stage();
        var rootId = (await world.RootAsync(Tenant, caseRef)).InstanceId;
        world.Contacts.Should().BeEmpty("the contact hours are closed on a Friday at 18:00 local time");

        rig.Time.SetUtcNow(MondayOpening);
        await rig.DrainAsync();
        world.Contacts.Should().ContainSingle();
        (await world.PublishAsync(Tenant, "reply-1", "case.reply_received", caseRef, new JsonObject { ["text"] = noise.Reply }.ToJsonString())).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await rig.DrainAsync();
        await Stage();
        (await world.ChildrenAsync(Tenant, rootId))["previsit.outreach"].Result.Should().Be("previsit_closed");

        rig.Time.Advance(TimeSpan.FromHours(1));
        await rig.DrainAsync();
        await Stage();
        var review = await world.TaskAsync(Tenant, "physician_review");
        var shown = JsonNode.Parse(review.ShowJson)!.AsObject();
        shown["opportunities"]!.AsArray().Should().HaveCount(2);
        shown["suggested_code"]!.GetValue<string>().Should().Be("C-100");
        shown["suggestion_usable"]!.GetValue<bool>().Should().BeTrue();
        world.ClaimStatus.Calls.Should().BeEmpty("nothing reaches billing before the physician review");
        var changed = noise.Code is not null;
        await world.CompleteAsync(review, changed ? "changed" : "approved", changed ? new JsonObject { ["code"] = noise.Code }.ToJsonString() : "{}");
        await Stage();
        (await rig.Engine.ListOpenTasksAsync(Tenant, "physician_review")).Should().BeEmpty();

        (await world.PublishAsync(Tenant, "claim-1", "claim.submitted", caseRef)).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await rig.DrainAsync();
        rig.Time.Advance(TimeSpan.FromDays(1));
        await rig.DrainAsync();
        await Stage();

        var root = await world.RootAsync(Tenant, caseRef);
        var children = await world.ChildrenAsync(Tenant, rootId);
        world.Suggest.Calls.Should().ContainSingle().Which.Request.Should().Match<EffectRequest>(request => request.Model == "model-a" && request.PromptId == "coding-suggest" && request.PromptVersion == 1);
        rig.Ports.SelectMany(port => port.Calls).Where(call => call.Request.EffectKind == ProcessEffectKind.Action).Select(call => call.Request.Port)
            .Should().OnlyContain(port => port == "messaging.reminder", "the only thing the case ever does is send a reminder");
        if (!changed)
        {
            await AssertHistoryMatchesTheOracleAsync(world, caseRef, root, children);
        }

        return new HappyRun(root, children, stages);
    }

    private static async Task AssertHistoryMatchesTheOracleAsync(CaseWorld world, string caseRef, ProcessInstanceInfo root, Dictionary<string, ProcessInstanceInfo> children)
    {
        var processes = ReferenceFlows.All().ToDictionary(process => process.Id);
        var zones = new Dictionary<string, TimeZoneInfo> { [CaseWorld.Zone] = TestZoneResolver.Eastern };
        JsonObject Input(string json) => JsonNode.Parse(json)!.AsObject();

        var previsit = PureOracle.Replay(
            processes["previsit.outreach"],
            FridayEvening,
            Input("{\"case_ref\":\"" + caseRef + "\",\"practice_zone\":\"" + CaseWorld.Zone + "\"}"),
            [
                Ev.Complete("check", "{\"missing_count\":2}"),
                Ev.Advance("PT63H"),
                Ev.Complete("send"),
                Ev.Event("case.reply_received", caseRef),
                Ev.Complete("check", "{\"missing_count\":0}"),
            ],
            zones);
        var visit = PureOracle.Replay(
            processes["visit.note_wait"],
            MondayOpening,
            Input("{\"case_ref\":\"" + caseRef + "\"}"),
            [Ev.Complete("await_note", "{\"status\":\"draft\"}"), Ev.Advance("PT1H"), Ev.Complete("await_note", "{\"status\":\"signed\"}")]);
        var postvisit = PureOracle.Replay(
            processes["postvisit.coding_review"],
            MondayOpening,
            Input("{\"case_ref\":\"" + caseRef + "\",\"assist_enabled\":true}"),
            [Ev.Complete("read_note", "{\"items\":6,\"flagged\":2}"), Ev.Complete("suggest", "{\"code\":\"C-100\",\"confidence\":85}"), Ev.Task("physician_review", "approved")]);
        var collection = PureOracle.Replay(
            processes["collection.followup"],
            MondayOpening,
            Input("{\"case_ref\":\"" + caseRef + "\"}"),
            [Ev.Event("claim.submitted", caseRef), Ev.Complete("poll_claim", "{\"status\":\"in_review\"}"), Ev.Advance("P1D"), Ev.Complete("poll_claim", "{\"status\":\"paid\"}")]);
        var lifecycle = PureOracle.Replay(
            processes["case.lifecycle"],
            FridayEvening,
            Input("{\"case_ref\":\"" + caseRef + "\",\"practice_zone\":\"" + CaseWorld.Zone + "\",\"assist_enabled\":true}"),
            [
                Ev.Child("previsit", "previsit_closed", previsit.Output),
                Ev.Child("visit", "note_signed", visit.Output),
                Ev.Child("postvisit", "coding_approved", postvisit.Output),
                Ev.Child("collection", "collected", collection.Output),
            ]);

        var expected = new (ProcessInstanceInfo Instance, PureOracle Oracle)[]
        {
            (root, lifecycle),
            (children["previsit.outreach"], previsit),
            (children["visit.note_wait"], visit),
            (children["postvisit.coding_review"], postvisit),
            (children["collection.followup"], collection),
        };
        foreach (var (instance, oracle) in expected)
        {
            var history = await world.Rig.Engine.GetHistoryAsync(Tenant, instance.InstanceId);
            history.Select(entry => entry.Kind).Should().Equal(oracle.Kinds, instance.ProcessId);
            history.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step).Should().Equal(oracle.Trace, instance.ProcessId);
            history.Select(entry => entry.Sequence).Should().Equal(Enumerable.Range(1, history.Count).Select(number => (long)number));
            instance.State!.Variables.Should().Equal(oracle.Final.Variables, instance.ProcessId);
            instance.Result.Should().Be(oracle.Final.Result);
        }
    }

    /// <summary>The sentinel values a run pushes through the case; all null for a quiet run.</summary>
    private sealed record Noise(string? Opened, string? Reply, string? Output, string? Code);

    private sealed record HappyRun(ProcessInstanceInfo Root, Dictionary<string, ProcessInstanceInfo> Children, List<string> Stages);
}
