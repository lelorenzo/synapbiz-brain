using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>Everything Brain found out about a draft: the data of the answer, and the facts later steps need.</summary>
/// <param name="Data">The members of the answer that describe the draft.</param>
/// <param name="Say">The sentence for the person.</param>
/// <param name="Process">The loaded process; null when the loader refused the draft.</param>
/// <param name="Report">The scenario report; null when the draft did not load.</param>
/// <param name="Readiness">The verdict of the checks.</param>
/// <param name="Changes">What the draft changes in its base, in the asked language; null when there is no base to compare with.</param>
internal sealed record FlowAnalysis(JsonObject Data, string Say, BrainProcess? Process, FlowScenarioReport? Report, ReadinessReport Readiness, FlowChangeView? Changes)
{
    internal bool Ready => Readiness.Ready;
}

/// <summary>Runs the loader, the catalog check, the tests and the readiness checks on a draft and writes what they found.</summary>
internal static class FlowAnalyzer
{
    internal static FlowAnalysis Analyze(ToolContext context, AuthoringCatalog catalog, string id, StoredDraft draft, WorkbenchLocale locale)
    {
        var document = AuthoringJson.TryObject(draft.Text);
        var loadContext = context.FlowContext(catalog, id);
        var loaded = BrainProcessLoader.Load(draft.Text, loadContext);
        var process = loaded.Process;
        var problems = document is null ? [] : CatalogCheck.Draft(catalog, document);
        var report = process is null ? null : FlowScenarioReport.Of(process, locale);
        var readiness = ReadinessChecks.Run(document, loaded.Errors, process, report, draft.KnownGaps, catalog, problems);
        var known = process is null ? [] : context.ApprovedFlows(catalog).Select(item => item.Process).OfType<BrainProcess>().ToList();

        var data = new JsonObject
        {
            ["draft"] = DraftJson(id, draft, process),
            ["loaded"] = process is not null,
            ["ready"] = readiness.Ready,
            ["errors"] = ErrorsJson(loaded.Errors, document, locale),
            ["catalogProblems"] = Views.CatalogProblems(problems, document, locale),
        };

        FlowChangeView? changes = null;
        if (process is not null)
        {
            data["description"] = Views.Description(FlowDescriber.Describe(process, locale, known));
            data["diagram"] = Views.Diagram(FlowGraph.From(process, locale, known));
            changes = Changes(context, catalog, draft, process, locale, data);
            data["scenarios"] = Views.Scenarios(report!);
            data["coverage"] = Views.Coverage(report!.Coverage, draft.KnownGaps);
        }

        data["readiness"] = Views.Readiness(readiness, locale);
        data["knownGaps"] = Views.KnownGaps(draft.KnownGaps);
        return new FlowAnalysis(data, Say(process, loaded.Errors, readiness, changes, report, locale), process, report, readiness, changes);
    }

    private static JsonObject DraftJson(string id, StoredDraft draft, BrainProcess? process)
    {
        var node = new JsonObject
        {
            ["id"] = id,
            ["version"] = process?.Version,
            ["contentHash"] = AuthoringJson.Hash(DocumentKind.Flow, FormatOrText(draft.Text)),
        };
        node["basedOn"] = draft.Base is null
            ? null
            : new JsonObject { ["id"] = draft.Base.Id, ["version"] = draft.Base.Version, ["contentHash"] = draft.Base.ContentHash };
        return node;
    }

    internal static string FormatOrText(string text) => AuthoringJson.TryObject(text) is { } node ? AuthoringJson.Format(node) : text;

    private static FlowChangeView? Changes(ToolContext context, AuthoringCatalog catalog, StoredDraft draft, BrainProcess process, WorkbenchLocale locale, JsonObject data)
    {
        if (draft.Base is null)
        {
            var text = FlowChangeDescriber.DescribeAdded(process, locale);
            data["changes"] = new JsonObject
            {
                ["isNew"] = true,
                ["summary"] = text,
                ["items"] = new JsonArray(),
                ["behaviour"] = new JsonArray(),
                ["hasChanges"] = true,
            };
            return null;
        }

        var approved = context.ApprovedFlow(catalog, draft.Base.Id);
        if (approved?.Process is not { } before)
        {
            data["changes"] = new JsonObject { ["isNew"] = false, ["baseAvailable"] = false, ["summary"] = AuthoringText.Text(locale, "change.nobase") };
            return null;
        }

        var view = FlowChangeDescriber.Describe(before, process, locale);
        var node = Views.Changes(view);
        node["isNew"] = false;
        node["baseChanged"] = !string.Equals(AuthoringJson.Hash(DocumentKind.Flow, approved.Text), draft.Base.ContentHash, StringComparison.Ordinal);
        data["changes"] = node;
        return view;
    }

    internal static JsonArray ErrorsJson(IReadOnlyList<BrainSpecError> errors, JsonObject? document, WorkbenchLocale locale) =>
        new([.. errors.Select(error => (JsonNode?)new JsonObject
        {
            ["code"] = error.Code,
            ["path"] = error.Path,
            ["where"] = PathWords.Describe(error.Path, document, locale),
            ["message"] = locale == WorkbenchLocale.Es ? error.MessageEs : error.MessageEn,
            ["hint"] = FixHints.For(error.Code) ?? error.FixHint,
        })]);

    private static string Say(BrainProcess? process, IReadOnlyList<BrainSpecError> errors, ReadinessReport readiness, FlowChangeView? changes, FlowScenarioReport? report, WorkbenchLocale locale)
    {
        if (process is null)
        {
            var first = string.Empty;
            if (errors.Count > 0)
            {
                first = locale == WorkbenchLocale.Es ? errors[0].MessageEs : errors[0].MessageEn;
            }
            return AuthoringText.Text(locale, "say.draft.refused", ("n", AuthoringText.Number(errors.Count)), ("first", first));
        }

        var title = FlowChangeDescriber.Quote(FlowWords.For(process, locale).Title());
        var tests = AuthoringText.Text(locale, "say.draft.tests", ("pass", AuthoringText.Number(report!.Scenarios.Count(item => item.Passed))), ("total", AuthoringText.Number(report.Scenarios.Count)));
        if (readiness.Ready)
        {
            var change = changes?.Summary;
            return AuthoringText.Text(locale, "say.draft.ready", ("title", title), ("tests", tests)) + (change is null ? string.Empty : " " + change);
        }

        var findings = readiness.Findings;
        return AuthoringText.Text(locale, "say.draft.notready", ("title", title), ("n", AuthoringText.Number(findings.Count)), ("first", findings[0].Text(locale)), ("tests", tests));
    }
}
