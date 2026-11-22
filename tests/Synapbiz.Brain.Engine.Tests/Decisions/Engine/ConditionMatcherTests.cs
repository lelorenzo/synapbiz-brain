using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Conditions;

namespace Nuvora.UnitTests.Decisions.Engine;

/// <summary>
/// P2-20.2 step 2 (paquete 0.A369): one fact per operator x type cell of the
/// matrix, Range inclusive/exclusive bounds, NotIn against a null fact,
/// StartsWith on Code, Money's same-currency-only matching, and the
/// forbidden cells <see cref="OperatorMatrix"/> refuses at construction.
/// </summary>
public sealed class ConditionMatcherTests
{
    [Fact]
    public void Equals_MatchesTheSameValue_OnEveryFactType()
    {
        Match(Condition.Equals("f", DecisionValue.Code("ABC")), FactType.Code, DecisionValue.Code("ABC")).Should().BeTrue();
        Match(Condition.Equals("f", DecisionValue.Enum("Web")), FactType.Enum, DecisionValue.Enum("Web")).Should().BeTrue();
        Match(Condition.Equals("f", DecisionValue.Boolean(true)), FactType.Boolean, DecisionValue.Boolean(true)).Should().BeTrue();
        Match(Condition.Equals("f", DecisionValue.Integer(5)), FactType.Integer, DecisionValue.Integer(5)).Should().BeTrue();
        Match(Condition.Equals("f", DecisionValue.Decimal(1.50m)), FactType.Decimal, DecisionValue.Decimal(1.5m)).Should().BeTrue();
        Match(Condition.Equals("f", DecisionValue.Date(new DateOnly(2026, 1, 1))), FactType.Date, DecisionValue.Date(new DateOnly(2026, 1, 1))).Should().BeTrue();
        var reference = Guid.NewGuid();
        Match(Condition.Equals("f", DecisionValue.Reference(reference)), FactType.Reference, DecisionValue.Reference(reference)).Should().BeTrue();
    }

    [Fact]
    public void Equals_RefusesANullFact()
    {
        Match(Condition.Equals("f", DecisionValue.Code("ABC")), FactType.Code, null).Should().BeFalse();
    }

    [Fact]
    public void NotEquals_MatchesADifferentValue_AndRefusesANullFact()
    {
        Match(Condition.NotEquals("f", DecisionValue.Code("ABC")), FactType.Code, DecisionValue.Code("XYZ")).Should().BeTrue();
        Match(Condition.NotEquals("f", DecisionValue.Code("ABC")), FactType.Code, DecisionValue.Code("ABC")).Should().BeFalse();
        Match(Condition.NotEquals("f", DecisionValue.Code("ABC")), FactType.Code, null).Should().BeFalse();
    }

    [Fact]
    public void In_MatchesMembership_AndRefusesANullFact()
    {
        var set = new[] { DecisionValue.Enum("Web"), DecisionValue.Enum("Pos") };

        Match(Condition.In("f", set), FactType.Enum, DecisionValue.Enum("Web")).Should().BeTrue();
        Match(Condition.In("f", set), FactType.Enum, DecisionValue.Enum("B2B")).Should().BeFalse();
        Match(Condition.In("f", set), FactType.Enum, null).Should().BeFalse();
    }

    /// <summary>
    /// The named fixture "NotIn with null": a naive "value not in set" that
    /// treats an absent fact as vacuously satisfying NotIn would wrongly
    /// match here. NotIn, like every non-null-checking operator, refuses a
    /// null fact.
    /// </summary>
    [Fact]
    public void NotIn_RefusesANullFact_RatherThanVacuouslyMatching()
    {
        var set = new[] { DecisionValue.Code("ABC") };

        Match(Condition.NotIn("f", set), FactType.Code, null).Should().BeFalse();
        Match(Condition.NotIn("f", set), FactType.Code, DecisionValue.Code("XYZ")).Should().BeTrue();
        Match(Condition.NotIn("f", set), FactType.Code, DecisionValue.Code("ABC")).Should().BeFalse();
    }

    [Fact]
    public void IsNull_MatchesOnlyAnAbsentFact()
    {
        Match(Condition.IsNull("f"), FactType.Code, null).Should().BeTrue();
        Match(Condition.IsNull("f"), FactType.Code, DecisionValue.Code("ABC")).Should().BeFalse();
    }

