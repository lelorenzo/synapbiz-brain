using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// The reference outreach process of the interpreter library, end to end on the durable host: every scenario of the document is
/// replayed with fake ports, a fake clock and the engine, and the stored history is compared with what the pure interpreter
/// writes for the same events.
/// </summary>
public sealed class OutreachFlowTests
{
    public static TheoryData<int> Scenarios => [0, 1, 2, 3, 4];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task EveryScenarioOfTheDocumentEndsAsItExpectsAndStoresWhatTheInterpreterWrites(int index)
    {
        var document = JsonNode.Parse(Fixtures.OutreachText())!;
        var scenario = document["body"]!["scenarios"]![index]!.AsObject();
        var expect = scenario["expect"]!.AsObject();
        await using var rig = await Rig.CreateAsync();
        var process = Fixtures.Outreach();
        rig.Catalog.Add(process);
        var answers = new Dictionary<string, Queue<PortResult>>(StringComparer.Ordinal);
        var unscripted = new List<string>();
        foreach (var name in new[] { "intake.status", "messaging.notice" })
        {
            rig.Port(name).Handler = (call, _) =>
            {
                if (answers.TryGetValue(call.Request.Step, out var queue) && queue.TryDequeue(out var answer))
                {
                    return Task.FromResult(answer);
                }

                unscripted.Add(call.Request.EffectId);
                return Task.FromResult<PortResult>(new PortFailed(ProcessErrorKind.Permanent, "test.unscripted"));
            };
        }

        // The answers of the ports are given up front, in script order per step: what an effect answers does not depend on
        // when it is asked, and the instance stays where it is until a timer, an event or a task moves it on.
        var events = scenario["events"]!.AsArray().Select(node => node!.AsObject()).ToList();
        foreach (var item in events)
        {
            Script(answers, item);
        }

        var start = scenario["start"]!.AsObject();
        var id = await rig.StartAsync("previsit.outreach", "case-1", start["input"]!.ToJsonString());
        await rig.DrainAsync();
        foreach (var item in events)
        {
            await ApplyAsync(rig, id, item);
        }

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(Enum.Parse<ProcessStatus>(expect["status"]!.GetValue<string>()));
        info.Result.Should().Be(expect["result"]!.GetValue<string>());
        var history = await rig.Engine.GetHistoryAsync(rig.Tenant, id);
        history.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step)
            .Should().Equal(expect["trace"]!.AsArray().Select(node => node!.GetValue<string>()));
        foreach (var (name, value) in expect["variables"]?.AsObject() ?? [])
        {
            info.State!.Variables[name].Should().Be(value!.ToJsonString());
        }

