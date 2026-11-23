using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The draft state and the apply / undo engine: the draft is the base with the edits of every step, and is never left unreadable.</summary>
public sealed class DraftEngineTests
{
    private static DraftStep Outcome(string key, string field, DecisionValue? value) =>
        new(StepKind.Row, key, [new SetOutcome(key, field, value)]);

    [Fact]
    public void StartingADraftReadsTheBaseWithTheVersionRaisedAndRunsItsCases()
    {
        var host = Fx.Editable("intake.first");

        var draft = DraftEngine.Start(host);

        draft.Rule.Should().NotBeNull();
        draft.Rule!.Version.Should().Be(host.Rule!.Version + 1);
        draft.Text.Should().Contain("\"version\": 2");
        draft.Steps.Should().BeEmpty();
        draft.HasChanges.Should().BeFalse();
        draft.Run!.FailedCount.Should().Be(0);
        draft.Run.Cases.Should().HaveCount(host.Rule.Goldens.Count);
        draft.Problems.Should().BeEmpty();
        draft.BaseHash.Should().Be(RuleReviewJson.ContentHash(host.RuleText!));
        draft.ViewDraft.Should().BeTrue();
    }

    [Fact]
    public void AStepThatTheLoaderReadsBecomesTheDraftAndRaisesTheRevision()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var revision = draft.Revision;

        var result = DraftEngine.Apply(host, draft, [Outcome("billing-topic", "route", DecisionValue.Enum("Manual"))]);

