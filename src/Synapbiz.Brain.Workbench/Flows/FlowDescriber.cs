using System.Globalization;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>A stage of a flow with the words a person reads.</summary>
/// <param name="Id">The stage id.</param>
/// <param name="Label">The label in the asked language, else English, else a readable form of the id.</param>
public sealed record FlowStageView(string Id, string Label);

/// <summary>One way out of a step, in words.</summary>
/// <param name="Label">The label of the route (<c>next</c>, <c>onTimeout</c>, <c>on:approved</c>, <c>interrupt</c> and so on).</param>
/// <param name="Phrase">The whole phrase: the condition, a colon and where it goes, for example <c>If no answer arrives 2 hours before `visit_at`: go to Call the patient</c>.</param>
/// <param name="Target">The id of the step it leads to.</param>
/// <param name="TargetLabel">The label of that step.</param>
public sealed record FlowRouteView(string Label, string Phrase, string Target, string TargetLabel);

/// <summary>What a reviewer must see about an <c>ai</c> step: what is asked of whom, what goes in, what must come out, and where the flow goes when it does not work.</summary>
public sealed record FlowAiView
{
    /// <summary>The model the step is pinned to.</summary>
    public required string Model { get; init; }

    /// <summary>The id of the prompt the step is pinned to.</summary>
    public required string PromptId { get; init; }

    /// <summary>The version of the prompt the step is pinned to.</summary>
    public required long PromptVersion { get; init; }

    /// <summary>How many answers the step may ask for when the answer does not fit the output schema.</summary>
    public required int MaxAttempts { get; init; }

    /// <summary>The variables that go in.</summary>
    public required IReadOnlyList<string> InputNames { get; init; }

    /// <summary>The input schema as canonical JSON, or null when the step declares none.</summary>
    public string? InputSchemaJson { get; init; }

    /// <summary>The output schema as canonical JSON: what must come out. Null when the step declares none.</summary>
    public string? OutputSchemaJson { get; init; }

    /// <summary>The variables the answer fills.</summary>
    public required IReadOnlyList<string> OutputNames { get; init; }

    /// <summary>Where the flow goes when the answer does not fit the output schema, after every attempt; null when the document has no such route.</summary>
    public FlowRouteView? OnInvalid { get; init; }

    /// <summary>Where the flow goes when the port fails; null when the document has no such route.</summary>
    public FlowRouteView? OnError { get; init; }

    /// <summary>Where the flow goes when the port is not available; null when the document has no such route.</summary>
    public FlowRouteView? OnNotAvailable { get; init; }

    /// <summary>Where the flow goes when the plan does not include the port; null when the document has no such route.</summary>
    public FlowRouteView? OnNotEntitled { get; init; }

    /// <summary>The id of the rule that judges the answer when the next step is a <c>decide</c>; null otherwise.</summary>
    public string? JudgingRule { get; init; }

    /// <summary>Sentences for a reviewer: the attempts, the pinned model and prompt, the rule that judges the answer and the flow that goes on without the AI.</summary>
    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>A step of a flow in words.</summary>
public sealed record FlowStepView
{
    /// <summary>The id of the step.</summary>
    public required string Id { get; init; }

    /// <summary>The label of the step.</summary>
    public required string Label { get; init; }

    /// <summary>The kind of step.</summary>
    public required OutlineStepKind Kind { get; init; }

    /// <summary>The kind in words.</summary>
    public required string KindName { get; init; }

    /// <summary>The id of the stage, or null.</summary>
    public string? Stage { get; init; }

    /// <summary>The label of the stage, or null.</summary>
    public string? StageLabel { get; init; }

    /// <summary>One or a few sentences that say what the step does.</summary>
    public required string Sentence { get; init; }

    /// <summary>The ways out of the step, each in words.</summary>
    public required IReadOnlyList<FlowRouteView> Routes { get; init; }

