using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>The tools that read flows and the tools that write and check a flow draft.</summary>
internal static class FlowTools
{
    internal static IEnumerable<AuthoringTool> Create(ToolContext context)
    {
        yield return new AuthoringTool(
            "flow_list",
            "Lists the approved processes of the workspace (id, version, title, what starts them, step counts, whether their tests pass, route coverage, children) and the drafts in progress. Reads definitions only; it never touches a case.",
            ToolSchemas.Object([]),
            context,
            List);

        yield return new AuthoringTool(
            "flow_get",
            "Reads one process: its document, a plain-language description of every step, the diagram as nodes and edges, its tests and route coverage. Set `draft` to read the draft instead of the approved version. Definitions only.",
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The process id, for example visit.note_wait.")),
                    ("draft", ToolSchemas.Flag("True to read the draft; default false reads the approved document.")),
                ],
                "id"),
            context,
            Get);

        yield return new AuthoringTool(
            "flow_draft_save",
            "Stores a full changed document (brain.process@1) as the draft with the given id, then answers in one result: loader errors (each with a hint), catalog problems, a plain-language description, what changed against the approved version, test results, route coverage and the readiness verdict. "
            + "The draft is saved even when it has errors, so correct it and save again. The document must keep the same `id` and, to be proposed, a higher `version` than the approved one. Nothing is activated; only the draft changes. "
            + ToolSchemas.NoPatientText,
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The draft id; it is also the process id of the document.")),
                    ("document", ToolSchemas.DocumentArgument("The whole brain.process@1 document, as an object (or as JSON text).")),
                    ("basedOn", ToolSchemas.Id("The id of the approved process this draft comes from. Leave it out to keep the draft's base; a new draft of an id that has an approved version starts from that version.")),
                    ("knownGaps", ToolSchemas.KnownGapsArgument()),
                ],
                "id",
                "document"),
            context,
            Save);

        yield return new AuthoringTool(
            "flow_draft_patch",
            "Changes the stored draft with RFC 6902 JSON Patch operations (add, remove, replace, move, copy, test) and answers exactly as flow_draft_save does. Use it for a small change to a large document. Paths are JSON Pointers into the document, for example /body/steps/chase_note/queue. If an operation fails nothing is changed.",
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The draft id.")),
                    ("operations", ToolSchemas.Items(
                        "The operations, applied in order.",
                        new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["op"] = ToolSchemas.Enum("The operation.", "add", "remove", "replace", "move", "copy", "test"),
                                ["path"] = ToolSchemas.Text("A JSON Pointer.", 500),
                                ["from"] = ToolSchemas.Text("A JSON Pointer, for move and copy.", 500),
                                ["value"] = new JsonObject { ["description"] = "The value, for add, replace and test." },
                            },
                            ["required"] = AuthoringJson.Array(["op", "path"]),
                            ["additionalProperties"] = false,
                        })),
                    ("knownGaps", ToolSchemas.KnownGapsArgument()),
                ],
                "id",
                "operations"),
            context,
            Patch);

        yield return new AuthoringTool(
            "flow_draft_add_scenario",
            "Adds a test to the draft, either from a simulation (`fromSimulation`: the session and a name; the test replays what happened and expects where the case ended) or written in full (`scenario`). Answers as flow_draft_save does. "
            + ToolSchemas.NoPatientText,
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The draft id.")),
                    ("fromSimulation", ToolSchemas.Object(
                        [
                            ("session", ToolSchemas.Text("The simulation session token.", 40)),
                            ("name", ToolSchemas.FreeText("The name of the new test, a plain sentence about the situation.", 200)),
                        ],
                        "session",
                        "name").WithoutLanguageMember()),
                    ("scenario", ToolSchemas.AnyObject("A whole scenario object of the format: name, start, events, expect.")),
                ],
                "id"),
            context,
            AddScenario);
    }

    private static JsonObject WithoutLanguageMember(this JsonObject schema)
    {
        schema["properties"]!.AsObject().Remove("language");
        schema.Remove("$schema");
        return schema;
    }

    private static JsonObject List(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var approved = context.ApprovedFlows(catalog);
        var processes = approved.Select(item => item.Process).OfType<BrainProcess>().ToList();
        var entries = FlowLibrary.From(processes, locale);
        var flows = new JsonArray();
        foreach (var entry in entries)
        {
            flows.Add(new JsonObject
            {
                ["id"] = entry.Id,
                ["version"] = entry.Version,
                ["contentHash"] = entry.ContentHash,
                ["title"] = entry.Title,
                ["subject"] = entry.Subject,
                ["stages"] = Views.Strings(entry.StageLabels),
                ["triggers"] = Views.Strings(entry.Triggers),
                ["steps"] = new JsonObject(entry.StepCounts.Select(pair => new KeyValuePair<string, JsonNode?>(Views.Camel(pair.Key.ToString()), pair.Value))),
                ["scenarios"] = entry.ScenarioCount,
                ["scenariosPass"] = entry.ScenariosPass,
                ["coveredRoutes"] = entry.CoveredRoutes,
                ["totalRoutes"] = entry.TotalRoutes,
                ["children"] = new JsonArray([.. entry.Children.Select(child => (JsonNode?)new JsonObject { ["id"] = child.ProcessId, ["title"] = child.Title, ["missing"] = child.IsMissing })]),
                ["startedBy"] = Views.Strings(entry.StartedBy),
                ["rules"] = Views.Strings(entry.Rules),
            });
        }

        var broken = new JsonArray([.. approved.Where(item => item.Process is null).Select(item => (JsonNode?)new JsonObject
        {
            ["id"] = item.Id,
            ["errors"] = FlowAnalyzer.ErrorsJson(item.Errors, AuthoringJson.TryObject(item.Text), locale),
        })]);
        var drafts = new JsonArray();
        foreach (var id in context.Store.ListDrafts(DocumentKind.Flow))
        {
            var draft = context.Store.ReadDraft(DocumentKind.Flow, id);
            drafts.Add(new JsonObject
            {
                ["id"] = id,
                ["basedOn"] = draft?.Base is null ? null : new JsonObject { ["id"] = draft.Base.Id, ["version"] = draft.Base.Version },
            });
        }

        var say = AuthoringText.Text(locale, "say.flow.list", ("n", AuthoringText.Number(entries.Count)), ("d", AuthoringText.Number(drafts.Count)));
        return Results.Ok(say, new JsonObject { ["flows"] = flows, ["notLoading"] = broken, ["drafts"] = drafts });
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
            var draft = context.Store.ReadDraft(DocumentKind.Flow, id);
            if (draft is null)
            {
                return Results.Refuse(locale, "draft.not_found", [("id", id)], "There is no draft with this id. List the drafts with flow_list, or save one with flow_draft_save.");
            }

            var analysis = FlowAnalyzer.Analyze(context, catalog, id, draft, locale);
            var data = new JsonObject { ["source"] = "draft", ["document"] = AuthoringJson.TryParse(draft.Text) };
            foreach (var (name, value) in analysis.Data.ToArray())
            {
                analysis.Data.Remove(name);
                data[name] = value;
            }

            return Results.Ok(analysis.Say, data);
        }

        var approved = context.ApprovedFlow(catalog, id);
        if (approved is null)
        {
            return Results.Refuse(locale, "flow.not_found", [("id", id)], "There is no approved process with this id. Call flow_list to see the ids.");
        }

        var result = new JsonObject { ["source"] = "approved", ["id"] = id, ["document"] = AuthoringJson.TryParse(approved.Text), ["contentHash"] = AuthoringJson.Hash(DocumentKind.Flow, approved.Text) };
        if (approved.Process is not { } process)
        {
            result["loaded"] = false;
            result["errors"] = FlowAnalyzer.ErrorsJson(approved.Errors, AuthoringJson.TryObject(approved.Text), locale);
            return Results.Ok(AuthoringText.Text(locale, "say.flow.notloading", ("id", id)), result);
        }

        var known = context.ApprovedFlows(catalog).Select(item => item.Process).OfType<BrainProcess>().ToList();
        var report = FlowScenarioReport.Of(process, locale);
        var view = FlowDescriber.Describe(process, locale, known);
        result["loaded"] = true;
        result["version"] = process.Version;
        result["description"] = Views.Description(view);
        result["diagram"] = Views.Diagram(FlowGraph.From(process, locale, known));
        result["scenarios"] = Views.Scenarios(report);
        result["coverage"] = Views.Coverage(report.Coverage, []);
        var say = AuthoringText.Text(locale, "say.flow.get", ("title", FlowChangeDescriber.Quote(view.Title)), ("version", AuthoringText.Number(process.Version)), ("steps", AuthoringText.Number(view.Steps.Count)), ("pass", AuthoringText.Number(report.Scenarios.Count(item => item.Passed))), ("total", AuthoringText.Number(report.Scenarios.Count)));
        return Results.Ok(say, result);
    }

    private static JsonObject Save(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var id = AuthoringJson.Text(input["id"])!;
        var text = DocumentText(input["document"]);
        if (text.Length > context.Options.MaxDocumentLength)
        {
            return Results.Refuse(locale, "draft.too_large", [("max", AuthoringText.Number(context.Options.MaxDocumentLength))], "The document is longer than this workspace accepts. Make it smaller.");
        }

        if (DocumentIdProblem(id, text) is { } mismatch)
        {
            return Results.Refuse(locale, "draft.id_mismatch", [("id", id), ("found", mismatch)], "The draft id and the `id` member of the document must be the same. Change one of them.");
        }

        var existing = context.Store.ReadDraft(DocumentKind.Flow, id);
        var baseId = AuthoringJson.Text(input["basedOn"]);
        var baseDraft = existing?.Base;
        if (baseId is not null)
        {
            baseDraft = BaseOf(context, baseId);
            if (baseDraft is null)
            {
                return Results.Refuse(locale, "draft.base_not_found", [("id", baseId)], "There is no approved process with the id given in `basedOn`. Call flow_list to see the ids.");
            }
        }
        else if (existing is null && context.Store.ReadApproved(DocumentKind.Flow, id) is not null)
        {
            baseDraft = BaseOf(context, id);
        }

        var gaps = input["knownGaps"] is JsonArray listed ? ReadGaps(listed) : existing?.KnownGaps ?? [];
        var draft = new StoredDraft(text, baseDraft, gaps);
        context.Store.WriteDraft(DocumentKind.Flow, id, draft);
        return Answer(context, catalog, id, draft, locale, null);
    }

    private static JsonObject Patch(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var id = AuthoringJson.Text(input["id"])!;
        var existing = context.Store.ReadDraft(DocumentKind.Flow, id);
        if (existing is null)
        {
            return Results.Refuse(locale, "draft.not_found", [("id", id)], "There is no draft with this id. Save one with flow_draft_save first.");
        }

        var document = AuthoringJson.TryParse(existing.Text);
        if (document is null)
        {
            return Results.Refuse(locale, "draft.not_json", [("id", id)], "The stored draft is not JSON, so it cannot be patched. Save a whole document with flow_draft_save.");
        }

        JsonNode patched;
        try
        {
            patched = JsonPatch.Apply(document, (JsonArray)input["operations"]!);
        }
        catch (JsonPatchException error)
        {
            return Results.Refuse(locale, "patch.failed", [("n", AuthoringText.Number(error.Index + 1)), ("detail", error.Message)], "A patch operation failed and nothing was changed: " + error.Message, new JsonObject { ["failedOperation"] = error.Index, ["failedPath"] = error.Path });
        }

        var text = patched is JsonObject ? AuthoringJson.Format(patched) : AuthoringJson.CompactText(patched);
        if (text.Length > context.Options.MaxDocumentLength)
        {
            return Results.Refuse(locale, "draft.too_large", [("max", AuthoringText.Number(context.Options.MaxDocumentLength))], "The patched document is longer than this workspace accepts.");
        }

        var gaps = input["knownGaps"] is JsonArray listed ? ReadGaps(listed) : existing.KnownGaps;
        var draft = existing with { Text = text, KnownGaps = gaps };
        context.Store.WriteDraft(DocumentKind.Flow, id, draft);
        return Answer(context, catalog, id, draft, locale, null);
    }

    private static JsonObject AddScenario(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var id = AuthoringJson.Text(input["id"])!;
        var fromSimulation = input["fromSimulation"] as JsonObject;
        var written = input["scenario"] as JsonObject;
        if ((fromSimulation is null) == (written is null))
        {
            return Results.Refuse(locale, "scenario.source", [], "Give exactly one of `fromSimulation` and `scenario`.");
        }

        var existing = context.Store.ReadDraft(DocumentKind.Flow, id);
        if (existing is null)
        {
            return Results.Refuse(locale, "draft.not_found", [("id", id)], "There is no draft with this id. Save one with flow_draft_save first.");
        }

        var document = AuthoringJson.TryObject(existing.Text);
        var loaded = BrainProcessLoader.Load(existing.Text, context.FlowContext(catalog, id));
        if (document is null || loaded.Process is not { } process)
        {
            return Results.Refuse(locale, "scenario.draft_not_loaded", [("id", id)], "The draft does not load, so a test cannot be added to it. Fix the loader errors first.");
        }

        JsonObject scenario;
        if (fromSimulation is not null)
        {
            var made = ScenarioFromSimulation(context, id, existing, fromSimulation, locale);
            if (made.Refusal is not null)
            {
                return made.Refusal;
            }

            scenario = made.Scenario!;
        }
        else
        {
            scenario = (JsonObject)written!.DeepClone();
        }

        var name = AuthoringJson.Text(scenario["name"]);
        var scenarios = (document["body"] as JsonObject)?["scenarios"] as JsonArray;
        if (name is not null && scenarios is not null && scenarios.Any(item => AuthoringJson.Text((item as JsonObject)?["name"]) == name))
        {
            return Results.Refuse(locale, "scenario.name_taken", [("name", FlowChangeDescriber.Quote(name))], "A test with this name exists. Choose another name.");
        }

        var problems = ProcessOutline.CheckScenario(process, scenario);
        if (problems.Count > 0)
        {
            return Results.Refuse(locale, "scenario.invalid", [("detail", locale == WorkbenchLocale.Es ? problems[0].MessageEs : problems[0].MessageEn)], "The scenario does not fit the document: " + string.Join("; ", problems.Take(5).Select(problem => problem.Path + " " + problem.MessageEn)), new JsonObject { ["errors"] = FlowAnalyzer.ErrorsJson(problems, scenario, locale) });
        }

        var body = (JsonObject)document["body"]!;
        var list = scenarios ?? new JsonArray();
        list.Add(scenario);
        body["scenarios"] = list;
        var draft = existing with { Text = AuthoringJson.Format(document) };
        context.Store.WriteDraft(DocumentKind.Flow, id, draft);
        return Answer(context, catalog, id, draft, locale, name);
    }

    private static (JsonObject? Scenario, JsonObject? Refusal) ScenarioFromSimulation(ToolContext context, string id, StoredDraft draft, JsonObject request, WorkbenchLocale locale)
    {
        var token = AuthoringJson.Text(request["session"])!;
        var session = context.Sessions.Touch(token);
        if (session is null)
        {
            return (null, Results.Refuse(locale, "simulation.not_found", [], "No simulation with this token is alive (it may have expired). Start a new one with flow_draft_simulate."));
        }

        if (session.DraftId != id || session.DraftHash != AuthoringJson.Hash(DocumentKind.Flow, FlowAnalyzer.FormatOrText(draft.Text)))
        {
            return (null, Results.Refuse(locale, "simulation.draft_changed", [], "The draft changed after this simulation started, so its steps may not fit. Run the case again on the current draft."));
        }

        try
        {
            return (JsonNode.Parse(session.Simulation.ToScenarioJson(AuthoringJson.Text(request["name"])!)) as JsonObject, null);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return (null, Results.Refuse(locale, "scenario.not_writable", [("detail", error.Message)], "This simulation cannot be written as a test: " + error.Message));
        }
    }

    private static JsonObject Answer(ToolContext context, AuthoringCatalog catalog, string id, StoredDraft draft, WorkbenchLocale locale, string? addedScenario)
    {
        var analysis = FlowAnalyzer.Analyze(context, catalog, id, draft, locale);
        var data = analysis.Data;
        if (addedScenario is not null)
        {
            var result = analysis.Report?.Scenarios.FirstOrDefault(item => item.Name == addedScenario);
            data["scenarioAdded"] = new JsonObject { ["name"] = addedScenario, ["passed"] = result?.Passed };
            var say = AuthoringText.Text(locale, result is { Passed: true } ? "say.scenario.added" : "say.scenario.addedfailing", ("name", FlowChangeDescriber.Quote(addedScenario)));
            return Results.Ok(say + " " + analysis.Say, data);
        }

        return Results.Ok(analysis.Say, data);
    }

    private static string DocumentText(JsonNode? document) => document switch
    {
        JsonObject node => AuthoringJson.Format(node),
        JsonNode node when AuthoringJson.Text(node) is { } text => AuthoringJson.TryObject(text) is { } parsed ? AuthoringJson.Format(parsed) : text,
        _ => string.Empty,
    };

    private static string? DocumentIdProblem(string id, string text)
    {
        var found = AuthoringJson.Text(AuthoringJson.TryObject(text)?["id"]);
        return found is not null && !string.Equals(found, id, StringComparison.Ordinal) ? found : null;
    }

    private static DraftBase? BaseOf(ToolContext context, string id)
    {
        var text = context.Store.ReadApproved(DocumentKind.Flow, id);
        if (text is null)
        {
            return null;
        }

        var version = AuthoringJson.Whole(AuthoringJson.TryObject(text)?["version"]) ?? 0;
        return new DraftBase(id, version, AuthoringJson.Hash(DocumentKind.Flow, text));
    }

    private static List<KnownGap> ReadGaps(JsonArray listed) =>
        [.. listed.OfType<JsonObject>().Select(item => new KnownGap(
            AuthoringJson.Text(item["check"])!,
            AuthoringJson.Text(item["step"])!,
            AuthoringJson.Text(item["route"]),
            AuthoringJson.Text(item["reason"])!))];
}
