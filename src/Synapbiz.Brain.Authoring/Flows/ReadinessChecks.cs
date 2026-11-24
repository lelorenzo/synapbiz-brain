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

/// <summary>What the readiness checks need to know about the approved process a draft came from.</summary>
/// <param name="Id">The id of the approved process.</param>
/// <param name="Version">Its version when the draft was based on it.</param>
/// <param name="CurrentVersion">The version of the approved process now; null when it can no longer be read.</param>
/// <param name="Stale">True when the approved process is no longer the one the draft was based on.</param>
public sealed record ReadinessBase(string Id, long Version, long? CurrentVersion, bool Stale);

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

    /// <summary>The process has no test at all.</summary>
    public const string NoScenarios = "authoring.ready.no_scenarios";

    /// <summary>Two tests have the same name.</summary>
    public const string ScenarioNamesDuplicated = "authoring.ready.scenario_names_duplicated";

    /// <summary>The process has steps that read, act, poll or ask an AI, and no catalog was given to check them against.</summary>
    public const string CatalogMissing = "authoring.ready.catalog_missing";

    /// <summary>The version of the draft is not greater than the version of the approved process it comes from.</summary>
    public const string VersionNotNewer = "authoring.ready.version_not_newer";

    /// <summary>The approved process changed after the draft was based on it.</summary>
    public const string BaseStale = "authoring.ready.base_stale";

    /// <summary>The draft is another process than the approved one it was based on.</summary>
    public const string BaseMismatch = "authoring.ready.base_mismatch";

    /// <summary>The codes of the checks, in the order they run.</summary>
    public static IReadOnlyList<string> Codes { get; } =
        [LoaderErrors, ScenarioFailing, NoScenarios, ScenarioNamesDuplicated, LabelMissing, RouteUntested, ContactUnsafe, CatalogMissing, AiWithoutFallback, WaitWithoutTimeout, OutsideCatalog, VersionNotNewer, BaseStale, BaseMismatch];

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
    /// <param name="baseInfo">The approved process the draft came from; null for a new process.</param>
    public static ReadinessReport Run(
        JsonObject? document,
        IReadOnlyList<BrainSpecError> errors,
        BrainProcess? process,
        FlowScenarioReport? report,
        IReadOnlyList<KnownGap> gaps,
        AuthoringCatalog catalog,
        IReadOnlyList<CatalogProblem> catalogProblems,
        ReadinessBase? baseInfo = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalogProblems);
        var outline = process is null ? null : ProcessOutline.From(process);
        return new ReadinessReport(
        [
            new(LoaderErrors, [.. errors.Select(error => LoaderFinding(error, document))]),
            new(ScenarioFailing, report is null ? [] : [.. report.Scenarios.Where(result => !result.Passed).Select(result => ScenarioFinding(process!, result))]),
            new(NoScenarios, report is { Scenarios.Count: 0 } ? [Both(NoScenarios, null, null, "ready.finding.noscenarios", _ => [])] : []),
            new(ScenarioNamesDuplicated, report is null ? [] : DuplicateNames(report)),
            new(LabelMissing, process is null ? [] : Labels(process, outline!)),
            new(RouteUntested, report is null ? [] : Untested(process!, outline!, report, gaps)),
            new(ContactUnsafe, outline is null ? [] : Contacts(process!, outline, catalog)),
            new(CatalogMissing, outline is null ? [] : NoCatalog(outline, catalog)),
            new(AiWithoutFallback, outline is null ? [] : AiFallbacks(process!, outline)),
            new(WaitWithoutTimeout, outline is null ? [] : Waits(process!, outline, gaps)),
            new(OutsideCatalog, [.. catalogProblems.Select(problem => CatalogFinding(problem, document))]),
            new(VersionNotNewer, process is null || baseInfo is null || baseInfo.CurrentVersion is not { } current || process.Version > current ? [] : [Both(VersionNotNewer, null, null, "ready.finding.version", _ => [("current", AuthoringText.Number(current))])]),
            new(BaseStale, baseInfo is { Stale: true } ? [Both(BaseStale, null, null, "ready.finding.stale", _ => [])] : []),
            new(BaseMismatch, process is null || baseInfo is null || string.Equals(baseInfo.Id, process.Id, StringComparison.Ordinal) ? [] : [Both(BaseMismatch, null, null, "ready.finding.basemismatch", _ => [("base", FlowChangeDescriber.Quote(baseInfo.Id)), ("id", FlowChangeDescriber.Quote(process.Id))])]),
        ]);
    }

    /// <summary>The gaps the author accepted that still excuse something: a route no test takes, or a wait with no time limit.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="report">Its tests and coverage.</param>
    /// <param name="gaps">What the author accepted knowingly.</param>
    public static IReadOnlyList<KnownGap> Applied(BrainProcess process, FlowScenarioReport report, IReadOnlyList<KnownGap> gaps)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(gaps);
        var outline = ProcessOutline.From(process);
        return [.. gaps.Where(gap =>
            (gap.Check == KnownGap.RouteUntested && report.Coverage.NeverTaken.Any(route => route.Step == gap.Step && route.Label == gap.Route))
            || (gap.Check == KnownGap.WaitWithoutTimeout && outline.Steps.Any(step => step.Id == gap.Step && step.Kind == OutlineStepKind.Wait && step.Timeout is null)))];
    }

    /// <summary>One accepted gap in words, with the labels of the process.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="gap">The gap.</param>
    /// <param name="locale">The language.</param>
    public static string GapWords(BrainProcess process, KnownGap gap, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(gap);
        var step = FlowChangeDescriber.Quote(FlowWords.For(process, locale).Step(gap.Step));
        if (gap.Check == KnownGap.WaitWithoutTimeout || gap.Route is null)
        {
            return AuthoringText.Text(locale, "gap.wait", ("step", step));
        }

        return AuthoringText.Text(locale, "gap.route", ("step", step), ("route", RouteWords(ProcessOutline.From(process), process, gap.Step, gap.Route, locale)));
    }

    private static List<ReadinessFinding> DuplicateNames(FlowScenarioReport report) =>
        [.. report.Scenarios.GroupBy(result => result.Name, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => Both(ScenarioNamesDuplicated, null, null, "ready.finding.scenarionames", _ => [("name", FlowChangeDescriber.Quote(group.Key))]))];

    // A process that reads, acts, polls or asks an AI uses connectors; with no catalog nothing says they exist or whether they contact a person.
    private static List<ReadinessFinding> NoCatalog(ProcessOutline outline, AuthoringCatalog catalog) =>
        catalog.Ports is null && outline.Steps.Any(step => step.Kind is OutlineStepKind.Read or OutlineStepKind.Action or OutlineStepKind.Poll or OutlineStepKind.Ai)
            ? [Both(CatalogMissing, null, null, "ready.finding.nocatalog", _ => [])]
            : [];

    private static ReadinessFinding LoaderFinding(BrainSpecError error, JsonObject? document) =>
        Both(LoaderErrors, null, null, "ready.finding.loader", locale => [("sentence", PersonWords.Error(locale, error, document))]);

    private static ReadinessFinding ScenarioFinding(BrainProcess process, FlowScenarioResult result) =>
        Both(ScenarioFailing, null, null, "ready.finding.scenario", locale => [("name", FlowChangeDescriber.Quote(result.Name)), ("reason", PersonWords.ScenarioFailure(locale, result.Failure, process))]);

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

            // What a person is shown: the results the cases end with, the queues of the tasks and the choices a person makes.
            Missing(texts, "results", outline.Steps.Select(step => step.Result), "result", (words, id) => words.Result(id));
            Missing(texts, "queues", outline.Steps.Select(step => step.Queue), "queue", (words, id) => words.Queue(id));
            Missing(texts, "outcomes", outline.Steps.SelectMany(step => step.Outcomes ?? []), "outcome", (words, id) => words.Outcome(id));

            void Missing(JsonObject? display, string group, IEnumerable<string?> ids, string kind, Func<FlowWords, string, string> label)
            {
                foreach (var id in ids.OfType<string>().Distinct(StringComparer.Ordinal).Where(id => string.IsNullOrWhiteSpace(AuthoringJson.Text((display?[group] as JsonObject)?[id]))))
                {
                    findings.Add(Both(LabelMissing, null, null, "ready.finding.label." + kind, locale => [("name", FlowChangeDescriber.Quote(label(FlowWords.For(process, locale), id))), ("language", AuthoringText.Text(locale, "language." + language))]));
                }
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
                if (route is null && label != "onError")
                {
                    // The engine sends a failure without its own route to onError, which is checked on its own turn.
                    continue;
                }

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
            ("name", FlowChangeDescriber.Quote(problem.Name)),
            ("where", PathWords.Describe(problem.Path, document, locale)),
            ("available", problem.Available.Count == 0 ? AuthoringText.Text(locale, "ready.none") : AuthoringText.Join(locale, [.. problem.Available.Select(FlowChangeDescriber.Quote)])),
            ("detail", DetailWords(problem, locale)),
        ]);

    private static string DetailWords(CatalogProblem problem, WorkbenchLocale locale)
    {
        if (problem.Detail is null)
        {
            return string.Empty;
        }

        var key = (problem.Code == CatalogCheck.PortKind ? "portkind." : "delivery.") + problem.Detail;
        return AuthoringText.Has(key) ? AuthoringText.Text(locale, key) : FlowChangeDescriber.Quote(problem.Detail);
    }

    private static ReadinessFinding Both(string code, string? step, string? route, string key, Func<WorkbenchLocale, (string, string)[]> values) =>
        new(code, step, route, AuthoringText.Text(WorkbenchLocale.En, key, values(WorkbenchLocale.En)), AuthoringText.Text(WorkbenchLocale.Es, key, values(WorkbenchLocale.Es)));
}
