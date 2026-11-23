using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>Every refusal of the loader and of the validator, produced by the real loader, in the words of a person.</summary>
public sealed class DraftWordingTests
{
    private static readonly string[] OperatorNames = ["equals", "notEquals", "in", "notIn", "isNotNull", "startsWith", "other"];

    private static RuleEdit[] Outcome(string field, DecisionValue? value) => [new SetOutcome("billing-topic", field, value)];

    private static IEnumerable<(string Name, RuleEdit[] Edits)> Cases() =>
    [
        ("operator", [new SetCondition("billing-topic", Condition.StartsWith("channel", "x"))]),
        ("range", [new SetCondition("billing-topic", Condition.Range("ageDays", DecisionValue.Integer(1), DecisionValue.Decimal(3m), true, true))]),
        ("cell", [new SetCondition("billing-topic", Condition.Equals("ageDays", DecisionValue.Code("x")))]),
        ("money", [new SetCondition("billing-topic", Condition.Range("amount", DecisionValue.Money(1m, "USD"), DecisionValue.Money(3m, "EUR"), true, true))]),
        ("enum", Outcome("route", DecisionValue.Enum("Nope"))),
        ("enumCondition", [new SetCondition("billing-topic", Condition.Equals("channel", DecisionValue.Enum("Nope")))]),
        ("required", Outcome("queue", null)),
        ("outcomeType", Outcome("route", DecisionValue.Integer(3))),
        ("caseRow", [new RemoveRow("billing-topic")]),
        ("internal", [new SetOutcome("not-a-row", "route", DecisionValue.Enum("Fast"))]),
        ("internal2", [new SetCondition("billing-topic", Condition.Equals("nope", DecisionValue.Code("x")))]),
        ("caseData", [new AddCase(new ProposedCase("Made up", new Dictionary<string, DecisionValue> { ["ageDays"] = DecisionValue.Code("x") }, true, null, null))]),
        ("caseFact", [new AddCase(new ProposedCase("Made up", new Dictionary<string, DecisionValue> { ["nope"] = DecisionValue.Code("x") }, true, null, null))]),
        ("caseOutcome", [new SetCaseExpectation("A billing topic on the web goes to billing", "billing-topic", new Dictionary<string, DecisionValue>())]),
    ];

