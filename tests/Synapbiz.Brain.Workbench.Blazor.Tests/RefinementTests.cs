using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The refinements of the design review (W-B2): review line, value texts, headline, money, folding and the note rule.</summary>
public sealed class RefinementTests
{
    private const string FastRoute = """{"route":"Fast"}""";

    private static bool IsEs(WorkbenchLocale locale) => locale == WorkbenchLocale.Es;

    private static string Run(WorkbenchLocale locale) => IsEs(locale) ? "Simular" : "Simulate";

    private static Row RowOf(string key, params Condition[] conditions) => new(key, conditions, FastRoute);

    private static Condition AnyOf(string fact) => new() { FactName = fact, Operator = Operator.Any };

    /// <summary>The first intake rule with another table (and, optionally, another kind); its other parts stay.</summary>
    private static WorkbenchRule Custom(DecisionTable table, WorkbenchLocale locale = WorkbenchLocale.En, DecisionKindDescriptor? kind = null)
    {
        var source = Fx.Rule("intake.first", locale);
        return source with { Key = "custom", Rule = Fx.WithTable(source.Rule!, table, kind) };
    }

    private static WorkbenchRule FirstWins(WorkbenchLocale locale, params Row[] rows) => Custom(new DecisionTable(HitPolicy.First, rows, null), locale);

    private static Harness Open(WorkbenchRule rule, WorkbenchTab tab, WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var harness = new Harness();
        harness.Render([rule], locale);
        harness.Open(tab);
        return harness;
    }

    /// <summary>Fills the valid part of the intake form and runs the simulation.</summary>
    private static void Simulate(Harness harness, WorkbenchLocale locale, string topic = "x", string age = "30", string rating = "", string amount = "")
    {
        harness.Type(IsEs(locale) ? "Canal de contacto" : "Contact channel", "Web");
        harness.Type("topic", topic);
        harness.Choose("urgent", "false");
        harness.Type(IsEs(locale) ? "Antigüedad en días" : "Age in days", age);
        if (rating.Length > 0)
        {
            harness.Type("rating", rating);
        }

        if (amount.Length > 0)
        {
            harness.Type("amount", amount);
        }

        harness.Press(Run(locale));
    }

    private static List<string> Lines(Harness harness, int row = 0) =>
        [.. harness.FindAll(".wb-live .wb-row")[row].QuerySelectorAll(".wb-cond").Select(item => Harness.Normalise(item.TextContent))];

    // ----- 1. review line in the test table ------------------------------------------------------------------------------

    private static IReadOnlyList<IElement> CaseRows(Harness harness) => harness.FindAll(".wb-table tbody tr:not(.wb-review-line)");

    private static IElement ReviewTrigger(Harness harness, int index) => CaseRows(harness)[index].QuerySelectorAll("td")[4].QuerySelector(".wb-cell-gap button")!;

    [Theory]
    [InlineData(WorkbenchLocale.En, "Review")]
    [InlineData(WorkbenchLocale.Es, "Revisar")]
    public void EachCaseRowHasOneSmallSecondaryReviewButtonAndNoLineYet(WorkbenchLocale locale, string word)
    {
        using var harness = Open(Fx.Rule("intake.first", locale), WorkbenchTab.Tests, locale);

        var triggers = Enumerable.Range(0, 9).Select(index => ReviewTrigger(harness, index)).ToList();
        triggers.Should().OnlyContain(button => button.TextContent == word);
        triggers.Should().OnlyContain(button => button.ClassList.Contains("wb-btn-secondary") && button.ClassList.Contains("wb-btn-sm"));
        triggers.Should().OnlyContain(button => button.GetAttribute("aria-expanded") == "false" && !string.IsNullOrEmpty(button.GetAttribute("aria-controls")));
        harness.FindAll(".wb-review-line, .wb-table .wb-rv, .wb-table textarea").Should().BeEmpty();
    }

    [Fact]
    public void AProposedCaseHasNoReviewButton()
    {
        using var harness = Open(Fx.Rule("readiness"), WorkbenchTab.Tests);
        harness.Press("Add a case");
        harness.Type("Case name", "Mine");
        harness.Type("Stage", "Intake");
        harness.Type("Score", "95");
        harness.FindAll(".wb-radios-stack input[type=checkbox]")[3].Change(true);
        harness.Press("Save case");

        CaseRows(harness).Should().HaveCount(4);
        CaseRows(harness)[3].QuerySelectorAll("button").Should().BeEmpty();
    }

