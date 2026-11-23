using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>Everything Brain found out about a rule draft.</summary>
/// <param name="Data">The members of the answer that describe the draft.</param>
/// <param name="Say">The sentence for the person.</param>
/// <param name="Rule">The loaded draft; null when the loader refused it.</param>
/// <param name="Base">The loaded approved rule the draft comes from; null when there is none or it does not load.</param>
/// <param name="Ready">True when the draft loads and every one of its tests passes.</param>
/// <param name="Reasons">The reasons the proposal needs review.</param>
internal sealed record RuleAnalysis(JsonObject Data, string Say, BrainRule? Rule, BrainRule? Base, bool Ready, IReadOnlyList<string> Reasons)
{
    internal bool NeedsReview => Reasons.Count > 0;
}

internal static class RuleAnalyzer
{
    internal static RuleAnalysis Analyze(ToolContext context, AuthoringCatalog catalog, string id, StoredDraft draft, WorkbenchLocale locale)
    {
        var contracts = catalog.Contracts;
        var loaded = BrainRuleLoader.Load(draft.Text, contracts);
        var document = AuthoringJson.TryObject(draft.Text);
        var data = new JsonObject
        {
            ["draft"] = new JsonObject
            {
                ["id"] = id,
                ["version"] = loaded.Rule?.Version,
                ["contentHash"] = RuleReviewJson.ContentHash(draft.Text),
                ["basedOn"] = draft.Base is null ? null : new JsonObject { ["id"] = draft.Base.Id, ["version"] = draft.Base.Version, ["contentHash"] = draft.Base.ContentHash },
                ["editsApplied"] = (AuthoringJson.TryParse(draft.EditsJson) as JsonArray)?.Count ?? 0,
            },
            ["loaded"] = loaded.Rule is not null,
            ["errors"] = FlowAnalyzer.ErrorsJson(loaded.Errors, document, locale),
        };

        if (loaded.Rule is not { } rule)
        {
            data["ready"] = false;
            var message = string.Empty;
            if (loaded.Errors.Count > 0)
            {
                message = locale == WorkbenchLocale.Es ? loaded.Errors[0].MessageEs : loaded.Errors[0].MessageEn;
            }

            return new RuleAnalysis(data, AuthoringText.Text(locale, "say.rule.refused", ("n", AuthoringText.Number(loaded.Errors.Count)), ("first", message)), null, null, false, []);
        }

        var labels = RuleLabels.FromMeta(rule.Meta, locale);
        var view = RuleDescriber.Describe(rule, locale, labels);
        var tests = RuleTester.Run(rule);
        data["view"] = ViewJson(view);
        data["tests"] = TestsJson(tests);

        var before = draft.Base is null ? null : context.ApprovedRule(catalog, draft.Base.Id)?.Rule;

        string? change = null;
        IReadOnlyList<string> reasons = [];
        if (before is not null)
        {
            var changes = RuleChangeDescriber.Describe(before, rule, RuleLabels.FromMeta(before.Meta, locale), labels, locale);
            data["changes"] = ChangesJson(changes);
            change = changes.Summary;
            reasons = RuleReviewNeed.Reasons(before, rule);
        }

        data["needsReview"] = reasons.Count > 0;
        data["needsReviewReasons"] = new JsonArray([.. reasons.Select(reason => (JsonNode?)new JsonObject { ["code"] = reason, ["say"] = AuthoringText.Text(locale, "rule.review." + reason) })]);
        var ready = tests.FailedCount == 0;
        data["ready"] = ready;
        var title = FlowChangeDescriber.Quote(view.Title);
        var say = ready
            ? AuthoringText.Text(locale, "say.rule.ready", ("title", title), ("pass", AuthoringText.Number(tests.PassedCount)), ("total", AuthoringText.Number(tests.Cases.Count)))
            : AuthoringText.Text(locale, "say.rule.failing", ("title", title), ("n", AuthoringText.Number(tests.FailedCount)), ("name", FlowChangeDescriber.Quote(tests.Cases.First(item => !item.Passed).Name)));
        if (change is not null)
        {
            say += " " + change;
        }

        if (ready && before is not null)
        {
            say += " " + AuthoringText.Text(locale, reasons.Count > 0 ? "say.rule.review.needed" : "say.rule.review.notneeded");
        }

        return new RuleAnalysis(data, say, rule, before, ready, reasons);
    }