    private static readonly Dictionary<string, (string En, string Es)> Expected = new()
    {
        ["operator"] = ("\"starts with\" cannot be used with Contact channel. Choose another condition.", "«empieza por» no se puede usar con Canal de contacto. Elija otra condición."),
        ["range"] = ("The two values of Age in days must be of the same kind.", "Los dos valores de Antigüedad en días deben ser del mismo tipo."),
        ["cell"] = ("The value of Age in days is not in the form the rule expects.", "El valor de Antigüedad en días no tiene la forma que la regla espera."),
        ["money"] = ("The amounts of amount must be in the same currency.", "Los montos de amount deben estar en la misma moneda."),
        ["enum"] = ("Nope is not an option of Route.", "Nope no es una opción de Ruta."),
        ["enumCondition"] = ("Nope is not an option of Contact channel.", "Nope no es una opción de Canal de contacto."),
        ["required"] = ("The outcome \"queue\" is missing in row 2.", "Falta el resultado «queue» en la fila 2."),
        ["outcomeType"] = ("The outcome \"Route\" is not in the form the rule expects.", "El resultado «Ruta» no tiene la forma que la regla espera."),
        ["caseRow"] = ("The case \"A billing topic on the web goes to billing\" expected a row that no longer exists. Change what it expects or remove the case.", "El caso «A billing topic on the web goes to billing» esperaba una fila que ya no existe. Cambie lo que se espera o quite el caso."),
        ["internal"] = ("The change could not be applied because of an internal problem, not because of what you entered. Cancel and tell your support contact.", "No se pudo aplicar el cambio por un problema interno, no por lo que usted escribió. Cancele y avise a quien le da soporte."),
        ["internal2"] = ("The change could not be applied because of an internal problem, not because of what you entered. Cancel and tell your support contact.", "No se pudo aplicar el cambio por un problema interno, no por lo que usted escribió. Cancele y avise a quien le da soporte."),
        ["caseData"] = ("The data of the case \"Made up\" does not fit this rule: check Age in days.", "Los datos del caso «Made up» no sirven para esta regla: revise Antigüedad en días."),
        ["caseFact"] = ("The data of the case \"Made up\" does not fit this rule: check nope.", "Los datos del caso «Made up» no sirven para esta regla: revise nope."),
        ["caseOutcome"] = ("The case \"A billing topic on the web goes to billing\" has no expected result.", "Al caso «A billing topic on the web goes to billing» le falta el resultado esperado."),
    };

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void EveryReasonOfTheLoaderIsASentenceInTheWordsOfThePerson(WorkbenchLocale locale)
    {
        var shown = new List<string>();
        foreach (var (name, edits) in Cases())
        {
            var host = Fx.Editable("intake.first", locale);
            var draft = DraftEngine.Start(host);
            var result = DraftEngine.Apply(host, draft, [new DraftStep(StepKind.Row, "billing-topic", edits)]);
            result.IsOk.Should().BeFalse(name);
            var view = RuleDescriber.Describe(host.Rule!, locale, host.Labels);
            var lines = DraftWording.Lines(new UiText(locale, null), result.Problems, view, result.Rule, new Dictionary<string, string>());

            lines.Should().ContainSingle(name).Which.Should().Be(locale == WorkbenchLocale.Es ? Expected[name].Es : Expected[name].En);
            shown.AddRange(lines);
        }

        shown.Should().NotContain(line => line.Contains("decisions.", StringComparison.Ordinal) || line.Contains("brain.", StringComparison.Ordinal) || line.Contains("/body", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWordsTheFormChoseNameTheOperatorOfARefusal()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var result = DraftEngine.Apply(host, draft, [new DraftStep(StepKind.Row, "billing-topic", [new SetCondition("billing-topic", Condition.StartsWith("channel", "x"))])]);
        var view = RuleDescriber.Describe(host.Rule!, WorkbenchLocale.En, host.Labels);

        DraftWording.Lines(new UiText(WorkbenchLocale.En, null), result.Problems, view, result.Rule, new Dictionary<string, string> { ["channel"] = "begins with" })
            .Should().ContainSingle().Which.Should().StartWith("\"begins with\" cannot be used");
        DraftWording.IsInternal(result.Problems).Should().BeFalse();
    }

    [Fact]
    public void OnlyTheCodesAFormCannotCauseAreInternal()
    {
        var host = Fx.Editable("intake.first");
        var draft = DraftEngine.Start(host);
        var internalOnly = DraftEngine.Apply(host, draft, [new DraftStep(StepKind.Row, "billing-topic", [new SetOutcome("not-a-row", "route", DecisionValue.Enum("Fast"))])]);

        DraftWording.IsInternal(internalOnly.Problems).Should().BeTrue();
        DraftWording.OperatorKey("range").Should().Be("op.between");
        DraftWording.OperatorKey("isNull").Should().Be("op.isnull");
        string.Join("|", OperatorNames.Select(DraftWording.OperatorKey)).Should().Be("op.eq|op.neq|op.in|op.nin|op.notnull|op.starts|op.any");
        DraftWording.OperatorKey(null).Should().Be("op.any");
    }

    [Fact]
    public void ACaseThatLacksItsExpectedResultIsTheCasesProblemWhateverTheLoaderCallsIt()
    {
        var host = Fx.Editable("intake.first");
        var view = RuleDescriber.Describe(host.Rule!, WorkbenchLocale.En, host.Labels);
        var place = new DraftLocation(null, null, false, null, null, "Made up");
        DraftProblem[] problems =
        [
            new("brain.rule.golden_outcome_missing", "/body/goldens/9", place, [], null, null, null),
            new("brain.outcome.required_field", "/body/goldens/9/expect/outcome/queue", place, [], null, null, null),
        ];

        DraftWording.Lines(new UiText(WorkbenchLocale.En, null), problems, view, null, new Dictionary<string, string>())
            .Should().ContainSingle().Which.Should().Be("The case \"Made up\" has no expected result.");
    }
}
