using System.Globalization;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Authoring;

/// <summary>The kinds of change between two flow documents.</summary>
public enum FlowChangeKind
{
    /// <summary>A step the later document has and the earlier does not.</summary>
    StepAdded = 0,

    /// <summary>A step the earlier document has and the later does not.</summary>
    StepRemoved = 1,

    /// <summary>A step that is the same except for its id: <c>Subject</c> is the old id, <c>After</c> the new one.</summary>
    StepRenamed = 2,

    /// <summary>One aspect of a step changed (see <see cref="FlowChange.Aspect"/>).</summary>
    StepChanged = 3,

    /// <summary>A route of a step that now goes to another step.</summary>
    RouteChanged = 4,

    /// <summary>A route a step did not have.</summary>
    RouteAdded = 5,

    /// <summary>A route a step no longer has.</summary>
    RouteRemoved = 6,

    /// <summary>The first step changed.</summary>
    StartChanged = 7,

    /// <summary>A delivery window was declared.</summary>
    WindowAdded = 8,

    /// <summary>A delivery window was dropped.</summary>
    WindowRemoved = 9,

    /// <summary>A delivery window changed days, hours or zone.</summary>
    WindowChanged = 10,

    /// <summary>A trigger was added.</summary>
    TriggerAdded = 11,

    /// <summary>A trigger was removed.</summary>
    TriggerRemoved = 12,

    /// <summary>A trigger changed.</summary>
    TriggerChanged = 13,

    /// <summary>A variable was declared.</summary>
    VariableAdded = 14,

    /// <summary>A variable was dropped.</summary>
    VariableRemoved = 15,

    /// <summary>A variable changed its type or whether it is an input.</summary>
    VariableChanged = 16,

    /// <summary>A test was added.</summary>
    ScenarioAdded = 17,

    /// <summary>A test was removed.</summary>
    ScenarioRemoved = 18,

    /// <summary>A test with the same name now says something else.</summary>
    ScenarioChanged = 19,

    /// <summary>A label (the title, or the name of a stage, step, result, queue, outcome, event or window) changed in a language.</summary>
    LabelChanged = 20,
}

/// <summary>What about a step changed.</summary>
public enum FlowAspect
{
    /// <summary>The stage of the step.</summary>
    Stage = 0,

    /// <summary>The port.</summary>
    Port = 1,

    /// <summary>The delivery guarantee of an action.</summary>
    Delivery = 2,

    /// <summary>Whether an action contacts a person.</summary>
    Contact = 3,

    /// <summary>The delivery window of an action.</summary>
    Window = 4,

    /// <summary>The event that ends the wait for a window.</summary>
    Interrupt = 5,

    /// <summary>The retry policy.</summary>
    Retry = 6,

    /// <summary>The rule.</summary>
    Rule = 7,

    /// <summary>The field of the rule outcome that routes the case.</summary>
    RouteField = 8,

    /// <summary>The variable that collects every matching outcome.</summary>
    CollectInto = 9,

    /// <summary>The task queue.</summary>
    Queue = 10,

    /// <summary>The outcomes a person may choose.</summary>
    Outcomes = 11,

    /// <summary>What the person sees in a task.</summary>
    Show = 12,

    /// <summary>The event a wait waits for.</summary>
    Event = 13,

    /// <summary>The variable an event must correlate on.</summary>
    Correlation = 14,

    /// <summary>The timeout of a wait or a task.</summary>
    Timeout = 15,

    /// <summary>The delay of a timer.</summary>
    Timer = 16,

    /// <summary>The limit of a repetition.</summary>
    Loop = 17,

    /// <summary>The timing of a repeated read.</summary>
    Poll = 18,

    /// <summary>The child process of a subprocess step.</summary>
    Child = 19,

    /// <summary>The result of an end step.</summary>
    Result = 20,

    /// <summary>The pinned model and prompt of an AI step.</summary>
    Ai = 21,

    /// <summary>What the step sends.</summary>
    Inputs = 22,

    /// <summary>What the step gives its rule.</summary>
    Facts = 23,

    /// <summary>What the step keeps.</summary>
    Outputs = 24,

    /// <summary>The kind of the step.</summary>
    Kind = 25,
}