    /// <summary>What a reviewer must see about an <c>ai</c> step; null on the other kinds.</summary>
    public FlowAiView? Ai { get; init; }
}

/// <summary>A flow described step by step in the reader's language.</summary>
/// <param name="Id">The id of the document.</param>
/// <param name="Version">The version of the document.</param>
/// <param name="Title">The title in the asked language, else English, else a readable form of the id.</param>
/// <param name="StartStep">The id of the first step.</param>
/// <param name="Stages">The stages, in document order.</param>
/// <param name="Steps">The steps, in document order.</param>
public sealed record FlowView(
    string Id,
    long Version,
    string Title,
    string StartStep,
    IReadOnlyList<FlowStageView> Stages,
    IReadOnlyList<FlowStepView> Steps);

/// <summary>Describes a flow, step by step, in plain words. The sentences come from templates per kind and per fact; labels come from <see cref="ProcessDisplay"/>.</summary>
public static class FlowDescriber
{
    private static readonly string[] FallbackLabels = ["onInvalid", "onError", "onNotAvailable", "onNotEntitled"];

    /// <summary>Describes a flow.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="locale">The language of the sentences.</param>
    public static FlowView Describe(BrainProcess process, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(process);
        var outline = ProcessOutline.From(process);
        var labels = new FlowLabels(process, locale);
        return new FlowView(
            outline.Id,
            outline.Version,
            labels.Title(),
            outline.Start,
            [.. outline.Stages.Select(stage => new FlowStageView(stage, labels.Stage(stage)))],
            [.. outline.Steps.Select(step => DescribeStep(outline, step, labels, locale))]);
    }

    /// <summary>What a trigger of the document does, in words, for example <c>Starts when `case.opened` arrives.</c>.</summary>
    /// <param name="trigger">The trigger declaration.</param>
    /// <param name="locale">The language.</param>
    public static string DescribeTrigger(ProcessTriggerDeclaration trigger, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger switch
        {
            EventTriggerDeclaration declared => FlowTemplates.Text(
                locale,
                "trigger.event.key",
                ("event", declared.Event),
                ("key", declared.BusinessKeyMember)),
            ScheduleTriggerDeclaration schedule => Schedule(schedule, locale),
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Unknown trigger kind."),
        };
    }

    /// <summary>The kind of a step in words.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="locale">The language.</param>
    public static string KindName(OutlineStepKind kind, WorkbenchLocale locale) =>
        FlowTemplates.Text(locale, "kind." + KindKey(kind));

    internal static string KindKey(OutlineStepKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];

    /// <summary>The whole phrase of one route of a step.</summary>
    internal static FlowRouteView RouteView(OutlineStep step, OutlineRoute route, FlowLabels labels, WorkbenchLocale locale)
    {
        var target = labels.Step(route.Target);
        var parameters = new List<(string, string)> { ("target", target) };
        string baseName;
        if (route.Label.StartsWith("on:", StringComparison.Ordinal))
        {
            baseName = "on";
            var value = route.Label[3..];
            parameters.Add(("field", step.RouteField ?? string.Empty));
            parameters.Add(("value", step.Kind == OutlineStepKind.HumanTask ? labels.Outcome(value) : WorkbenchText.Humanize(value)));
        }
        else
        {
            baseName = route.Label;
            if (route.Label == "onTimeout" && step.Timeout is { } timeout)
            {
                parameters.Add(("when", FlowText.When(locale, timeout)));
            }

            if (route.Label == "interrupt")
            {
                parameters.Add(("event", step.Interrupt?.Event ?? string.Empty));
            }
        }

        var specific = "route." + baseName + "." + KindKey(step.Kind);
        var key = FlowTemplates.Has(locale, specific) ? specific : "route." + baseName;
        return new FlowRouteView(route.Label, FlowTemplates.Text(locale, key, [.. parameters]), route.Target, target);
    }

