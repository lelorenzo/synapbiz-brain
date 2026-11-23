using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>
/// The routes out of the steps of a process document and the routes a run took. A route is a step and a label: <c>next</c>,
/// <c>body</c>, one of the <c>on...</c> members, or <c>on:&lt;value&gt;</c> for an entry of <c>on</c> (a rule outcome, a task
/// outcome or a child result). The route a run took is read from the history entries between the step and the next step entered,
/// and checked against the document: the target of that route must be the step the run went to.
/// </summary>
internal static class RouteMap
{
    private static readonly string[] Members =
    [
        "next", "body", "onError", "onNotAvailable", "onNotEntitled", "onUnknown", "onTimeout", "onExhausted", "onNoDecision", "onInvalid", "onExpired",
    ];

    /// <summary>The labels of every route a step declares.</summary>
    internal static IReadOnlyList<string> Declared(JsonObject step)
    {
        var labels = Members.Where(member => step[member] is JsonValue).ToList();
        labels.AddRange((step["on"]?.AsObject() ?? []).Select(entry => "on:" + entry.Key));
        return labels;
    }

    /// <summary>The step a route leads to.</summary>
    internal static string Target(JsonObject step, string label) =>
        label.StartsWith("on:", StringComparison.Ordinal)
            ? step["on"]![label[3..]]!.GetValue<string>()
            : step[label]!.GetValue<string>();

    /// <summary>Every (step, label) the run took, in order.</summary>
    internal static IReadOnlyList<(string Step, string Label)> Taken(JsonObject steps, IReadOnlyList<ProcessHistoryEntry> history)
    {
        var taken = new List<(string Step, string Label)>();
        var entered = Enumerable.Range(0, history.Count).Where(index => history[index].Kind == ProcessHistoryKind.StepEntered).ToList();
        for (var position = 0; position + 1 < entered.Count; position++)
        {
            var from = history[entered[position]].Step!;
            var to = history[entered[position + 1]].Step!;
            var segment = history.Skip(entered[position] + 1).Take(entered[position + 1] - entered[position] - 1).ToList();
            var step = steps[from]!.AsObject();
            var label = Label(step, segment)
                ?? throw new InvalidOperationException($"No route of '{from}' explains the history up to '{to}': {string.Join(", ", segment.Select(entry => entry.Kind))}.");
            var target = Target(step, label);
            if (!string.Equals(target, to, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The route '{label}' of '{from}' leads to '{target}', but the run went to '{to}'.");
            }

            taken.Add((from, label));
        }

        return taken;
    }

    private static string? Label(JsonObject step, List<ProcessHistoryEntry> segment) => step["kind"]!.GetValue<string>() switch
    {
        "read" or "action" or "ai" or "poll" => EffectLabel(step, segment),
        "decide" => DecideLabel(step, segment),
        "humanTask" => segment.Any(entry => entry.Kind == ProcessHistoryKind.TimedOut) ? "onTimeout" : "on:" + Data(segment.Last(entry => entry.Kind == ProcessHistoryKind.TaskCompleted))["outcome"]!.GetValue<string>(),
        "wait" => segment.Any(entry => entry.Kind == ProcessHistoryKind.TimedOut) ? "onTimeout" : "next",
        "timer" => "next",
        "loop" => segment.Any(entry => entry.Kind == ProcessHistoryKind.LoopExhausted) ? "onExhausted" : "body",
        "subprocess" => SubprocessLabel(step, segment),
        _ => null,
    };

    private static string? EffectLabel(JsonObject step, List<ProcessHistoryEntry> segment)
    {
        var isPoll = step["kind"]!.GetValue<string>() == "poll";
        string? label = null;
        for (var index = 0; index < segment.Count; index++)
        {
            var entry = segment[index];
            var following = index + 1 < segment.Count ? segment[index + 1].Kind : (ProcessHistoryKind?)null;
            switch (entry.Kind)
            {
                case ProcessHistoryKind.EffectCompleted:
                    label = "next";
                    break;
                case ProcessHistoryKind.Decided when isPoll && entry.Code is not null:
                    label = "onError";
                    break;
                case ProcessHistoryKind.Decided when isPoll:
                    label = Data(entry)["outcome"] is JsonObject outcome && Ends(step, outcome) ? "next" : null;
                    break;
                case ProcessHistoryKind.PollExpired:
                    label = "onExpired";
                    break;
                case ProcessHistoryKind.EffectFailed when following is ProcessHistoryKind.RetryScheduled:
                case ProcessHistoryKind.EffectFailed when following is ProcessHistoryKind.EffectRequested && entry.Code == "process.ai.output_invalid":
                    break;
                case ProcessHistoryKind.EffectFailed:
                    label = FailureLabel(step, entry);
                    break;
                default:
                    break;
            }
        }

        return label;
    }

    private static bool Ends(JsonObject poll, JsonObject outcome)
    {
        var value = outcome[poll["field"]!.GetValue<string>()]!.ToJsonString().Trim('"');
        return poll["until"]!.AsArray().Any(item => item!.GetValue<string>() == value);
    }

    private static string FailureLabel(JsonObject step, ProcessHistoryEntry entry)
    {
        if (entry.Code == "process.ai.output_invalid")
        {
            return "onInvalid";
        }

        var kind = entry.DataJson is { } text ? JsonNode.Parse(text)?["kind"]?.GetValue<string>() : null;
        var specific = kind switch
        {
            "NotAvailable" => "onNotAvailable",
            "NotEntitled" => "onNotEntitled",
            "Unknown" => "onUnknown",
            _ => null,
        };
        return specific is not null && step[specific] is not null ? specific : "onError";
    }

    private static string DecideLabel(JsonObject step, List<ProcessHistoryEntry> segment)
    {
        var decided = segment.Last(entry => entry.Kind == ProcessHistoryKind.Decided);
        if (decided.Code is not null)
        {
            return "onError";
        }

        var data = Data(decided);
        if (!data["decided"]!.GetValue<bool>())
        {
            return "onNoDecision";
        }

        if (step["route"] is not { } route)
        {
            return "next";
        }

        var key = data["outcome"]![route.GetValue<string>()]!.ToJsonString().Trim('"');
        return step["on"]![key] is null ? "onError" : "on:" + key;
    }

    private static string SubprocessLabel(JsonObject step, List<ProcessHistoryEntry> segment)
    {
        if (segment.Any(entry => entry.Kind == ProcessHistoryKind.ChildFailed))
        {
            return "onError";
        }

        if (step["on"] is not JsonObject on)
        {
            return "next";
        }

        var result = Data(segment.Last(entry => entry.Kind == ProcessHistoryKind.ChildCompleted))["result"]!.GetValue<string>();
        return on[result] is null ? "onError" : "on:" + result;
    }

    private static JsonObject Data(ProcessHistoryEntry entry) => JsonNode.Parse(entry.DataJson!)!.AsObject();
}
