using System.Text.Json;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>
/// A second, independent player of the embedded scenarios. It keeps every result of every Start and Advance, so that tests can look at the
/// state after every step and at the serialized requests and history of a whole run.
/// </summary>
internal sealed class Player
{
    private readonly BrainProcess process;
    private readonly Dictionary<string, DateTimeOffset> timers = new(StringComparer.Ordinal);
    private ProcessState state = null!;
    private DateTimeOffset now;

    internal Player(BrainProcess process) => this.process = process;

    internal List<AdvanceResult> Results { get; } = [];

    internal IEnumerable<ProcessState> States => Results.Select(result => result.State);

    /// <summary>The whole run in a form that two runs can be compared by.</summary>
    internal string Transcript() => string.Join(
        "\n",
        Results.Select(result => string.Join(
            "|",
            result.Disposition,
            result.Code,
            result.State.ToJson(),
            JsonSerializer.Serialize(result.Requests.Select(request => JsonSerializer.Serialize((object)request)).ToArray()),
            JsonSerializer.Serialize(result.History))));

    internal static IEnumerable<JsonObject> Scenarios(JsonNode document) =>
        document["body"]!["scenarios"]!.AsArray().Select(node => node!.AsObject());

    internal void Run(JsonObject scenario)
    {
        var start = scenario["start"]!.AsObject();
        now = DateTimeOffset.Parse(start["at"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        Absorb(process.Start("scenario", start["input"] as JsonObject, Docs.Clock(now)));
        foreach (var item in scenario["events"]!.AsArray().Select(node => node!.AsObject()))
        {
            Apply(item);
        }
    }

    private void Absorb(AdvanceResult result)
    {
        Results.Add(result);
        state = result.State;
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

    private void Send(ProcessTrigger trigger) => Absorb(process.Advance(state, trigger, Docs.Clock(now)));

    private static string Text(JsonNode? node) => node is null ? "{}" : node.ToJsonString();

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
            TimeSpan? retryAfter = item["retryAfter"] is { } after ? System.Xml.XmlConvert.ToTimeSpan(after.GetValue<string>()) : null;
            Send(new EffectFailed(id, kind, item["code"]!.GetValue<string>(), retryAfter));
        }
        else if (item["advance"] is { } advance)
        {
            Advance(System.Xml.XmlConvert.ToTimeSpan(advance.GetValue<string>()));
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

    private void Advance(TimeSpan span)
    {
        var target = now + span;
        while (timers.Where(timer => timer.Value <= target).OrderBy(timer => timer.Value).ThenBy(timer => timer.Key, StringComparer.Ordinal).FirstOrDefault() is { Key: not null } due)
        {
            timers.Remove(due.Key);
            now = due.Value > now ? due.Value : now;
            Send(new TimerFired(due.Key));
        }

        now = target;
    }
}