/// <summary>One change between two flow documents, as data. <see cref="Before"/> and <see cref="After"/> hold the values of the outline (a string, a <see cref="TimeSpan"/>, an outline record, a trigger declaration or a list of names); null means absent.</summary>
/// <param name="Kind">What kind of change.</param>
/// <param name="Subject">The id of the step, window, variable, scenario or label the change is about (the id in the later document, except for a removal and for a rename).</param>
public sealed record FlowChange(FlowChangeKind Kind, string Subject)
{
    /// <summary>For a <see cref="FlowChangeKind.StepChanged"/>, what changed.</summary>
    public FlowAspect? Aspect { get; init; }

    /// <summary>For a route change, the label of the route (<c>next</c>, <c>onError</c>, <c>on:signed</c>).</summary>
    public string? Route { get; init; }

    /// <summary>For a label change, the group (<c>title</c>, <c>stages</c>, <c>steps</c>, <c>results</c>, <c>queues</c>, <c>outcomes</c>, <c>events</c> or <c>windows</c>).</summary>
    public string? Group { get; init; }

    /// <summary>For a label change, the language.</summary>
    public string? Language { get; init; }

    /// <summary>The earlier value; null when there was none.</summary>
    public object? Before { get; init; }

    /// <summary>The later value; null when there is none.</summary>
    public object? After { get; init; }
}

/// <summary>What changed between two flow documents, as data, in a fixed order.</summary>
/// <param name="Changes">The changes.</param>
public sealed record FlowDiff(IReadOnlyList<FlowChange> Changes)
{
    /// <summary>True when the documents differ in anything the differ looks at.</summary>
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>Compares two loaded flows: steps added, removed and renamed, routes, timings, windows, triggers, variables, labels and tests.</summary>
public static class FlowDiffer
{
    private static readonly string[] LabelGroups = ["stages", "steps", "results", "queues", "outcomes", "events", "windows"];

    /// <summary>Compares the two documents. A removed step and an added step that are the same in everything but their id and routes are one rename.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    public static FlowDiff Diff(BrainProcess before, BrainProcess after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var old = ProcessOutline.From(before);
        var now = ProcessOutline.From(after);
        var changes = new List<FlowChange>();

        var removed = old.Steps.Where(step => now.Step(step.Id) is null).ToList();
        var added = now.Steps.Where(step => old.Step(step.Id) is null).ToList();
        var renames = Renames(removed, added);
        foreach (var (from, to) in renames)
        {
            changes.Add(new FlowChange(FlowChangeKind.StepRenamed, from) { Before = from, After = to });
        }

        changes.AddRange(added.Where(step => !renames.ContainsValue(step.Id)).Select(step => new FlowChange(FlowChangeKind.StepAdded, step.Id) { After = step.Kind }));
        changes.AddRange(removed.Where(step => !renames.ContainsKey(step.Id)).Select(step => new FlowChange(FlowChangeKind.StepRemoved, step.Id) { Before = step.Kind }));

        foreach (var step in now.Steps)
        {
            var earlierId = renames.FirstOrDefault(pair => pair.Value == step.Id).Key ?? step.Id;
            if (old.Step(earlierId) is { } earlier)
            {
                StepChanges(changes, earlier, step, renames);
            }
        }

        var start = renames.GetValueOrDefault(old.Start, old.Start);
        if (!string.Equals(start, now.Start, StringComparison.Ordinal))
        {
            changes.Add(new FlowChange(FlowChangeKind.StartChanged, now.Start) { Before = old.Start, After = now.Start });
        }

        Windows(changes, old, now);
        Triggers(changes, old, now);
        Variables(changes, old, now);
        Scenarios(changes, old, now);
        Labels(changes, before.Meta, after.Meta);
        return new FlowDiff(changes);
    }