    [Fact]
    public void IsNotNull_MatchesOnlyAPresentFact()
    {
        Match(Condition.IsNotNull("f"), FactType.Code, DecisionValue.Code("ABC")).Should().BeTrue();
        Match(Condition.IsNotNull("f"), FactType.Code, null).Should().BeFalse();
    }

    [Fact]
    public void StartsWith_MatchesAPrefix_OnCodeOnly()
    {
        Match(Condition.StartsWith("f", "AB"), FactType.Code, DecisionValue.Code("ABCDEF")).Should().BeTrue();
        Match(Condition.StartsWith("f", "AB"), FactType.Code, DecisionValue.Code("XYZ")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, true, 10, true)] // [10,20] inclusive: lower bound itself matches
    [InlineData(true, true, 20, true)] // [10,20] inclusive: upper bound itself matches
    [InlineData(false, false, 10, false)] // (10,20) exclusive: lower bound itself does not match
    [InlineData(false, false, 20, false)] // (10,20) exclusive: upper bound itself does not match
    [InlineData(true, true, 15, true)] // interior always matches regardless of inclusivity
    public void Range_RespectsInclusiveAndExclusiveBounds(bool lowerInclusive, bool upperInclusive, long factValue, bool expected)
    {
        var condition = Condition.Range("f", DecisionValue.Integer(10), DecisionValue.Integer(20), lowerInclusive, upperInclusive);

        Match(condition, FactType.Integer, DecisionValue.Integer(factValue)).Should().Be(expected);
    }

    [Fact]
    public void Range_ANullFact_DoesNotMatch()
    {
        var condition = Condition.Range("f", DecisionValue.Integer(1), DecisionValue.Integer(10), true, true);

        Match(condition, FactType.Integer, null).Should().BeFalse();
    }

    [Fact]
    public void Range_OnPlainDecimal_RespectsBounds()
    {
        var condition = Condition.Range("f", DecisionValue.Decimal(1.5m), DecisionValue.Decimal(9.5m), true, true);

        Match(condition, FactType.Decimal, DecisionValue.Decimal(5m)).Should().BeTrue();
        Match(condition, FactType.Decimal, DecisionValue.Decimal(10m)).Should().BeFalse();
    }

    /// <summary>
    /// ConditionMatcher.Create only validates the OPERATOR against the
    /// descriptor's declared type; it never re-checks that a value handed to
    /// Matches() later actually IS that type. A caller that hands a
    /// differently-typed fact value hits the engine's own defensive check.
    /// </summary>
    [Fact]
    public void Range_ABoundTypeThatDoesNotMatchTheActualFactValueType_ThrowsRangeTypeMismatch()
    {
        var condition = Condition.Range("f", DecisionValue.Integer(1), DecisionValue.Integer(10), true, true);
        var matcher = ConditionMatcher.Create(condition, FactType.Integer);

        var act = () => matcher.Matches(DecisionValue.Decimal(5m));

        act.Should().Throw<DecisionEngineException>().Where(e => e.ErrorCode == ConditionMatcher.ErrorRangeTypeMismatch);
    }

    [Fact]
    public void Range_OnDates_RespectsBounds()
    {
        var condition = Condition.Range(
            "f",
            DecisionValue.Date(new DateOnly(2026, 1, 1)),
            DecisionValue.Date(new DateOnly(2026, 12, 31)),
            lowerInclusive: true,
            upperInclusive: false);

        Match(condition, FactType.Date, DecisionValue.Date(new DateOnly(2026, 6, 15))).Should().BeTrue();
        Match(condition, FactType.Date, DecisionValue.Date(new DateOnly(2026, 12, 31))).Should().BeFalse();
        Match(condition, FactType.Date, DecisionValue.Date(new DateOnly(2025, 12, 31))).Should().BeFalse();
    }

    [Fact]
    public void Range_OnMoney_MatchesOnlyTheSameCurrency()
    {
        var condition = Condition.Range(
            "f", DecisionValue.Money(0m, "USD"), DecisionValue.Money(100m, "USD"), lowerInclusive: true, upperInclusive: true);

        Match(condition, FactType.Money, DecisionValue.Money(50m, "USD")).Should().BeTrue();
    }