        history.Select(entry => entry.Kind).Should().Equal(PureKinds(process, scenario));
        history.Select(entry => entry.Sequence).Should().Equal(Enumerable.Range(1, history.Count).Select(number => (long)number));
        unscripted.Should().BeEmpty();
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
        (await rig.CountAsync("process_effect", "status in (1, 2)")).Should().Be(0);
    }

    [Fact]
    public async Task ARunningInstanceHoldsExactlyTheWorkItsStepAsked()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Outreach());
        rig.Port("intake.status").Handler = (_, _) => Task.FromResult<PortResult>(new PortCompleted("{\"missing_count\": 2}"));
        rig.Port("messaging.notice");

        var id = await rig.StartAsync("previsit.outreach", "case-1", "{\"case_ref\":\"c-1\"}");
        await rig.DrainAsync();

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.CurrentStep.Should().Be("reply");
        info.Stage.Should().Be("previsit");
        (await rig.CountAsync("process_wait", "instance_id = @i", ("i", id))).Should().Be(1);
        (await rig.CountAsync("process_timer", "instance_id = @i", ("i", id))).Should().Be(1);
        (await rig.CountAsync("process_effect", "instance_id = @i and status = 3", ("i", id))).Should().Be(2);
    }

    private static void Script(Dictionary<string, Queue<PortResult>> answers, JsonObject item)
    {
        if (item["complete"] is { } complete)
        {
            Enqueue(answers, complete.GetValue<string>(), new PortCompleted(item["output"]?.ToJsonString() ?? "{}"));
        }
        else if (item["fail"] is { } fail)
        {
            TimeSpan? retryAfter = item["retryAfter"] is { } after ? XmlConvert.ToTimeSpan(after.GetValue<string>()) : null;
            Enqueue(answers, fail.GetValue<string>(), new PortFailed(Enum.Parse<ProcessErrorKind>(item["error"]!.GetValue<string>()), item["code"]!.GetValue<string>(), retryAfter));
        }
    }

    private static async Task ApplyAsync(Rig rig, string id, JsonObject item)
    {
        if (item["advance"] is { } advance)
        {
            rig.Time.Advance(XmlConvert.ToTimeSpan(advance.GetValue<string>()));
        }
        else if (item["event"] is { } name)
        {
            var published = await rig.Engine.PublishEventAsync(new ProcessEvent(
                rig.Tenant,
                Guid.NewGuid().ToString("N"),
                name.GetValue<string>(),
                item["correlation"]?.GetValue<string>(),
                item["payload"]?.ToJsonString() ?? "{}"));
            published.Status.Should().Be(PublishStatus.Queued);
        }
        else if (item["task"] is { } task)
        {
            var open = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "front_desk")).Single(candidate => candidate.InstanceId == id && candidate.Step == task.GetValue<string>());
            var result = await rig.Engine.CompleteTaskAsync(rig.Tenant, open.TaskId, item["outcome"]!.GetValue<string>(), item["data"]?.ToJsonString() ?? "{}");
            result.Disposition.Should().Be(AdvanceDisposition.Applied);
        }

        await rig.DrainAsync();
    }

    private static void Enqueue(Dictionary<string, Queue<PortResult>> answers, string step, PortResult answer)
    {
        if (!answers.TryGetValue(step, out var queue))
        {
            answers[step] = queue = new Queue<PortResult>();
        }

        queue.Enqueue(answer);
    }

    /// <summary>The kinds of history entries the pure interpreter writes for the scenario: the oracle the host's history is compared with.</summary>
    private static List<ProcessHistoryKind> PureKinds(BrainProcess process, JsonObject scenario)
    {
        var timers = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var kinds = new List<ProcessHistoryKind>();
        var start = scenario["start"]!.AsObject();
        var now = DateTimeOffset.Parse(start["at"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        var state = (ProcessState)null!;

        void Absorb(AdvanceResult result)
        {
            state = result.State;
            kinds.AddRange(result.History.Select(entry => entry.Kind));
            foreach (var request in result.Requests)
            {
                if (request is TimerRequest timer)
                {
                    timers[timer.TimerId] = timer.DueAt;
                }
                else if (request is CancelTimerRequest cancel)
                {
                    timers.Remove(cancel.TimerId);
                }
            }
        }

        ProcessClock Clock() => new(now, DateOnly.FromDateTime(now.UtcDateTime));
        Absorb(process.Start("scenario", start["input"] as JsonObject, Clock()));
        foreach (var item in scenario["events"]!.AsArray().Select(node => node!.AsObject()))
        {
            var pending = state.Pending?.Id ?? "";
            if (item["complete"] is not null)
            {
                Absorb(process.Advance(state, new EffectCompleted(pending, item["output"]?.ToJsonString() ?? "{}"), Clock()));
            }
            else if (item["fail"] is not null)
            {
                TimeSpan? retryAfter = item["retryAfter"] is { } after ? XmlConvert.ToTimeSpan(after.GetValue<string>()) : null;
                Absorb(process.Advance(state, new EffectFailed(pending, Enum.Parse<ProcessErrorKind>(item["error"]!.GetValue<string>()), item["code"]!.GetValue<string>(), retryAfter), Clock()));
            }
            else if (item["advance"] is { } advance)
            {
                var target = now + XmlConvert.ToTimeSpan(advance.GetValue<string>());
                while (timers.Where(timer => timer.Value <= target).OrderBy(timer => timer.Value).ThenBy(timer => timer.Key, StringComparer.Ordinal).FirstOrDefault() is { Key: not null } due)
                {
                    timers.Remove(due.Key);
                    now = due.Value;
                    Absorb(process.Advance(state, new TimerFired(due.Key), Clock()));
                }

                now = target;
            }
            else if (item["event"] is { } name)
            {
                Absorb(process.Advance(state, new EventReceived(name.GetValue<string>(), item["correlation"]?.GetValue<string>(), item["payload"]?.ToJsonString() ?? "{}"), Clock()));
            }
            else if (item["task"] is not null)
            {
                Absorb(process.Advance(state, new TaskCompleted(pending, item["outcome"]!.GetValue<string>(), item["data"]?.ToJsonString() ?? "{}"), Clock()));
            }
        }

        return kinds;
    }
}
