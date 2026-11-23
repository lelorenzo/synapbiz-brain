using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>Finds and loads the reference documents of <c>samples/process-reference</c>, by path from the repository root.</summary>
internal static class ReferenceFlows
{
    private const string Root = "samples/process-reference/";

    private static readonly string[] Ids =
    [
        "previsit.outreach",
        "visit.note_wait",
        "postvisit.coding_review",
        "collection.followup",
        "case.lifecycle",
        "practice.daily_refresh",
    ];

    /// <summary>Every reference process, loaded the way a consumer would: its own rules, and the six ids as the known children.</summary>
    internal static BrainProcess[] All() => [.. Ids.Select(id => Load(id, text => text))];

    /// <summary>Every reference process, with the text of one document changed by <paramref name="edit"/> before it is loaded.</summary>
    internal static BrainProcess[] All(string editedId, Func<string, string> edit) => [.. Ids.Select(id => Load(id, id == editedId ? edit : text => text))];

    private static BrainProcess Load(string id, Func<string, string> edit)
    {
        var folder = Path.GetDirectoryName(Fixtures.Locate(Root + id + "/process.json"))!;
        var contractPath = Path.Combine(folder, "contract.json");
        var contracts = File.Exists(contractPath) ? new[] { BrainContract.Parse(File.ReadAllText(contractPath)) } : [];
        var rules = Directory.GetFiles(folder, "*.rule.json")
            .Select(path => BrainRuleLoader.Load(File.ReadAllText(path), contracts).Rule ?? throw new InvalidOperationException(path + " does not load."))
            .ToList();
        var loaded = BrainProcessLoader.Load(edit(File.ReadAllText(Path.Combine(folder, "process.json"))), new ProcessLoadContext(rules, Ids));
        return loaded.Process ?? throw new InvalidOperationException(id + " does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }
}

/// <summary>
/// The pure interpreter as an oracle: it replays a list of events (in the format of the scenarios of a document) against a
/// definition with a virtual clock, answering window requests with <see cref="DeliveryWindows.NextOpen"/>, and returns the history
/// kinds and the steps it enters. The host must write the same.
/// </summary>
internal sealed class PureOracle
{
    private readonly BrainProcess process;
    private readonly IReadOnlyDictionary<string, TimeZoneInfo> zones;
    private readonly Dictionary<string, DateTimeOffset> timers = new(StringComparer.Ordinal);
    private readonly List<ProcessHistoryEntry> history = [];
    private ProcessState state = null!;
    private DateTimeOffset now;

    private PureOracle(BrainProcess process, IReadOnlyDictionary<string, TimeZoneInfo> zones, DateTimeOffset start)
    {
        this.process = process;
        this.zones = zones;
        now = start;
    }

    /// <summary>The history kinds, in order.</summary>
    internal IReadOnlyList<ProcessHistoryKind> Kinds => [.. history.Select(entry => entry.Kind)];

    /// <summary>The steps entered, in order.</summary>
    internal IReadOnlyList<string> Trace => [.. history.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step!)];

    /// <summary>The state after the last event.</summary>
    internal ProcessState Final => state;

    /// <summary>The variables of the final state as the object a parent receives from a finished child.</summary>
    internal JsonObject Output => new(state.Variables.Select(pair => KeyValuePair.Create(pair.Key, JsonNode.Parse(pair.Value))));

    /// <summary>Replays <paramref name="events"/> from <paramref name="start"/>.</summary>
    internal static PureOracle Replay(BrainProcess process, DateTimeOffset start, JsonObject input, IEnumerable<JsonObject> events, IReadOnlyDictionary<string, TimeZoneInfo>? zones = null)
    {
        var oracle = new PureOracle(process, zones ?? new Dictionary<string, TimeZoneInfo>(), start);
        oracle.Absorb(process.Start("oracle", input, oracle.Clock()));
        foreach (var item in events)
        {
            oracle.Apply(item);
        }

        return oracle;
    }

    private static string Text(JsonNode? node) => node is null ? "{}" : node.ToJsonString();

    private ProcessClock Clock() => new(now, DateOnly.FromDateTime(now.UtcDateTime));

    private void Send(ProcessTrigger trigger) => Absorb(process.Advance(state, trigger, Clock()));

    private void Absorb(AdvanceResult result)
    {
        if (result.Disposition != AdvanceDisposition.Applied && history.Count > 0)
        {
            throw new InvalidOperationException($"The oracle sent a trigger the instance was not waiting for ({result.Disposition}, {result.Code}).");
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
        else
        {
            throw new InvalidOperationException("The oracle does not know this event: " + item.ToJsonString());
        }
    }
}

/// <summary>Small builders for the events of <see cref="PureOracle"/>, in the format of the scenarios of a document.</summary>
internal static class Ev
{
    internal static JsonObject Complete(string step, string? outputJson = null) =>
        new() { ["complete"] = step, ["output"] = outputJson is null ? null : JsonNode.Parse(outputJson) };

    internal static JsonObject Advance(string duration) => new() { ["advance"] = duration };

    internal static JsonObject Event(string name, string correlation) => new() { ["event"] = name, ["correlation"] = correlation, ["payload"] = new JsonObject() };

    internal static JsonObject Task(string step, string outcome, string? dataJson = null) =>
        new() { ["task"] = step, ["outcome"] = outcome, ["data"] = dataJson is null ? null : JsonNode.Parse(dataJson) };

    internal static JsonObject Child(string step, string result, JsonObject output) => new() { ["child"] = step, ["result"] = result, ["output"] = output };

    internal static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);
}
