using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 4 of the W1-C specification: the <c>poll</c> step.</summary>
public sealed class PollTests
{
    private const string Uses = """[{ "$ref": "rule/conf.flag_rule" }]""";

    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "count": { "type": "Integer" },
          "level": { "type": "Enum", "allowedValues": ["low", "high"] }
        }
        """;

    private static readonly DateTimeOffset Start = Docs.T0;

    private static readonly string[] EndIds = ["done", "late", "err", "na", "ne"];

    private static string Poll(string extra = "", string errors = ", \"onError\": \"err\"") => $$"""
        { "kind": "poll", "port": "ehr.visit_note", "input": { "case": "case_ref" }, "output": { "n": "count" },
          "rule": "conf.flag_rule", "facts": { "count": "count" }, "field": "flag", "until": ["true"],
          "every": "PT15M", "maxDuration": "PT1H", "next": "done", "onExpired": "late"{{errors}}{{extra}} }
        """;

    private static BrainProcess Process(string poll, string extra = "")
    {
        var used = EndIds.Where(id => poll.Contains($"\"{id}\"", StringComparison.Ordinal));
        var steps = "{ \"s\": " + poll + ", " + string.Join(", ", used.Select(id => $"\"{id}\": {{ \"kind\": \"end\", \"result\": \"{id}\" }}")) + " }";
        return Docs.Load(Variables, steps, extra, Uses, rules: Docs.FlagRule);
    }

    private static Driver Started(string poll, string extra = "")
    {
        var driver = new Driver(Process(poll, extra));
        driver.Start("""{ "case_ref": "c-1" }""");
        return driver;
    }

    private static void Read(Driver driver, int round, int count, DateTimeOffset at, int attempt = 1)
    {
        driver.Now = at;
        driver.Send(new EffectCompleted($"i-1/s/1/r{round}/{attempt}", $$"""{ "n": {{count}} }"""));
    }

    [Fact]
    public void EnteringThePollRequestsTheReadOfRoundOne()
    {
        var driver = Started(Poll());

        var request = driver.Single<EffectRequest>();

        request.Should().Be(new EffectRequest("s", "i-1/s/1/r1/1", "i-1/s/1/r1", ProcessEffectKind.Read, "ehr.visit_note", """{"case":"c-1"}""", 1, ProcessDelivery.AtLeastOnce));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Effect, "i-1/s/1/r1/1"));
        driver.State.Poll.Should().Be(new ProcessPoll(1, Start));
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.EffectRequested);
    }

    [Fact]
    public void ThePollEndsInTheFirstRoundWhenTheFieldIsInUntil()
    {
        var driver = Started(Poll());

        Read(driver, 1, 12, Start);

        driver.State.Status.Should().Be(ProcessStatus.Completed);
        driver.State.Result.Should().Be("done");
        driver.State.Poll.Should().BeNull();
        driver.Variable("count").Should().Be("12");
        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectCompleted, ProcessHistoryKind.Decided, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
        driver.Trace.Should().Equal("s", "done");
    }

    [Fact]
    public void TheDecidedEntryHasTheUsualProvenance()
    {
        var driver = Started(Poll());

        Read(driver, 1, 12, Start);

        var decided = driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.Decided);
        var data = JsonNode.Parse(decided.DataJson!)!.AsObject();
        decided.Step.Should().Be("s");
        decided.Entry.Should().Be(1);
        data.Select(member => member.Key).Should().Equal("rule", "version", "decided", "matchedRowKeys", "outcome", "factsHash", "facts");
        data["rule"]!.GetValue<string>().Should().Be("conf.flag_rule");
        data["decided"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void AnUnmetConditionWaitsForTheIntervalAndTheNextRoundReadsAgain()
    {
        var driver = Started(Poll());

        Read(driver, 1, 3, Start);

        driver.Single<TimerRequest>().Should().Be(new TimerRequest("s", "i-1/s/1/r1/poll", Start.AddMinutes(15)));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Timer, "i-1/s/1/r1/poll"));
        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectCompleted, ProcessHistoryKind.Decided, ProcessHistoryKind.TimerStarted);
        driver.Last.History[^1].DataJson.Should().Be("""{"dueAt":"2027-01-04T14:15:00.0000000+00:00","timerId":"i-1/s/1/r1/poll"}""");

        driver.Now = Start.AddMinutes(15);
        driver.Send(new TimerFired("i-1/s/1/r1/poll"));

        var request = driver.Single<EffectRequest>();
        request.EffectId.Should().Be("i-1/s/1/r2/1");
        request.IdempotencyKey.Should().Be("i-1/s/1/r2");
        request.Attempt.Should().Be(1);
        driver.State.Poll.Should().Be(new ProcessPoll(2, Start));
        driver.State.Attempt.Should().Be(1);
        driver.Kinds.Should().Equal(ProcessHistoryKind.TimerFired, ProcessHistoryKind.EffectRequested);

        Read(driver, 2, 10, Start.AddMinutes(15));

        driver.State.Result.Should().Be("done");
        driver.Variable("count").Should().Be("10");
        driver.Trace.Should().Equal("s", "done");
    }

    [Fact]
    public void ARuleThatDecidesNothingKeepsPolling()
    {
        var driver = Started(Poll());

        Read(driver, 1, -1, Start);

        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.Timer);
        driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.Decided).DataJson.Should().Contain("\"decided\":false");
    }

    [Fact]
    public void ABooleanUntilMayAskForFalse()
    {
        var driver = Started(Poll().Replace("\"until\": [\"true\"]", "\"until\": [\"false\"]", StringComparison.Ordinal));

        Read(driver, 1, 12, Start);
        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.Timer);
        driver.Now = Start.AddMinutes(15);
        driver.Send(new TimerFired("i-1/s/1/r1/poll"));
        Read(driver, 2, 4, Start.AddMinutes(15));

        driver.State.Result.Should().Be("done");
    }

    [Fact]
    public void AnEnumFieldEndsOnAnyListedValue()
    {
        var enumPoll = Poll().Replace("\"field\": \"flag\", \"until\": [\"true\"]", "\"field\": \"level\", \"until\": [\"high\"]", StringComparison.Ordinal);
        var driver = Started(enumPoll);

        Read(driver, 1, 5, Start);
        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.Timer);
        driver.Send(new TimerFired("i-1/s/1/r1/poll"));
        Read(driver, 2, 50, Start);

        driver.State.Result.Should().Be("done");
    }

    [Fact]
    public void ThePollExpiresWhenTheNextRoundWouldBeAfterTheLimit()
    {
        var driver = Started(Poll("", ", \"onError\": \"err\"").Replace("PT1H", "PT30M", StringComparison.Ordinal));

        // Rounds at +0, +15 and +30 minutes: the third round is exactly at the limit and runs; the next would be at +45.
        Read(driver, 1, 1, Start);
        driver.Now = Start.AddMinutes(15);
        driver.Send(new TimerFired("i-1/s/1/r1/poll"));
        Read(driver, 2, 1, Start.AddMinutes(15));
        driver.Single<TimerRequest>().DueAt.Should().Be(Start.AddMinutes(30));
        driver.Now = Start.AddMinutes(30);
        driver.Send(new TimerFired("i-1/s/1/r2/poll"));
        Read(driver, 3, 1, Start.AddMinutes(30));

        driver.State.Status.Should().Be(ProcessStatus.Completed);
        driver.State.Result.Should().Be("late");
        driver.Last.Requests.Should().BeEmpty();
        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectCompleted, ProcessHistoryKind.Decided, ProcessHistoryKind.PollExpired, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
        driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.PollExpired).DataJson.Should().Be("""{"rounds":3}""");
        driver.State.Poll.Should().BeNull();
    }

    [Fact]
    public void ALimitShorterThanTheIntervalExpiresAfterTheFirstRound()
    {
        var driver = Started(Poll().Replace("PT1H", "PT10M", StringComparison.Ordinal));

        Read(driver, 1, 1, Start);

        driver.State.Result.Should().Be("late");
        driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.PollExpired).DataJson.Should().Be("""{"rounds":1}""");
    }

    [Fact]
    public void ExponentialBackoffDoublesTheIntervalUpToMax()
    {
        var driver = Started(Poll(", \"backoff\": \"exponential\", \"max\": \"PT25M\"").Replace("PT15M", "PT10M", StringComparison.Ordinal).Replace("PT1H", "P1D", StringComparison.Ordinal));
        var at = Start;
        var due = new List<TimeSpan>();

        for (var round = 1; round <= 5; round++)
        {
            Read(driver, round, 1, at);
            var timer = driver.Single<TimerRequest>();
            due.Add(timer.DueAt - at);
            at = timer.DueAt;
            driver.Now = at;
            driver.Send(new TimerFired(timer.TimerId));
        }

        due.Should().Equal(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25));
    }

    [Fact]
    public void ExponentialBackoffWithoutMaxKeepsDoubling()
    {
        var driver = Started(Poll(", \"backoff\": \"exponential\"").Replace("PT15M", "PT1M", StringComparison.Ordinal).Replace("PT1H", "P3000D", StringComparison.Ordinal));
        var at = Start;
        var gaps = new List<TimeSpan>();

        for (var round = 1; round <= 40; round++)
        {
            Read(driver, round, 1, at);
            if (driver.State.Status != ProcessStatus.Waiting)
            {
                break;
            }

            var timer = driver.Single<TimerRequest>();
            gaps.Add(timer.DueAt - at);
            at = timer.DueAt;
            driver.Now = at;
            driver.Send(new TimerFired(timer.TimerId));
        }

        gaps.Take(5).Should().Equal(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(16));
        driver.State.Result.Should().Be("late");
    }

    [Fact]
    public void ARoundIsNotCountedAgainstTheStepLimit()
    {
        var driver = Started(Poll().Replace("PT1H", "P1D", StringComparison.Ordinal), "\"limits\": { \"maxSteps\": 2 }");
        var at = Start;

        for (var round = 1; round <= 20; round++)
        {
            Read(driver, round, 1, at);
            at = driver.Single<TimerRequest>().DueAt;
            driver.Now = at;
            driver.Send(new TimerFired($"i-1/s/1/r{round}/poll"));
        }

        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.State.StepCount.Should().Be(1);
        driver.State.Poll!.Round.Should().Be(21);
        Read(driver, 21, 99, at);
        driver.State.Result.Should().Be("done");
        driver.State.StepCount.Should().Be(2);
    }

    [Fact]
    public void ATransientFailureIsRetriedInsideTheRound()
    {
        var driver = Started(Poll(", \"retry\": { \"maxAttempts\": 2, \"backoff\": \"fixed\", \"initial\": \"PT1M\" }"));

        driver.Send(new EffectFailed("i-1/s/1/r1/1", ProcessErrorKind.Transient, "busy"));

        driver.Single<TimerRequest>().TimerId.Should().Be("i-1/s/1/r1/retry/1");
        driver.Now = Start.AddMinutes(1);
        driver.Send(new TimerFired("i-1/s/1/r1/retry/1"));
        var retry = driver.Single<EffectRequest>();
        retry.EffectId.Should().Be("i-1/s/1/r1/2");
        retry.IdempotencyKey.Should().Be("i-1/s/1/r1");
        retry.Attempt.Should().Be(2);
        Read(driver, 1, 1, Start.AddMinutes(1), attempt: 2);
        driver.Single<TimerRequest>().DueAt.Should().Be(Start.AddMinutes(16));
        driver.Now = Start.AddMinutes(16);
        driver.Send(new TimerFired("i-1/s/1/r1/poll"));

        // The second round begins at attempt 1 again, with its own retry identifiers.
        driver.State.Attempt.Should().Be(1);
        driver.Send(new EffectFailed("i-1/s/1/r2/1", ProcessErrorKind.RateLimited, "slow"));
        driver.Single<TimerRequest>().TimerId.Should().Be("i-1/s/1/r2/retry/1");
    }

    [Theory]
    [InlineData(ProcessErrorKind.Permanent, "err")]
    [InlineData(ProcessErrorKind.Unknown, "err")]
    [InlineData(ProcessErrorKind.Transient, "err")]
    [InlineData(ProcessErrorKind.NotAvailable, "na")]
    [InlineData(ProcessErrorKind.NotEntitled, "ne")]
    public void APortFailureAfterTheRetriesFollowsTheErrorRoutes(ProcessErrorKind kind, string expected)
    {
        var driver = Started(Poll(", \"onNotAvailable\": \"na\", \"onNotEntitled\": \"ne\""));

        driver.Send(new EffectFailed("i-1/s/1/r1/1", kind, "port.failed"));

        driver.State.Result.Should().Be(expected);
        driver.State.Poll.Should().BeNull();
    }

    [Fact]
    public void APortFailureInALaterRoundFollowsTheRoutesToo()
    {
        var driver = Started(Poll());
        Read(driver, 1, 1, Start);
        driver.Send(new TimerFired("i-1/s/1/r1/poll"));

        driver.Send(new EffectFailed("i-1/s/1/r2/1", ProcessErrorKind.Permanent, "gone"));

        driver.State.Result.Should().Be("err");
    }

    [Fact]
    public void APortFailureWithoutARouteSuspendsAndClearsThePollProgress()
    {
        var driver = Started(Poll(errors: ""));

        driver.Send(new EffectFailed("i-1/s/1/r1/1", ProcessErrorKind.Permanent, "gone"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("gone", "s"));
        driver.State.Poll.Should().BeNull();
    }

    [Fact]
    public void AnOutputThatDoesNotFitIsAPermanentFailure()
    {
        var driver = Started(Poll());

        driver.Send(new EffectCompleted("i-1/s/1/r1/1", """{ "n": "many" }"""));

        driver.State.Result.Should().Be("err");
        driver.Last.History.Select(entry => entry.Code).Should().Contain("process.variable.type_mismatch");
    }

    [Fact]
    public void ARuleErrorFollowsOnError()
    {
        var driver = Started(Poll());

        // The read answers without the count: the fact is missing and the engine fails.
        driver.Send(new EffectCompleted("i-1/s/1/r1/1", "{}"));

        driver.State.Result.Should().Be("err");
        var decided = driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.Decided);
        decided.Code.Should().NotBeNullOrEmpty();
        JsonNode.Parse(decided.DataJson!)!.AsObject().Select(member => member.Key).Should().Equal("error", "rule", "version");
    }

    [Fact]
    public void ARuleErrorWithoutOnErrorSuspendsWithTheErrorCode()
    {
        var driver = Started(Poll(errors: ""));

        driver.Send(new EffectCompleted("i-1/s/1/r1/1", "{}"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem!.Code.Should().Be(driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.Decided).Code);
        driver.State.Problem.Detail.Should().NotBeNullOrEmpty();
        driver.State.Poll.Should().BeNull();
    }

    [Fact]
    public void ResumingASuspendedPollStartsItAgainAtRoundOne()
    {
        var driver = Started(Poll(errors: ""));
        driver.Send(new EffectFailed("i-1/s/1/r1/1", ProcessErrorKind.Permanent, "gone"));

        driver.Now = Start.AddHours(2);
        driver.Send(new ResumeRequested());

        var request = driver.Single<EffectRequest>();
        request.EffectId.Should().Be("i-1/s/2/r1/1");
        request.IdempotencyKey.Should().Be("i-1/s/2/r1");
        driver.State.Poll.Should().Be(new ProcessPoll(1, Start.AddHours(2)));
        driver.State.StepCount.Should().Be(2);
    }

    [Fact]
    public void ATimerOfAnEarlierRoundIsIgnored()
    {
        var driver = Started(Poll());
        Read(driver, 1, 1, Start);
        driver.Send(new TimerFired("i-1/s/1/r1/poll"));

        var stale = driver.Send(new TimerFired("i-1/s/1/r1/poll"));

        stale.Disposition.Should().Be(AdvanceDisposition.Ignored);
        driver.State.Poll!.Round.Should().Be(2);
    }

    [Fact]
    public void CancellingWhileThePollWaitsCancelsItsTimer()
    {
        var driver = Started(Poll());
        Read(driver, 1, 1, Start);

        driver.Send(new CancelRequested("operator"));

        driver.State.Status.Should().Be(ProcessStatus.Cancelled);
        driver.State.Poll.Should().BeNull();
        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/r1/poll"));
    }

    [Fact]
    public void TheStateInTheMiddleOfAPollRoundTripsAndContinuesTheSame()
    {
        var driver = Started(Poll());
        Read(driver, 1, 1, Start.AddMilliseconds(123));
        var json = driver.State.ToJson();

        var loaded = ProcessState.FromJson(json);

        json.Should().Contain("\"poll\":{\"round\":1,\"startedAt\":\"2027-01-04T14:00:00.0000000+00:00\"}");
        loaded.Should().Be(driver.State);
        loaded.Poll.Should().Be(new ProcessPoll(1, Start));
        loaded.ToJson().Should().Be(json);

        var process = Process(Poll());
        var inMemory = process.Advance(driver.State, new TimerFired("i-1/s/1/r1/poll"), Docs.Clock(Start.AddMinutes(15)));
        var fromJson = process.Advance(loaded, new TimerFired("i-1/s/1/r1/poll"), Docs.Clock(Start.AddMinutes(15)));

        fromJson.State.ToJson().Should().Be(inMemory.State.ToJson());
        fromJson.Requests.Should().Equal(inMemory.Requests);
    }

    [Fact]
    public void ThePollStartIsKeptToTheTickWhenTheStateIsStored()
    {
        var driver = new Driver(Process(Poll()))
        {
            Now = Start.AddTicks(1234567),
        };
        driver.Start("""{ "case_ref": "c-1" }""");

        var loaded = ProcessState.FromJson(driver.State.ToJson());

        loaded.Poll!.StartedAt.Should().Be(Start.AddTicks(1234567));
        loaded.Should().Be(driver.State);
    }

    [Fact]
    public void ThePollRunsOnTheOutreachStyleOfScenarioWithAnAdvance()
    {
        var extra = """
            "scenarios": [
              { "name": "ready in the second round",
                "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
                "events": [
                  { "complete": "s", "output": { "n": 2 } },
                  { "advance": "PT15M" },
                  { "complete": "s", "output": { "n": 11 } } ],
                "expect": { "status": "Completed", "result": "done", "trace": ["s", "done"], "variables": { "count": 11 } } },
              { "name": "port failure in a round goes to the error route",
                "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
                "events": [
                  { "complete": "s", "output": { "n": 2 } },
                  { "advance": "PT15M" },
                  { "fail": "s", "error": "Permanent", "code": "gone" } ],
                "expect": { "status": "Completed", "result": "err" } },
              { "name": "expires",
                "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
                "events": [
                  { "complete": "s", "output": { "n": 2 } },
                  { "advance": "PT15M" },
                  { "complete": "s", "output": { "n": 2 } },
                  { "advance": "PT15M" },
                  { "complete": "s", "output": { "n": 2 } },
                  { "advance": "PT15M" },
                  { "complete": "s", "output": { "n": 2 } },
                  { "advance": "PT15M" },
                  { "complete": "s", "output": { "n": 2 } } ],
                "expect": { "status": "Completed", "result": "late" } } ]
            """;
        var process = Process(Poll(), extra);

        var result = process.RunScenarios();

        result.Results.Where(item => !item.Passed).Select(item => item.Name + ": " + item.Failure).Should().BeEmpty();
        result.Results.Should().HaveCount(3);
    }
}