    private static FlowStepView DescribeStep(ProcessOutline outline, OutlineStep step, FlowLabels labels, WorkbenchLocale locale)
    {
        var routes = step.Routes.Select(route => RouteView(step, route, labels, locale)).ToList();
        return new FlowStepView
        {
            Id = step.Id,
            Label = labels.Step(step.Id),
            Kind = step.Kind,
            KindName = KindName(step.Kind, locale),
            Stage = step.Stage,
            StageLabel = step.Stage is null ? null : labels.Stage(step.Stage),
            Sentence = Sentence(step, labels, locale, step.Kind == OutlineStepKind.Ai ? JudgingRule(outline, step) : null),
            Routes = routes,
            Ai = step.Kind == OutlineStepKind.Ai ? DescribeAi(outline, step, routes, locale) : null,
        };
    }

    private static string Sentence(OutlineStep step, FlowLabels labels, WorkbenchLocale locale, string? judging)
    {
        var parts = new List<string>();
        switch (step.Kind)
        {
            case OutlineStepKind.Read:
                parts.Add(FlowTemplates.Text(locale, "step.read", ("port", step.Port!)));
                Effect(parts, step, locale);
                break;
            case OutlineStepKind.Action:
                parts.Add(FlowTemplates.Text(locale, "step.action", ("port", step.Port!)));
                Action(parts, step, labels, locale);
                Effect(parts, step, locale);
                break;
            case OutlineStepKind.Ai:
                parts.Add(FlowTemplates.Text(
                    locale,
                    "step.ai",
                    ("model", step.Model!),
                    ("prompt", step.PromptId!),
                    ("version", FlowText.Number(step.PromptVersion ?? 0)),
                    ("port", step.Port!)));
                Effect(parts, step, locale);
                if (judging is not null)
                {
                    parts.Add(FlowTemplates.Text(locale, "ai.judged", ("rule", judging)));
                }

                break;
            case OutlineStepKind.Poll:
                Poll(parts, step, locale);
                Effect(parts, step, locale);
                break;
            case OutlineStepKind.Decide:
                Decide(parts, step, locale);
                break;
            case OutlineStepKind.HumanTask:
                Task(parts, step, labels, locale);
                break;
            case OutlineStepKind.Wait:
                parts.Add(FlowTemplates.Text(locale, "step.wait", ("event", step.Event!)));
                if (step.Correlation is { } correlation)
                {
                    parts.Add(FlowTemplates.Text(locale, "step.wait.correlation", ("variable", correlation)));
                }

                Keeps(parts, step, locale);
                break;
            case OutlineStepKind.Timer:
                parts.Add(Timer(step.Timer!, locale));
                break;
            case OutlineStepKind.Loop:
                parts.Add(Loop(step.Loop!, locale));
                break;
            case OutlineStepKind.Subprocess:
                parts.Add(FlowTemplates.Text(locale, "step.subprocess", ("process", step.Process!)));
                Sends(parts, step, locale);
                Keeps(parts, step, locale);
                break;
            case OutlineStepKind.End:
                parts.Add(FlowTemplates.Text(locale, "step.end", ("result", labels.Result(step.Result!))));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step.Kind, "Unknown step kind.");
        }

        return string.Join(' ', parts);
    }

    private static void Effect(List<string> parts, OutlineStep step, WorkbenchLocale locale)
    {
        Sends(parts, step, locale);
        Keeps(parts, step, locale);
        if (step.Retry is { MaxAttempts: > 1 } retry)
        {
            parts.Add(FlowTemplates.Text(locale, "step.retry", ("count", FlowText.Number(retry.MaxAttempts))));
        }
    }

    private static void Sends(List<string> parts, OutlineStep step, WorkbenchLocale locale)
    {
        if (step.InputNames.Count > 0)
        {
            parts.Add(FlowTemplates.Text(locale, "step.sends", ("names", FlowText.Codes(locale, step.InputNames))));
        }
    }

    private static void Keeps(List<string> parts, OutlineStep step, WorkbenchLocale locale)
    {
        if (step.OutputNames.Count > 0)
        {
            parts.Add(FlowTemplates.Text(locale, "step.keeps", ("names", FlowText.Codes(locale, step.OutputNames))));
        }
    }

