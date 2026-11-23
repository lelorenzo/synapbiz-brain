using System.Reflection;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The refusals no form can cause (the form only gives values of the right type), checked on the internal writer of the sentences.</summary>
public sealed class RefusalTests
{
    private static (string Line, string WayOut) Refusal(WorkbenchLocale locale, string code, IReadOnlyDictionary<string, DecisionValue>? facts = null)
    {
        var assembly = typeof(RuleWorkbench).Assembly;
        var uiText = assembly.GetType("Synapbiz.Brain.Workbench.Blazor.UiText", throwOnError: true)!;
        var refusal = assembly.GetType("Synapbiz.Brain.Workbench.Blazor.RefusalText", throwOnError: true)!;
        var text = Activator.CreateInstance(uiText, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, [locale, null], null)!;
        var rule = Fx.Rule("readiness", locale);
        var view = RuleDescriber.Describe(rule.Rule!, locale, rule.Labels);
        var result = refusal.GetMethod("For", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [text, view, facts ?? new Dictionary<string, DecisionValue>(), code])!;
        return ((string)refusal.GetProperty("Line")!.GetValue(result)!, refusal.GetProperty("WayOut")!.GetValue(result)!.ToString()!);
    }

    [Fact]
    public void AValueOfTheWrongTypeNamesTheFactsItWasGiven()
    {
        var facts = new Dictionary<string, DecisionValue> { ["score"] = DecisionValue.Integer(3) };
        Refusal(WorkbenchLocale.En, "decisions.facts.type_mismatch", facts)
            .Should().Be(("The value Score is not in the form the rule expects.", "FixData"));
        Refusal(WorkbenchLocale.Es, "decisions.facts.type_mismatch", facts)
            .Should().Be(("El dato Puntaje no tiene la forma que la regla espera.", "FixData"));
    }

    [Fact]
    public void AMissingValueNamesTheRequiredFactsThatWereNotGivenOrAllOfThemWhenNoneWasGiven()
    {
        Refusal(WorkbenchLocale.En, "decisions.facts.missing", new Dictionary<string, DecisionValue> { ["score"] = DecisionValue.Integer(3) })
            .Line.Should().Be("A required value is missing: Stage.");
        Refusal(WorkbenchLocale.En, "decisions.facts.missing", new Dictionary<string, DecisionValue>
        {
            ["score"] = DecisionValue.Integer(3),
            ["stage"] = DecisionValue.Enum("Review"),
        }).Line.Should().Be("A required value is missing: Stage, Score.");
        Refusal(WorkbenchLocale.En, "decisions.facts.missing").Line.Should().Be("A required value is missing: Stage, Score.");
    }

    [Theory]
    [InlineData("decisions.composition.cycle", "This rule depends on others that could not be computed here.", "AddToReview")]
    [InlineData("decisions.composition.too_deep", "This rule depends on others that could not be computed here.", "AddToReview")]
    [InlineData("decisions.something.new", "The engine could not evaluate the rule with this data.", "AddToReview")]
    [InlineData("decisions.facts.too_large", "The data is too long for this rule.", "FixData")]
    public void OtherCodesGetTheirOwnSentenceAndNeverShowTheCode(string code, string line, string wayOut)
    {
        Refusal(WorkbenchLocale.En, code).Should().Be((line, wayOut));
    }
}
