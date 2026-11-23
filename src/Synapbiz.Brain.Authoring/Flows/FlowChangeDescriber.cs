using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>One change in plain language.</summary>
/// <param name="Kind">The kind of change.</param>
/// <param name="Subject">The id the change is about.</param>
/// <param name="Text">One sentence for a person; it names things by their labels.</param>
public sealed record FlowChangeItem(FlowChangeKind Kind, string Subject, string Text);

/// <summary>What happened to one test, in plain language.</summary>
/// <param name="Scenario">The name of the test.</param>
/// <param name="Kind">How it behaves now.</param>
/// <param name="Text">One sentence for a person.</param>
public sealed record FlowBehaviourItem(string Scenario, FlowBehaviourKind Kind, string Text);

/// <summary>What a draft changes in a process, in words, with the effect on every test of the earlier document.</summary>
/// <param name="Summary">One or two sentences with the counts.</param>
/// <param name="Items">One sentence per change, in a fixed order.</param>
/// <param name="Behaviour">One sentence per test of the earlier document that now ends differently, goes another way or no longer applies.</param>
/// <param name="TestsReplayed">How many tests of the earlier document were replayed.</param>
/// <param name="TestsUnchanged">How many of them behave as before.</param>
/// <param name="HasChanges">True when anything differs.</param>
public sealed record FlowChangeView(
    string Summary,
    IReadOnlyList<FlowChangeItem> Items,
    IReadOnlyList<FlowBehaviourItem> Behaviour,
    int TestsReplayed,
    int TestsUnchanged,
    bool HasChanges);

/// <summary>Turns the difference between two flow documents into sentences, in English and Spanish, from deterministic templates and the labels of <c>meta.display</c>.</summary>
public static class FlowChangeDescriber
{
    /// <summary>Compares the documents, replays the tests of the earlier one on the later one and describes everything.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <param name="locale">The language of the sentences.</param>
    public static FlowChangeView Describe(BrainProcess before, BrainProcess after, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var diff = FlowDiffer.Diff(before, after);
        return Describe(before, after, diff, FlowBehaviour.Compare(before, after, diff), locale);
    }

    /// <summary>Describes a difference and a behaviour comparison that were already computed.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <param name="diff">The difference.</param>
    /// <param name="behaviour">The replay of the tests.</param>
    /// <param name="locale">The language of the sentences.</param>
    public static FlowChangeView Describe(BrainProcess before, BrainProcess after, FlowDiff diff, IReadOnlyList<FlowBehaviourChange> behaviour, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(behaviour);
        var writer = new Writer(before, after, locale);
        var items = diff.Changes.Select(change => new FlowChangeItem(change.Kind, change.Subject, writer.Sentence(change))).ToList();
        var different = behaviour.Where(change => change.Kind != FlowBehaviourKind.Same).ToList();
        var lines = different.Select(change => new FlowBehaviourItem(change.Scenario, change.Kind, writer.Sentence(change))).ToList();
        return new FlowChangeView(
            Summary(diff, different, behaviour.Count, locale),
            items,
            lines,
            behaviour.Count,
            behaviour.Count - different.Count,
            diff.HasChanges);
    }

    /// <summary>Describes a process that has no earlier version: one sentence that says what it is made of.</summary>
    /// <param name="process">The new document.</param>
    /// <param name="locale">The language.</param>
    public static string DescribeAdded(BrainProcess process, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(process);
        var outline = ProcessOutline.From(process);
        return AuthoringText.Text(
            locale,
            "change.new",
            ("title", Quote(FlowWords.For(process, locale).Title())),
            ("steps", AuthoringText.Number(outline.Steps.Count)),
            ("tests", AuthoringText.Number(outline.Scenarios.Count)));
    }

    internal static string Quote(string text) => "“" + text + "”";

