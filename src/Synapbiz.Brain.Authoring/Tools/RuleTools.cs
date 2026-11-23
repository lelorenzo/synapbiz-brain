using System.Globalization;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>The tools that read rules, edit a rule draft, simulate it and run its tests.</summary>
internal static class RuleTools
{
    internal static IEnumerable<AuthoringTool> Create(ToolContext context)
    {
        yield return new AuthoringTool(
            "rule_list",
            "Lists the approved rules of the workspace (id, version, title, decision kind, number of rows and tests) and the rule drafts in progress. Definitions only.",
            ToolSchemas.Object([]),
            context,
            List);

        yield return new AuthoringTool(
            "rule_get",
            "Reads one rule: its document, the facts it reads and the outcome fields it gives, every row as a sentence, and the result of its tests. Set `draft` to read the draft instead of the approved version.",
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The rule id, for example visit.note_ready.")),
                    ("draft", ToolSchemas.Flag("True to read the draft; default false reads the approved document.")),
                ],
                "id"),
            context,
            Get);

        yield return new AuthoringTool(
            "rule_draft_edit",
            "Changes a rule through guided edits (rows, conditions, outcome values, order, test cases, row names) and answers in one result: loader errors with hints, the rows as sentences, what changed against the approved rule, how the test cases now decide, the test results and whether a proposal would need review. "
            + "Edits are kept: each call adds to the edits of the draft so far, all applied to the approved rule; use `reset` to start again. Values are typed as plain JSON (text, number, true or false; money as \"10.00 USD\"; dates as yyyy-MM-dd). Nothing is activated. "
            + ToolSchemas.NoPatientText,
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The id of the approved rule to change.")),
                    ("edits", ToolSchemas.Items("The edits, in order.", EditSchema())),
                    ("reset", ToolSchemas.Flag("True to drop the edits kept so far and start from the approved rule.")),
                ],
                "id",
                "edits"),
            context,
            Edit);

        yield return new AuthoringTool(
            "rule_simulate",
            "Evaluates the rule for made-up facts on the real engine and says which row decides and why, row by row. Facts are plain JSON values typed by the person or invented by you; real cases and patients are never reached. "
            + ToolSchemas.NoPatientText,
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The rule id.")),
                    ("draft", ToolSchemas.Flag("True to evaluate the draft; default evaluates the approved rule.")),
                    ("facts", ToolSchemas.AnyObject("The fact values by fact name, for example {\"status\": \"signed\"}. A fact that is left out counts as empty, which only a nullable fact allows.")),
                    ("asOf", ToolSchemas.Text("The date the decision applies to, yyyy-MM-dd; default today on the host clock.", 10)),
                ],
                "id",
                "facts"),
            context,
            Simulate);

        yield return new AuthoringTool(
            "rule_test",
            "Runs every test case of the rule on the real engine: which pass, which fail and why, and which rows no passing case reaches. Set `draft` to test the draft.",
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The rule id.")),
                    ("draft", ToolSchemas.Flag("True to test the draft; default tests the approved rule.")),
                ],
                "id"),
            context,
            Test);
    }

    private static JsonObject EditSchema()
    {
        var condition = new JsonObject
        {
            ["fact"] = ToolSchemas.Text("The fact the condition is on.", 200),
            ["operator"] = ToolSchemas.Enum("The operator.", RuleEditReader.Operators),
            ["value"] = new JsonObject { ["description"] = "The operand of equals, notEquals, startsWith, and the lower end of a range." },
            ["values"] = new JsonObject { ["type"] = "array", ["description"] = "The operands of in and notIn.", ["items"] = new JsonObject() },
            ["upper"] = new JsonObject { ["description"] = "The upper end of a range." },
            ["lowerInclusive"] = ToolSchemas.Flag("Whether a range includes its lower end; default true."),
            ["upperInclusive"] = ToolSchemas.Flag("Whether a range includes its upper end; default true."),
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "One edit. `op` says which; the other members it reads are named in the description of the tool.",
            ["properties"] = new JsonObject
            {
                ["op"] = ToolSchemas.Enum("setCondition, clearCondition, setOutcome, addRow, removeRow, moveRow, setDefaultOutcome, setCaseExpectation, addCase, removeCase or setRowLabel.", RuleEditReader.Operations),
                ["row"] = ToolSchemas.Text("The key of the row.", 200),
                ["after"] = new JsonObject { ["type"] = AuthoringJson.Array(["string", "null"]), ["description"] = "The key of the row to put this row after; null or absent puts it first." },
                ["fact"] = condition["fact"]!.DeepClone(),
                ["operator"] = condition["operator"]!.DeepClone(),
                ["value"] = new JsonObject { ["description"] = "The operand of a condition, or the new value of an outcome field (null removes an optional field)." },
                ["values"] = condition["values"]!.DeepClone(),
                ["upper"] = condition["upper"]!.DeepClone(),
                ["lowerInclusive"] = condition["lowerInclusive"]!.DeepClone(),
                ["upperInclusive"] = condition["upperInclusive"]!.DeepClone(),
                ["field"] = ToolSchemas.Text("The outcome field.", 200),
                ["conditions"] = new JsonObject { ["type"] = "array", ["description"] = "For addRow: the conditions of the new row.", ["items"] = new JsonObject { ["type"] = "object", ["properties"] = condition, ["required"] = AuthoringJson.Array(["fact", "operator"]), ["additionalProperties"] = false } },
                ["outcome"] = ToolSchemas.AnyObject("For addRow and setCaseExpectation: the outcome fields and their values."),
                ["case"] = ToolSchemas.FreeText("The name of a test case.", 200),
                ["facts"] = ToolSchemas.AnyObject("For addCase: the fact values of the made-up case."),
                ["noDecision"] = ToolSchemas.Flag("For addCase and setCaseExpectation: the case expects that no row decides."),
                ["locale"] = ToolSchemas.Enum("For setRowLabel: the language of the name.", "en", "es"),
                ["text"] = new JsonObject { ["type"] = AuthoringJson.Array(["string", "null"]), ["description"] = "For setRowLabel: the name of the row; null clears it. " + ToolSchemas.NoPatientText },
            },
            ["required"] = AuthoringJson.Array(["op"]),
            ["additionalProperties"] = false,
        };
    }

    private static JsonObject List(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var rules = new JsonArray();
        var notLoading = new JsonArray();
        foreach (var item in context.ApprovedRules(catalog))
        {
            if (item.Rule is not { } rule)
            {
                notLoading.Add(new JsonObject { ["id"] = item.Id, ["errors"] = FlowAnalyzer.ErrorsJson(item.Errors, AuthoringJson.TryObject(item.Text), locale) });
                continue;
            }

            var view = RuleDescriber.Describe(rule, locale, RuleLabels.FromMeta(rule.Meta, locale));
            rules.Add(new JsonObject
            {
                ["id"] = item.Id,
                ["version"] = rule.Version,
                ["contentHash"] = RuleReviewJson.ContentHash(item.Text),
                ["title"] = view.Title,
                ["kind"] = view.Kind,
                ["hitPolicy"] = Views.Camel(view.HitPolicy.ToString()),
                ["rows"] = view.Rows.Count,
                ["tests"] = rule.Goldens.Count,
            });
        }

        var drafts = new JsonArray([.. context.Store.ListDrafts(DocumentKind.Rule).Select(id => (JsonNode?)new JsonObject { ["id"] = id })]);
        var say = AuthoringText.Text(locale, "say.rule.list", ("n", AuthoringText.Number(rules.Count)), ("d", AuthoringText.Number(drafts.Count)));
        return Results.Ok(say, new JsonObject { ["rules"] = rules, ["notLoading"] = notLoading, ["drafts"] = drafts });
    }

    private static JsonObject Get(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var id = AuthoringJson.Text(input["id"])!;
        if (input["draft"]?.GetValue<bool>() == true)
        {
            var draft = context.Store.ReadDraft(DocumentKind.Rule, id);
            if (draft is null)
            {
                return Results.Refuse(locale, "draft.not_found", [("id", id)], "There is no draft of this rule. Make one with rule_draft_edit.");
            }

            var analysis = RuleAnalyzer.Analyze(context, catalog, id, draft, locale);
            analysis.Data["source"] = "draft";
            analysis.Data["document"] = AuthoringJson.TryParse(draft.Text);
            return Results.Ok(analysis.Say, analysis.Data);
        }

        var approved = context.ApprovedRule(catalog, id);
        if (approved is null)
        {
            return Results.Refuse(locale, "rule.not_found", [("id", id)], "There is no approved rule with this id. Call rule_list to see the ids.");
        }

        var data = new JsonObject { ["source"] = "approved", ["document"] = AuthoringJson.TryParse(approved.Text), ["contentHash"] = RuleReviewJson.ContentHash(approved.Text), ["loaded"] = approved.Rule is not null };
        if (approved.Rule is not { } rule)
        {
            data["errors"] = FlowAnalyzer.ErrorsJson(approved.Errors, AuthoringJson.TryObject(approved.Text), locale);
            return Results.Ok(AuthoringText.Text(locale, "say.rule.notloading", ("id", id)), data);
        }

        var view = RuleDescriber.Describe(rule, locale, RuleLabels.FromMeta(rule.Meta, locale));
        var tests = RuleTester.Run(rule);
        data["view"] = RuleAnalyzer.ViewJson(view);
        data["tests"] = RuleAnalyzer.TestsJson(tests);
        var say = AuthoringText.Text(locale, "say.rule.get", ("title", FlowChangeDescriber.Quote(view.Title)), ("version", AuthoringText.Number(rule.Version)), ("rows", AuthoringText.Number(view.Rows.Count)), ("pass", AuthoringText.Number(tests.PassedCount)), ("total", AuthoringText.Number(tests.Cases.Count)));
        return Results.Ok(say, data);
    }

    private static JsonObject Edit(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var id = AuthoringJson.Text(input["id"])!;
        var approved = context.ApprovedRule(catalog, id);
        if (approved is null)
        {
            return Results.Refuse(locale, "rule.not_found", [("id", id)], "There is no approved rule with this id. Call rule_list to see the ids.");
        }

        if (approved.Rule is not { } baseRule)
        {
            return Results.Refuse(locale, "rule.not_loading", [("id", id)], "The approved rule does not load against the catalog, so it cannot be edited: " + string.Join("; ", approved.Errors.Take(3).Select(error => error.Code + " " + error.Path)));
        }

        var existing = context.Store.ReadDraft(DocumentKind.Rule, id);
        var kept = input["reset"]?.GetValue<bool>() == true ? [] : (AuthoringJson.TryParse(existing?.EditsJson) as JsonArray)?.Select(item => item?.DeepClone()).ToList() ?? [];
        var combined = new JsonArray([.. kept, .. ((JsonArray)input["edits"]!).Select(item => item?.DeepClone())]);
        var read = RuleEditReader.Read(combined, baseRule);
        if (read.Problem is { } problem)
        {
            var number = read.Index - kept.Count + 1;
            return Results.Refuse(locale, "rule.edit_invalid", [("n", AuthoringText.Number(number)), ("detail", problem)], "Edit " + (read.Index >= kept.Count ? number.ToString(CultureInfo.InvariantCulture) : "kept from an earlier call") + " cannot be used: " + problem);
        }

        var drafted = RuleDrafter.Apply(approved.Text, read.Edits);
        if (drafted.DraftJson is not { } text)
        {
            var error = drafted.Errors[0];
            return Results.Refuse(locale, "rule.edit_failed", [("detail", error.Message)], "An edit could not be applied (" + error.Code + " at " + error.Path + "): " + error.Message + (FixHints.For(error.Code) is { } hint ? " " + hint : string.Empty), new JsonObject { ["failedAt"] = error.Path, ["failedCode"] = error.Code });
        }

        var draft = new StoredDraft(text, new DraftBase(id, baseRule.Version, AuthoringJson.Hash(DocumentKind.Rule, approved.Text)), [], AuthoringJson.CompactText(combined));
        context.Store.WriteDraft(DocumentKind.Rule, id, draft);
        var analysis = RuleAnalyzer.Analyze(context, catalog, id, draft, locale);
        return Results.Ok(analysis.Say, analysis.Data);
    }

    private static JsonObject Simulate(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var loaded = LoadRule(context, input, locale, out var refusal);
        if (loaded is null)
        {
            return refusal!;
        }

        var rule = loaded;
        var texts = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            foreach (var (name, value) in (JsonObject)input["facts"]!)
            {
                texts[name] = RuleEditReader.Text(value);
            }
        }
        catch (FormatException error)
        {
            return Results.Refuse(locale, "rule.facts_invalid", [("detail", error.Message)], error.Message);
        }

        var unknown = texts.Keys.Where(name => rule.Kind.Facts.All(fact => fact.Name != name)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            return Results.Refuse(locale, "rule.facts_invalid", [("detail", string.Join(", ", unknown))], "The rule has no fact called " + string.Join(", ", unknown) + ". Its facts are " + string.Join(", ", rule.Kind.Facts.Select(fact => fact.Name)) + ".");
        }

        var parsed = FactInput.ParseAll(rule.Kind, texts);
        if (!parsed.IsValid)
        {
            var details = string.Join("; ", parsed.Errors.Select(pair => pair.Key + ": " + pair.Value));
            return Results.Refuse(locale, "rule.facts_invalid", [("detail", details)], "A fact value does not fit its type: " + details, new JsonObject { ["factErrors"] = new JsonObject(parsed.Errors.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value))) });
        }

        DateOnly asOf;
        if (AuthoringJson.Text(input["asOf"]) is { } day)
        {
            if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
            {
                return Results.Refuse(locale, "rule.facts_invalid", [("detail", day)], "`asOf` must be a date written yyyy-MM-dd.");
            }
        }
        else
        {
            asOf = DateOnly.FromDateTime(context.Options.Clock.GetUtcNow().UtcDateTime);
        }

        var result = RuleSimulator.Simulate(rule, parsed.Facts, asOf);
        var view = RuleDescriber.Describe(rule, locale, RuleLabels.FromMeta(rule.Meta, locale));
        return Results.Ok(SimulationSay(result, view, locale), SimulationData(result, view));
    }

    private static string SimulationSay(SimulationResult result, RuleView view, WorkbenchLocale locale)
    {
        if (result.Failure is { } failure)
        {
            return AuthoringText.Text(locale, "say.rule.sim.failed", ("detail", failure.Message));
        }

        if (result.Outcome is Decided decided)
        {
            var row = view.Rows.Concat(view.DefaultRow is null ? [] : [view.DefaultRow]).FirstOrDefault(item => item.Key == decided.RowKey);
            if (row is null)
            {
                return AuthoringText.Text(locale, "say.rule.sim.decided");
            }

            var number = row.IsDefault ? AuthoringText.Text(locale, "say.rule.sim.defaultrow") : AuthoringText.Number(row.Index + 1);
            return AuthoringText.Text(locale, "say.rule.sim.row", ("row", number), ("sentence", row.Sentence));
        }

        return AuthoringText.Text(locale, "say.rule.sim.none");
    }

    private static JsonObject SimulationData(SimulationResult result, RuleView view)
    {
        var data = new JsonObject { ["succeeded"] = result.Succeeded };
        if (result.Failure is { } failure)
        {
            data["failure"] = new JsonObject { ["code"] = failure.Code, ["message"] = failure.Message, ["hint"] = FixHints.For(failure.Code) };
            return data;
        }

        switch (result.Outcome)
        {
            case Decided decided:
                data["decided"] = true;
                data["rowKey"] = decided.RowKey;
                data["matchedRows"] = Views.Strings(decided.MatchedRowKeys);
                data["outcome"] = AuthoringJson.TryParse(decided.OutcomeJson);
                break;
            default:
                data["decided"] = false;
                break;
        }

        data["rows"] = new JsonArray([.. result.Rows.Select(row => (JsonNode?)new JsonObject
        {
            ["key"] = row.Key,
            ["isDefault"] = row.IsDefault,
            ["matched"] = row.Matched,
            ["selected"] = row.Selected,
            ["sentence"] = view.Rows.Concat(view.DefaultRow is null ? [] : [view.DefaultRow]).FirstOrDefault(item => item.Key == row.Key)?.Sentence,
            ["conditions"] = new JsonArray([.. row.Conditions.Select(condition => (JsonNode?)new JsonObject { ["fact"] = condition.FactName, ["passed"] = condition.Passed })]),
        })]);
        return data;
    }

    private static JsonObject Test(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var rule = LoadRule(context, input, locale, out var refusal);
        if (rule is null)
        {
            return refusal!;
        }

        var tests = RuleTester.Run(rule);
        var view = RuleDescriber.Describe(rule, locale, RuleLabels.FromMeta(rule.Meta, locale));
        var say = tests.FailedCount == 0
            ? AuthoringText.Text(locale, "say.rule.test.pass", ("pass", AuthoringText.Number(tests.PassedCount)), ("uncovered", AuthoringText.Number(tests.UncoveredRowKeys.Count)))
            : AuthoringText.Text(locale, "say.rule.test.fail", ("n", AuthoringText.Number(tests.FailedCount)), ("name", FlowChangeDescriber.Quote(tests.Cases.First(item => !item.Passed).Name)));
        var data = RuleAnalyzer.TestsJson(tests);
        data["rule"] = view.Id;
        return Results.Ok(say, data);
    }

    private static BrainRule? LoadRule(ToolContext context, JsonObject input, WorkbenchLocale locale, out JsonObject? refusal)
    {
        refusal = null;
        var catalog = context.Catalog(locale, out refusal);
        if (catalog is null)
        {
            return null;
        }

        var id = AuthoringJson.Text(input["id"])!;
        if (input["draft"]?.GetValue<bool>() == true)
        {
            var draft = context.Store.ReadDraft(DocumentKind.Rule, id);
            if (draft is null)
            {
                refusal = Results.Refuse(locale, "draft.not_found", [("id", id)], "There is no draft of this rule. Make one with rule_draft_edit.");
                return null;
            }

            var loadedDraft = BrainRuleLoader.Load(draft.Text, catalog.Contracts);
            if (loadedDraft.Rule is null)
            {
                refusal = Results.Refuse(locale, "rule.not_loading", [("id", id)], "The draft does not load: " + string.Join("; ", loadedDraft.Errors.Take(3).Select(error => error.Code + " " + error.Path)));
            }

            return loadedDraft.Rule;
        }

        var approved = context.ApprovedRule(catalog, id);
        if (approved is null)
        {
            refusal = Results.Refuse(locale, "rule.not_found", [("id", id)], "There is no approved rule with this id. Call rule_list to see the ids.");
            return null;
        }

        if (approved.Rule is null)
        {
            refusal = Results.Refuse(locale, "rule.not_loading", [("id", id)], "The approved rule does not load against the catalog: " + string.Join("; ", approved.Errors.Take(3).Select(error => error.Code + " " + error.Path)));
        }

        return approved.Rule;
    }
}