    private static void Action(List<string> parts, OutlineStep step, FlowLabels labels, WorkbenchLocale locale)
    {
        if (step.IsContact == true)
        {
            parts.Add(FlowTemplates.Text(locale, "step.action.contact"));
        }

        if (step.Delivery == ProcessDelivery.AtMostOnce)
        {
            parts.Add(FlowTemplates.Text(locale, "step.action.atMostOnce"));
        }

        if (step.Window is { } window)
        {
            parts.Add(FlowTemplates.Text(locale, "step.action.window", ("window", labels.Window(window))));
        }

        if (step.Interrupt is { } interrupt)
        {
            parts.Add(FlowTemplates.Text(locale, "step.action.interrupt", ("event", interrupt.Event)));
        }
    }

    private static void Poll(List<string> parts, OutlineStep step, WorkbenchLocale locale)
    {
        var poll = step.Poll!;
        parts.Add(FlowTemplates.Text(
            locale,
            "step.poll",
            ("port", step.Port!),
            ("field", poll.Field),
            ("values", FlowText.Codes(locale, poll.Until, "join.or"))));
        var timing = FlowTemplates.Text(
            locale,
            poll.Backoff == "exponential" ? "step.poll.exponential" : "step.poll.fixed",
            ("every", FlowText.Duration(locale, poll.Every)),
            ("total", FlowText.Duration(locale, poll.MaxDuration)));
        if (poll.Backoff == "exponential" && poll.Max is { } cap)
        {
            timing += FlowTemplates.Text(locale, "step.poll.cap", ("cap", FlowText.Duration(locale, cap)));
        }

        parts.Add(timing);
    }

    private static void Decide(List<string> parts, OutlineStep step, WorkbenchLocale locale)
    {
        parts.Add(step.InputNames.Count == 0
            ? FlowTemplates.Text(locale, "step.decide.nofacts", ("rule", step.Rule!))
            : FlowTemplates.Text(locale, "step.decide", ("rule", step.Rule!), ("facts", FlowText.Codes(locale, step.InputNames))));
        if (step.CollectInto is { } collect)
        {
            parts.Add(FlowTemplates.Text(locale, "step.decide.collect", ("variable", collect)));
        }

        if (step.RouteField is { } field)
        {
            parts.Add(FlowTemplates.Text(locale, "step.decide.route", ("field", field)));
        }

        var written = step.Outputs.Select(binding => binding.Variable).ToList();
        if (written.Count > 0)
        {
            parts.Add(FlowTemplates.Text(locale, "step.keeps", ("names", FlowText.Codes(locale, written))));
        }
    }

    private static void Task(List<string> parts, OutlineStep step, FlowLabels labels, WorkbenchLocale locale)
    {
        parts.Add(FlowTemplates.Text(
            locale,
            "step.task",
            ("queue", labels.Queue(step.Queue!)),
            ("outcomes", FlowText.Join(locale, [.. step.Outcomes!.Select(labels.Outcome)], "join.or"))));
        if (step.Show is { Count: > 0 } show)
        {
            parts.Add(FlowTemplates.Text(locale, "step.task.show", ("names", FlowText.Codes(locale, show))));
        }

        Keeps(parts, step, locale);
    }

    private static string Timer(OutlineTimer timer, WorkbenchLocale locale)
    {
        if (timer.Variable is { } variable)
        {
            var when = FlowText.When(locale, new OutlineTimeout(null, variable, timer.Offset));
            return FlowTemplates.Text(locale, "step.timer.until", ("when", when));
        }

        return FlowTemplates.Text(locale, "step.timer.after", ("duration", FlowText.Duration(locale, timer.After ?? TimeSpan.Zero)));
    }

    private static string Loop(OutlineLoop loop, WorkbenchLocale locale) =>
        loop.ForEach is { } list
            ? FlowTemplates.Text(locale, "step.loop.each", ("list", list), ("count", FlowText.Number(loop.MaxIterations)), ("item", loop.Item ?? string.Empty))
            : FlowTemplates.Text(locale, "step.loop", ("count", FlowText.Number(loop.MaxIterations)));

