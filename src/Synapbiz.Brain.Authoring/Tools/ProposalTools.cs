using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>The tools that turn a ready draft into a proposal file, and read the proposals. A proposal changes nothing.</summary>
internal static class ProposalTools
{
    private const string FlowPrefix = "flow.";
    private const string RulePrefix = "rule.";

    internal static IEnumerable<AuthoringTool> Create(ToolContext context)
    {
        yield return new AuthoringTool(
            "flow_propose",
            "Writes a brain.process-proposal@1 file for a draft that is ready (Brain accepts it, every test passes, every readiness check passes). It holds the base, the whole new document and its hash, the change list in plain language, the test results and coverage, who asked and when. "
            + "It returns where the file is and the text a person will read. A proposal changes nothing: a person applies it elsewhere, and a process proposal always waits for review by our team. Never tell the person the change is live. "
            + ToolSchemas.NoPatientText,
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The draft id.")),
                    ("summary", ToolSchemas.FreeText("Why the change is wanted, in one or two sentences, in the person's words.", 1000)),
                    ("requestedBy", ToolSchemas.Text("The name of the person who asks for the change (staff, never a patient).", 120)),
                ],
                "id",
                "summary",
                "requestedBy"),
            context,
            FlowPropose);

        yield return new AuthoringTool(
            "rule_propose",
            "Writes a brain.rule-proposal@1 file for a rule draft whose tests all pass. The file also says whether the proposal needs review by our team: false when it stays within the facts the rule reads and the outcomes it gives, true otherwise. "
            + "A proposal changes nothing; the host applies an approved proposal. Never tell the person the change is live. "
            + ToolSchemas.NoPatientText,
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The rule draft id.")),
                    ("summary", ToolSchemas.FreeText("Why the change is wanted, in one or two sentences, in the person's words.", 1000)),
                    ("requestedBy", ToolSchemas.Text("The name of the person who asks for the change (staff, never a patient).", 120)),
                ],
                "id",
                "summary",
                "requestedBy"),
            context,
            RulePropose);

        yield return new AuthoringTool(
            "proposal_list",
            "Lists the proposals written so far: name, kind (process or rule), id, version, who asked, when, and whether it needs review.",
            ToolSchemas.Object([]),
            context,
            ListProposals);

        yield return new AuthoringTool(
            "proposal_get",
            "Reads one proposal: the text a person reads, in the asked language, and the stored document.",
            ToolSchemas.Object([("name", ToolSchemas.Id("The proposal name from proposal_list or from the answer of the propose tool."))], "name"),
            context,
            GetProposal);
    }

    private static JsonObject FlowPropose(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var id = AuthoringJson.Text(input["id"])!;
        var draft = context.Store.ReadDraft(DocumentKind.Flow, id);
        if (draft is null)
        {
            return Results.Refuse(locale, "draft.not_found", [("id", id)], "There is no draft with this id. Save one with flow_draft_save first.");
        }

        var analysis = FlowAnalyzer.Analyze(context, catalog, id, draft, locale);
        if (!analysis.Ready || analysis.Process is not { } process || analysis.Report is not { } report)
        {
            var reasons = analysis.Readiness.Findings.Select(finding => finding.Text(locale)).ToList();
            return Results.Refuse(
                locale,
                "proposal.not_ready",
                [("n", AuthoringText.Number(reasons.Count)), ("first", reasons.Count > 0 ? reasons[0] : string.Empty)],
                "Only a ready draft can be proposed. Fix what `reasons` lists, save the draft again and check that `ready` is true.",
                new JsonObject { ["reasons"] = Views.Strings(reasons) });
        }

        var formatted = FlowProposalJson.FormatDocument(draft.Text);
        var proposal = new FlowProposal
        {
            ProcessId = process.Id,
            Version = process.Version,
            BaseVersion = draft.Base?.Version,
            BaseContentHash = draft.Base?.ContentHash,
            Author = AuthoringJson.Text(input["requestedBy"])!,
            CreatedAt = Whole(context.Options.Clock.GetUtcNow()),
            Language = locale,
            Summary = AuthoringJson.Text(input["summary"])!,
            NeedsReview = true,
            DocumentJson = formatted,
            DocumentHash = FlowProposalJson.HashOf(formatted),
            ChangesEn = ChangesIn(context, catalog, draft, process, WorkbenchLocale.En),
            ChangesEs = ChangesIn(context, catalog, draft, process, WorkbenchLocale.Es),
            Tests = [.. report.Scenarios.Select(result => new FlowProposalTest(result.Name, result.Passed, result.Failure))],
            TotalRoutes = report.Coverage.TotalRoutes,
            CoveredRoutes = report.Coverage.CoveredRoutes,
            NeverTaken = [.. report.Coverage.NeverTaken.Select(route => new FlowProposalRoute(route.Step, route.Label))],
            KnownGaps = draft.KnownGaps,
        };

        var approvedText = draft.Base is null ? context.Store.ReadApproved(DocumentKind.Flow, id) : context.Store.ReadApproved(DocumentKind.Flow, draft.Base.Id);
        var problems = FlowProposalCheck.Against(proposal, context.FlowContext(catalog, id), approvedText);
        if (problems.Count > 0)
        {
            var first = problems[0];
            return Results.Refuse(locale, "proposal.inconsistent", [("code", first.Code)], first.Message + " " + (FixHints.For(first.Code) ?? string.Empty), new JsonObject { ["problems"] = new JsonArray([.. problems.Select(problem => (JsonNode?)new JsonObject { ["code"] = problem.Code, ["path"] = problem.Path, ["message"] = problem.Message, ["hint"] = FixHints.For(problem.Code) })]) });
        }

        var name = FreeName(context, FlowPrefix + process.Id + ".v" + AuthoringText.Number(process.Version));
        var location = context.Store.WriteProposal(name, FlowProposalJson.Serialize(proposal));
        var text = ProposalText.Flow(proposal, locale);
        var data = new JsonObject { ["proposal"] = new JsonObject { ["name"] = name, ["location"] = location, ["kind"] = "process", ["id"] = process.Id, ["version"] = process.Version, ["needsReview"] = true, ["applied"] = false }, ["text"] = text };
        return Results.Ok(AuthoringText.Text(locale, "say.propose.flow", ("id", "`" + process.Id + "`"), ("location", location)), data);
    }

    private static FlowProposalChanges ChangesIn(ToolContext context, AuthoringCatalog catalog, StoredDraft draft, BrainProcess process, WorkbenchLocale locale)
    {
        var before = draft.Base is null ? null : context.ApprovedFlow(catalog, draft.Base.Id)?.Process;
        if (before is null)
        {
            return new FlowProposalChanges(FlowChangeDescriber.DescribeAdded(process, locale), [], []);
        }

        var view = FlowChangeDescriber.Describe(before, process, locale);
        return new FlowProposalChanges(view.Summary, [.. view.Items.Select(item => item.Text)], [.. view.Behaviour.Select(item => item.Text)]);
    }

    private static JsonObject RulePropose(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var id = AuthoringJson.Text(input["id"])!;
        var draft = context.Store.ReadDraft(DocumentKind.Rule, id);
        if (draft?.Base is null)
        {
            return Results.Refuse(locale, "draft.not_found", [("id", id)], "There is no draft of this rule. Make one with rule_draft_edit.");
        }

        var approvedText = context.Store.ReadApproved(DocumentKind.Rule, draft.Base.Id);
        if (approvedText is null || !string.Equals(AuthoringJson.Hash(DocumentKind.Rule, approvedText), draft.Base.ContentHash, StringComparison.Ordinal))
        {
            return Results.Refuse(locale, "proposal.base_stale", [("id", id)], "The approved rule changed after the draft was made. Run rule_draft_edit again with `reset` true and the edits you want.");
        }

        var analysis = RuleAnalyzer.Analyze(context, catalog, id, draft, locale);
        if (!analysis.Ready || analysis.Rule is not { } rule || analysis.Base is not { } before)
        {
            return Results.Refuse(locale, "proposal.not_ready", [("n", "1"), ("first", analysis.Say)], "Only a rule draft that loads and whose tests all pass can be proposed. Fix the draft with rule_draft_edit first.", new JsonObject { ["reasons"] = Views.Strings([analysis.Say]) });
        }

        var proposal = new RuleProposal(id, draft.Base.Version, RuleReviewJson.ContentHash(approvedText), AuthoringJson.Text(input["requestedBy"])!, Whole(context.Options.Clock.GetUtcNow()), AuthoringJson.Text(input["summary"])!, draft.Text);
        var file = new RuleProposalFile(proposal, analysis.NeedsReview, analysis.Reasons, ChangeSummary(before, rule, WorkbenchLocale.En), ChangeSummary(before, rule, WorkbenchLocale.Es));
        var name = FreeName(context, RulePrefix + id + ".v" + AuthoringText.Number(rule.Version));
        var location = context.Store.WriteProposal(name, RuleProposalFiles.Serialize(file));
        var data = new JsonObject
        {
            ["proposal"] = new JsonObject { ["name"] = name, ["location"] = location, ["kind"] = "rule", ["id"] = id, ["version"] = rule.Version, ["needsReview"] = file.NeedsReview, ["applied"] = false },
            ["text"] = ProposalText.Rule(file, locale),
        };
        var key = file.NeedsReview ? "say.propose.rule.review" : "say.propose.rule.clinic";
        return Results.Ok(AuthoringText.Text(locale, key, ("id", "`" + id + "`"), ("location", location)), data);
    }

    private static string ChangeSummary(Synapbiz.Brain.Spec.BrainRule before, Synapbiz.Brain.Spec.BrainRule after, WorkbenchLocale locale) =>
        RuleChangeDescriber.Describe(before, after, RuleLabels.FromMeta(before.Meta, locale), RuleLabels.FromMeta(after.Meta, locale), locale).Summary;

    private static JsonObject ListProposals(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var items = new JsonArray();
        foreach (var name in context.Store.ListProposals())
        {
            items.Add(Summarize(name, context.Store.ReadProposal(name)));
        }

        return Results.Ok(AuthoringText.Text(locale, "say.proposal.list", ("n", AuthoringText.Number(items.Count))), new JsonObject { ["proposals"] = items });
    }

    private static JsonObject Summarize(string name, string? text)
    {
        var item = new JsonObject { ["name"] = name };
        if (text is not null && FlowProposalJson.Parse(text) is { Proposal: { } flow })
        {
            item["kind"] = "process";
            item["id"] = flow.ProcessId;
            item["version"] = flow.Version;
            item["author"] = flow.Author;
            item["createdAt"] = flow.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            item["needsReview"] = true;
        }
        else if (text is not null && RuleProposalFiles.Parse(text) is { File: { } rule })
        {
            item["kind"] = "rule";
            item["id"] = rule.Proposal.RuleId;
            item["version"] = rule.Proposal.BaseVersion + 1;
            item["author"] = rule.Proposal.Author;
            item["createdAt"] = rule.Proposal.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            item["needsReview"] = rule.NeedsReview;
        }
        else
        {
            item["kind"] = "unreadable";
        }

        return item;
    }

    private static JsonObject GetProposal(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var name = AuthoringJson.Text(input["name"])!;
        var text = context.Store.ReadProposal(name);
        if (text is null)
        {
            return Results.Refuse(locale, "proposal.not_found", [("name", name)], "There is no proposal with this name. Call proposal_list to see the names.");
        }

        var summary = Summarize(name, text);
        string? readable = null;
        if (FlowProposalJson.Parse(text).Proposal is { } flow)
        {
            readable = ProposalText.Flow(flow, locale);
        }
        else if (RuleProposalFiles.Parse(text).File is { } rule)
        {
            readable = ProposalText.Rule(rule, locale);
        }

        if (readable is null)
        {
            return Results.Refuse(locale, "proposal.unreadable", [("name", name)], "The file is not a proposal this version of Brain can read.");
        }

        summary["text"] = readable;
        summary["document"] = AuthoringJson.TryParse(text);
        return Results.Ok(AuthoringText.Text(locale, "say.proposal.get", ("name", name)), summary);
    }

    private static string FreeName(ToolContext context, string baseName)
    {
        var taken = context.Store.ListProposals().ToHashSet(StringComparer.Ordinal);
        var name = baseName;
        var n = 2;
        while (taken.Contains(name))
        {
            name = baseName + "-" + AuthoringText.Number(n);
            n++;
        }

        return name;
    }

    private static DateTimeOffset Whole(DateTimeOffset instant) => new(instant.Ticks - (instant.Ticks % TimeSpan.TicksPerSecond), instant.Offset);
}