    /// <summary>D-11: a Range on Money never converts — a different currency does not match, and it is not an exception.</summary>
    [Fact]
    public void Range_OnMoney_DifferentCurrency_DoesNotMatch_AndDoesNotThrow()
    {
        var condition = Condition.Range(
            "f", DecisionValue.Money(0m, "USD"), DecisionValue.Money(100m, "USD"), lowerInclusive: true, upperInclusive: true);

        var act = () => Match(condition, FactType.Money, DecisionValue.Money(50m, "COP"));

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    /// <summary>D-11: Equals on Money never converts either — a different currency is "not equal", not an exception.</summary>
    [Fact]
    public void Equals_OnMoney_DifferentCurrency_DoesNotMatch_AndDoesNotThrow()
    {
        var condition = Condition.Equals("f", DecisionValue.Money(50m, "USD"));

        var act = () => Match(condition, FactType.Money, DecisionValue.Money(50m, "COP"));

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    /// <summary>
    /// D-11: NotEquals on Money must be exactly as currency-scoped as
    /// Equals - a different currency is a non-match for every operator, never a match
    /// just because the negation flips polarity. 100 USD is not "not-100-COP" any more
    /// than it is "100 COP": the condition does not speak to that currency at all.
    /// </summary>
    [Fact]
    public void NotEquals_OnMoney_DifferentCurrency_DoesNotMatch_AndDoesNotThrow()
    {
        var condition = Condition.NotEquals("f", DecisionValue.Money(50m, "COP"));

        var act = () => Match(condition, FactType.Money, DecisionValue.Money(100m, "USD"));

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    /// <summary>D-11: same rule as NotEquals, for NotIn - a set entirely in a foreign currency is a non-match, not a vacuous "not in the set".</summary>
    [Fact]
    public void NotIn_OnMoney_SetEntirelyInADifferentCurrency_DoesNotMatch_AndDoesNotThrow()
    {
        var condition = Condition.NotIn("f", [DecisionValue.Money(50m, "COP")]);

        var act = () => Match(condition, FactType.Money, DecisionValue.Money(100m, "USD"));

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    /// <summary>D-12: a NotIn set that has at least one same-currency element still works normally for that fact — only an ALL-foreign-currency set is a non-match.</summary>
    [Fact]
    public void NotIn_OnMoney_SetWithASameCurrencyElement_StillMatchesNormally()
    {
        var condition = Condition.NotIn("f", [DecisionValue.Money(100m, "USD"), DecisionValue.Money(50m, "COP")]);

        Match(condition, FactType.Money, DecisionValue.Money(200m, "USD")).Should().BeTrue(); // not 100 USD -> NotIn matches
        Match(condition, FactType.Money, DecisionValue.Money(100m, "USD")).Should().BeFalse(); // equals the USD element -> NotIn does not match
    }

    [Theory]
    [InlineData(nameof(Operator.StartsWith), FactType.Integer)]
    [InlineData(nameof(Operator.Range), FactType.Code)]
    [InlineData(nameof(Operator.In), FactType.Boolean)]
    [InlineData(nameof(Operator.NotIn), FactType.Boolean)]
    public void Create_RefusesAForbiddenOperatorTypeCell_AtConstruction(string operatorName, FactType type)
    {
        var op = Enum.Parse<Operator>(operatorName);
        var condition = op switch
        {
            Operator.StartsWith => Condition.StartsWith("f", "A"),
            Operator.Range => Condition.Range("f", DecisionValue.Code("A"), DecisionValue.Code("Z"), true, true),
            Operator.In => Condition.In("f", [DecisionValue.Boolean(true)]),
            Operator.NotIn => Condition.NotIn("f", [DecisionValue.Boolean(true)]),
            _ => throw new ArgumentOutOfRangeException(nameof(operatorName)),
        };

        var act = () => ConditionMatcher.Create(condition, type);

        act.Should().Throw<DecisionEngineException>().Where(e => e.ErrorCode == ConditionMatcher.ErrorOperatorNotAllowed);
    }

    private static bool Match(Condition condition, FactType type, DecisionValue? factValue) =>
        ConditionMatcher.Create(condition, type).Matches(factValue);
}