    private static string Summary(FlowDiff diff, List<FlowBehaviourChange> different, int replayed, WorkbenchLocale locale)
    {
        if (!diff.HasChanges)
        {
            return AuthoringText.Text(locale, "change.summary.none");
        }

        var parts = new List<string>();
        Count(parts, locale, "steps.added", diff.Changes.Count(change => change.Kind == FlowChangeKind.StepAdded));
        Count(parts, locale, "steps.removed", diff.Changes.Count(change => change.Kind == FlowChangeKind.StepRemoved));
        Count(parts, locale, "steps.renamed", diff.Changes.Count(change => change.Kind == FlowChangeKind.StepRenamed));
        Count(parts, locale, "routes", diff.Changes.Count(change => change.Kind is FlowChangeKind.RouteAdded or FlowChangeKind.RouteChanged or FlowChangeKind.RouteRemoved or FlowChangeKind.StartChanged));
        Count(parts, locale, "settings", diff.Changes.Count(change => change.Kind is FlowChangeKind.StepChanged or FlowChangeKind.WindowAdded or FlowChangeKind.WindowChanged or FlowChangeKind.WindowRemoved
            or FlowChangeKind.TriggerAdded or FlowChangeKind.TriggerChanged or FlowChangeKind.TriggerRemoved or FlowChangeKind.VariableAdded or FlowChangeKind.VariableChanged or FlowChangeKind.VariableRemoved));
        Count(parts, locale, "tests", diff.Changes.Count(change => change.Kind is FlowChangeKind.ScenarioAdded or FlowChangeKind.ScenarioChanged or FlowChangeKind.ScenarioRemoved));
        Count(parts, locale, "labels", diff.Changes.Count(change => change.Kind == FlowChangeKind.LabelChanged));
        var head = AuthoringText.Text(locale, "change.summary.some", ("n", AuthoringText.Number(diff.Changes.Count)), ("parts", string.Join("; ", parts)));
        var tail = AuthoringText.Text(locale, "change.summary.notests");
        if (replayed > 0)
        {
            tail = different.Count == 0
                ? AuthoringText.Text(locale, "change.summary.allsame", ("n", AuthoringText.Number(replayed)))
                : AuthoringText.Text(locale, "change.summary.somedifferent", ("k", AuthoringText.Number(different.Count)), ("n", AuthoringText.Number(replayed)));
        }

        return head + " " + tail;
    }

    private static void Count(List<string> parts, WorkbenchLocale locale, string key, int count)
    {
        if (count > 0)
        {
            parts.Add(AuthoringText.Text(locale, "change.count." + key, ("n", AuthoringText.Number(count))));
        }
    }

    private sealed class Writer(BrainProcess before, BrainProcess after, WorkbenchLocale locale)
    {
        private readonly FlowWords wordsBefore = FlowWords.For(before, locale);
        private readonly FlowWords wordsAfter = FlowWords.For(after, locale);
        private readonly ProcessOutline outlineBefore = ProcessOutline.From(before);
        private readonly ProcessOutline outlineAfter = ProcessOutline.From(after);