        result.IsOk.Should().BeTrue();
        draft.Steps.Should().ContainSingle();
        draft.Revision.Should().Be(revision + 1);
        draft.Rule!.Table.Rows.Single(row => row.Key == "billing-topic").Outcome.Should().Contain("Manual");
        draft.Text.Should().Contain("Manual");
        draft.HasChanges.Should().BeTrue();
        draft.CaseStepCount.Should().Be(0);
    }

    [Fact]
    public void AStepTheLoaderRefusesLeavesTheDraftAsItWasAndNamesTheRowAndTheField()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var before = (draft.Text, draft.Revision, draft.Steps.Count);

        var result = DraftEngine.Apply(host, draft, [Outcome("billing-topic", "route", DecisionValue.Enum("Nowhere"))]);

        result.IsOk.Should().BeFalse();
        result.Rule.Should().BeNull();
        (draft.Text, draft.Revision, draft.Steps.Count).Should().Be(before);
        var problem = result.Problems.Should().ContainSingle().Subject;
        problem.Code.Should().Be("brain.fact.enum_not_allowed");
        problem.Place.RowKey.Should().Be("billing-topic");
        problem.Place.RowNumber.Should().Be(2);
        problem.Place.OutcomeField.Should().Be("route");
        problem.Value.Should().Be("Nowhere");
    }

    [Fact]
    public void AConditionThatTheContractRefusesNamesItsFactItsOperatorAndItsValue()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var step = new DraftStep(StepKind.Row, "billing-topic", [new SetCondition("billing-topic", Condition.Equals("channel", DecisionValue.Enum("Fax")))]);

        var problem = DraftEngine.Apply(host, draft, [step]).Problems.Should().ContainSingle().Subject;

        problem.Code.Should().Be("brain.fact.enum_not_allowed");
        problem.Place.Fact.Should().Be("channel");
        problem.Fact.Should().Be("channel");
        problem.Op.Should().Be("equals");
        problem.Value.Should().Be("Fax");
    }

    [Fact]
    public void ADrafterRefusalIsAProblemWithNoPlace()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);

        var result = DraftEngine.Apply(host, draft, [new DraftStep(StepKind.Row, "nowhere", [new SetOutcome("nowhere", "route", DecisionValue.Enum("Fast"))])]);

        result.Text.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Code.Should().Be("workbench.draft.rowNotFound");
        draft.Steps.Should().BeEmpty();
    }

    [Fact]
    public void ARemovedRowThatACaseExpectsIsRefusedWithOneProblemForEachCase()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);

        var result = DraftEngine.Apply(host, draft, [new DraftStep(StepKind.Remove, "billing-topic", [new RemoveRow("billing-topic")])]);

        var problem = result.Problems.Should().ContainSingle().Subject;
        problem.Code.Should().Be("brain.golden.unknown_row");
        problem.Place.CaseName.Should().Be("A billing topic on the web goes to billing");
    }

    [Fact]
    public void ACaseWithAFactTheContractDoesNotKnowNamesTheFact()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var added = new ProposedCase("A made-up case", new Dictionary<string, DecisionValue> { ["nope"] = DecisionValue.Code("x") }, true, null, null);

        var problem = DraftEngine.Apply(host, draft, [new DraftStep(StepKind.CaseAdd, "A made-up case", [new AddCase(added)])]).Problems.Should().ContainSingle().Subject;

        problem.Code.Should().Be("decisions.facts.missing");
        problem.Place.CaseName.Should().Be("A made-up case");
        problem.Fact.Should().Be("nope");
    }

    [Fact]
    public void RemovingTheStepsRebuildsTheDraftFromWhatIsLeft()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        DraftEngine.Apply(host, draft, [Outcome("billing-topic", "route", DecisionValue.Enum("Manual"))]);
        DraftEngine.Apply(host, draft, [.. draft.Steps, Outcome("fresh-request", "route", DecisionValue.Enum("Manual"))]);
        draft.Steps.Should().HaveCount(2);

        var result = DraftEngine.Apply(host, draft, DraftEngine.Without(draft, step => step.Key == "billing-topic"));

        result.IsOk.Should().BeTrue();
        draft.Steps.Should().ContainSingle().Which.Key.Should().Be("fresh-request");
        draft.Rule!.Table.Rows.Single(row => row.Key == "billing-topic").Outcome.Should().NotContain("Manual");
    }

    [Fact]
    public void RemovingAStepThatALaterStepNeedsIsRefusedAndTheDraftStays()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var add = new DraftStep(StepKind.Add, "row-new", [new AddRow("row-new", "billing-topic", [], new Dictionary<string, DecisionValue> { ["route"] = DecisionValue.Enum("Fast"), ["queue"] = DecisionValue.Code("general") })]);
        DraftEngine.Apply(host, draft, [add]);
        DraftEngine.Apply(host, draft, [.. draft.Steps, new DraftStep(StepKind.Move, "email-invoice", [new MoveRow("email-invoice", "row-new")])]);
        var text = draft.Text;

        var result = DraftEngine.Apply(host, draft, DraftEngine.Without(draft, step => step.Kind == StepKind.Add));

        result.IsOk.Should().BeFalse();
        draft.Text.Should().Be(text);
        draft.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void ADraftTheHostsLoaderRefusesEvenWithNoEditKeepsTheReasonsAndHasNoRule()
    {
        var failing = Fx.Editable("intake.first") with { LoadDraft = _ => BrainRuleLoader.Load("{}", []) };

        var draft = DraftEngine.Start(failing);

        draft.Rule.Should().BeNull();
        draft.Text.Should().BeNull();
        draft.Run.Should().BeNull();
        draft.Findings.Should().BeNull();
        draft.Problems.Should().NotBeEmpty();
    }

    [Fact]
    public void AnOverlapTheBaseAlreadyHadDoesNotRefuseAnEdit()
    {
        var host = Fx.Editable("intake.unique");
        var baseOverlaps = DraftFindings.Of(host.Rule!).Overlaps;
        var draft = DraftEngine.Start(host);
        draft.BaseFindings.Overlaps.Should().BeEquivalentTo(baseOverlaps);

        var harmless = DraftEngine.Apply(host, draft, [Outcome("phone-any-urgency", "queue", DecisionValue.Code("phone"))]);
        harmless.IsOk.Should().BeTrue();
    }

    [Fact]
    public void ARowThatNowOverlapsAnotherUnderAUniquePolicyIsRefusedWithTheTwoRowKeys()
    {
        var text = Fx.Edited("intake.unique", root => root["body"]!["rows"]![1]!["conditions"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{"fact":"urgent","op":"equals","value":{"type":"Boolean","value":false}}""")));
        var host = Fx.EditableText("intake.unique", text);
        var draft = DraftEngine.Start(host);
        draft.BaseFindings.Overlaps.Should().BeEmpty();
        var step = new DraftStep(StepKind.Row, "phone-any-urgency", [new ClearCondition("phone-any-urgency", "urgent")]);

        var result = DraftEngine.Apply(host, draft, [step]);

        result.IsOk.Should().BeFalse();
        result.Rule.Should().NotBeNull("the candidate was read; only the overlap refuses it");
        var problem = result.Problems.Should().ContainSingle().Subject;
        problem.Code.Should().Be("decisions.validation.overlap");
        problem.RowKeys.Should().BeEquivalentTo("urgent-any-channel", "phone-any-urgency");
        draft.Steps.Should().BeEmpty();
    }

    [Fact]
    public void TheFindingsOfARuleAreItsUnreachableRowsItsOverlapsAndItsGap()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var shadow = new DraftStep(StepKind.Row, "billing-topic", [new ClearCondition("billing-topic", "topic"), new ClearCondition("billing-topic", "channel")]);

        var result = DraftEngine.Apply(host, draft, [shadow]);

        result.IsOk.Should().BeTrue();
        result.Findings!.Unreachable.Should().Contain("email-invoice");
        draft.BaseFindings.Unreachable.Should().NotContain("email-invoice");
        draft.Findings!.Unreachable.Should().Contain("email-invoice");
    }

    [Fact]
    public void TheViewTheChangeAndTheRunAreKeptUntilTheDraftOrTheLabelsChange()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var labels = host.Labels;

        var view = draft.View(WorkbenchLocale.En, labels);
        draft.View(WorkbenchLocale.En, labels).Should().BeSameAs(view);
        draft.View(WorkbenchLocale.Es, labels).Should().NotBeSameAs(view);
        var change = draft.Change(WorkbenchLocale.En, labels, labels);
        draft.Change(WorkbenchLocale.En, labels, labels).Should().BeSameAs(change);
        draft.Change(WorkbenchLocale.Es, labels, labels).Should().NotBeSameAs(change);

        DraftEngine.Apply(host, draft, [Outcome("billing-topic", "route", DecisionValue.Enum("Manual"))]);

        draft.Change(WorkbenchLocale.Es, labels, labels).Rows.Should().ContainSingle();
        draft.View(WorkbenchLocale.Es, labels).Should().NotBeSameAs(view);
    }

    [Fact]
    public void TheStateOfExportIsTrackedByRevision()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        draft.IsUnexported.Should().BeFalse("a draft with no change has nothing to lose");

        DraftEngine.Apply(host, draft, [Outcome("billing-topic", "route", DecisionValue.Enum("Manual"))]);
        draft.IsUnexported.Should().BeTrue();
        draft.IsExported.Should().BeFalse();

        draft.ExportedRevision = draft.Revision;
        draft.IsExported.Should().BeTrue();
        draft.IsUnexported.Should().BeFalse();
        draft.ChangedAfterExport.Should().BeFalse();

        DraftEngine.Apply(host, draft, [.. draft.Steps, Outcome("fresh-request", "route", DecisionValue.Enum("Manual"))]);
        draft.ChangedAfterExport.Should().BeTrue();
        draft.IsUnexported.Should().BeTrue();
    }

    [Fact]
    public void TheStepsKnowWhichOnesConcernTestCases()
    {
        var accept = new DraftStep(StepKind.CaseAccept, "a", []);
        var edit = new DraftStep(StepKind.CaseEdit, "b", []);
        var added = new DraftStep(StepKind.CaseAdd, "c", []);
        var row = new DraftStep(StepKind.Row, "d", []);
        new[] { accept, edit, added, row }.Select(step => step.IsCaseStep).Should().Equal(true, true, true, false);
        new[] { accept, edit, added, row }.Select(step => step.IsCaseDecision).Should().Equal(true, true, false, false);
    }

    [Fact]
    public void TheTypedValuesOfAnOutcomeFollowTheKindsFieldTypes()
    {
        var kind = Fx.Load("intake.first").Kind;

        OutcomeValues.Typed(kind, """{"route":"Fast","queue":"general","notify":true,"priority":3,"other":1}""").Should().Equal(new Dictionary<string, DecisionValue>
        {
            ["route"] = DecisionValue.Enum("Fast"),
            ["queue"] = DecisionValue.Code("general"),
            ["notify"] = DecisionValue.Boolean(true),
            ["priority"] = DecisionValue.Integer(3),
        });
        OutcomeValues.Typed(kind, """[{"route":"Manual"},{"route":"Fast"}]""").Should().Equal(new Dictionary<string, DecisionValue> { ["route"] = DecisionValue.Enum("Manual") });
        OutcomeValues.Typed(kind, "[]").Should().BeEmpty();
        OutcomeValues.Typed(kind, """{"priority":"many"}""").Should().BeEmpty();
        OutcomeValues.TextOf(System.Text.Json.Nodes.JsonNode.Parse("""{"amount":"12.50","currency":"USD"}""")!).Should().Be("12.50 USD");
        OutcomeValues.TextOf(System.Text.Json.Nodes.JsonNode.Parse("12.5")!).Should().Be("12.5");
        OutcomeValues.TextOf(System.Text.Json.Nodes.JsonNode.Parse("false")!).Should().Be("false");
    }

    [Fact]
    public void TheSessionCountsDraftsThatWereNeverExportedAndClearDropsThem()
    {
        var host = Fx.Editable("intake.first");
        var session = new WorkbenchSession();
        session.UnexportedDraftCount.Should().Be(0);
        var work = session.Work(host.Key);
        work.Draft = DraftEngine.Start(host);
        session.UnexportedDraftCount.Should().Be(0);

        DraftEngine.Apply(host, work.Draft, [Outcome("billing-topic", "route", DecisionValue.Enum("Manual"))]);
        session.UnexportedDraftCount.Should().Be(1);

        work.Draft.ExportedRevision = work.Draft.Revision;
        session.UnexportedDraftCount.Should().Be(0);
        DraftEngine.Apply(host, work.Draft, [.. work.Draft.Steps, Outcome("fresh-request", "route", DecisionValue.Enum("Manual"))]);
        session.UnexportedDraftCount.Should().Be(1);

        session.Clear();
        session.UnexportedDraftCount.Should().Be(0);
        session.Work(host.Key).Draft.Should().BeNull();
    }

    [Fact]
    public void GuidedEditingNeedsTheRuleItsTextAndALoader()
    {
        var host = Fx.Editable("intake.first");
        host.CanDraft.Should().BeTrue();
        (host with { LoadDraft = null }).CanDraft.Should().BeFalse();
        (host with { RuleText = null }).CanDraft.Should().BeFalse();
        (host with { Rule = null }).CanDraft.Should().BeFalse();
        Fx.Rule("intake.first").CanDraft.Should().BeFalse();
    }
}
