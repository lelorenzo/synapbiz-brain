using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>A change the loader or the validator refuses, and the warnings that do not block: in the form, under the row and in Read.</summary>
public sealed class EditRefusalTests
{
    /// <summary>
    /// A host whose loader reads the first draft with a contract that knows one more member and one more fact, and every later draft
    /// with the narrower contract, so that a form built from the first draft can offer what the loader then refuses.
    /// </summary>
    private static WorkbenchRule WiderThanItsLoader(WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var node = JsonNode.Parse(Fx.Read("tests/fixtures/workbench/intake.contract.json"))!;
        var facts = node["facts"]!.AsArray();
        facts.First(fact => (string?)fact!["name"] == "channel")!["allowedValues"]!.AsArray().Add("Chat");
        facts.Add(JsonNode.Parse("""{"kind":"sample.intake","name":"region","type":"Code","nullable":true,"allowedValues":[],"labelSource":null,"catalogFactId":null}"""));
        var wider = BrainContract.Parse(node.ToJsonString());
        var narrow = Fx.ContractOf("intake.first");
        var started = false;
        return Fx.FromText("intake.first", Fx.TextOf("intake.first"), wider, locale) with
        {
            LoadDraft = text =>
            {
                var contract = started ? narrow : wider;
                started = true;
                return BrainRuleLoader.Load(text, [contract]);
            },
        };
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void ARefusedConditionKeepsTheFormOpenMarksTheFactAndExplainsInAnAlertBox(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale, WiderThanItsLoader(locale));
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("q");
        var label = es ? "Canal de contacto" : "Contact channel";
        Ed.Choose(harness, label, es ? "es" : "is");
        Ed.Pick(harness, label, "Chat");
        var calls = harness.FocusCount;

        Ed.Apply(harness, locale);

        harness.Find(".wb-rowform").Should().NotBeNull("the form stays open");
        var box = harness.Find(".wb-rowform .wb-refusal");
        box.GetAttribute("role").Should().Be("alert");
        box.QuerySelector(".wb-refusal-t")!.TextContent.Should().Be(es ? "Este cambio no se puede aplicar" : "This change cannot be applied");
        box.QuerySelector("li")!.TextContent.Should().Be(es ? "Chat no es una opción de Canal de contacto." : "Chat is not an option of Contact channel.");
        box.QuerySelector(".wb-refusal-stays")!.TextContent.Should().Be(es ? "Su borrador sigue como estaba. Corrija el dato marcado o cancele." : "Your draft stays as it was. Correct the marked value or cancel.");
        Ed.Operator(harness, label).GetAttribute("aria-invalid").Should().Be("true");
        Ed.Line(harness, label).QuerySelector(".wb-in-err").Should().NotBeNull();
        Ed.Draft(harness).Steps.Should().BeEmpty();
        Ed.Draft(harness).Rule!.Table.Rows.Should().HaveCount(8);
        harness.FocusCount.Should().BeGreaterThan(calls);

        Ed.Pick(harness, label, es ? "el sitio web" : "the website");
        harness.FindAll(".wb-rowform .wb-refusal").Should().BeEmpty("the box goes when the person corrects the field");
        Ed.Apply(harness, locale);
        harness.FindAll(".wb-rowform").Should().BeEmpty();
    }

    [Fact]
    public void AProblemThePersonCannotCauseSaysSoAndOmitsTheWayOut()
    {
        using var harness = Ed.Open(rule: WiderThanItsLoader());
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("q");
        Ed.Choose(harness, "region", "is");
        Ed.Type(harness, "region", "north");

        Ed.Apply(harness);

        var box = harness.Find(".wb-rowform .wb-refusal");
        box.QuerySelector("li")!.TextContent.Should().Be("The change could not be applied because of an internal problem, not because of what you entered. Cancel and tell your support contact.");
        box.QuerySelector(".wb-refusal-stays").Should().BeNull();
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }

    [Fact]
    public void AnOutcomeTheLoaderRefusesMarksItsFieldAndNamesItByItsLabel()
    {
        var host = Fx.Editable("intake.first");
        var bare = host with { Labels = host.Labels with { Values = new Dictionary<string, IReadOnlyDictionary<string, string>>() } };
        using var harness = Ed.Open(rule: bare);
        Ed.Edit(harness, 2);
        Ed.OutcomeInput(harness, "Route").GetAttribute("type").Should().Be("text", "a field with no list of values is typed");

        Ed.OutcomeInput(harness, "Route").Change("Nope");
        Ed.Apply(harness);

        harness.Find(".wb-rowform .wb-refusal li").TextContent.Should().Be("Nope is not an option of Route.");
        Ed.OutcomeInput(harness, "Route").GetAttribute("aria-invalid").Should().Be("true");
    }

    private static WorkbenchRule UniqueWithOneRow(WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var node = JsonNode.Parse(Fx.TextOf("intake.unique"))!;
        var body = node["body"]!.AsObject();
        body["rows"]!.AsArray().RemoveAt(1);
        body["goldens"]!.AsArray().Clear();
        return Fx.EditableText("intake.unique", node.ToJsonString(), locale, Fx.ContractOf("intake.unique"));
    }