        internal string Sentence(FlowChange change) => change.Kind switch
        {
            FlowChangeKind.StepAdded => T("change.step.added", ("step", Step(change.Subject, true)), ("kind", KindName((OutlineStepKind)change.After!))),
            FlowChangeKind.StepRemoved => T("change.step.removed", ("step", Step(change.Subject, false)), ("kind", KindName((OutlineStepKind)change.Before!))),
            FlowChangeKind.StepRenamed => T("change.step.renamed", ("old", Step(change.Subject, false)), ("new", Step((string)change.After!, true))),
            FlowChangeKind.StepChanged => StepChanged(change),
            FlowChangeKind.RouteChanged => T("change.route.changed", ("step", Step(change.Subject, true)), ("route", Route(change.Subject, change.Route!)), ("target", Step((string)change.After!, true)), ("old", Step((string)change.Before!, false))),
            FlowChangeKind.RouteAdded => T("change.route.added", ("step", Step(change.Subject, true)), ("route", Route(change.Subject, change.Route!)), ("target", Step((string)change.After!, true))),
            FlowChangeKind.RouteRemoved => T("change.route.removed", ("step", Step(change.Subject, true)), ("route", Route(change.Subject, change.Route!)), ("old", Step((string)change.Before!, false))),
            FlowChangeKind.StartChanged => T("change.start", ("new", Step(change.Subject, true)), ("old", Step((string)change.Before!, false))),
            FlowChangeKind.WindowAdded => T("change.window.added", ("window", wordsAfter.Window(change.Subject)), ("what", WindowWords((OutlineWindow)change.After!))),
            FlowChangeKind.WindowRemoved => T("change.window.removed", ("window", wordsBefore.Window(change.Subject))),
            FlowChangeKind.WindowChanged => T("change.window.changed", ("window", wordsAfter.Window(change.Subject)), ("old", WindowWords((OutlineWindow)change.Before!)), ("new", WindowWords((OutlineWindow)change.After!))),
            FlowChangeKind.TriggerAdded => T("change.trigger.added", ("what", TriggerWords((ProcessTriggerDeclaration)change.After!))),
            FlowChangeKind.TriggerRemoved => T("change.trigger.removed", ("what", TriggerWords((ProcessTriggerDeclaration)change.Before!))),
            FlowChangeKind.TriggerChanged => T("change.trigger.changed", ("old", TriggerWords((ProcessTriggerDeclaration)change.Before!)), ("new", TriggerWords((ProcessTriggerDeclaration)change.After!))),
            FlowChangeKind.VariableAdded => T("change.variable.added", ("variable", Code(change.Subject)), ("what", VariableWords((OutlineVariable)change.After!))),
            FlowChangeKind.VariableRemoved => T("change.variable.removed", ("variable", Code(change.Subject))),
            FlowChangeKind.VariableChanged => T("change.variable.changed", ("variable", Code(change.Subject)), ("old", VariableWords((OutlineVariable)change.Before!)), ("new", VariableWords((OutlineVariable)change.After!))),
            FlowChangeKind.ScenarioAdded => T("change.test.added", ("name", Quote(change.Subject))),
            FlowChangeKind.ScenarioRemoved => T("change.test.removed", ("name", Quote(change.Subject))),
            FlowChangeKind.ScenarioChanged => T("change.test.changed", ("name", Quote(change.Subject))),
            FlowChangeKind.LabelChanged => Label(change),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change.Kind, "Unknown kind of change."),
        };

        internal string Sentence(FlowBehaviourChange change) => change.Kind switch
        {
            FlowBehaviourKind.NoLongerApplies => T("change.behaviour.gone", ("name", Quote(change.Scenario))),
            FlowBehaviourKind.DifferentPath => T("change.behaviour.path", ("name", Quote(change.Scenario)), ("end", Ending(change.After!, true))),
            _ => T("change.behaviour.ending", ("name", Quote(change.Scenario)), ("old", Ending(change.Before!, false)), ("new", Ending(change.After!, true))),
        };

        private string T(string key, params (string Name, string Value)[] values) => AuthoringText.Text(locale, key, values);

        private string Step(string id, bool later) => Quote((later ? wordsAfter : wordsBefore).Step(id));

