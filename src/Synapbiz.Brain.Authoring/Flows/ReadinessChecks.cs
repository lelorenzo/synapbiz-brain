using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>One thing that stops a draft from being ready, in both languages.</summary>
/// <param name="Code">The code of the check that found it.</param>
/// <param name="Step">The step it is about, or null.</param>
/// <param name="Route">The route it is about, or null.</param>
/// <param name="TextEn">The sentence in English.</param>
/// <param name="TextEs">The sentence in Spanish.</param>
public sealed record ReadinessFinding(string Code, string? Step, string? Route, string TextEn, string TextEs)
{
    /// <summary>The sentence in a language.</summary>
    /// <param name="locale">The language.</param>
    public string Text(WorkbenchLocale locale) => locale == WorkbenchLocale.Es ? TextEs : TextEn;
}

/// <summary>The outcome of one check.</summary>
/// <param name="Code">The code of the check.</param>
/// <param name="Findings">What it found; empty when it passes.</param>
public sealed record ReadinessCheckResult(string Code, IReadOnlyList<ReadinessFinding> Findings)
{
    /// <summary>True when the check found nothing.</summary>
    public bool Passed => Findings.Count == 0;
}

/// <summary>The verdict on a draft.</summary>
/// <param name="Checks">Every check, in order.</param>
public sealed record ReadinessReport(IReadOnlyList<ReadinessCheckResult> Checks)
{
    /// <summary>True when every check passes: the draft may be proposed.</summary>
    public bool Ready => Checks.All(check => check.Passed);

    /// <summary>Every finding of every check.</summary>
    public IReadOnlyList<ReadinessFinding> Findings => [.. Checks.SelectMany(check => check.Findings)];
}

/// <summary>The checks beyond the loader that decide whether a draft is "ready to propose".</summary>
public static class ReadinessChecks
{
    /// <summary>The loader refuses the document.</summary>
    public const string LoaderErrors = "authoring.ready.loader_errors";

    /// <summary>A test of the document does not pass.</summary>
    public const string ScenarioFailing = "authoring.ready.scenario_failing";

    /// <summary>A step or a stage has no name in English or in Spanish.</summary>
    public const string LabelMissing = "authoring.ready.label_missing";

    /// <summary>A route is taken by no test and the draft does not say why.</summary>
    public const string RouteUntested = "authoring.ready.route_untested";

    /// <summary>A step that contacts a person can send twice, goes out outside a window or has no way out when it fails.</summary>
    public const string ContactUnsafe = "authoring.ready.contact_unsafe";

    /// <summary>An AI step has no route that goes on without AI.</summary>
    public const string AiWithoutFallback = "authoring.ready.ai_without_fallback";

    /// <summary>A wait has no timeout and the draft does not say why.</summary>
    public const string WaitWithoutTimeout = "authoring.ready.wait_without_timeout";

    /// <summary>The document names something outside the catalog.</summary>
    public const string OutsideCatalog = "authoring.ready.outside_catalog";

    /// <summary>The codes of the checks, in the order they run.</summary>
    public static IReadOnlyList<string> Codes { get; } =
        [LoaderErrors, ScenarioFailing, LabelMissing, RouteUntested, ContactUnsafe, AiWithoutFallback, WaitWithoutTimeout, OutsideCatalog];

    /// <summary>What a check requires, as one sentence.</summary>
    /// <param name="code">The code of the check.</param>
    /// <param name="locale">The language.</param>
    public static string Requirement(string code, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(code);
        return AuthoringText.Text(locale, "ready.requirement." + code);
    }