    [Fact]
    public void ARowThatNowOverlapsAnotherUnderAUniquePolicyIsRefusedInTheBoxWithBothNumbers()
    {
        using var harness = new Harness();
        harness.Render([UniqueWithOneRow()]);
        harness.Find(".wb-head button").Click();
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("phone");
        Ed.OutcomeInput(harness, "route").Change("Fast");
        Ed.Choose(harness, "channel", "is");
        Ed.PickValue(harness, "channel", "Phone");

        Ed.Apply(harness);

        harness.Find(".wb-rowform .wb-refusal li").TextContent.Should().Be("Rows 1 and 2 can match at once and this rule allows only one.");
        harness.Session.Work("intake.unique").Draft!.Steps.Should().BeEmpty();
    }

    [Fact]
    public void ARowThatMatchesAlwaysUnderFirstWarnsAboveTheButtonsAndStillApplies()
    {
        using var harness = Ed.Open();
        Ed.OpenMenu(harness, 1);
        Ed.MenuItem(harness, "Add a row below").Click();
        Ed.OutcomeInput(harness, "queue").Change("all");

        var warnings = harness.FindAll(".wb-rowform .wb-notice").Select(item => item.TextContent).ToList();
        warnings.Should().Contain("This row always matches. The rows below it will never decide.");
        warnings.Should().Contain(text => text.StartsWith("Row ", StringComparison.Ordinal) && text.EndsWith("will never decide: an earlier row always matches first.", StringComparison.Ordinal));
        harness.Find(".wb-rowform .wb-actions").PreviousElementSibling!.ClassList.Should().Contain("wb-notice", "the warnings sit right above the buttons");

        Ed.Apply(harness);

        harness.FindAll(".wb-rowform").Should().BeEmpty();
        Ed.Tags(Ed.Row(harness, 2)).Should().Be("New");
        harness.FindAll("[role=tabpanel] .wb-notice-row").Select(item => item.TextContent).Should().Contain(text => text.EndsWith("will never decide: an earlier row always matches first.", StringComparison.Ordinal));
    }

    [Fact]
    public void ARowWithTheSameConditionsAsAnotherSaysWhichOne()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 2);
        harness.Find(".wb-rowform .wb-link").Click();

        Ed.Choose(harness, "topic", "any value");
        Ed.Choose(harness, "Contact channel", "is");
        Ed.Pick(harness, "Contact channel", "the phone line");
        Ed.Choose(harness, "urgent", "is");

        harness.FindAll(".wb-rowform .wb-notice").Select(item => item.TextContent).Should().Contain("Row 1 has the same conditions.");
    }

    [Fact]
    public void AGapInCoverageThatTheChangeCreatesIsAWarningAndStaysInReadAboveTheRows()
    {
        var rule = Ed.Variant(node =>
        {
            var body = node["body"]!.AsObject();
            var rows = body["rows"]!.AsArray();
            while (rows.Count > 2)
            {
                rows.RemoveAt(2);
            }

            rows[0]!["conditions"] = JsonNode.Parse("""[{"fact":"urgent","op":"equals","value":{"type":"Boolean","value":true}}]""");
            rows[1]!["conditions"] = JsonNode.Parse("""[{"fact":"urgent","op":"equals","value":{"type":"Boolean","value":false}}]""");
            body.Remove("defaultRow");
            body["goldens"]!.AsArray().Clear();
        });
        using var harness = Ed.Open(rule: rule);
        harness.FindAll("[role=tabpanel] .wb-notice").Should().BeEmpty("the base has no gap");
        Ed.Edit(harness, 2);

        Ed.Pick(harness, "urgent", "Yes");

        harness.FindAll(".wb-rowform .wb-notice").Select(item => item.TextContent).Should().Contain("With this change, some data is decided by no row.");
        Ed.Apply(harness);
        harness.Find("[role=tabpanel] .wb-notice").TextContent.Should().Be("With this change, some data is decided by no row.");
        harness.FindAll("[role=tabpanel] .wb-notice-row").Should().NotBeEmpty("row 2 never decides");
    }

    [Fact]
    public void ADraftWhoseBaseChangedUnderneathIsReadOnlyAndHasNoEditControls()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 2);
        Ed.Pick(harness, "Route", "manual review");
        Ed.Apply(harness);

        harness.Render([Fx.EditableText("intake.first", Fx.TextOf("intake.first") + "\n")], session: harness.Session);

        Ed.Rows(harness).Should().HaveCount(9);
        harness.FindAll("[role=tabpanel] .wb-rowctl button").Should().BeEmpty();
        harness.FindAll("[role=tabpanel] .wb-row-add").Should().BeEmpty();
        Ed.Tags(Ed.Row(harness, 2)).Should().Be("Changed", "the tags stay so the reader can see what the draft holds");
    }
}
