using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>One scenario played to its end: the whole history and the last state.</summary>
/// <param name="Name">The scenario name.</param>
/// <param name="History">Every history entry of the run, in order.</param>
/// <param name="Final">The state after the last event.</param>
internal sealed record ScenarioRun(string Name, IReadOnlyList<ProcessHistoryEntry> History, ProcessState Final)
{
    /// <summary>The steps entered, in order.</summary>
    internal IReadOnlyList<string> Trace => [.. History.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step!)];
}

/// <summary>
/// A second player of the embedded scenarios, written against <c>Start</c> and <c>Advance</c> only. It answers a window request with
/// <see cref="DeliveryWindows.NextOpen"/> for the zones the scenario lists (and with <see cref="WindowFailed"/> for any other zone),
/// fires timers in time order at the virtual clock, and keeps every history entry, so that the route a scenario took out of every
/// step can be read from the run.
/// </summary>
internal sealed class ScenarioTracer
{
    private readonly BrainProcess process;
    private readonly Dictionary<string, TimeZoneInfo> zones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> timers = new(StringComparer.Ordinal);
    private readonly List<ProcessHistoryEntry> history = [];
    private ProcessState state = null!;
    private DateTimeOffset now;

    private ScenarioTracer(BrainProcess process) => this.process = process;

    /// <summary>Plays one scenario of a process document.</summary>
    internal static ScenarioRun Play(BrainProcess process, JsonObject scenario)
    {
        var tracer = new ScenarioTracer(process);
        var name = scenario["name"]!.GetValue<string>();
        foreach (var (id, offset) in scenario["zones"]?.AsObject() ?? [])
        {
            var text = offset!.GetValue<string>();
            var magnitude = TimeSpan.ParseExact(text.TrimStart('+', '-'), "hh\\:mm", CultureInfo.InvariantCulture);
            tracer.zones[id] = TimeZoneInfo.CreateCustomTimeZone(id, text.StartsWith('-') ? -magnitude : magnitude, id, id);
        }

        var start = scenario["start"]!.AsObject();
        tracer.now = DateTimeOffset.Parse(start["at"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        tracer.Absorb(process.Start("scenario", start["input"] as JsonObject, tracer.Clock()));
        foreach (var item in scenario["events"]!.AsArray().Select(node => node!.AsObject()))
        {
            tracer.Apply(item);
        }

        return new ScenarioRun(name, tracer.history, tracer.state);
    }

    private static string Text(JsonNode? node) => node is null ? "{}" : node.ToJsonString();

    private ProcessClock Clock() => new(now, DateOnly.FromDateTime(now.UtcDateTime));

    private void Send(ProcessTrigger trigger) => Absorb(process.Advance(state, trigger, Clock()));

    private void Absorb(AdvanceResult result)
    {
        if (result.Disposition != AdvanceDisposition.Applied && history.Count > 0)
        {
            throw new InvalidOperationException($"The trigger was {result.Disposition} ({result.Code}): a scenario may only send triggers the instance is waiting for.");
        }

        state = result.State;
        history.AddRange(result.History);
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
                    Send(new WindowFailed(window.TimerId, "process.window.zone_unknown"));
                    return;
                default:
                    break;
            }
        }

        FireDue(now);
    }

    private void FireDue(DateTimeOffset until)
    {
        while (timers.Where(timer => timer.Value <= until).OrderBy(timer => timer.Value).ThenBy(timer => timer.Key, StringComparer.Ordinal).FirstOrDefault() is { Key: not null } due)
        {
            timers.Remove(due.Key);
            now = due.Value > now ? due.Value : now;
            Send(new TimerFired(due.Key));
        }
    }

    private void Apply(JsonObject item)
    {
        var id = state.Pending?.Id ?? "";
        if (item["complete"] is not null)
        {
            Send(new EffectCompleted(id, Text(item["output"])));
        }
        else if (item["fail"] is not null)
        {
            var kind = Enum.Parse<ProcessErrorKind>(item["error"]!.GetValue<string>());
            TimeSpan? retryAfter = item["retryAfter"] is { } after ? XmlConvert.ToTimeSpan(after.GetValue<string>()) : null;
            Send(new EffectFailed(id, kind, item["code"]!.GetValue<string>(), retryAfter));
        }
        else if (item["advance"] is { } advance)
        {
            var target = now + XmlConvert.ToTimeSpan(advance.GetValue<string>());
            FireDue(target);
            now = target;
        }
        else if (item["event"] is { } name)
        {
            Send(new EventReceived(name.GetValue<string>(), item["correlation"]?.GetValue<string>(), Text(item["payload"])));
        }
        else if (item["task"] is not null)
        {
            Send(new TaskCompleted(id, item["outcome"]!.GetValue<string>(), Text(item["data"])));
        }
        else if (item["child"] is not null)
        {
            Send(new ChildCompleted(id, item["result"]!.GetValue<string>(), Text(item["output"])));
        }
        else if (item["childFailed"] is not null)
        {
            Send(new ChildFailed(id, item["code"]!.GetValue<string>()));
        }
        else if (item["cancel"] is { } reason)
        {
            Send(new CancelRequested(reason.GetValue<string>()));
        }
        else
        {
            Send(new ResumeRequested());
        }
    }
}