    /// <summary>Runs every check.</summary>
    /// <param name="document">The draft as plain JSON; null when it is not JSON.</param>
    /// <param name="errors">The errors of the loader.</param>
    /// <param name="process">The loaded process; null when the loader refused the document.</param>
    /// <param name="report">The tests and coverage of the loaded process; null when it did not load.</param>
    /// <param name="gaps">What the author accepted knowingly.</param>
    /// <param name="catalog">The catalog.</param>
    /// <param name="catalogProblems">What the catalog check found.</param>
    public static ReadinessReport Run(
        JsonObject? document,
        IReadOnlyList<BrainSpecError> errors,
        BrainProcess? process,
        FlowScenarioReport? report,
        IReadOnlyList<KnownGap> gaps,
        AuthoringCatalog catalog,
        IReadOnlyList<CatalogProblem> catalogProblems)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalogProblems);
        var outline = process is null ? null : ProcessOutline.From(process);
        return new ReadinessReport(
        [
            new(LoaderErrors, [.. errors.Select(error => LoaderFinding(error, document))]),
            new(ScenarioFailing, report is null ? [] : [.. report.Scenarios.Where(result => !result.Passed).Select(ScenarioFinding)]),
            new(LabelMissing, process is null ? [] : Labels(process, outline!)),
            new(RouteUntested, report is null ? [] : Untested(process!, outline!, report, gaps)),
            new(ContactUnsafe, outline is null ? [] : Contacts(process!, outline, catalog)),
            new(AiWithoutFallback, outline is null ? [] : AiFallbacks(process!, outline)),
            new(WaitWithoutTimeout, outline is null ? [] : Waits(process!, outline, gaps)),
            new(OutsideCatalog, [.. catalogProblems.Select(problem => CatalogFinding(problem, document))]),
        ]);
    }

    private static ReadinessFinding LoaderFinding(BrainSpecError error, JsonObject? document) =>
        new(LoaderErrors, null, null,
            AuthoringText.Text(WorkbenchLocale.En, "ready.finding.loader", ("where", PathWords.Describe(error.Path, document, WorkbenchLocale.En)), ("message", error.MessageEn)),
            AuthoringText.Text(WorkbenchLocale.Es, "ready.finding.loader", ("where", PathWords.Describe(error.Path, document, WorkbenchLocale.Es)), ("message", error.MessageEs)));

    private static ReadinessFinding ScenarioFinding(FlowScenarioResult result) =>
        Both(ScenarioFailing, null, null, "ready.finding.scenario", locale => [("name", FlowChangeDescriber.Quote(result.Name)), ("reason", result.Failure ?? string.Empty)]);

    private static List<ReadinessFinding> Labels(BrainProcess process, ProcessOutline outline)
    {
        var findings = new List<ReadinessFinding>();
        var meta = process.Meta;
        foreach (var language in new[] { "en", "es" })
        {
            var texts = (meta["display"] as JsonObject)?[language] as JsonObject;
            foreach (var stepId in outline.Steps.Where(step => string.IsNullOrWhiteSpace(AuthoringJson.Text((texts?["steps"] as JsonObject)?[step.Id]))).Select(step => step.Id))
            {
                findings.Add(Both(LabelMissing, stepId, null, "ready.finding.label.step", locale => [("step", FlowChangeDescriber.Quote(FlowWords.For(process, locale).Step(stepId))), ("language", AuthoringText.Text(locale, "language." + language))]));
            }

            foreach (var stage in outline.Stages.Where(stage => string.IsNullOrWhiteSpace(AuthoringJson.Text((texts?["stages"] as JsonObject)?[stage]))))
            {
                findings.Add(Both(LabelMissing, null, null, "ready.finding.label.stage", locale => [("stage", FlowChangeDescriber.Quote(FlowWords.For(process, locale).Stage(stage))), ("language", AuthoringText.Text(locale, "language." + language))]));
            }
        }

        return findings;
    }

    private static List<ReadinessFinding> Untested(BrainProcess process, ProcessOutline outline, FlowScenarioReport report, IReadOnlyList<KnownGap> gaps) =>
        [.. report.Coverage.NeverTaken
            .Where(route => !gaps.Any(gap => gap.Check == KnownGap.RouteUntested && gap.Step == route.Step && gap.Route == route.Label))
            .Select(route => Both(RouteUntested, route.Step, route.Label, "ready.finding.untested", locale => [("step", FlowChangeDescriber.Quote(FlowWords.For(process, locale).Step(route.Step))), ("route", RouteWords(outline, process, route.Step, route.Label, locale)), ("target", FlowChangeDescriber.Quote(FlowWords.For(process, locale).Step(route.Target)))]))];

    internal static string RouteWords(ProcessOutline outline, BrainProcess process, string stepId, string label, WorkbenchLocale locale)
    {
        if (label.StartsWith("on:", StringComparison.Ordinal))
        {
            var value = label[3..];
            var words = FlowWords.For(process, locale);
            return AuthoringText.Text(locale, "route.on", ("value", outline.Step(stepId)?.Kind == OutlineStepKind.HumanTask ? words.Outcome(value) : value));
        }

        return AuthoringText.Text(locale, "route." + label);
    }

    private static List<ReadinessFinding> Contacts(BrainProcess process, ProcessOutline outline, AuthoringCatalog catalog)
    {
        var findings = new List<ReadinessFinding>();
        foreach (var step in outline.Steps.Where(step => step.Kind == OutlineStepKind.Action))
        {
            var contacts = step.IsContact == true || catalog.Ports?.FirstOrDefault(port => port.Name == step.Port)?.ContactsPerson == true;
            if (!contacts)
            {
                continue;
            }

            void Add(string key) => findings.Add(Both(ContactUnsafe, step.Id, null, key, locale => [("step", FlowChangeDescriber.Quote(FlowWords.For(process, locale).Step(step.Id)))]));
            if (step.Delivery != ProcessDelivery.AtMostOnce)
            {
                Add("ready.finding.contact.once");
            }

            if (step.Window is null)
            {
                Add("ready.finding.contact.window");
            }

            if (!step.Routes.Any(route => route.Label == "onError"))
            {
                Add("ready.finding.contact.failure");
            }

            if (!step.Routes.Any(route => route.Label == "onUnknown"))
            {
                Add("ready.finding.contact.unknown");
            }
        }

        return findings;
    }

    private static List<ReadinessFinding> AiFallbacks(BrainProcess process, ProcessOutline outline)
    {
        var findings = new List<ReadinessFinding>();
        foreach (var step in outline.Steps.Where(step => step.Kind == OutlineStepKind.Ai))
        {
            foreach (var label in new[] { "onInvalid", "onError", "onNotAvailable", "onNotEntitled" })
            {
                var route = step.Routes.FirstOrDefault(item => item.Label == label);
                string? key = null;
                if (route is null)
                {
                    key = "ready.finding.ai.missing";
                }
                else if (!EndsWithoutAi(outline, route.Target))
                {
                    key = "ready.finding.ai.needsAi";
                }

                if (key is not null)
                {
                    findings.Add(Both(AiWithoutFallback, step.Id, label, key, locale => [("step", FlowChangeDescriber.Quote(FlowWords.For(process, locale).Step(step.Id))), ("route", RouteWords(outline, process, step.Id, label, locale))]));
                }
            }
        }

        return findings;
    }

    // True when, from the step, some way of going on reaches an end without passing through an AI step.
    private static bool EndsWithoutAi(ProcessOutline outline, string target)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([target]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || outline.Step(id) is not { } step || step.Kind == OutlineStepKind.Ai)
            {
                continue;
            }

            if (step.Kind == OutlineStepKind.End)
            {
                return true;
            }

            foreach (var route in step.Routes)
            {
                queue.Enqueue(route.Target);
            }
        }

        return false;
    }

    private static List<ReadinessFinding> Waits(BrainProcess process, ProcessOutline outline, IReadOnlyList<KnownGap> gaps) =>
        [.. outline.Steps
            .Where(step => step.Kind == OutlineStepKind.Wait && step.Timeout is null)
            .Where(step => !gaps.Any(gap => gap.Check == KnownGap.WaitWithoutTimeout && gap.Step == step.Id))
            .Select(step => Both(WaitWithoutTimeout, step.Id, null, "ready.finding.wait", locale => [("step", FlowChangeDescriber.Quote(FlowWords.For(process, locale).Step(step.Id)))]))];

    private static string CatalogKey(CatalogProblem problem)
    {
        if (problem.Code == CatalogCheck.PortKind)
        {
            return "ready.finding.catalog.portkind";
        }

        return problem.Code == CatalogCheck.DeliveryUnsupported ? "ready.finding.catalog.delivery" : "ready.finding.catalog." + problem.What;
    }

    internal static ReadinessFinding CatalogFinding(CatalogProblem problem, JsonObject? document) =>
        Both(OutsideCatalog, problem.Step, null, CatalogKey(problem), locale =>
        [
            ("name", "`" + problem.Name + "`"),
            ("where", PathWords.Describe(problem.Path, document, locale)),
            ("available", problem.Available.Count == 0 ? AuthoringText.Text(locale, "ready.none") : AuthoringText.Join(locale, [.. problem.Available.Select(name => "`" + name + "`")])),
            ("detail", problem.Detail ?? string.Empty),
        ]);

    private static ReadinessFinding Both(string code, string? step, string? route, string key, Func<WorkbenchLocale, (string, string)[]> values) =>
        new(code, step, route, AuthoringText.Text(WorkbenchLocale.En, key, values(WorkbenchLocale.En)), AuthoringText.Text(WorkbenchLocale.Es, key, values(WorkbenchLocale.Es)));
}