    [Fact]
    public void PressingReviewOpensAFullWidthLineWithTheControlAndPressingAgainClosesIt()
    {
        using var harness = Open(Fx.Rule("intake.first"), WorkbenchTab.Tests);
        var trigger = ReviewTrigger(harness, 2);

        trigger.Click();

        var line = CaseRows(harness)[2].NextElementSibling!;
        line.ClassList.Should().Contain("wb-review-line");
        line.QuerySelector("td")!.GetAttribute("colspan").Should().Be("5");
        line.Id.Should().Be(ReviewTrigger(harness, 2).GetAttribute("aria-controls"));
        ReviewTrigger(harness, 2).GetAttribute("aria-expanded").Should().Be("true");
        line.QuerySelectorAll(".wb-rv button").Select(item => item.TextContent).Should().Equal("Confirm", "Fix", "Question");
        line.QuerySelector(".wb-rv")!.GetAttribute("aria-label").Should().StartWith("Review of the case ");
        harness.FindAll(".wb-review-line").Should().ContainSingle();

        ReviewTrigger(harness, 2).Click();

        harness.FindAll(".wb-review-line").Should().BeEmpty();
        ReviewTrigger(harness, 2).GetAttribute("aria-expanded").Should().Be("false");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, 0, "Confirmed", "wb-pill-ok")]
    [InlineData(WorkbenchLocale.En, 1, "Fix", "wb-pill-warn")]
    [InlineData(WorkbenchLocale.En, 2, "Question", "wb-pill-warn")]
    [InlineData(WorkbenchLocale.Es, 0, "Confirmada", "wb-pill-ok")]
    [InlineData(WorkbenchLocale.Es, 1, "Corregir", "wb-pill-warn")]
    [InlineData(WorkbenchLocale.Es, 2, "Pregunta", "wb-pill-warn")]
    public void OnceAStateIsSetItsPillReplacesTheButtonAndTogglesTheSameLine(WorkbenchLocale locale, int part, string word, string tone)
    {
        using var harness = Open(Fx.Rule("intake.first", locale), WorkbenchTab.Tests, locale);
        ReviewTrigger(harness, 0).Click();

        CaseRows(harness)[0].NextElementSibling!.QuerySelectorAll(".wb-rv button")[part].Click();

        var pill = ReviewTrigger(harness, 0);
        pill.TextContent.Should().Be(word);
        pill.ClassList.Should().Contain("wb-pill").And.Contain(tone);
        pill.TagName.Should().Be("BUTTON");
        pill.GetAttribute("aria-expanded").Should().Be("true");
        pill.GetAttribute("aria-controls").Should().Be(CaseRows(harness)[0].NextElementSibling!.Id);

        pill.Click();
        harness.FindAll(".wb-review-line").Should().BeEmpty();
        ReviewTrigger(harness, 0).GetAttribute("aria-expanded").Should().Be("false");
        ReviewTrigger(harness, 0).TextContent.Should().Be(word);

        ReviewTrigger(harness, 0).Click();
        harness.FindAll(".wb-review-line").Should().ContainSingle();
    }

    [Fact]
    public void TheLineKeepsItsNoteFieldForAFixAndOffersAddNoteForAConfirmation()
    {
        using var harness = Open(Fx.Rule("intake.first"), WorkbenchTab.Tests);
        ReviewTrigger(harness, 0).Click();
        var buttons = () => CaseRows(harness)[0].NextElementSibling!.QuerySelectorAll(".wb-rv button");

        buttons()[0].Click();
        CaseRows(harness)[0].NextElementSibling!.QuerySelector("button.wb-link")!.TextContent.Should().Be("Add a note");
        harness.FindAll(".wb-review-line textarea").Should().BeEmpty();

        buttons()[1].Click();
        harness.FindAll(".wb-review-line textarea").Should().ContainSingle();
    }

    // ----- 2 and 3. no repeated value, any-value conditions ---------------------------------------------------------------

    private sealed record OperatorCase(
        string Name, Condition Condition, string Topic, string Age, string Rating, bool Met,
        string WhyEn, string WhyEs, string LineEn, string LineEs);

    private static readonly OperatorCase[] OperatorCases =
    [
        new("equals met", Condition.Equals("topic", DecisionValue.Code("x")), "x", "30", "", true,
            "Row 1 decided because topic is x.", "Decidió la fila 1 porque topic es x.", "Met Topic is x.", "Se cumple Topic es x."),
        new("equals not met", Condition.Equals("topic", DecisionValue.Code("x")), "y", "30", "", false,
            "", "", "Not met Topic is x. You entered y.", "No se cumple Topic es x. Usted puso y."),
        new("in met", Condition.In("topic", [DecisionValue.Code("x"), DecisionValue.Code("z")]), "x", "30", "", true,
            "Row 1 decided because topic is x or z.", "Decidió la fila 1 porque topic es x o z.", "Met Topic is x or z.", "Se cumple Topic es x o z."),
        new("in not met", Condition.In("topic", [DecisionValue.Code("x"), DecisionValue.Code("z")]), "y", "30", "", false,
            "", "", "Not met Topic is x or z. You entered y.", "No se cumple Topic es x o z. Usted puso y."),
        new("is empty met", Condition.IsNull("rating"), "x", "30", "", true,
            "Row 1 decided because rating has no value.", "Decidió la fila 1 porque rating no tiene valor.", "Met Rating has no value.", "Se cumple Rating no tiene valor."),
        new("is empty not met", Condition.IsNull("rating"), "x", "30", "3", false,
            "", "", "Not met Rating has no value. You entered 3.", "No se cumple Rating no tiene valor. Usted puso 3."),
        new("not equals met", Condition.NotEquals("topic", DecisionValue.Code("x")), "y", "30", "", true,
            "Row 1 decided because topic (y) is not x.", "Decidió la fila 1 porque topic (y) no es x.", "Met Topic is not x. You entered y.", "Se cumple Topic no es x. Usted puso y."),
        new("not equals not met", Condition.NotEquals("topic", DecisionValue.Code("x")), "x", "30", "", false,
            "", "", "Not met Topic is not x. You entered x.", "No se cumple Topic no es x. Usted puso x."),
        new("not in met", Condition.NotIn("topic", [DecisionValue.Code("x"), DecisionValue.Code("z")]), "y", "30", "", true,
            "Row 1 decided because topic (y) is not x or z.", "Decidió la fila 1 porque topic (y) no es x ni z.", "Met Topic is not x or z. You entered y.", "Se cumple Topic no es x ni z. Usted puso y."),
        new("not in not met", Condition.NotIn("topic", [DecisionValue.Code("x"), DecisionValue.Code("z")]), "z", "30", "", false,
            "", "", "Not met Topic is not x or z. You entered z.", "No se cumple Topic no es x ni z. Usted puso z."),
        new("range met", Condition.Range("ageDays", DecisionValue.Integer(10), DecisionValue.Integer(40), true, true), "x", "30", "", true,
            "Row 1 decided because Age in days (30) is between 10 and 40.", "Decidió la fila 1 porque Antigüedad en días (30) está entre 10 y 40.",
            "Met Age in days is between 10 and 40. You entered 30.", "Se cumple Antigüedad en días está entre 10 y 40. Usted puso 30."),
        new("range not met", Condition.Range("ageDays", DecisionValue.Integer(10), DecisionValue.Integer(40), true, true), "x", "50", "", false,
            "", "", "Not met Age in days is between 10 and 40. You entered 50.", "No se cumple Antigüedad en días está entre 10 y 40. Usted puso 50."),
        new("starts with met", Condition.StartsWith("topic", "ab"), "abc", "30", "", true,
            "Row 1 decided because topic (abc) starts with ab.", "Decidió la fila 1 porque topic (abc) empieza por ab.", "Met Topic starts with ab. You entered abc.", "Se cumple Topic empieza por ab. Usted puso abc."),
        new("starts with not met", Condition.StartsWith("topic", "ab"), "xyz", "30", "", false,
            "", "", "Not met Topic starts with ab. You entered xyz.", "No se cumple Topic empieza por ab. Usted puso xyz."),
        new("has a value met", Condition.IsNotNull("rating"), "x", "30", "3", true,
            "Row 1 decided because rating (3) has a value.", "Decidió la fila 1 porque rating (3) tiene valor.", "Met Rating has a value. You entered 3.", "Se cumple Rating tiene valor. Usted puso 3."),
        new("has a value not met", Condition.IsNotNull("rating"), "x", "30", "", false,
            "", "", "Not met Rating has a value. You did not enter this.", "No se cumple Rating tiene valor. Usted no puso este dato."),
    ];

    public static TheoryData<string, WorkbenchLocale> OperatorData
    {
        get
        {
            var data = new TheoryData<string, WorkbenchLocale>();
            foreach (var item in OperatorCases)
            {
                data.Add(item.Name, WorkbenchLocale.En);
                data.Add(item.Name, WorkbenchLocale.Es);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OperatorData))]
    public void TheWhySentenceAndTheRowLineAddTheTypedValueOnlyWhenTheConditionTextDoesNotSayIt(string name, WorkbenchLocale locale)
    {
        var item = OperatorCases.Single(entry => entry.Name == name);
        using var harness = Open(FirstWins(locale, RowOf("r1", item.Condition)), WorkbenchTab.Simulate, locale);

        Simulate(harness, locale, item.Topic, item.Age, item.Rating);

        Lines(harness).Should().Equal(IsEs(locale) ? item.LineEs : item.LineEn);
        if (item.Met)
        {
            harness.Find(".wb-why").TextContent.Should().Be(IsEs(locale) ? item.WhyEs : item.WhyEn);
        }
        else
        {
            harness.FindAll(".wb-why").Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Always met.", "Row 1 decided because it applies in every case.")]
    [InlineData(WorkbenchLocale.Es, "Se cumple siempre.", "Decidió la fila 1 porque aplica en todos los casos.")]
    public void ARowWhoseEveryConditionAcceptsAnyValueShowsOneLineAndTheEveryCaseReason(WorkbenchLocale locale, string line, string why)
    {
        var unbounded = Condition.Range("ageDays", null, null, true, true);
        using var harness = Open(FirstWins(locale, RowOf("r1", AnyOf("topic"), unbounded)), WorkbenchTab.Simulate, locale);

        Simulate(harness, locale);

        Lines(harness).Should().Equal(line);
        harness.FindAll(".wb-live .wb-row")[0].QuerySelectorAll(".wb-cond .wb-pill").Should().BeEmpty();
        harness.Find(".wb-why").TextContent.Should().Be(why);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Met Topic is x.", "Row 1 decided because topic is x.")]
    [InlineData(WorkbenchLocale.Es, "Se cumple Topic es x.", "Decidió la fila 1 porque topic es x.")]
    public void ARowThatMixesAnyValueConditionsWithAnotherListsAndNamesOnlyTheOther(WorkbenchLocale locale, string line, string why)
    {
        var rule = FirstWins(locale, RowOf(
            "r1", AnyOf("channel"), Condition.Range("ageDays", null, null, true, true), Condition.Equals("topic", DecisionValue.Code("x"))));
        using var harness = Open(rule, WorkbenchTab.Simulate, locale);

        Simulate(harness, locale);

        Lines(harness).Should().Equal(line);
        harness.Find(".wb-why").TextContent.Should().Be(why);
        harness.Find(".wb-live .wb-sent").TextContent.Should().NotContain("anything").And.NotContain("cualquier");
    }

    [Fact]
    public void ARowThatDoesNotApplyIsShortenedWithoutItsAnyValueConditions()
    {
        var rule = FirstWins(
            WorkbenchLocale.En,
            RowOf("r1", Condition.Equals("topic", DecisionValue.Code("x"))),
            RowOf("r2", AnyOf("channel"), Condition.Equals("topic", DecisionValue.Code("y"))));
        using var harness = Open(rule, WorkbenchTab.Simulate);

        Simulate(harness, WorkbenchLocale.En);

        harness.FindAll(".wb-live .wb-row")[1].QuerySelector(".wb-sent")!.TextContent.Should().Be("If topic is y…");
    }

    // ----- 4. headline and labelled values --------------------------------------------------------------------------------

    private static void Escalate(Harness harness, WorkbenchLocale locale)
    {
        harness.Type(IsEs(locale) ? "Canal de contacto" : "Contact channel", "Phone");
        harness.Type("topic", "billing-late");
        harness.Choose("urgent", "true");
        harness.Type(IsEs(locale) ? "Antigüedad en días" : "Age in days", "100");
        harness.Press(Run(locale));
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, new[] { "queue", "route" }, "Result: Urgent queue, escalation")]
    [InlineData(WorkbenchLocale.En, new[] { "priority", "notify" }, "Result: priority: 1, notify: yes")]
    [InlineData(WorkbenchLocale.En, new[] { "route", "notify", "priority" }, "Result: escalation, notify: yes, priority: 1")]
    [InlineData(WorkbenchLocale.En, new[] { "nothing", "route" }, "Result: escalation")]
    [InlineData(WorkbenchLocale.En, new[] { "nothing" }, "Result: escalation")]
    [InlineData(WorkbenchLocale.En, new string[0], "Result: escalation")]
    [InlineData(WorkbenchLocale.Es, new[] { "queue", "route" }, "Resultado: Cola urgente, escalado")]
    [InlineData(WorkbenchLocale.Es, new[] { "priority", "notify" }, "Resultado: priority: 1, notify: sí")]
    [InlineData(WorkbenchLocale.Es, null, "Resultado: escalado")]
    public void TheHeadingShowsTheHostsHeadlineFieldsInOrderAndTheFirstFieldByDefault(WorkbenchLocale locale, string[]? headline, string expected)
    {
        var rule = Fx.Rule("intake.first", locale) with { HeadlineFields = headline };
        using var harness = Open(rule, WorkbenchTab.Simulate, locale);

        Escalate(harness, locale);

        harness.Find(".wb-result .wb-h2").TextContent.Should().Be(expected);
        harness.FindAll(".wb-result dl dt").Should().HaveCount(4);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Result: cost: 12.5 EUR, rate: 2.5, count: 3")]
    [InlineData(WorkbenchLocale.Es, "Resultado: cost: 12.5 EUR, rate: 2.5, count: 3")]
    public void MoneyNumberAndWholeNumberValuesCarryTheirLabelInTheHeading(WorkbenchLocale locale, string expected)
    {
        var source = Fx.Rule("intake.first", locale);
        var kind = source.Rule!.Kind with
        {
            OutcomeFields =
            [
                .. source.Rule.Kind.OutcomeFields,
                new OutcomeFieldDescriptor("cost", FactType.Money, true),
                new OutcomeFieldDescriptor("rate", FactType.Decimal, true),
                new OutcomeFieldDescriptor("count", FactType.Integer, true),
            ],
        };
        var row = new Row("r1", [AnyOf("topic")], """{"route":"Fast","cost":{"amount":12.5,"currency":"EUR"},"rate":2.5,"count":3}""");
        var rule = Custom(new DecisionTable(HitPolicy.First, [row], null), locale, kind) with { HeadlineFields = ["cost", "rate", "count"] };
        using var harness = Open(rule, WorkbenchTab.Simulate, locale);

        Simulate(harness, locale);

        harness.Find(".wb-result .wb-h2").TextContent.Should().Be(expected);
        harness.FindAll(".wb-result dd").Select(item => item.TextContent).Should().Contain("12.5 EUR");
    }

    [Fact]
    public void TheExpectedAndActualColumnsLabelBooleanAndNumberValuesInSpanishToo()
    {
        using var harness = Open(Fx.Rule("intake.first", WorkbenchLocale.Es), WorkbenchTab.Tests, WorkbenchLocale.Es);

        var cells = CaseRows(harness)[0].QuerySelectorAll("td");

        cells[2].TextContent.Should().Be("escalado, Cola urgente, notify: sí, priority: 1");
        cells[3].TextContent.Should().Be("escalado, Cola urgente, notify: sí, priority: 1");
    }

    // ----- 5 and 6. money input and date language -------------------------------------------------------------------------

    private static DecisionTable MoneyTable(params (string Key, string Lower, string Upper, string Currency)[] rows) => new(
        HitPolicy.First,
        [.. rows.Select(item => RowOf(
            item.Key,
            Condition.Range(
                "amount",
                DecisionValue.Money(decimal.Parse(item.Lower, System.Globalization.CultureInfo.InvariantCulture), item.Currency),
                DecisionValue.Money(decimal.Parse(item.Upper, System.Globalization.CultureInfo.InvariantCulture), item.Currency),
                true,
                true)))],
        null);

    private static IReadOnlyList<string> Options(IElement select) => [.. select.QuerySelectorAll("option").Select(item => item.TextContent)];

    [Theory]
    [InlineData(WorkbenchLocale.En, "Amount in dollars.")]
    [InlineData(WorkbenchLocale.Es, "Monto en dólares.")]
    public void AMoneyFactOfARuleInOneCurrencyOffersUsdAndTheDollarsHint(WorkbenchLocale locale, string hint)
    {
        using var harness = Open(Fx.Rule("intake.first", locale), WorkbenchTab.Simulate, locale);

        var select = harness.Find("select.wb-in-currency");
        Options(select).Should().Equal("USD");
        select.GetAttribute("value").Should().Be("USD");
        harness.Find("#" + select.GetAttribute("aria-describedby")!.Split(' ')[0]).TextContent.Should().Be(hint);
        harness.FindAll(".wb-money input").Should().ContainSingle().Which.GetAttribute("placeholder").Should().BeNull();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Amount and currency.")]
    [InlineData(WorkbenchLocale.Es, "Monto y moneda.")]
    public void AMoneyFactOfARuleInSeveralCurrenciesOffersUsdFirstThenTheOthersInOrdinalOrder(WorkbenchLocale locale, string hint)
    {
        var table = MoneyTable(("r1", "100", "500", "EUR"), ("r2", "100", "500", "CAD"), ("r3", "100", "500", "USD"), ("r4", "5", "9", "EUR"));
        using var harness = Open(Custom(table, locale), WorkbenchTab.Simulate, locale);

        var select = harness.Find("select.wb-in-currency");
        Options(select).Should().Equal("USD", "CAD", "EUR");
        select.GetAttribute("value").Should().Be("USD");
        harness.Find("#" + select.GetAttribute("aria-describedby")!.Split(' ')[0]).TextContent.Should().Be(hint);
    }

    [Fact]
    public void ARuleThatNamesNoUsdStillOffersUsdFirst()
    {
        using var harness = Open(Custom(MoneyTable(("r1", "100", "500", "EUR"))), WorkbenchTab.Simulate);
        Options(harness.Find("select.wb-in-currency")).Should().Equal("USD", "EUR");
    }

    [Fact]
    public void AnEmptyAmountIsAnEmptyFactWhateverTheSelectShows()
    {
        var table = MoneyTable(("r1", "100", "500", "EUR"));
        using var harness = Open(Custom(table), WorkbenchTab.Simulate);
        harness.Find("select.wb-in-currency").Change("EUR");

        Simulate(harness, WorkbenchLocale.En);

        harness.FindAll(".wb-errt").Should().BeEmpty();
        harness.FindAll(".wb-result").Should().ContainSingle();
        harness.Find("select.wb-in-currency").GetAttribute("value").Should().Be("EUR");
    }

    [Fact]
    public void TheChosenCurrencyGoesWithTheAmountAndUsdIsTheDefault()
    {
        var table = MoneyTable(("r1", "100", "500", "EUR"), ("r2", "100", "500", "USD"));
        using var harness = Open(Custom(table), WorkbenchTab.Simulate);

        Simulate(harness, WorkbenchLocale.En, amount: "250");
        harness.Find(".wb-why").TextContent.Should().Be("Row 2 decided because amount (250 USD) is between 100 USD and 500 USD.");

        harness.Find("select.wb-in-currency").Change("EUR");
        harness.Press("Simulate");
        harness.Find(".wb-why").TextContent.Should().Be("Row 1 decided because amount (250 EUR) is between 100 EUR and 500 EUR.");
    }

    [Fact]
    public void ACurrencyLoadedFromACaseIsKeptAsAnOptionSoLoadingNeverChangesIt()
    {
        var source = Fx.Rule("intake.first");
        var eur = DecisionValue.Money(5, "EUR");
        var golden = new Nuvora.Decisions.Contracts.Goldens.GoldenCase(
            "A euro case",
            new Dictionary<string, DecisionValue> { ["amount"] = eur },
            new Nuvora.Decisions.Contracts.Goldens.ExpectedNoDecision());
        var rule = source with { Rule = Fx.WithTable(source.Rule!, source.Rule!.Table, goldens: [golden]) };
        using var harness = Open(rule, WorkbenchTab.Simulate);

        harness.Find("select[id$=-load]").Change("0");

        var select = harness.Find("select.wb-in-currency");
        Options(select).Should().Equal("USD", "EUR");
        select.GetAttribute("value").Should().Be("EUR");
        harness.Input("amount").GetAttribute("value").Should().Be("5");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "en")]
    [InlineData(WorkbenchLocale.Es, "es")]
    public void TheDateInputCarriesThePageLanguage(WorkbenchLocale locale, string lang)
    {
        using var harness = Open(Fx.Rule("intake.first", locale), WorkbenchTab.Simulate, locale);
        harness.Input("received on").GetAttribute("lang").Should().Be(lang);

        harness.Open(WorkbenchTab.Tests);
        harness.Press(IsEs(locale) ? "Agregar un caso" : "Add a case");
        harness.Input("received on").GetAttribute("lang").Should().Be(lang);
    }

    // ----- 7. folding -----------------------------------------------------------------------------------------------------

    private static DecisionTable Rows(int count, bool withDefault, params int[] matching)
    {
        var rows = Enumerable.Range(1, count).Select(index =>
            RowOf("r" + index, Condition.Equals("topic", DecisionValue.Code(matching.Contains(index) ? "x" : "other-" + index)))).ToList();
        return new DecisionTable(HitPolicy.First, rows, withDefault ? RowOf("fallback") : null);
    }

    private static int ShownRows(Harness harness) => harness.FindAll(".wb-live .wb-row").Count;

    [Theory]
    [InlineData(6, false)]
    [InlineData(5, true)]
    public void ARuleWithSixRowsShowsEveryRow(int rows, bool withDefault)
    {
        using var harness = Open(Custom(Rows(rows, withDefault, 1)), WorkbenchTab.Simulate);

        Simulate(harness, WorkbenchLocale.En);

        ShownRows(harness).Should().Be(6);
        harness.FindAll(".wb-live button").Select(item => item.TextContent).Should().NotContain(text => text.StartsWith("Show the", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(6, true)]
    public void ARuleWithSevenRowsFoldsTheOnesThatDoNotApplyBehindTheButton(int rows, bool withDefault)
    {
        using var harness = Open(Custom(Rows(rows, withDefault, 1)), WorkbenchTab.Simulate);

        Simulate(harness, WorkbenchLocale.En);

        ShownRows(harness).Should().Be(1);
        harness.Find(".wb-live").TextContent.Should().Contain("Show the 6 rows that do not apply");
        harness.Press("Show the 6 rows that do not apply");
        ShownRows(harness).Should().Be(7);
    }

    [Fact]
    public void TheDecidingRowAndEveryRowThatMatchedAreAlwaysShownWhenFolding()
    {
        using var harness = Open(Custom(Rows(8, true, 2, 5, 7)), WorkbenchTab.Simulate);

        Simulate(harness, WorkbenchLocale.En);

        Words(harness).Should().Equal("Decides", "Matches, does not decide", "Matches, does not decide");
        harness.FindAll(".wb-live .wb-row .wb-num").Select(item => item.TextContent).Should().Equal("2", "5", "7");
        harness.Find(".wb-live").TextContent.Should().Contain("Show the 6 rows that do not apply");
    }

    [Fact]
    public void TheFoldButtonIsInSpanishToo()
    {
        using var harness = Open(Custom(Rows(8, false, 1), WorkbenchLocale.Es), WorkbenchTab.Simulate, WorkbenchLocale.Es);
        Simulate(harness, WorkbenchLocale.Es);
        harness.Find(".wb-live").TextContent.Should().Contain("Mostrar las 7 filas que no aplican");
    }

    private static List<string> Words(Harness harness) => [.. harness.FindAll(".wb-live .wb-row").Select(item => item.LastElementChild!.TextContent)];

    // ----- 8. a fix or a question needs its note --------------------------------------------------------------------------

    private static int FocusCalls(Harness harness) =>
        harness.Ctx.JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");

    private static void RowPart(Harness harness, int row, string word) =>
        harness.FindAll(".wb-row .wb-rv")[row].QuerySelectorAll("button").First(item => item.TextContent.StartsWith(word, StringComparison.Ordinal)).Click();

    private static Harness ReadinessWithAFixAndAQuestion(WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var harness = new Harness();
        harness.Render([Fx.Rule("readiness", locale)], locale);
        RowPart(harness, 0, IsEs(locale) ? "Corregir" : "Fix");
        RowPart(harness, 1, IsEs(locale) ? "Pregunta" : "Question");
        RowPart(harness, 2, IsEs(locale) ? "Confirmar" : "Confirm");
        harness.Open(WorkbenchTab.Review);
        return harness;
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Note missing", "Write the note", "Confirmed")]
    [InlineData(WorkbenchLocale.Es, "Falta la nota", "Escribir la nota", "Confirmada")]
    public void AFixOrAQuestionWithNoNoteIsMarkedWithTheWarnTextAndAButton(WorkbenchLocale locale, string missing, string write, string confirmed)
    {
        using var harness = ReadinessWithAFixAndAQuestion(locale);

        var rows = harness.FindAll(".wb-grid-review .wb-table tbody tr");
        rows[0].QuerySelectorAll("td")[2].QuerySelector(".wb-missing")!.TextContent.Should().Be(missing);
        rows[0].QuerySelectorAll("td")[2].QuerySelector("button")!.TextContent.Should().Be(write);
        rows[1].QuerySelectorAll("td")[2].QuerySelector(".wb-missing")!.TextContent.Should().Be(missing);
        rows[2].QuerySelectorAll("td")[1].TextContent.Should().Be(confirmed);
        rows[2].QuerySelector(".wb-missing").Should().BeNull();
        rows[2].QuerySelectorAll("button").Should().BeEmpty();
    }

    [Fact]
    public void AWholeRuleFixWithNoNoteIsMarkedInItsCard()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[1].Click();

        harness.Find(".wb-card-gap .wb-missing").TextContent.Should().Be("Note missing");
        harness.Find(".wb-card-gap button.wb-btn-secondary").TextContent.Should().Be("Write the note");

        var before = FocusCalls(harness);
        harness.Find(".wb-card-gap button.wb-btn-secondary").Click();

        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Review);
        FocusCalls(harness).Should().BeGreaterThan(before);
        harness.Find(".wb-card-gap textarea").Change("Say why.");
        harness.FindAll(".wb-missing").Should().BeEmpty();
    }

    [Fact]
    public void WriteTheNoteOnARowOpensItsFieldInReadAndFocusesIt()
    {
        using var harness = ReadinessWithAFixAndAQuestion();
        var before = FocusCalls(harness);

        harness.FindAll(".wb-grid-review .wb-table tbody tr")[1].QuerySelector("button")!.Click();

        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);
        harness.FindAll("[role=tabpanel] textarea").Should().HaveCount(2);
        FocusCalls(harness).Should().BeGreaterThan(before);
    }

    [Fact]
    public void WriteTheNoteOnACaseOpensItsReviewLineInTheTestTableAndFocusesIt()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        harness.Open(WorkbenchTab.Tests);
        ReviewTrigger(harness, 1).Click();
        CaseRows(harness)[1].NextElementSibling!.QuerySelectorAll(".wb-rv button")[2].Click();
        ReviewTrigger(harness, 1).Click();
        harness.Open(WorkbenchTab.Review);
        harness.FindAll(".wb-review-line").Should().BeEmpty();
        var before = FocusCalls(harness);

        harness.Find(".wb-grid-review .wb-table tbody tr button").Click();

        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Tests);
        harness.FindAll(".wb-review-line textarea").Should().ContainSingle();
        FocusCalls(harness).Should().BeGreaterThan(before);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Write the note for 2 items before exporting.")]
    [InlineData(WorkbenchLocale.Es, "Escriba la nota de 2 elementos antes de exportar.")]
    public void ExportIsHeldWithTheCountWhileAnItemNeedsItsNote(WorkbenchLocale locale, string message)
    {
        using var harness = ReadinessWithAFixAndAQuestion(locale);
        harness.Type(IsEs(locale) ? "Su nombre" : "Your name", "Reviewer One");

        harness.Press(IsEs(locale) ? "Exportar la revisión" : "Export the review");

        harness.Exports.Should().BeEmpty();
        var alert = harness.Find(".wb-grid-review [role=alert]");
        alert.TextContent.Should().Be(message);
        harness.FindAll(".wb-saved").Should().BeEmpty();
        harness.Session.UnexportedRuleCount.Should().Be(1);
    }

    [Fact]
    public void TheCountSpansEveryRuleOfTheSession()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness"), Fx.Rule("intake.unique")]);
        RowPart(harness, 0, "Fix");
        harness.FindAll(".wb-rule")[1].Click();
        RowPart(harness, 0, "Question");
        harness.Open(WorkbenchTab.Review);
        harness.Type("Your name", "Reviewer One");

        harness.Press("Export the review");

        harness.Exports.Should().BeEmpty();
        harness.Find(".wb-grid-review [role=alert]").TextContent.Should().Be("Write the note for 2 items before exporting.");
    }

    [Fact]
    public void ExportProceedsOnceEveryItemHasItsNoteAndTheMessageGoes()
    {
        using var harness = ReadinessWithAFixAndAQuestion();
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");
        harness.Exports.Should().BeEmpty();

        harness.Find(".wb-grid-review .wb-table tbody button").Click();
        harness.Find("[role=tabpanel] textarea").Change("The limit should be 50.");
        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-grid-review [role=alert]").TextContent.Should().Be("Write the note for 1 items before exporting.");
        harness.Find(".wb-grid-review .wb-table tbody button").Click();
        harness.FindAll("[role=tabpanel] textarea")[^1].Change("Why is this one a question?");
        harness.Open(WorkbenchTab.Review);

        harness.FindAll(".wb-grid-review [role=alert]").Should().BeEmpty();
        harness.FindAll(".wb-missing").Should().BeEmpty();
        harness.Press("Export the review");

        harness.Exports.Should().ContainSingle();
        harness.Exports[0].Files[0].Review!.Items.Where(item => item.State is ReviewState.NeedsFix or ReviewState.Question)
            .Should().OnlyContain(item => item.Note.Length > 0);
        harness.Find(".wb-saved").TextContent.Should().Be("Review saved to the demo folder.");
    }

    [Fact]
    public void AConfirmationNeedsNoNoteToExport()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        RowPart(harness, 0, "Confirm");
        harness.Open(WorkbenchTab.Review);
        harness.Type("Your name", "Reviewer One");

        harness.Press("Export the review");

        harness.Exports.Should().ContainSingle();
        harness.FindAll(".wb-missing, [role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void ANoteOfOnlyWhiteSpaceDoesNotCount()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        RowPart(harness, 0, "Fix");
        harness.FindAll("[role=tabpanel] textarea").Should().ContainSingle().Which.Change("   \n  ");
        harness.Open(WorkbenchTab.Review);

        harness.Find(".wb-grid-review .wb-table .wb-missing").TextContent.Should().Be("Note missing");
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");
        harness.Exports.Should().BeEmpty();
    }

    [Fact]
    public void AReviewerWhoHasNoNameAndMissingNotesSeesBothMessagesAndNothingIsExported()
    {
        using var harness = ReadinessWithAFixAndAQuestion();

        harness.Press("Export the review");

        harness.Exports.Should().BeEmpty();
        harness.Find(".wb-grid-review [role=alert]").TextContent.Should().Be("Write the note for 2 items before exporting.");
        harness.Find(".wb-grid-review .wb-errt:not([role=alert])").TextContent.Should().Be("Enter your name to sign the review.");
    }

    // ----- strings --------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("tests.review", "Revisar", "Review")]
    [InlineData("row.always", "Se cumple siempre.", "Always met.")]
    [InlineData("hint.moneyUsd", "Monto en dólares.", "Amount in dollars.")]
    [InlineData("hint.money", "Monto y moneda.", "Amount and currency.")]
    [InlineData("review.noteMissing", "Falta la nota", "Note missing")]
    [InlineData("review.writeNote", "Escribir la nota", "Write the note")]
    [InlineData("review.notesNeeded", "Escriba la nota de {n} elementos antes de exportar.", "Write the note for {n} items before exporting.")]
    public void TheNewKeysHaveTheAgreedDefaults(string key, string es, string en)
    {
        WorkbenchStrings.Keys.Should().Contain(key);
        WorkbenchStrings.Default(key, WorkbenchLocale.Es).Should().Be(es);
        WorkbenchStrings.Default(key, WorkbenchLocale.En).Should().Be(en);
    }

    [Fact]
    public void TheNewTextsCanBeOverridden()
    {
        using var harness = new Harness();
        harness.Render(
            [Fx.Rule("intake.first")],
            overrides: new Dictionary<string, string> { ["tests.review"] = "Check", ["row.always"] = "Every time." });
        harness.Open(WorkbenchTab.Tests);
        ReviewTrigger(harness, 0).TextContent.Should().Be("Check");
    }
}