        private string KindName(OutlineStepKind kind) => T("kind." + char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..]);

        private string Route(string stepId, string label)
        {
            var step = outlineAfter.Step(stepId) ?? outlineBefore.Step(stepId);
            if (label.StartsWith("on:", StringComparison.Ordinal))
            {
                var value = label[3..];
                var shown = step?.Kind == OutlineStepKind.HumanTask ? wordsAfter.Outcome(value) : value;
                return T("route.on", ("value", shown));
            }

            return T("route." + label);
        }

        private string Ending(FlowEnding ending, bool later)
        {
            var words = later ? wordsAfter : wordsBefore;
            return ending.Status switch
            {
                "Completed" => T("end.completed", ("result", Quote(ending.Result is null ? string.Empty : words.Result(ending.Result)))),
                "Waiting" => T("end.waiting", ("step", ending.Step is null ? string.Empty : Quote(words.Step(ending.Step)))),
                "Suspended" => T("end.suspended", ("step", ending.Step is null ? string.Empty : Quote(words.Step(ending.Step)))),
                _ => T("end.cancelled"),
            };
        }

        private string StepChanged(FlowChange change)
        {
            var step = Step(change.Subject, true);
            if (change.Aspect == FlowAspect.Kind)
            {
                return T("change.step.kind", ("step", step), ("old", KindName((OutlineStepKind)change.Before!)), ("new", KindName((OutlineStepKind)change.After!)));
            }

            var aspect = T("aspect." + char.ToLowerInvariant(change.Aspect!.Value.ToString()[0]) + change.Aspect.Value.ToString()[1..]);
            var old = ValueOrNull(change.Aspect.Value, change.Before);
            var now = ValueOrNull(change.Aspect.Value, change.After);
            if (old is not null && now is not null)
            {
                return T("change.step.changed", ("step", step), ("aspect", aspect), ("old", old), ("new", now));
            }

            return now is not null
                ? T("change.step.set", ("step", step), ("aspect", aspect), ("new", now))
                : T("change.step.cleared", ("step", step), ("aspect", aspect), ("old", old!));
        }

        private string Value(FlowAspect aspect, object value) => aspect switch
        {
            FlowAspect.Stage => Quote(wordsAfter.Stage((string)value)),
            FlowAspect.Window => Quote(wordsAfter.Window((string)value)),
            FlowAspect.Queue => Quote(wordsAfter.Queue((string)value)),
            FlowAspect.Result => Quote(wordsAfter.Result((string)value)),
            FlowAspect.Delivery => T(value is ProcessDelivery.AtMostOnce ? "delivery.atMostOnce" : "delivery.atLeastOnce"),
            FlowAspect.Contact => T((bool)value ? "yes" : "no"),
            FlowAspect.Outcomes => AuthoringText.Join(locale, [.. ((IReadOnlyList<string>)value).Select(outcome => Quote(wordsAfter.Outcome(outcome)))]),
            FlowAspect.Show => AuthoringText.Join(locale, [.. ((IReadOnlyList<string>)value).Select(Code)]),
            FlowAspect.Interrupt => Code(((OutlineInterrupt)value).Event),
            FlowAspect.Timeout => When((OutlineTimeout)value),
            FlowAspect.Timer => TimerWords((OutlineTimer)value),
            FlowAspect.Retry => RetryWords((OutlineRetry)value),
            FlowAspect.Loop => LoopWords((OutlineLoop)value),
            FlowAspect.Poll => PollWords((OutlinePoll)value),
            FlowAspect.Ai => AiWords((OutlineStep)value),
            FlowAspect.Inputs or FlowAspect.Facts or FlowAspect.Outputs => Mapping((IReadOnlyList<OutlineBinding>)value),
            _ => Code((string)value),
        };

        private string Mapping(IReadOnlyList<OutlineBinding> bindings) =>
            AuthoringText.Join(locale, [.. bindings.Select(binding => T("mapping.item", ("name", Code(binding.Name)), ("variable", Code(binding.Variable))))]);

        private string? ValueOrNull(FlowAspect aspect, object? value)
        {
            if (aspect == FlowAspect.Contact)
            {
                return Value(aspect, value ?? false);
            }

            return value is null ? null : Value(aspect, value);
        }

        private string Duration(TimeSpan span) => AuthoringText.Duration(locale, span);

        private string When(OutlineTimeout timeout)
        {
            if (timeout.Variable is { } variable)
            {
                var offset = timeout.Offset ?? TimeSpan.Zero;
                if (offset == TimeSpan.Zero)
                {
                    return T("when.at", ("variable", Code(variable)));
                }

                var key = offset < TimeSpan.Zero ? "when.before" : "when.after";
                return T(key, ("duration", Duration(offset)), ("variable", Code(variable)));
            }

            return T("when.within", ("duration", Duration(timeout.Duration ?? TimeSpan.Zero)));
        }

        private string TimerWords(OutlineTimer timer) =>
            timer.Variable is { } variable
                ? When(new OutlineTimeout(null, variable, timer.Offset))
                : T("timer.after", ("duration", Duration(timer.After ?? TimeSpan.Zero)));

        private string RetryWords(OutlineRetry retry) =>
            retry.MaxAttempts <= 1
                ? T("retry.none")
                : T("retry.some", ("n", AuthoringText.Number(retry.MaxAttempts)), ("backoff", T("backoff." + retry.Backoff)), ("initial", Duration(retry.Initial)));

        private string LoopWords(OutlineLoop loop) =>
            loop.ForEach is { } list
                ? T("loop.each", ("list", Code(list)), ("n", AuthoringText.Number(loop.MaxIterations)))
                : T("loop.rounds", ("n", AuthoringText.Number(loop.MaxIterations)));

        private string PollWords(OutlinePoll poll) =>
            T("poll.some", ("every", Duration(poll.Every)), ("total", Duration(poll.MaxDuration)), ("backoff", T("backoff." + poll.Backoff)), ("field", Code(poll.Field)), ("values", AuthoringText.Join(locale, [.. poll.Until.Select(Code)], "join.or")));

        private string AiWords(OutlineStep step) =>
            T("ai.some", ("model", Code(step.Model!)), ("prompt", Code(step.PromptId!)), ("version", AuthoringText.Number(step.PromptVersion ?? 0)), ("n", AuthoringText.Number(step.MaxAttempts ?? 1)));

        private string WindowWords(OutlineWindow window)
        {
            var days = AuthoringText.Join(locale, [.. window.Days.OrderBy(day => ((int)day + 6) % 7).Select(day => T("day." + (int)day))]);
            var zone = window.Zone ?? (window.ZoneVariable is null ? string.Empty : T("window.zoneVariable", ("variable", Code(window.ZoneVariable))));
            return T("window.what", ("days", days), ("from", window.From.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)), ("to", window.To == TimeOnly.MinValue ? "24:00" : window.To.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)), ("zone", zone));
        }

        private string TriggerWords(ProcessTriggerDeclaration trigger) => trigger switch
        {
            EventTriggerDeclaration declared => T("trigger.event", ("event", Code(declared.Event)), ("key", Code(declared.BusinessKeyMember))),
            ScheduleTriggerDeclaration schedule => T("trigger.schedule", ("id", Code(schedule.Id)), ("cron", Code(schedule.Cron)), ("zone", schedule.Zone ?? T("trigger.zone.activation"))),
            _ => trigger.Kind,
        };

        private string VariableWords(OutlineVariable variable) =>
            T(variable.IsInput ? "variable.input" : "variable.plain", ("type", variable.Type));

        private string Label(FlowChange change)
        {
            var language = change.Language is "en" or "es" ? T("language." + change.Language) : change.Language!;
            var id = change.Subject;
            var kind = change.Group == "title" ? "title" : change.Group![..^1];
            var subject = T("label.group." + kind, ("id", Code(id)));
            if (change.Before is string was && change.After is string text)
            {
                return T("change.label.changed", ("subject", subject), ("language", language), ("old", Quote(was)), ("new", Quote(text)));
            }

            return change.After is string added
                ? T("change.label.added", ("subject", subject), ("language", language), ("new", Quote(added)))
                : T("change.label.removed", ("subject", subject), ("language", language), ("old", Quote((string)change.Before!)));
        }

        private static string Code(string id) => "`" + id + "`";
    }
}
