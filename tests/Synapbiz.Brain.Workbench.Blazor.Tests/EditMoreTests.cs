using System.Text.Json.Nodes;
using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>Outcome inputs by type, the sentences a host knows, the sentence about another open form and the undo of the last change.</summary>
public sealed class EditMoreTests
{
    /// <summary>The intake rule with a money and a date outcome field the rows do not use, and the sentences the host knows for the queue.</summary>
    private static WorkbenchRule WithMoreOutcomes()
    {
        var node = JsonNode.Parse(Fx.Read("tests/fixtures/workbench/intake.contract.json"))!;
        var outcomes = node["kinds"]![0]!["outcomes"]!.AsArray();
        outcomes.Add(JsonNode.Parse("""{"name":"fee","type":"Money","nullable":true,"labelSource":null}"""));
        outcomes.Add(JsonNode.Parse("""{"name":"due on","type":"Date","nullable":true,"labelSource":null}"""));
        var contract = BrainContract.Parse(node.ToJsonString());
        var host = Fx.FromText("intake.first", Fx.TextOf("intake.first"), contract, WorkbenchLocale.En) with
        {
            LoadDraft = text => BrainRuleLoader.Load(text, [contract]),
            OutcomeTexts = new Dictionary<string, IReadOnlyList<WorkbenchOutcomeText>>
            {
                ["queue"] = [new WorkbenchOutcomeText(DecisionValue.Code("urgent"), "The urgent desk answers within the hour."), new WorkbenchOutcomeText(DecisionValue.Code("billing"), "Billing answers by the next day.")],
            },
        };
        return host;
    }

    [Fact]
    public void AMoneyAndADateOutcomeAreTypedWithTheirOwnInputsAndComeBackWhenTheRowIsEditedAgain()
    {
        using var harness = Ed.Open(rule: WithMoreOutcomes());
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.PickValue(harness, "queue", "urgent");
        Ed.OutcomeInput(harness, "fee").Change("12.5");
        Ed.Line(harness, "fee").QuerySelector("select.wb-in-currency")!.Change("USD");
        Ed.OutcomeInput(harness, "due on").GetAttribute("type").Should().Be("date");
        Ed.OutcomeInput(harness, "due on").Change("2026-04-01");

        Ed.Apply(harness);

        var created = harness.Session.Work("intake.first").Draft!.Rule!.Table.Rows[^1];
        created.Outcome.Should().Contain("12.5").And.Contain("2026-04-01").And.Contain("urgent");
        Ed.Edit(harness, 9);
        Ed.OutcomeInput(harness, "fee").GetAttribute("value").Should().Be("12.5");
        Ed.Line(harness, "fee").QuerySelector("select.wb-in-currency")!.GetAttribute("value").Should().Be("USD");
        Ed.OutcomeInput(harness, "due on").GetAttribute("value").Should().Be("2026-04-01");
        Ed.Type(harness, "fee", "oops");
    }

    [Fact]
    public void AFieldWhoseValuesTheHostKnowsIsASelectOfTheFullSentencesAndTheFirstOneIsChosenAtFirst()
    {
        using var harness = Ed.Open(rule: WithMoreOutcomes());
        harness.Find("[role=tabpanel] .wb-row-add button").Click();

        var select = Ed.Line(harness, "queue").QuerySelector("select")!;

        Ed.Join(select.QuerySelectorAll("option").Select(item => item.TextContent)).Should().Be("The urgent desk answers within the hour.|Billing answers by the next day.");
        select.GetAttribute("value").Should().Be("urgent");
        Ed.PickValue(harness, "queue", "billing");
        Ed.Preview(harness).Should().NotBeEmpty();
        harness.FindAll("[role=tabpanel] .wb-rowform").Should().ContainSingle();
    }

    [Fact]
    public void AMoneyAmountThatIsNotANumberIsAFieldErrorUnderItsInput()
    {
        using var harness = Ed.Open(rule: WithMoreOutcomes());
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "fee").Change("lots");

        Ed.Apply(harness);

        Ed.Line(harness, "fee").QuerySelector(".wb-errt")!.TextContent.Should().NotBeEmpty();
        Ed.OutcomeInput(harness, "fee").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void AFormThatIsOpenAndTouchedSaysWhichRowMustBeAppliedFirstWhateverItIs()
    {
        using var harness = Ed.Open();
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("late");

        Ed.Edit(harness, 1);
        Harness.Normalise(harness.Find(".wb-rowform [role=status]").TextContent).Should().Be("Apply or cancel the new row before editing another one.");
        Ed.Cancel(harness);

        Ed.Button(Ed.Row(harness, 9), "Edit").Click();
        Ed.Pick(harness, "Route", "manual review");
        Ed.Edit(harness, 1);
        Harness.Normalise(harness.Find(".wb-rowform [role=status]").TextContent).Should().Be("Apply or cancel the fallback row before editing another one.");
    }

    [Fact]
    public void TheCurrencyOfAnAmountInAConditionCanBeChosen()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 6);
        harness.Find(".wb-rowform .wb-link").Click();

        Ed.Line(harness, "amount").QuerySelector("select.wb-in-currency")!.Change("USD");

        harness.Find(".wb-rowform").Should().NotBeNull();
        Ed.Draft(harness).Form!.Touched.Should().BeTrue();
    }

    [Fact]
    public void TheLastChangeIsTakenOffFromTheBoxOfAnUnreadableDraft()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 2);
        Ed.Pick(harness, "Route", "manual review");
        Ed.Apply(harness);
        var draft = Ed.Draft(harness);
        draft.Unreadable(new RebuildResult(null, null, null, [new DraftProblem("workbench.draft.rowNotFound", "/edits/0", new DraftLocation(null, null, false, null, null, null), [], null, null, null)]));
        harness.Session.Touch();
        harness.FindAll(".wb-refusal").Should().ContainSingle();

        Ed.Button(harness.Find(".wb-refusal"), "Undo the last change").Click();

        harness.FindAll(".wb-refusal").Should().BeEmpty();
        draft.Steps.Should().BeEmpty();
        Ed.Rows(harness).Should().HaveCount(9);
    }

    [Fact]
    public void AnUndoThatTheLoaderRefusesShowsItsReasonUnderTheRowAndKeepsTheDraft()
    {
        using var harness = Ed.Open();
        Ed.OpenMenu(harness, 1);
        Ed.MenuItem(harness, "Add a row below").Click();
        Ed.OutcomeInput(harness, "queue").Change("a");
        Ed.Apply(harness);
        Ed.OpenMenu(harness, 2);
        Ed.MenuItem(harness, "Add a row below").Click();
        Ed.OutcomeInput(harness, "queue").Change("b");
        Ed.Apply(harness);
        var steps = Ed.Draft(harness).Steps.Count;

        Ed.OpenMenu(harness, 2);
        Ed.MenuItem(harness, "Remove the row").Click();

        Ed.Draft(harness).Steps.Count.Should().Be(steps, "the second new row hangs from the first, so the first stays");
        harness.FindAll("[role=tabpanel] .wb-refusal").Should().ContainSingle();
    }
}