    private static Dictionary<string, string> Renames(List<OutlineStep> removed, List<OutlineStep> added)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var step in removed)
        {
            var signature = Signature(step);
            var sameRemoved = removed.Count(other => Signature(other) == signature);
            var candidates = added.Where(other => Signature(other) == signature && !map.ContainsValue(other.Id)).ToList();
            if (sameRemoved == 1 && candidates.Count == 1)
            {
                map[step.Id] = candidates[0].Id;
            }
        }

        return map;
    }

    private static string Signature(OutlineStep step) =>
        step.Kind + "|" + string.Join(';', Aspects(step).Select(item => item.Aspect + "=" + item.Key));

    private static void StepChanges(List<FlowChange> changes, OutlineStep earlier, OutlineStep step, Dictionary<string, string> renames)
    {
        if (earlier.Kind != step.Kind)
        {
            changes.Add(new FlowChange(FlowChangeKind.StepChanged, step.Id) { Aspect = FlowAspect.Kind, Before = earlier.Kind, After = step.Kind });
        }

        var before = Aspects(earlier).ToDictionary(item => item.Aspect);
        var after = Aspects(step).ToDictionary(item => item.Aspect);
        foreach (var aspect in Enum.GetValues<FlowAspect>().Where(aspect => aspect != FlowAspect.Kind))
        {
            before.TryGetValue(aspect, out var was);
            after.TryGetValue(aspect, out var now);
            if (was.Key != now.Key)
            {
                changes.Add(new FlowChange(FlowChangeKind.StepChanged, step.Id) { Aspect = aspect, Before = was.Value, After = now.Value });
            }
        }

        var oldRoutes = earlier.Routes.ToDictionary(route => route.Label, route => renames.GetValueOrDefault(route.Target, route.Target), StringComparer.Ordinal);
        var rawOld = earlier.Routes.ToDictionary(route => route.Label, route => route.Target, StringComparer.Ordinal);
        var newRoutes = step.Routes.ToDictionary(route => route.Label, route => route.Target, StringComparer.Ordinal);
        foreach (var (label, target) in newRoutes)
        {
            if (!oldRoutes.TryGetValue(label, out var was))
            {
                changes.Add(new FlowChange(FlowChangeKind.RouteAdded, step.Id) { Route = label, After = target });
            }
            else if (!string.Equals(was, target, StringComparison.Ordinal))
            {
                changes.Add(new FlowChange(FlowChangeKind.RouteChanged, step.Id) { Route = label, Before = rawOld[label], After = target });
            }
        }

        changes.AddRange(oldRoutes.Keys.Where(label => !newRoutes.ContainsKey(label))
            .Select(label => new FlowChange(FlowChangeKind.RouteRemoved, step.Id) { Route = label, Before = rawOld[label] }));
    }

    internal static IEnumerable<(FlowAspect Aspect, object? Value, string Key)> Aspects(OutlineStep step)
    {
        if (step.Stage is not null)
        {
            yield return (FlowAspect.Stage, step.Stage, step.Stage);
        }

        if (step.Port is not null)
        {
            yield return (FlowAspect.Port, step.Port, step.Port);
        }

        if (step.Delivery is { } delivery)
        {
            yield return (FlowAspect.Delivery, delivery, delivery.ToString());
        }

        if (step.IsContact == true)
        {
            yield return (FlowAspect.Contact, true, "true");
        }

        if (step.Window is not null)
        {
            yield return (FlowAspect.Window, step.Window, step.Window);
        }

        if (step.Interrupt is { } interrupt)
        {
            yield return (FlowAspect.Interrupt, interrupt, interrupt.Event + "|" + interrupt.Correlation);
        }

        if (step.Retry is { } retry)
        {
            yield return (FlowAspect.Retry, retry, Invariant($"{retry.MaxAttempts}|{retry.Backoff}|{retry.Initial}|{retry.Max}"));
        }

        if (step.Rule is not null)
        {
            yield return (FlowAspect.Rule, step.Rule, step.Rule);
        }

        if (step.RouteField is not null)
        {
            yield return (FlowAspect.RouteField, step.RouteField, step.RouteField);
        }

        if (step.CollectInto is not null)
        {
            yield return (FlowAspect.CollectInto, step.CollectInto, step.CollectInto);
        }

        if (step.Queue is not null)
        {
            yield return (FlowAspect.Queue, step.Queue, step.Queue);
        }

        if (step.Outcomes is { } outcomes)
        {
            yield return (FlowAspect.Outcomes, outcomes, string.Join(',', outcomes.Order(StringComparer.Ordinal)));
        }

        if (step.Show is { } show)
        {
            yield return (FlowAspect.Show, show, string.Join(',', show));
        }

        if (step.Event is not null)
        {
            yield return (FlowAspect.Event, step.Event, step.Event);
        }

        if (step.Correlation is not null)
        {
            yield return (FlowAspect.Correlation, step.Correlation, step.Correlation);
        }

        if (step.Timeout is { } timeout)
        {
            yield return (FlowAspect.Timeout, timeout, Invariant($"{timeout.Duration}|{timeout.Variable}|{timeout.Offset}"));
        }

        if (step.Timer is { } timer)
        {
            yield return (FlowAspect.Timer, timer, Invariant($"{timer.After}|{timer.Variable}|{timer.Offset}"));
        }

        if (step.Loop is { } loop)
        {
            yield return (FlowAspect.Loop, loop, Invariant($"{loop.MaxIterations}|{loop.ForEach}|{loop.Item}"));
        }

        if (step.Poll is { } poll)
        {
            yield return (FlowAspect.Poll, poll, Invariant($"{poll.Field}|{string.Join(',', poll.Until)}|{poll.Every}|{poll.Backoff}|{poll.Max}|{poll.MaxDuration}"));
        }

        if (step.Process is not null)
        {
            yield return (FlowAspect.Child, step.Process, step.Process);
        }

        if (step.Result is not null)
        {
            yield return (FlowAspect.Result, step.Result, step.Result);
        }

        if (step.Model is not null)
        {
            yield return (FlowAspect.Ai, step, Invariant($"{step.Model}|{step.PromptId}|{step.PromptVersion}|{step.MaxAttempts}"));
        }

        if (step.Inputs.Count > 0)
        {
            yield return (FlowAspect.Inputs, step.Inputs, Mapping(step.Inputs));
        }

        if (step.Facts.Count > 0)
        {
            yield return (FlowAspect.Facts, step.Facts, Mapping(step.Facts));
        }

        if (step.Outputs.Count > 0)
        {
            yield return (FlowAspect.Outputs, step.Outputs, Mapping(step.Outputs));
        }
    }

    private static string Mapping(IReadOnlyList<OutlineBinding> bindings) =>
        string.Join(';', bindings.OrderBy(binding => binding.Name, StringComparer.Ordinal).Select(binding => binding.Name + ">" + binding.Variable));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static void Windows(List<FlowChange> changes, ProcessOutline old, ProcessOutline now)
    {
        foreach (var window in now.Windows)
        {
            var was = old.Windows.FirstOrDefault(item => item.Name == window.Name);
            if (was is null)
            {
                changes.Add(new FlowChange(FlowChangeKind.WindowAdded, window.Name) { After = window });
            }
            else if (WindowKey(was) != WindowKey(window))
            {
                changes.Add(new FlowChange(FlowChangeKind.WindowChanged, window.Name) { Before = was, After = window });
            }
        }

        changes.AddRange(old.Windows.Where(window => now.Windows.All(item => item.Name != window.Name))
            .Select(window => new FlowChange(FlowChangeKind.WindowRemoved, window.Name) { Before = window }));
    }

    private static string WindowKey(OutlineWindow window) =>
        Invariant($"{window.Zone}|{window.ZoneVariable}|{string.Join(',', window.Days)}|{window.From}|{window.To}");

    private static string TriggerId(ProcessTriggerDeclaration trigger) => trigger switch
    {
        EventTriggerDeclaration declared => "event:" + declared.Event,
        ScheduleTriggerDeclaration schedule => "schedule:" + schedule.Id,
        _ => trigger.Kind,
    };

    private static string TriggerKey(ProcessTriggerDeclaration trigger) => trigger switch
    {
        EventTriggerDeclaration declared => declared.BusinessKeyMember + "|" + InputMapping(declared.Input),
        ScheduleTriggerDeclaration schedule => Invariant($"{schedule.Cron}|{schedule.Zone}|{schedule.Misfire}|{InputMapping(schedule.Input)}"),
        _ => string.Empty,
    };

    private static string InputMapping(IReadOnlyDictionary<string, string> input) =>
        string.Join(';', input.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ">" + pair.Value));

    private static void Triggers(List<FlowChange> changes, ProcessOutline old, ProcessOutline now)
    {
        foreach (var trigger in now.Triggers)
        {
            var was = old.Triggers.FirstOrDefault(item => TriggerId(item) == TriggerId(trigger));
            if (was is null)
            {
                changes.Add(new FlowChange(FlowChangeKind.TriggerAdded, TriggerId(trigger)) { After = trigger });
            }
            else if (TriggerKey(was) != TriggerKey(trigger))
            {
                changes.Add(new FlowChange(FlowChangeKind.TriggerChanged, TriggerId(trigger)) { Before = was, After = trigger });
            }
        }

        changes.AddRange(old.Triggers.Where(trigger => now.Triggers.All(item => TriggerId(item) != TriggerId(trigger)))
            .Select(trigger => new FlowChange(FlowChangeKind.TriggerRemoved, TriggerId(trigger)) { Before = trigger }));
    }

    private static void Variables(List<FlowChange> changes, ProcessOutline old, ProcessOutline now)
    {
        foreach (var variable in now.Variables)
        {
            var was = old.Variables.FirstOrDefault(item => item.Name == variable.Name);
            if (was is null)
            {
                changes.Add(new FlowChange(FlowChangeKind.VariableAdded, variable.Name) { After = variable });
            }
            else if (was.Type != variable.Type || was.IsInput != variable.IsInput)
            {
                changes.Add(new FlowChange(FlowChangeKind.VariableChanged, variable.Name) { Before = was, After = variable });
            }
        }

        changes.AddRange(old.Variables.Where(variable => now.Variables.All(item => item.Name != variable.Name))
            .Select(variable => new FlowChange(FlowChangeKind.VariableRemoved, variable.Name) { Before = variable }));
    }

    private static void Scenarios(List<FlowChange> changes, ProcessOutline old, ProcessOutline now)
    {
        foreach (var name in now.Scenarios.Distinct(StringComparer.Ordinal))
        {
            if (!old.Scenarios.Contains(name, StringComparer.Ordinal))
            {
                changes.Add(new FlowChange(FlowChangeKind.ScenarioAdded, name));
            }
            else if (!string.Equals(old.ScenarioJson(name), now.ScenarioJson(name), StringComparison.Ordinal))
            {
                changes.Add(new FlowChange(FlowChangeKind.ScenarioChanged, name));
            }
        }

        changes.AddRange(old.Scenarios.Distinct(StringComparer.Ordinal).Where(name => !now.Scenarios.Contains(name, StringComparer.Ordinal))
            .Select(name => new FlowChange(FlowChangeKind.ScenarioRemoved, name)));
    }

    private static void Labels(List<FlowChange> changes, JsonObject before, JsonObject after)
    {
        var old = LabelsOf(before);
        var now = LabelsOf(after);
        foreach (var key in old.Keys.Concat(now.Keys).Distinct().OrderBy(key => key.Language, StringComparer.Ordinal).ThenBy(key => key.Group, StringComparer.Ordinal).ThenBy(key => key.Id, StringComparer.Ordinal))
        {
            old.TryGetValue(key, out var was);
            now.TryGetValue(key, out var text);
            if (!string.Equals(was, text, StringComparison.Ordinal))
            {
                changes.Add(new FlowChange(FlowChangeKind.LabelChanged, key.Id) { Group = key.Group, Language = key.Language, Before = was, After = text });
            }
        }
    }

    private static Dictionary<(string Language, string Group, string Id), string> LabelsOf(JsonObject meta)
    {
        var labels = new Dictionary<(string, string, string), string>();
        if (meta["display"] is not JsonObject display)
        {
            return labels;
        }

        foreach (var (language, node) in display)
        {
            if (node is not JsonObject texts)
            {
                continue;
            }

            if (AuthoringJson.Text(texts["title"]) is { } title)
            {
                labels[(language, "title", string.Empty)] = title;
            }

            foreach (var group in LabelGroups)
            {
                if (texts[group] is JsonObject entries)
                {
                    foreach (var (id, text) in entries)
                    {
                        if (AuthoringJson.Text(text) is { } label)
                        {
                            labels[(language, group, id)] = label;
                        }
                    }
                }
            }
        }

        return labels;
    }
}
