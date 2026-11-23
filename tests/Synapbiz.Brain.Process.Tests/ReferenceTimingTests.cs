using System.Globalization;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>
/// The durations, delivery windows and zones of the reference documents, pinned by <c>Start</c> and <c>Advance</c> alone. The
/// scenarios of a document pass whatever its durations are (an <c>advance</c> that is long enough fires every timer), so these tests
/// say what each timer, wait, task timeout, poll round and window really is, with the values written out as literals: a change to
/// one of them in a document fails here and is a decision, not an accident.
/// </summary>
public sealed class ReferenceTimingTests
{
    private const string Zone = "America/New_York";
    private const string Input = "{\"case_ref\":\"c-1001\",\"practice_zone\":\"" + Zone + "\"}";

    /// <summary>A zone of the pattern of the practice, UTC-5 with no daylight saving: January is all the tests need.</summary>
    private static readonly TimeZoneInfo Practice = TimeZoneInfo.CreateCustomTimeZone("Test/Minus5", TimeSpan.FromHours(-5), "Test", "Test");

    private static readonly string[] OutcomesContinueClose = ["continue", "close"];

    private static readonly string[] OutcomesHandled = ["handled"];

    // ---- delivery window of previsit.outreach

    [Fact]
    public void TheReminderWaitsForTheWindowOfTheContactHoursInTheZoneOfTheCase()
    {
        var driver = ReachSend();

        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.Window);
        var request = driver.Single<WindowRequest>();
        request.Step.Should().Be("send");
        request.Zone.Should().Be(Zone, "the zone is the value of the variable practice_zone, not a name in the document");
        request.Days.Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);
        request.From.Should().Be(new TimeOnly(9, 0));
        request.To.Should().Be(new TimeOnly(18, 0));
        driver.Last.Requests.OfType<EffectRequest>().Should().BeEmpty("nothing is sent until the host says the window is open");
    }

    [Theory]
    [InlineData("2027-01-04T15:00:00Z", "2027-01-04T15:00:00Z")] // Monday 10:00 local: inside
    [InlineData("2027-01-04T14:00:00Z", "2027-01-04T14:00:00Z")] // Monday 09:00 local: the opening is inside
    [InlineData("2027-01-05T22:59:59Z", "2027-01-05T22:59:59Z")] // Tuesday 17:59:59 local: the last second
    [InlineData("2027-01-04T13:59:00Z", "2027-01-04T14:00:00Z")] // Monday 08:59 local: just before the opening
    [InlineData("2027-01-08T23:00:00Z", "2027-01-11T14:00:00Z")] // Friday 18:00 local: the closing is outside, the next opening is Monday
    [InlineData("2027-01-08T22:59:59Z", "2027-01-08T22:59:59Z")] // Friday 17:59:59 local: inside
    [InlineData("2027-01-09T17:00:00Z", "2027-01-11T14:00:00Z")] // Saturday noon local: no Saturday
    [InlineData("2027-01-10T17:00:00Z", "2027-01-11T14:00:00Z")] // Sunday noon local: no Sunday
    public void TheWindowOpensWhereTheContactHoursOpenInThePracticeZone(string asked, string opens)
    {
        var request = ReachSend().Single<WindowRequest>();

        DeliveryWindows.NextOpen(At(asked), Practice, request.Days, request.From, request.To).Should().Be(At(opens));
    }

    [Fact]
    public void TheReminderIsRequestedOnlyWhenTheWindowTimerFiresAndEveryRetryPassesTheGateAgain()
    {
        var driver = ReachSend();
        OpenWindow(driver);

        driver.Single<EffectRequest>().Port.Should().Be("messaging.reminder");
        driver.Send(Fail(driver, ProcessErrorKind.Transient));
        var retry = driver.Single<TimerRequest>();
        retry.DueAt.Should().Be(Docs.T0 + TimeSpan.FromMinutes(1), "the first retry waits the initial PT1M");
        Fire(driver, retry);
        driver.Single<WindowRequest>().Step.Should().Be("send", "the retry waits for the window again");
        OpenWindow(driver);
        driver.Send(Fail(driver, ProcessErrorKind.Transient));
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromMinutes(1) + TimeSpan.FromMinutes(2), "the second retry waits twice as long, PT2M, up to a cap of PT10M");
    }

    // ---- a zone that is missing or cannot be resolved

    [Fact]
    public void ACaseWithoutItsZoneIsSuspendedAtTheReminderAndNeitherResumingNorTheRoutesGetItOut()
    {
        var driver = new Driver(ReferenceDocs.Process("previsit.outreach"));
        driver.Start("{\"case_ref\":\"c-1001\"}");
        driver.Send(Complete(driver, "{\"missing_count\":2}"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.window.zone_missing", "send"));
        driver.Last.Requests.OfType<WindowRequest>().Should().BeEmpty();
        driver.Last.Requests.OfType<EffectRequest>().Should().BeEmpty("nothing is sent to a case whose contact hours are unknown");

        driver.Send(new ResumeRequested());

        driver.State.Status.Should().Be(ProcessStatus.Suspended, "the zone is an input, and an input cannot be patched in a running instance");
        driver.State.Problem!.Code.Should().Be("process.window.zone_missing");
        driver.Send(new CancelRequested("operator.cancelled"));
        driver.State.Status.Should().Be(ProcessStatus.Cancelled, "cancelling is the way out: the parent then raises the task of its stage");
    }

    [Fact]
    public void AZoneThatIsNotAZoneIsAFailureOfTheStepThatRoutesToTheFrontDesk()
    {
        var driver = new Driver(ReferenceDocs.Process("previsit.outreach"));
        driver.Start("{\"case_ref\":\"c-1001\",\"practice_zone\":\"Factory\"}");
        driver.Send(Complete(driver, "{\"missing_count\":2}"));
        var window = driver.Single<WindowRequest>();
        window.Zone.Should().Be("Factory");

        driver.Send(new WindowFailed(window.TimerId, "process.window.zone_unknown"));

        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.State.CurrentStep.Should().Be("escalate");
        driver.Single<TaskRequest>().Queue.Should().Be("front_desk");
    }

    [Fact]
    public void ACaseOpenedWithoutItsZoneIsHandedToOperationsBeforeAnyStageStartsAndNeverStartsThePrevisitChild()
    {
        var driver = new Driver(ReferenceDocs.Process("case.lifecycle"));
        driver.Start("{\"case_ref\":\"c-1001\",\"assist_enabled\":true}");

        driver.State.CurrentStep.Should().Be("ops_zone");
        driver.Requests.OfType<ChildStartRequest>().Should().BeEmpty();
        driver.Single<TaskRequest>().Should().Match<TaskRequest>(task => task.Queue == "operations" && task.Outcomes.SequenceEqual(OutcomesContinueClose));
        driver.Last.Requests.OfType<TimerRequest>().Should().BeEmpty("a task of operations has no timeout");
        driver.Kinds.Should().Contain(ProcessHistoryKind.Decided);
    }

    [Fact]
    public void ACaseOpenedWithItsZoneStartsThePrevisitChildWithTheZone()
    {
        var driver = new Driver(ReferenceDocs.Process("case.lifecycle"));
        driver.Start("{\"case_ref\":\"c-1001\",\"practice_zone\":\"" + Zone + "\",\"assist_enabled\":true}");

        var child = driver.Single<ChildStartRequest>();
        child.ProcessId.Should().Be("previsit.outreach");
        child.InputJson.Should().Be("{\"case_ref\":\"c-1001\",\"practice_zone\":\"" + Zone + "\"}");
    }

    // ---- previsit.outreach

    [Fact]
    public void ThePrevisitTimersAreThoseOfItsDocument()
    {
        var driver = new Driver(ReferenceDocs.Process("previsit.outreach"));
        driver.Start(Input);
        driver.Send(Fail(driver, ProcessErrorKind.Transient));
        var first = driver.Single<TimerRequest>();
        Fire(driver, first);
        driver.Send(Fail(driver, ProcessErrorKind.Transient));

        first.DueAt.Should().Be(Docs.T0 + TimeSpan.FromSeconds(30), "the read retries after PT30S");
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromSeconds(30) + TimeSpan.FromSeconds(60), "then after PT1M");
        Fire(driver, driver.Single<TimerRequest>());
        driver.Send(Fail(driver, ProcessErrorKind.Transient));
        driver.State.CurrentStep.Should().Be("escalate", "three attempts are all the read gets");
    }

    [Fact]
    public void TheReplyIsAwaitedForADayAndEveryFrontDeskTaskForThreeDays()
    {
        var driver = ReachSend();
        OpenWindow(driver);
        driver.Send(Complete(driver, "{}"));

        driver.Single<WaitRequest>().Should().Match<WaitRequest>(wait => wait.Event == "case.reply_received" && wait.Correlation == "c-1001");
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromHours(24));

        var escalate = new Driver(ReferenceDocs.Process("previsit.outreach"));
        escalate.Start(Input);
        escalate.Send(Fail(escalate, ProcessErrorKind.Permanent));
        escalate.State.CurrentStep.Should().Be("escalate");
        escalate.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromDays(3));

        var final = new Driver(ReferenceDocs.Process("previsit.outreach"));
        final.Start(Input);
        final.Send(Fail(final, ProcessErrorKind.NotAvailable));
        final.State.CurrentStep.Should().Be("escalate_final");
        final.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromDays(3));
    }

    // ---- visit.note_wait

    [Fact]
    public void TheNotePollReadsAtGrowingIntervalsAndGivesUpAfterTwoDaysInNineReads()
    {
        var driver = new Driver(ReferenceDocs.Process("visit.note_wait"));
        driver.Start("{\"case_ref\":\"c-1001\"}");
        var gaps = new List<TimeSpan>();
        var reads = 1;
        while (true)
        {
            driver.Send(Complete(driver, "{\"status\":\"draft\"}"));
            if (driver.State.CurrentStep != "await_note")
            {
                break;
            }

            var timer = driver.Single<TimerRequest>();
            gaps.Add(timer.DueAt - Docs.T0);
            Fire(driver, timer);
            reads++;
        }

        gaps.Should().Equal(
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1 + 2),
            TimeSpan.FromHours(1 + 2 + 4),
            TimeSpan.FromHours(1 + 2 + 4 + 8),
            TimeSpan.FromHours(1 + 2 + 4 + 8 + 8),
            TimeSpan.FromHours(1 + 2 + 4 + 8 + 8 + 8),
            TimeSpan.FromHours(1 + 2 + 4 + 8 + 8 + 8 + 8),
            TimeSpan.FromHours(1 + 2 + 4 + 8 + 8 + 8 + 8 + 8));
        reads.Should().Be(9);
        driver.History.Should().Contain(entry => entry.Kind == ProcessHistoryKind.PollExpired);
        driver.State.CurrentStep.Should().Be("chase_note");
        driver.Single<TaskRequest>().Queue.Should().Be("note_followup");
        driver.Last.Requests.OfType<TimerRequest>().Should().BeEmpty("the chase task has no timeout");
    }

    [Fact]
    public void AFailedReadOfTheNoteIsRetriedAfterThirtySecondsThenAMinute()
    {
        var driver = new Driver(ReferenceDocs.Process("visit.note_wait"));
        driver.Start("{\"case_ref\":\"c-1001\"}");
        driver.Send(Fail(driver, ProcessErrorKind.Transient));
        var first = driver.Single<TimerRequest>();
        Fire(driver, first);
        driver.Send(Fail(driver, ProcessErrorKind.Transient));

        first.DueAt.Should().Be(Docs.T0 + TimeSpan.FromSeconds(30));
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromSeconds(90));
    }

    // ---- postvisit.coding_review

    [Fact]
    public void ThePostvisitRetriesAreThoseOfItsDocumentAndTheReviewHasNoTimeout()
    {
        var read = new Driver(ReferenceDocs.Process("postvisit.coding_review"));
        read.Start("{\"case_ref\":\"c-1001\",\"assist_enabled\":true}");
        read.Send(Fail(read, ProcessErrorKind.Transient));
        var first = read.Single<TimerRequest>();
        Fire(read, first);
        read.Send(Fail(read, ProcessErrorKind.Transient));

        first.DueAt.Should().Be(Docs.T0 + TimeSpan.FromSeconds(30));
        read.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromSeconds(90));

        var ai = ReachSuggest();
        ai.Send(Fail(ai, ProcessErrorKind.Transient));
        ai.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromMinutes(1), "the assistant is asked again after a fixed PT1M");
        Fire(ai, ai.Single<TimerRequest>());
        ai.Send(Fail(ai, ProcessErrorKind.Transient));
        ai.State.CurrentStep.Should().Be("physician_review", "two attempts are all the assistant gets");
        ai.Last.Requests.OfType<TimerRequest>().Should().BeEmpty("the physician review waits for the physician for as long as it takes");
        ai.Single<TaskRequest>().Queue.Should().Be("physician_review");
    }

    // ---- collection.followup

    [Fact]
    public void TheClaimIsAwaitedTenDaysAndThePollReadsAtGrowingIntervalsForThirtyDaysInSevenReads()
    {
        var driver = new Driver(ReferenceDocs.Process("collection.followup"));
        driver.Start("{\"case_ref\":\"c-1001\"}");
        driver.Single<WaitRequest>().Event.Should().Be("claim.submitted");
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromDays(10), "the wait for the submission ends after P10D");

        driver.Send(new EventReceived("claim.submitted", "c-1001", "{}"));
        var gaps = new List<TimeSpan>();
        var reads = 1;
        while (true)
        {
            driver.Send(Complete(driver, "{\"status\":\"in_review\"}"));
            if (driver.State.CurrentStep != "poll_claim")
            {
                break;
            }

            var timer = driver.Single<TimerRequest>();
            gaps.Add(timer.DueAt - Docs.T0);
            Fire(driver, timer);
            reads++;
        }

        gaps.Should().Equal(
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(1 + 2),
            TimeSpan.FromDays(1 + 2 + 4),
            TimeSpan.FromDays(1 + 2 + 4 + 7),
            TimeSpan.FromDays(1 + 2 + 4 + 7 + 7),
            TimeSpan.FromDays(1 + 2 + 4 + 7 + 7 + 7));
        reads.Should().Be(7);
        driver.History.Should().Contain(entry => entry.Kind == ProcessHistoryKind.PollExpired);
        driver.State.CurrentStep.Should().Be("stalled");
        driver.Single<TaskRequest>().Queue.Should().Be("billing_followup");
    }

    [Fact]
    public void APartialPaymentWaitsAWeekBeforeTheNextRead()
    {
        var driver = new Driver(ReferenceDocs.Process("collection.followup"));
        driver.Start("{\"case_ref\":\"c-1001\"}");
        driver.Send(new EventReceived("claim.submitted", "c-1001", "{}"));
        driver.Send(Complete(driver, "{\"status\":\"partial\"}"));

        driver.State.CurrentStep.Should().Be("balance_wait");
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromDays(7));
    }

    [Fact]
    public void AFailedReadOfTheClaimIsRetriedAfterThirtySecondsThenAMinute()
    {
        var driver = new Driver(ReferenceDocs.Process("collection.followup"));
        driver.Start("{\"case_ref\":\"c-1001\"}");
        driver.Send(new EventReceived("claim.submitted", "c-1001", "{}"));
        driver.Send(Fail(driver, ProcessErrorKind.Transient));
        var first = driver.Single<TimerRequest>();
        Fire(driver, first);
        driver.Send(Fail(driver, ProcessErrorKind.Transient));

        first.DueAt.Should().Be(Docs.T0 + TimeSpan.FromSeconds(30));
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromSeconds(90));
    }

    // ---- practice.daily_refresh and case.lifecycle

    [Fact]
    public void TheDailyRefreshRetriesTheReadAfterAMinuteThenTwoAndLeavesItsTaskWithoutTimeout()
    {
        var driver = new Driver(ReferenceDocs.Process("practice.daily_refresh"));
        driver.Start("{\"run_for\":\"2027-01-05\"}");
        driver.Send(Fail(driver, ProcessErrorKind.Transient));
        var first = driver.Single<TimerRequest>();
        Fire(driver, first);
        driver.Send(Fail(driver, ProcessErrorKind.Transient));

        first.DueAt.Should().Be(Docs.T0 + TimeSpan.FromMinutes(1));
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0 + TimeSpan.FromMinutes(3));
        Fire(driver, driver.Single<TimerRequest>());
        driver.Send(Fail(driver, ProcessErrorKind.Transient));
        driver.State.CurrentStep.Should().Be("refresh_attention");
        driver.Single<TaskRequest>().Should().Match<TaskRequest>(task => task.Queue == "operations" && task.Outcomes.SequenceEqual(OutcomesHandled));
        driver.Last.Requests.OfType<TimerRequest>().Should().BeEmpty();
    }

    [Fact]
    public void NoStepOfTheLifecycleStartsATimerAndTheTasksOfOperationsHaveNoTimeout()
    {
        var driver = new Driver(ReferenceDocs.Process("case.lifecycle"));
        driver.Start("{\"case_ref\":\"c-1001\",\"practice_zone\":\"" + Zone + "\",\"assist_enabled\":true}");
        var child = driver.Single<ChildStartRequest>();
        driver.Send(new ChildFailed(child.ChildKey, "process.child.cancelled"));

        driver.State.CurrentStep.Should().Be("ops_previsit");
        driver.Requests.OfType<TimerRequest>().Should().BeEmpty();
    }

    // ---- helpers

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    private static Driver ReachSend()
    {
        var driver = new Driver(ReferenceDocs.Process("previsit.outreach"));
        driver.Start(Input);
        driver.Send(Complete(driver, "{\"missing_count\":2}"));
        return driver;
    }

    private static Driver ReachSuggest()
    {
        var driver = new Driver(ReferenceDocs.Process("postvisit.coding_review"));
        driver.Start("{\"case_ref\":\"c-1001\",\"assist_enabled\":true}");
        driver.Send(Complete(driver, "{\"items\":6,\"flagged\":2}"));
        driver.Single<EffectRequest>().Port.Should().Be("assist.suggest");
        return driver;
    }

    private static EffectCompleted Complete(Driver driver, string output) => new(driver.State.Pending!.Id, output);

    private static EffectFailed Fail(Driver driver, ProcessErrorKind kind) => new(driver.State.Pending!.Id, kind, "x.failed");

    private static void Fire(Driver driver, TimerRequest timer)
    {
        driver.Now = timer.DueAt;
        driver.Send(new TimerFired(timer.TimerId));
    }

    /// <summary>What the host does for a window request: store a timer at the opening in the zone of the case, then fire it.</summary>
    private static void OpenWindow(Driver driver)
    {
        var window = driver.Single<WindowRequest>();
        driver.Now = DeliveryWindows.NextOpen(driver.Now, Practice, window.Days, window.From, window.To);
        driver.Send(new TimerFired(window.TimerId));
    }
}
