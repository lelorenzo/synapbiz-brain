using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class SimulateTests
{
    private static Harness Open(WorkbenchRule rule, WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var harness = new Harness();
        harness.Render([rule], locale);
        harness.Open(WorkbenchTab.Simulate);
        return harness;
    }

    private static Harness OpenIntake(WorkbenchLocale locale = WorkbenchLocale.En) => Open(Fx.Rule("intake.first", locale), locale);

    private static string Run(WorkbenchLocale locale) => locale == WorkbenchLocale.Es ? "Simular" : "Simulate";

    private static (string Channel, string Age) IntakeLabels(WorkbenchLocale locale) =>
        locale == WorkbenchLocale.Es ? ("Canal de contacto", "Antigüedad en días") : ("Contact channel", "Age in days");

    /// <summary>Fills the intake form with a valid fact set and runs it.</summary>
    private static void FillIntake(Harness harness, WorkbenchLocale locale, string channel, string topic, string urgent, string age, bool run = true)
    {
        var (channelLabel, ageLabel) = IntakeLabels(locale);
        harness.Type(channelLabel, channel);
        harness.Type("topic", topic);
        harness.Choose("urgent", urgent);
        harness.Type(ageLabel, age);
        if (run)
        {
            harness.Press(Run(locale));
        }
    }

    private static List<string> Words(Harness harness) =>
        [.. harness.FindAll(".wb-live .wb-row").Select(item => item.LastElementChild!.TextContent)];

    [Fact]
    public void EveryFactHasTheInputOfItsType()
    {
        using var harness = OpenIntake();

        var channel = harness.Input("Contact channel");
        channel.TagName.Should().Be("SELECT");
        channel.QuerySelectorAll("option").Select(item => item.TextContent + "=" + item.GetAttribute("value"))
            .Should().Equal("the website=Web", "the phone line=Phone", "email=Email");
        harness.Input("topic").GetAttribute("type").Should().Be("text");
        harness.Input("Age in days").GetAttribute("inputmode").Should().Be("numeric");
        harness.Input("rating").GetAttribute("inputmode").Should().Be("decimal");
        harness.Input("received on").GetAttribute("type").Should().Be("date");

        var group = harness.Group("urgent");
        group.GetAttribute("role").Should().Be("radiogroup");
        group.QuerySelectorAll("label").Select(item => item.TextContent).Should().Equal("Yes", "No");

        var money = harness.Input("amount");
        money.GetAttribute("placeholder").Should().Be("Amount");
        var currency = harness.Find("input.wb-in-currency");
        currency.GetAttribute("maxlength").Should().Be("3");
        currency.GetAttribute("placeholder").Should().Be("Three-letter currency");
    }

    [Fact]
    public void ReferenceFactsAreNotShown()
    {
        using var harness = OpenIntake();
        harness.FindAll("label.wb-label, span.wb-label").Select(item => item.TextContent)
            .Should().Equal("Contact channel", "topic", "urgent", "Age in days", "rating", "amount", "received on");
    }

    [Fact]
    public void ARequiredEnumStartsOnItsFirstValue()
    {
        using var harness = OpenIntake();
        harness.Input("Contact channel").GetAttribute("value").Should().Be("Web");
    }

    [Fact]
    public void OptionalEnumAndBooleanFactsOfferNoValue()
    {
        using var harness = Open(Fx.FromText("intake.first", Fx.TextOf("intake.first"), Fx.OptionalContract()));
        harness.Input("Contact channel").QuerySelectorAll("option").Select(item => item.TextContent)
            .Should().Equal("No value", "the website", "the phone line", "email");
        harness.Group("urgent").QuerySelectorAll("label").Select(item => item.TextContent).Should().Equal("Yes", "No", "No value");
        harness.Group("urgent").GetAttribute("aria-describedby").Should().BeNull();
    }

    [Fact]
    public void RequiredFactsSayRequiredInTheirHintAndNumbersSayWhatTheyTake()
    {
        using var harness = OpenIntake();
        harness.FindAll(".wb-hint").Select(item => item.TextContent).Should().Contain(
            ["Required.", "Whole number. Required.", "Number, with a decimal point.", "Amount and a three-letter currency."]);
        harness.Input("Age in days").GetAttribute("aria-describedby").Should().NotBeNull();
        harness.Input("received on").GetAttribute("aria-describedby").Should().BeNull();
    }

    [Fact]
    public void BeforeAnyRunTheResultAreaSaysWhatToDoAndIsLive()
    {
        using var harness = OpenIntake();
        harness.Find(".wb-live").GetAttribute("aria-live").Should().Be("polite");
        harness.Find(".wb-live").TextContent.Trim().Should().Be("Enter the example data and press Simulate.");
        harness.Find(".wb-band").TextContent.Should().Be("SimulationSimulated with example data. This is not a real case, and nothing changes.");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Result: escalation, Urgent queue, yes, 1", "Row 1 decided because urgent (yes) is yes and Contact channel (the phone line) is the phone line.")]
    [InlineData(WorkbenchLocale.Es, "Resultado: escalado, Cola urgente, sí, 1", "Decidió la fila 1 porque urgent (sí) es sí y Canal de contacto (la línea telefónica) es la línea telefónica.")]
    public void ADecidedResultHasItsHeadingItsReasonAndItsOutcome(WorkbenchLocale locale, string heading, string why)
    {
        using var harness = OpenIntake(locale);
        FillIntake(harness, locale, "Phone", "billing-late", "true", "100");

        var result = harness.Find(".wb-result");
        result.ClassList.Should().NotContain("wb-result-none");
        result.QuerySelector(".wb-h2")!.TextContent.Should().Be(heading);
        result.QuerySelector(".wb-result-head .wb-pill")!.TextContent.Should().Be(locale == WorkbenchLocale.Es ? "Simulación" : "Simulation");
        result.QuerySelector(".wb-why")!.TextContent.Should().Be(why);
        string[] terms = locale == WorkbenchLocale.Es ? ["Ruta", "queue", "notify", "priority"] : ["Route", "queue", "notify", "priority"];
        string[] details = locale == WorkbenchLocale.Es ? ["escalado", "Cola urgente", "sí", "1"] : ["escalation", "Urgent queue", "yes", "1"];
        result.QuerySelectorAll("dt").Select(item => item.TextContent).Should().Equal(terms);
        result.QuerySelectorAll("dd").Select(item => item.TextContent).Should().Equal(details);
    }

    [Fact]
    public void EveryRowIsExplainedWithAWordAndItsConditions()
    {
        using var harness = OpenIntake();
        FillIntake(harness, WorkbenchLocale.En, "Phone", "billing-late", "true", "100");

        Words(harness).Should().Equal(
            "Decides", "Matches, does not decide", "Does not apply", "Does not apply", "Does not apply",
            "Does not apply", "Matches, does not decide", "Does not apply", "Does not apply");
        var rows = harness.FindAll(".wb-live .wb-row");
        rows[0].QuerySelector(".wb-sent")!.TextContent.Should().StartWith("If urgent is yes and Contact channel is the phone line, then Route is escalation");
        rows[1].QuerySelector(".wb-sent")!.TextContent.Should().Be("If topic starts with bill and Contact channel is not email…");
        rows[1].QuerySelector(".wb-sub")!.TextContent.Should().Be("An earlier row decided first.");
        rows[0].ClassList.Should().Contain("wb-win");
        rows[1].ClassList.Should().NotContain("wb-win");
        rows[0].QuerySelectorAll(".wb-cond").Select(item => Harness.Normalise(item.TextContent)).Should().Equal(
            "Met Urgent is yes. You entered yes.",
            "Met Contact channel is the phone line. You entered the phone line.",
            "Met Topic can be anything. You entered billing-late.");
        rows[2].QuerySelectorAll(".wb-cond .wb-pill").Select(item => item.TextContent).Should().Equal("Not met", "Not met");
        Harness.Normalise(rows[3].QuerySelectorAll(".wb-cond")[1].TextContent).Should().Be("Met Rating is empty. You did not enter this.");
        rows[^1].QuerySelector(".wb-num")!.TextContent.Should().Be("–");
        harness.FindAll(".wb-live .wb-row .wb-pill").Where(item => item.TextContent == "Decides").Should().ContainSingle()
            .Which.ClassList.Should().Contain("wb-pill-ok");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Row 4 decided because Age in days (3) is at least 0 and less than 7 and rating is empty.")]
    [InlineData(WorkbenchLocale.Es, "Decidió la fila 4 porque Antigüedad en días (3) es como mínimo 0 y menor que 7 y rating está vacío.")]
    public void TheReasonNamesTheTypedValueRightAfterTheLabelOfEachCondition(WorkbenchLocale locale, string why)
    {
        using var harness = OpenIntake(locale);
        FillIntake(harness, locale, "Email", "x", "false", "3");
        harness.Find(".wb-why").TextContent.Should().Be(why);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "No row matched, so the default case applies.", "Result: standard handling, General queue", "Decides")]
    [InlineData(WorkbenchLocale.Es, "Ninguna fila se cumplió, así que aplica el caso por defecto.", "Resultado: trámite estándar, Cola general", "Decide")]
    public void WhenNoRowMatchesTheDefaultRowDecides(WorkbenchLocale locale, string why, string heading, string word)
    {
        using var harness = OpenIntake(locale);
        FillIntake(harness, locale, "Email", "x", "false", "10", run: false);
        harness.Type("rating", "1");
        harness.Press(Run(locale));

        harness.Find(".wb-why").TextContent.Should().Be(why);
        harness.Find(".wb-result .wb-h2").TextContent.Should().Be(heading);
        Words(harness)[^1].Should().Be(word);
        harness.FindAll(".wb-live .wb-win").Should().ContainSingle().Which.ClassList.Should().Contain("wb-row");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Rows 2 and 3 decided.", "Result: Middle score; Late stage", "Row 2", "Row 3")]
    [InlineData(WorkbenchLocale.Es, "Decidieron las filas 2 y 3.", "Resultado: Puntaje medio; Etapa avanzada", "Fila 2", "Fila 3")]
    public void ACollectRuleShowsOneGroupPerDecidingRow(WorkbenchLocale locale, string why, string heading, string first, string second)
    {
        using var harness = Open(Fx.Rule("readiness", locale), locale);
        harness.Type(locale == WorkbenchLocale.Es ? "Etapa" : "Stage", "Review");
        harness.Type(locale == WorkbenchLocale.Es ? "Puntaje" : "Score", "65");
        harness.Press(Run(locale));

        harness.Find(".wb-why").TextContent.Should().Be(why);
        harness.Find(".wb-result .wb-h2").TextContent.Should().Be(heading);
        harness.FindAll(".wb-group-note").Select(item => item.TextContent).Should().Equal(first, second);
        harness.FindAll(".wb-result dl").Should().HaveCount(2);
        harness.FindAll(".wb-live .wb-win").Should().HaveCount(2);
    }

    [Fact]
    public void ACollectRuleWithOneDecidingRowSaysWhichRowAndWhy()
    {
        using var harness = Open(Fx.Rule("readiness"));
        harness.Type("Stage", "Intake");
        harness.Type("Score", "20");
        harness.Press("Simulate");

        harness.Find(".wb-why").TextContent.Should().Be("Row 1 decided because Score (20) is between 0 and 49.");
        harness.FindAll(".wb-group-note").Should().BeEmpty();
    }

    [Fact]
    public void ARowWithNothingToSayGivesAnEveryCaseReason()
    {
        using var harness = Open(Fx.FromText("readiness", Fx.AlwaysRow()));
        harness.Type("Stage", "Intake");
        harness.Type("Score", "95");
        harness.Press("Simulate");

        harness.Find(".wb-why").TextContent.Should().Be("Row 1 decided because it applies in every case.");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "The rule does not decide with this data")]
    [InlineData(WorkbenchLocale.Es, "La regla no decide con estos datos")]
    public void WhenNothingMatchesAndThereIsNoDefaultTheRuleDoesNotDecide(WorkbenchLocale locale, string heading)
    {
        using var harness = Open(Fx.Rule("readiness", locale), locale);
        harness.Type(locale == WorkbenchLocale.Es ? "Etapa" : "Stage", "Intake");
        harness.Type(locale == WorkbenchLocale.Es ? "Puntaje" : "Score", "95");
        harness.Press(Run(locale));

        var result = harness.Find(".wb-result");
        result.ClassList.Should().Contain("wb-result-none");
        result.QuerySelector(".wb-h2")!.TextContent.Should().Be(heading);
        result.QuerySelector(".wb-why").Should().BeNull();
        result.QuerySelectorAll("dl").Should().BeEmpty();
        Words(harness).Should().OnlyContain(word => word == "Does not apply" || word == "No aplica");
    }

    [Theory]
    [InlineData("Phone", "Two rows match this data and the rule allows only one. This is a defect in the rule.")]
    public void TwoRowsAtOnceUnderTheUniquePolicyIsRefusedWithoutARowList(string channel, string line)
    {
        using var harness = Open(Fx.Rule("intake.unique"));
        harness.Type("channel", channel);
        harness.Type("topic", "x");
        harness.Choose("urgent", "true");
        harness.Type("age days", "3");
        harness.Press("Simulate");

        var alert = harness.Find("[role=alert]");
        alert.QuerySelector(".wb-h2")!.TextContent.Should().Be("Could not simulate");
        alert.QuerySelector(".wb-refuse-line")!.TextContent.Should().Be("The rule engine did not accept this data, so there is no result. " + line);
        alert.QuerySelectorAll("button").Select(item => item.TextContent).Should().Equal("Add to the review");
        harness.FindAll(".wb-live .wb-row").Should().BeEmpty();
        harness.FindAll(".wb-result-head").Should().BeEmpty();
        harness.Find(".wb-live").TextContent.Should().NotContain("decisions.");
    }

    [Fact]
    public void AMissingFactTheFormCannotAskForIsRefusedByTheEngineAndNamedByItsLabel()
    {
        var rule = Fx.FromText("intake.first", Fx.TextOf("intake.first"), Fx.RequiredAccountContract());
        using var harness = Open(rule);
        FillIntake(harness, WorkbenchLocale.En, "Phone", "x", "false", "3");

        harness.Find("[role=alert] .wb-refuse-line").TextContent.Should().Be(
            "The rule engine did not accept this data, so there is no result. A required value is missing: account.");
        harness.Find("[role=alert] .wb-small").TextContent.Should().Be("Correct the marked value and simulate again.");
        harness.FindAll("[role=alert] button").Should().BeEmpty();
        harness.FindAll(".wb-live .wb-row").Should().BeEmpty();
    }

    [Fact]
    public void FactsThatAreTooLongAreRefusedByTheEngine()
    {
        using var harness = OpenIntake();
        FillIntake(harness, WorkbenchLocale.En, "Phone", new string('x', 5000), "false", "3");
        harness.Find("[role=alert] .wb-refuse-line").TextContent.Should().EndWith("The data is too long for this rule.");
        harness.Find("[role=alert] .wb-small").TextContent.Should().Be("Correct the marked value and simulate again.");
    }

    [Fact]
    public void ARuleWithFactsComputedElsewhereIsRefusedBecauseNoResolverIsGiven()
    {
        using var harness = Open(Fx.Composed());
        FillIntake(harness, WorkbenchLocale.En, "Phone", "x", "false", "3");

        harness.Find("[role=alert] .wb-refuse-line").TextContent.Should().EndWith("This rule depends on others that could not be computed here.");
        harness.Find("[role=alert] .wb-small").TextContent.Should().Be("Note it in the review of this rule.");
        harness.FindAll(".wb-live .wb-row").Should().BeEmpty();
        harness.FindAll("label.wb-label, span.wb-label").Select(item => item.TextContent).Should().NotContain("derived");
    }

    [Fact]
    public void AddingARefusalToTheReviewOpensTheReviewTabWithTheWholeRuleMarkedToFix()
    {
        using var harness = Open(Fx.Rule("intake.unique"));
        harness.Type("channel", "Phone");
        harness.Type("topic", "x");
        harness.Choose("urgent", "true");
        harness.Type("age days", "3");
        harness.Press("Simulate");
        harness.Press("Add to the review");

        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Review);
        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[1].GetAttribute("aria-pressed").Should().Be("true");
        harness.FindAll("textarea").Should().ContainSingle();
        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().NotBeEmpty();
        harness.Find(".wb-detail").TextContent.Should().NotContain("multiple_matches");
    }

    [Fact]
    public void EachBadValueGetsItsOwnMessageAndNoResultIsDrawn()
    {
        var cases = new (string Label, string Value, string Message)[]
        {
            ("Age in days", "abc", "Enter a whole number, for example 12."),
            ("rating", "x", "Enter a number, for example 7.5."),
            ("received on", "not-a-date", "Choose a date."),
            ("amount", "12", "Enter an amount and a three-letter currency, for example 12.50 USD."),
            ("Contact channel", "Bogus", "Choose one of the options."),
            ("topic", "", "This is required."),
        };

        foreach (var (label, value, message) in cases)
        {
            using var harness = OpenIntake();
            FillIntake(harness, WorkbenchLocale.En, "Phone", "x", "false", "3", run: false);
            harness.Type(label, value);
            harness.Press("Simulate");

            var field = harness.Input(label);
            field.GetAttribute("aria-invalid").Should().Be("true", label);
            field.ClassList.Should().Contain("wb-in-err");
            var error = harness.FindAll(".wb-errt").Should().ContainSingle(label).Subject;
            error.TextContent.Should().Be(message);
            field.GetAttribute("aria-describedby")!.Split(' ').Should().Contain(error.Id);
            harness.FindAll(".wb-result, .wb-live .wb-row, [role=alert]").Should().BeEmpty(label);
            harness.Find(".wb-live").TextContent.Trim().Should().Be("Correct the marked value and simulate again.");
        }
    }

    [Fact]
    public void ABooleanThatIsNeitherYesNorNoIsRefusedAndTheGroupIsDescribedByTheMessage()
    {
        using var harness = OpenIntake();
        FillIntake(harness, WorkbenchLocale.En, "Phone", "x", "false", "3", run: false);
        harness.Group("urgent").QuerySelector("input")!.Change("maybe");
        harness.Press("Simulate");

        var group = harness.Group("urgent");
        group.GetAttribute("aria-invalid").Should().Be("true");
        harness.Find(".wb-errt").TextContent.Should().Be("Choose Yes or No.");
        group.GetAttribute("aria-describedby")!.Split(' ').Should().Contain(harness.Find(".wb-errt").Id);
    }

    [Fact]
    public void AnAmountWithItsCurrencyIsAcceptedAndRead()
    {
        using var harness = OpenIntake();
        FillIntake(harness, WorkbenchLocale.En, "Web", "x", "false", "30", run: false);
        harness.Type("rating", "2");
        harness.Type("amount", "250.00");
        harness.Find("input.wb-in-currency").Change("usd");
        harness.Press("Simulate");

        harness.Find(".wb-result .wb-h2").TextContent.Should().Be("Result: manual review, Review queue, 4");
        harness.Find(".wb-why").TextContent.Should().Contain("amount (250.00 USD)");
    }

    [Fact]
    public void ChangingAFieldAfterARunMarksTheResultStale()
    {
        using var harness = OpenIntake();
        FillIntake(harness, WorkbenchLocale.En, "Phone", "billing-late", "true", "100");
        harness.FindAll(".wb-stale-note").Should().BeEmpty();

        harness.Type("Age in days", "99");

        harness.Find(".wb-stale-note").TextContent.Should().Be("The data changed. Simulate again.");
        harness.Find(".wb-result").ClassList.Should().Contain("wb-stale");
        harness.FindAll(".wb-live .wb-row.wb-stale").Should().NotBeEmpty();

        harness.Press("Simulate");
        harness.FindAll(".wb-stale-note").Should().BeEmpty();
    }

    [Fact]
    public void ARefusalGoesStaleToo()
    {
        using var harness = Open(Fx.Rule("intake.unique"));
        harness.Type("channel", "Phone");
        harness.Type("topic", "x");
        harness.Choose("urgent", "true");
        harness.Type("age days", "3");
        harness.Press("Simulate");
        harness.Type("age days", "4");

        harness.Find("[role=alert]").ClassList.Should().Contain("wb-stale");
        harness.Find("[role=alert] .wb-stale-note").TextContent.Should().Be("The data changed. Simulate again.");
    }

    [Fact]
    public void ClearEmptiesTheFieldsAndTheResult()
    {
        using var harness = OpenIntake();
        FillIntake(harness, WorkbenchLocale.En, "Phone", "billing-late", "true", "100");
        harness.Press("Clear");

        harness.Input("topic").GetAttribute("value").Should().BeNullOrEmpty();
        harness.Input("Contact channel").GetAttribute("value").Should().Be("Web");
        harness.FindAll(".wb-result").Should().BeEmpty();
        harness.Find(".wb-live").TextContent.Trim().Should().Be("Enter the example data and press Simulate.");
    }

    [Fact]
    public void ACaseCanBeLoadedIntoTheFields()
    {
        using var harness = OpenIntake();
        var select = harness.Find("select[id$=-load]");
        select.QuerySelectorAll("option").Select(item => item.TextContent).First().Should().Be("Choose a case…");
        select.QuerySelectorAll("option").Should().HaveCount(10);

        select.Change("0");

        harness.Input("Contact channel").GetAttribute("value").Should().Be("Phone");
        harness.Input("topic").GetAttribute("value").Should().Be("billing-late");
        harness.Input("Age in days").GetAttribute("value").Should().Be("100");
        harness.Group("urgent").QuerySelector("input[value=true]")!.HasAttribute("checked").Should().BeTrue();
        harness.Find(".wb-live").TextContent.Trim().Should().Be("Enter the example data and press Simulate.");

        harness.Press("Simulate");
        harness.Find(".wb-result .wb-h2").TextContent.Should().StartWith("Result: escalation");
    }

    [Fact]
    public void LoadingACaseWithMoneyAndNoValueFillsAndEmptiesTheFields()
    {
        using var harness = OpenIntake();
        harness.Find("select[id$=-load]").Change("5");
        harness.Input("amount").GetAttribute("value").Should().Be("250");
        harness.Find("input.wb-in-currency").GetAttribute("value").Should().Be("USD");
        harness.Input("rating").GetAttribute("value").Should().Be("2");

        harness.Find("select[id$=-load]").Change("0");
        harness.Input("amount").GetAttribute("value").Should().BeNullOrEmpty();
        harness.Find("select[id$=-load]").Change("");
        harness.Find("select[id$=-load]").Change("99");
    }

    [Fact]
    public void LoadingACaseGivesAnOptionalEnumItsEmptyValueWhenTheCaseHasNone()
    {
        using var harness = Open(Fx.FromText("intake.first", Fx.TextOf("intake.first"), Fx.OptionalContract()));
        harness.Find("select[id$=-load]").Change("0");
        harness.Input("Contact channel").GetAttribute("value").Should().Be("Phone");
    }

    [Fact]
    public void WhenThereAreManyRowsTheOnesThatDoNotApplyAreFoldedUntilAsked()
    {
        using var harness = Open(Fx.FromText("readiness", Fx.ManyRows()));
        harness.Type("Stage", "Intake");
        harness.Type("Score", "20");
        harness.Press("Simulate");

        harness.FindAll(".wb-live .wb-row").Should().HaveCount(1);
        harness.Find(".wb-live").TextContent.Should().Contain("Show the 14 rows that do not apply");

        harness.Press("Show the 14 rows that do not apply");

        harness.FindAll(".wb-live .wb-row").Should().HaveCount(15);
        harness.Find(".wb-live").TextContent.Should().NotContain("Show the");
    }

    [Fact]
    public void ARunAfterShowingAllRowsFoldsThemAgain()
    {
        using var harness = Open(Fx.FromText("readiness", Fx.ManyRows()));
        harness.Type("Stage", "Intake");
        harness.Type("Score", "20");
        harness.Press("Simulate");
        harness.Press("Show the 14 rows that do not apply");
        harness.Press("Simulate");
        harness.FindAll(".wb-live .wb-row").Should().HaveCount(1);
    }

    [Fact]
    public void TheSimulationUsesTheHostsClockForItsDate()
    {
        var seen = new List<DateTimeOffset>();
        using var harness = new Harness();
        harness.Now = () =>
        {
            seen.Add(Fx.Moment);
            return Fx.Moment;
        };
        harness.Render([Fx.Rule("intake.first")]);
        harness.Open(WorkbenchTab.Simulate);
        FillIntake(harness, WorkbenchLocale.En, "Phone", "x", "false", "3");
        seen.Should().NotBeEmpty();
    }

    [Fact]
    public void TheFieldsAndTheResultSurviveSwitchingRuleAndTab()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.first"), Fx.Rule("readiness")]);
        harness.Open(WorkbenchTab.Simulate);
        FillIntake(harness, WorkbenchLocale.En, "Phone", "billing-late", "true", "100");

        harness.FindAll(".wb-rule")[1].Click();
        harness.Open(WorkbenchTab.Read);
        harness.FindAll(".wb-rule")[0].Click();
        harness.Open(WorkbenchTab.Simulate);

        harness.Input("topic").GetAttribute("value").Should().Be("billing-late");
        harness.Find(".wb-result .wb-h2").TextContent.Should().StartWith("Result: escalation");
    }

    [Fact]
    public void TheFirstFieldWithAnErrorIsFocused()
    {
        using var harness = OpenIntake();
        harness.Press("Simulate");
        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().NotBeEmpty();
    }
}
