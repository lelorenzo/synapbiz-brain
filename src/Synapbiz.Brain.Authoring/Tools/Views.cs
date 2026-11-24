using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>Turns the objects of the libraries into the JSON the tools answer with. Names are camelCase; texts are in the asked language.</summary>
internal static class Views
{
    internal static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    internal static JsonArray Strings(IEnumerable<string> items) => AuthoringJson.Array(items);

    internal static JsonNode CatalogProblems(IReadOnlyList<CatalogProblem> problems, JsonObject? document, WorkbenchLocale locale) =>
        new JsonArray([.. problems.Select(problem => (JsonNode?)new JsonObject
        {
            ["code"] = problem.Code,
            ["path"] = problem.Path,
            ["name"] = problem.Name,
            ["step"] = problem.Step,
            ["available"] = Strings(problem.Available),
            ["say"] = ReadinessChecks.CatalogFinding(problem, document).Text(locale),
        })]);

    internal static JsonObject Description(FlowView view) => new()
    {
        ["title"] = view.Title,
        ["start"] = view.StartStep,
        ["stages"] = new JsonArray([.. view.Stages.Select(stage => (JsonNode?)new JsonObject { ["id"] = stage.Id, ["label"] = stage.Label })]),
        ["steps"] = new JsonArray([.. view.Steps.Select(step => (JsonNode?)new JsonObject
        {
            ["id"] = step.Id,
            ["label"] = step.Label,
            ["kind"] = Camel(step.Kind.ToString()),
            ["kindName"] = step.KindName,
            ["stage"] = step.StageLabel,
            ["sentence"] = PersonWords.Plain(step.Sentence),
            ["routes"] = new JsonArray([.. step.Routes.Select(route => (JsonNode?)new JsonObject
            {
                ["label"] = route.Label,
                ["phrase"] = PersonWords.Plain(route.Phrase),
                ["target"] = route.Target,
                ["targetLabel"] = route.TargetLabel,
            })]),
        })]),
    };

    internal static JsonObject Diagram(FlowGraph graph) => new()
    {
        ["nodes"] = new JsonArray([.. graph.Nodes.Select(node => (JsonNode?)new JsonObject
        {
            ["id"] = node.Id,
            ["label"] = node.Label,
            ["kind"] = Camel(node.Kind.ToString()),
            ["stage"] = node.Stage,
            ["isStart"] = node.IsStart,
            ["isEnd"] = node.IsEnd,
        })]),
        ["edges"] = new JsonArray([.. graph.Edges.Select(edge => (JsonNode?)new JsonObject
        {
            ["from"] = edge.From,
            ["to"] = edge.To,
            ["label"] = edge.Label,
            ["phrase"] = PersonWords.Plain(edge.Phrase),
            ["happyPath"] = edge.IsHappyPath,
            ["backEdge"] = edge.IsBackEdge,
        })]),
    };

    internal static JsonArray Scenarios(FlowScenarioReport report, BrainProcess process, WorkbenchLocale locale) =>
        new([.. report.Scenarios.Select(result => (JsonNode?)new JsonObject
        {
            ["name"] = result.Name,
            ["passed"] = result.Passed,
            ["say"] = result.Passed ? null : PersonWords.ScenarioFailure(locale, result.Failure, process),
            ["failure"] = result.Failure,
            ["trace"] = Strings(result.Trace),
        })]);

    internal static JsonObject Coverage(FlowCoverageResult coverage, IReadOnlyList<KnownGap> gaps) => new()
    {
        ["totalRoutes"] = coverage.TotalRoutes,
        ["coveredRoutes"] = coverage.CoveredRoutes,
        ["neverTaken"] = new JsonArray([.. coverage.NeverTaken.Select(route => (JsonNode?)new JsonObject
        {
            ["step"] = route.Step,
            ["route"] = route.Label,
            ["target"] = route.Target,
            ["knownUntested"] = gaps.Any(gap => gap.Check == KnownGap.RouteUntested && gap.Step == route.Step && gap.Route == route.Label),
        })]),
        ["stepsNeverEntered"] = Strings(coverage.StepsNeverEntered),
    };

    internal static JsonObject Readiness(ReadinessReport report, WorkbenchLocale locale) => new()
    {
        ["ready"] = report.Ready,
        ["checks"] = new JsonArray([.. report.Checks.Select(check => (JsonNode?)new JsonObject
        {
            ["code"] = check.Code,
            ["requirement"] = ReadinessChecks.Requirement(check.Code, locale),
            ["passed"] = check.Passed,
            ["findings"] = new JsonArray([.. check.Findings.Select(finding => (JsonNode?)new JsonObject
            {
                ["step"] = finding.Step,
                ["route"] = finding.Route,
                ["say"] = finding.Text(locale),
            })]),
        })]),
    };

    internal static JsonArray KnownGaps(IReadOnlyList<KnownGap> gaps) =>
        new([.. gaps.Select(gap => (JsonNode?)new JsonObject
        {
            ["check"] = gap.Check,
            ["step"] = gap.Step,
            ["route"] = gap.Route,
            ["reason"] = gap.Reason,
        })]);

    internal static JsonObject Changes(FlowChangeView view) => new()
    {
        ["summary"] = view.Summary,
        ["hasChanges"] = view.HasChanges,
        ["items"] = new JsonArray([.. view.Items.Select(item => (JsonNode?)new JsonObject
        {
            ["kind"] = Camel(item.Kind.ToString()),
            ["say"] = item.Text,
        })]),
        ["behaviour"] = new JsonArray([.. view.Behaviour.Select(item => (JsonNode?)new JsonObject
        {
            ["scenario"] = item.Scenario,
            ["kind"] = Camel(item.Kind.ToString()),
            ["say"] = item.Text,
        })]),
        ["testsReplayed"] = view.TestsReplayed,
        ["testsUnchanged"] = view.TestsUnchanged,
    };
}