    // The rule that judges the answer of an ai step: the rule of the decide that the step's `next` leads to.
    private static string? JudgingRule(ProcessOutline outline, OutlineStep step)
    {
        var next = step.Routes.FirstOrDefault(route => route.Label == "next");
        if (next is null)
        {
            return null;
        }

        return outline.Step(next.Target) is { Kind: OutlineStepKind.Decide } decide ? decide.Rule : null;
    }

    private static FlowAiView DescribeAi(ProcessOutline outline, OutlineStep step, List<FlowRouteView> routes, WorkbenchLocale locale)
    {
        FlowRouteView? Route(string label) => routes.FirstOrDefault(route => route.Label == label);
        var judging = JudgingRule(outline, step);
        var maxAttempts = step.MaxAttempts ?? 1;
        var notes = new List<string>
        {
            FlowTemplates.Text(locale, "ai.pinned", ("model", step.Model!), ("prompt", step.PromptId!), ("version", FlowText.Number(step.PromptVersion ?? 0))),
            maxAttempts == 1
                ? FlowTemplates.Text(locale, "ai.attempts.one")
                : FlowTemplates.Text(locale, "ai.attempts.many", ("count", FlowText.Number(maxAttempts))),
        };
        if (judging is not null)
        {
            notes.Add(FlowTemplates.Text(locale, "ai.judged", ("rule", judging)));
        }

        var fallbacks = FallbackLabels
            .Select(Route).OfType<FlowRouteView>().Select(route => route.TargetLabel).Distinct(StringComparer.Ordinal).ToList();
        if (fallbacks.Count > 0)
        {
            notes.Add(FlowTemplates.Text(locale, "ai.fallback", ("targets", FlowText.Join(locale, fallbacks))));
        }

        return new FlowAiView
        {
            Model = step.Model!,
            PromptId = step.PromptId!,
            PromptVersion = step.PromptVersion ?? 0,
            MaxAttempts = maxAttempts,
            InputNames = step.InputNames,
            InputSchemaJson = step.InputSchemaJson,
            OutputSchemaJson = step.OutputSchemaJson,
            OutputNames = step.OutputNames,
            OnInvalid = Route("onInvalid"),
            OnError = Route("onError"),
            OnNotAvailable = Route("onNotAvailable"),
            OnNotEntitled = Route("onNotEntitled"),
            JudgingRule = judging,
            Notes = notes,
        };
    }

    private static string Schedule(ScheduleTriggerDeclaration schedule, WorkbenchLocale locale)
    {
        var zone = schedule.Zone ?? FlowTemplates.Text(locale, "trigger.zone.activation");
        var fields = schedule.Cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 5
            && int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            && fields[2] == "*" && fields[3] == "*")
        {
            var time = hour.ToString("00", CultureInfo.InvariantCulture) + ":" + minute.ToString("00", CultureInfo.InvariantCulture);
            if (fields[4] == "*")
            {
                return FlowTemplates.Text(locale, "trigger.schedule.daily", ("time", time), ("zone", zone));
            }

            if (fields[4] == "1-5")
            {
                return FlowTemplates.Text(locale, "trigger.schedule.weekdays", ("time", time), ("zone", zone));
            }

            if (Days(fields[4]) is { Count: > 0 } days)
            {
                var names = days.Select(day => FlowTemplates.Text(locale, "day." + FlowText.Number(day))).ToList();
                return FlowTemplates.Text(locale, "trigger.schedule.days", ("days", FlowText.Join(locale, names)), ("time", time), ("zone", zone));
            }
        }

        return FlowTemplates.Text(locale, "trigger.schedule.cron", ("cron", schedule.Cron), ("zone", zone));
    }

    // The weekdays a day-of-week field lists, as 0 (Sunday) to 6: single days and ranges separated by commas; null for anything else.
    private static List<int>? Days(string field)
    {
        var days = new SortedSet<int>();
        foreach (var part in field.Split(','))
        {
            var bounds = part.Split('-');
            if (bounds.Length is < 1 or > 2
                || !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first)
                || !int.TryParse(bounds[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var last)
                || first > last || last > 7)
            {
                return null;
            }

            for (var day = first; day <= last; day++)
            {
                days.Add(day % 7);
            }
        }

        return [.. days];
    }
}
