using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// The follow-up process of the interpreter library (a contact action in a delivery window, then a poll), end to end on the durable
/// host: every scenario of the document is replayed with fake ports, a fake clock and the zones its <c>zones</c> member lists, and
/// the stored history is compared with what the pure interpreter writes when the window and timer answers are computed by
/// <see cref="DeliveryWindows.NextOpen"/> and the virtual clock.
/// </summary>
public sealed class FollowupFlowTests
{
    private const string ProcessId = "notice.followup";

    public static TheoryData<int> Scenarios => [0, 1, 2, 3, 4];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task EveryScenarioOfTheDocumentEndsAsItExpectsAndStoresWhatTheInterpreterWrites(int index)
    {
        var document = JsonNode.Parse(Fixtures.FollowupText())!;
        var scenario = document["body"]!["scenarios"]![index]!.AsObject();
        var expect = scenario["expect"]!.AsObject();
        var start = scenario["start"]!.AsObject();
        await using var rig = await Rig.CreateAsync();
        var process = Fixtures.Followup();
        rig.Catalog.Add(process);
        var zones = Zones(scenario);
        rig.Zones.Clear();
        foreach (var (id, zone) in zones)
        {
            rig.Zones.Add(id, zone);
        }

        var answers = new Dictionary<string, Queue<PortResult>>(StringComparer.Ordinal);
        var unscripted = new List<string>();
        foreach (var name in new[] { "messaging.notice", "intake.replies" })
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

        var events = scenario["events"]!.AsArray().Select(node => node!.AsObject()).ToList();
        foreach (var item in events)
        {
            Script(answers, item);
        }

        rig.Time.SetUtcNow(DateTimeOffset.Parse(start["at"]!.GetValue<string>(), CultureInfo.InvariantCulture));
        var id1 = await rig.StartAsync(ProcessId, "case-1", start["input"]!.ToJsonString());
        await rig.DrainAsync();
        foreach (var item in events)
        {
            await ApplyAsync(rig, item);
        }

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id1);
        info!.Status.Should().Be(Enum.Parse<ProcessStatus>(expect["status"]!.GetValue<string>()));
        info.Result.Should().Be(expect["result"]!.GetValue<string>());
        var history = await rig.Engine.GetHistoryAsync(rig.Tenant, id1);
        history.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step)
            .Should().Equal(expect["trace"]!.AsArray().Select(node => node!.GetValue<string>()));
        foreach (var (name, value) in expect["variables"]?.AsObject() ?? [])
        {
            info.State!.Variables[name].Should().Be(value!.ToJsonString());
        }

        history.Select(entry => entry.Kind).Should().Equal(PureKinds(process, scenario, zones));
        history.Select(entry => entry.Sequence).Should().Equal(Enumerable.Range(1, history.Count).Select(number => (long)number));
        unscripted.Should().BeEmpty();
        (await rig.OutstandingWorkAsync(id1)).Should().Be(0);
        (await rig.CountAsync("process_effect", "status in (1, 2)")).Should().Be(0);
        (await rig.CountAsync("process_inbox", "status = 1")).Should().Be(0);
    }

    [Fact]
    public async Task APollKeepsItsRoundsInTheStoredStateAndAskedForOneReadAndOneTimerPerRound()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Followup());
        var replies = 0;
        rig.Port("messaging.notice");
        var reads = rig.Port("intake.replies");
        reads.Handler = (_, _) => Task.FromResult<PortResult>(new PortCompleted("{\"reply_count\":" + replies + "}"));

        var id = await rig.StartAsync(ProcessId, "case-1", "{\"case_ref\":\"c-1\"}");
        await rig.DrainAsync();
        var first = await rig.Engine.GetInstanceAsync(rig.Tenant, id);

        first!.CurrentStep.Should().Be("await_reply");
        first.State!.Poll!.Round.Should().Be(1);
        first.State.Poll.StartedAt.Should().Be(Rig.Start);
        (await rig.ColumnAsync<string>("select timer_id from {schema}.process_timer")).Should().Equal(id + "/await_reply/2/r1/poll");
        (await rig.ColumnAsync<DateTimeOffset>("select due_at from {schema}.process_timer")).Should().Equal(Rig.Start + TimeSpan.FromHours(1));
        reads.Calls.Select(call => call.Request.EffectId).Should().Equal(id + "/await_reply/2/r1/1");

        rig.Time.Advance(TimeSpan.FromHours(1));
        await rig.DrainAsync();
        var second = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        second!.State!.Poll!.Round.Should().Be(2);
        // The interval doubles each round: round 2 began at one hour, so the next round is due two hours after that.
        (await rig.ColumnAsync<DateTimeOffset>("select due_at from {schema}.process_timer")).Should().Equal(Rig.Start + TimeSpan.FromHours(3));
        reads.Calls.Select(call => call.Request.EffectId).Should().Equal(id + "/await_reply/2/r1/1", id + "/await_reply/2/r2/1");

        replies = 3;
        rig.Time.Advance(TimeSpan.FromHours(2));
        await rig.DrainAsync();
        var done = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        done!.Result.Should().Be("answered");
        done.State!.Poll.Should().BeNull();
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    private static Dictionary<string, TimeZoneInfo> Zones(JsonObject scenario)
    {
        var zones = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);
        foreach (var (id, offset) in scenario["zones"]?.AsObject() ?? [])
        {
            var text = offset!.GetValue<string>();
            var magnitude = TimeSpan.ParseExact(text.TrimStart('+', '-'), "hh\\:mm", CultureInfo.InvariantCulture);
            zones[id] = TimeZoneInfo.CreateCustomTimeZone(id, text.StartsWith('-') ? -magnitude : magnitude, id, id);
        }

        return zones;
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

    private static async Task ApplyAsync(Rig rig, JsonObject item)
    {
        if (item["advance"] is { } advance)
        {
            rig.Time.Advance(XmlConvert.ToTimeSpan(advance.GetValue<string>()));
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

    /// <summary>
    /// The kinds of history entries the pure interpreter writes for the scenario: the oracle the history of the host is compared
    /// with. A window request is answered with the opening <see cref="DeliveryWindows.NextOpen"/> computes (or a failure when the
    /// zone is not listed), a timer fires when the virtual clock reaches it, as the scenario runner of the library does.
    /// </summary>
    private static List<ProcessHistoryKind> PureKinds(BrainProcess process, JsonObject scenario, Dictionary<string, TimeZoneInfo> zones)
    {
        var timers = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var kinds = new List<ProcessHistoryKind>();
        var start = scenario["start"]!.AsObject();
        var now = DateTimeOffset.Parse(start["at"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        var state = (ProcessState)null!;

        ProcessClock Clock() => new(now, DateOnly.FromDateTime(now.UtcDateTime));

        void Absorb(AdvanceResult result)
        {
            state = result.State;
            kinds.AddRange(result.History.Select(entry => entry.Kind));
            foreach (var request in result.Requests)
            {
                switch (request)
                {
                    case TimerRequest timer:
                        timers[timer.TimerId] = timer.DueAt;
                        break;
                    case CancelTimerRequest cancel:
                        timers.Remove(cancel.TimerId);
                        break;
                    case WindowRequest window when zones.TryGetValue(window.Zone, out var zone):
                        timers[window.TimerId] = DeliveryWindows.NextOpen(now, zone, window.Days, window.From, window.To);
                        break;
                    case WindowRequest window:
                        Absorb(process.Advance(state, new WindowFailed(window.TimerId, "process.window.zone_unknown"), Clock()));
                        break;
                }
            }

            FireDue();
        }

        void FireDue()
        {
            while (timers.Where(timer => timer.Value <= now).OrderBy(timer => timer.Value).ThenBy(timer => timer.Key, StringComparer.Ordinal).FirstOrDefault() is { Key: not null } due)
            {
                timers.Remove(due.Key);
                Absorb(process.Advance(state, new TimerFired(due.Key), Clock()));
            }
        }

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
                    now = due.Value > now ? due.Value : now;
                    Absorb(process.Advance(state, new TimerFired(due.Key), Clock()));
                }

                now = target;
            }
        }

        return kinds;
    }
}