    internal static JsonObject ViewJson(RuleView view)
    {
        JsonObject Row(RowView row) => new()
        {
            ["key"] = row.Key,
            ["number"] = row.IsDefault ? null : row.Index + 1,
            ["isDefault"] = row.IsDefault,
            ["title"] = row.Title,
            ["sentence"] = row.Sentence,
        };

        return new JsonObject
        {
            ["id"] = view.Id,
            ["version"] = view.Version,
            ["title"] = view.Title,
            ["kind"] = view.Kind,
            ["hitPolicy"] = Views.Camel(view.HitPolicy.ToString()),
            ["facts"] = new JsonArray([.. view.Facts.Select(fact => (JsonNode?)new JsonObject
            {
                ["name"] = fact.Name,
                ["label"] = fact.Label,
                ["type"] = fact.Type.ToString(),
                ["nullable"] = fact.Nullable,
                ["allowedValues"] = new JsonArray([.. fact.AllowedValues.Select(option => (JsonNode?)new JsonObject { ["value"] = option.Value, ["label"] = option.Label })]),
            })]),
            ["outcomeFields"] = new JsonArray([.. view.OutcomeFields.Select(field => (JsonNode?)new JsonObject
            {
                ["name"] = field.Name,
                ["label"] = field.Label,
                ["type"] = field.Type.ToString(),
                ["nullable"] = field.Nullable,
            })]),
            ["rows"] = new JsonArray([.. view.Rows.Select(row => (JsonNode?)Row(row))]),
            ["defaultRow"] = view.DefaultRow is null ? null : Row(view.DefaultRow),
        };
    }

    internal static JsonObject TestsJson(TestRunView tests) => new()
    {
        ["passed"] = tests.PassedCount,
        ["failed"] = tests.FailedCount,
        ["uncoveredRows"] = Views.Strings(tests.UncoveredRowKeys),
        ["cases"] = new JsonArray([.. tests.Cases.Select(item => (JsonNode?)new JsonObject
        {
            ["name"] = item.Name,
            ["passed"] = item.Passed,
            ["expected"] = Expected(item.Expected),
            ["actual"] = item.Actual switch
            {
                Decided decided => AuthoringJson.TryParse(decided.OutcomeJson),
                NoDecision => JsonValue.Create("noDecision"),
                _ => null,
            },
            ["matchedRows"] = Views.Strings(item.MatchedRowKeys),
            ["failure"] = item.Failure is null ? null : new JsonObject { ["code"] = item.Failure.Code, ["message"] = item.Failure.Message },
        })]),
    };

    private static JsonNode? Expected(GoldenExpectation expected) => expected switch
    {
        ExpectedDecided decided => new JsonObject { ["outcome"] = AuthoringJson.TryParse(decided.OutcomeJson), ["row"] = decided.RowKey },
        ExpectedNoDecision => JsonValue.Create("noDecision"),
        _ => JsonValue.Create("other"),
    };

    internal static JsonObject ChangesJson(RuleChangeView changes) => new()
    {
        ["summary"] = changes.Summary,
        ["hitPolicyChanged"] = changes.HitPolicyChanged,
        ["defaultRowChanged"] = changes.DefaultRowChanged,
        ["rows"] = new JsonArray([.. changes.Rows.Select(row => (JsonNode?)new JsonObject
        {
            ["key"] = row.Key,
            ["label"] = row.Label,
            ["change"] = Views.Camel(row.Tag.ToString()),
            ["before"] = row.SentenceBefore,
            ["after"] = row.SentenceAfter,
            ["nameBefore"] = row.NameBefore,
            ["nameAfter"] = row.NameAfter,
        })]),
        ["casesThatChange"] = new JsonArray([.. changes.CasesThatChange.Select(item => (JsonNode?)new JsonObject
        {
            ["name"] = item.Name,
            ["before"] = item.Before,
            ["after"] = item.After,
        })]),
    };
}
