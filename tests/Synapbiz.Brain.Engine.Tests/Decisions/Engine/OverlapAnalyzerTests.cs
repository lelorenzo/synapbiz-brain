using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Validation;

namespace Nuvora.UnitTests.Decisions.Engine;

/// <summary>
/// P2-20.2 step 4 follow-up (paquete 0.A369, closed after the coverage-gate
/// report showed <c>OverlapAnalyzer</c> at 30% line coverage): the per-column
/// domain-set algebra behind <see cref="TableValidatorTests"/>'s two named
/// fixtures (Allowed/Allowed overlap, trivial-equality unreachability) is
/// exercised directly here for every other combination the algebra models -
/// Range/Range, Excluded/Allowed, Prefix/Prefix, the null-admitting cases,
/// and Range subsumption - so the validator's actual decision surface is not
/// shipped with its hardest logic untested.
/// </summary>
public sealed class OverlapAnalyzerTests
{
    [Fact]
    public void RowsOverlap_OverlappingRanges_IsTrue()
    {
        var descriptor = AgeDescriptor();
        var a = Row("a", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), true, true));
        var b = Row("b", Condition.Range("age", DecisionValue.Integer(15), DecisionValue.Integer(25), true, true));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    [Fact]
    public void RowsOverlap_DisjointRanges_IsFalse()
    {
        var descriptor = AgeDescriptor();
        var a = Row("a", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), true, true));
        var b = Row("b", Condition.Range("age", DecisionValue.Integer(30), DecisionValue.Integer(40), true, true));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_EqualsAndNotEquals_SameValue_IsFalse()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.Equals("code", DecisionValue.Code("X")));
        var b = Row("b", Condition.NotEquals("code", DecisionValue.Code("X")));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_EqualsAndNotEquals_DifferentValue_IsTrue()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.Equals("code", DecisionValue.Code("X")));
        var b = Row("b", Condition.NotEquals("code", DecisionValue.Code("Y")));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    [Fact]
    public void RowsOverlap_NestedPrefixes_IsTrue()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.StartsWith("code", "AB"));
        var b = Row("b", Condition.StartsWith("code", "ABC"));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    [Fact]
    public void RowsOverlap_DisjointPrefixes_IsFalse()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.StartsWith("code", "AB"));
        var b = Row("b", Condition.StartsWith("code", "XY"));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_IsNullAndIsNull_IsTrue()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.IsNull("code"));
        var b = Row("b", Condition.IsNull("code"));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    [Fact]
    public void RowsOverlap_IsNullAndIsNotNull_IsFalse()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.IsNull("code"));
        var b = Row("b", Condition.IsNotNull("code"));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_IsNotNullAndEquals_IsTrue()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.IsNotNull("code"));
        var b = Row("b", Condition.Equals("code", DecisionValue.Code("X")));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    /// <summary>
    /// The Boolean domain has exactly two values: excluding both via two
    /// NotEquals conditions exhausts it, so the rows cannot both match any
    /// value - exercises IsBoundedDomainExhausted's Excluded/Excluded branch.
    /// </summary>
    [Fact]
    public void RowsOverlap_NotEqualsBothBooleanValues_ExhaustsTheDomain_IsFalse()
    {
        var descriptor = BooleanDescriptor();
        var a = Row("a", Condition.NotEquals("flag", DecisionValue.Boolean(true))); // flag must be false
        var b = Row("b", Condition.NotEquals("flag", DecisionValue.Boolean(false))); // flag must be true

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeFalse();
    }

    [Fact]
    public void IsSubsumedByEarlierRow_ANarrowerLaterRange_IsTrue()
    {
        var earlier = Row("earlier", Condition.Range("age", DecisionValue.Integer(0), DecisionValue.Integer(100), true, true));
        var later = Row("later", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), true, true));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeTrue();
    }

    [Fact]
    public void IsSubsumedByEarlierRow_AWiderLaterRange_IsFalse()
    {
        var earlier = Row("earlier", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), true, true));
        var later = Row("later", Condition.Range("age", DecisionValue.Integer(0), DecisionValue.Integer(100), true, true));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeFalse();
    }

    /// <summary>
    /// The earlier row excludes its own lower boundary (10, exclusive); the
    /// later row includes it. The later row therefore admits a value (10)
    /// the earlier row does not, so it is NOT a subset despite both having
    /// the "same" numeric bound - exercises the inclusive/exclusive equal-
    /// boundary branch of LowerIsAtLeastAsStrict.
    /// </summary>
    [Fact]
    public void IsSubsumedByEarlierRow_EqualBoundaryButLessStrictInclusivity_IsFalse()
    {
        var earlier = Row("earlier", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), lowerInclusive: false, upperInclusive: true));
        var later = Row("later", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), lowerInclusive: true, upperInclusive: true));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeFalse();
    }

    [Fact]
    public void IsSubsumedByEarlierRow_ASingletonSubsetOfAnInSet_IsTrue()
    {
        var earlier = Row("earlier", Condition.In("code", [DecisionValue.Code("A"), DecisionValue.Code("B"), DecisionValue.Code("C")]));
        var later = Row("later", Condition.Equals("code", DecisionValue.Code("A")));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeTrue();
    }

    [Fact]
    public void IsSubsumedByEarlierRow_AValueOutsideTheInSet_IsFalse()
    {
        var earlier = Row("earlier", Condition.In("code", [DecisionValue.Code("A"), DecisionValue.Code("B")]));
        var later = Row("later", Condition.Equals("code", DecisionValue.Code("C")));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_AnExplicitAnyCondition_OverlapsWithAnything()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.Any("code"));
        var b = Row("b", Condition.Equals("code", DecisionValue.Code("X")));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    /// <summary>A column neither row's own Conditions list mentions is treated as <see cref="Operator.Any"/> on that row (the class's own documented contract).</summary>
    [Fact]
    public void RowsOverlap_AColumnOnlyOneRowConditionsOn_IsTreatedAsAnyOnTheOtherRow()
    {
        var descriptor = TwoFactDescriptor();
        var a = new Row("a", [Condition.Equals("code", DecisionValue.Code("X"))], "{}"); // silent on "age"
        var b = new Row("b", [Condition.Range("age", DecisionValue.Integer(0), DecisionValue.Integer(10), true, true)], "{}"); // silent on "code"

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    /// <summary>Symmetric to IsNotNullAndEquals: NonNullAny on the SECOND row this time.</summary>
    [Fact]
    public void RowsOverlap_EqualsAndIsNotNull_IsTrue()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.Equals("code", DecisionValue.Code("X")));
        var b = Row("b", Condition.IsNotNull("code"));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    /// <summary>IsNotNull against an Excluded set over an UNBOUNDED domain (Code): always satisfiable, never exhausted.</summary>
    [Fact]
    public void RowsOverlap_IsNotNullAndNotEquals_OnAnUnboundedDomain_IsTrue()
    {
        var descriptor = CodeDescriptor();
        var a = Row("a", Condition.IsNotNull("code"));
        var b = Row("b", Condition.NotEquals("code", DecisionValue.Code("X")));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    /// <summary>IsNotNull against a degenerate (lower &gt; upper) Range: the range itself is empty, so nothing can satisfy both.</summary>
    [Fact]
    public void RowsOverlap_IsNotNullAndADegenerateRange_IsFalse()
    {
        var descriptor = AgeDescriptor();
        var a = Row("a", Condition.IsNotNull("age"));
        var b = Row("b", Condition.Range("age", DecisionValue.Integer(100), DecisionValue.Integer(50), true, true));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_NotEqualsBothValues_OnATwoValueEnum_ExhaustsTheDomain_IsFalse()
    {
        var descriptor = BaseDescriptor("test.overlap_enum_fixture", new FactDescriptor("channel", FactType.Enum, AllowedValues: ["Web", "Pos"]));
        var a = Row("a", Condition.NotEquals("channel", DecisionValue.Enum("Web")));
        var b = Row("b", Condition.NotEquals("channel", DecisionValue.Enum("Pos")));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeFalse();
    }

    [Theory]
    [InlineData(15, true)] // inside [10,20]
    [InlineData(99, false)] // outside [10,20]
    public void RowsOverlap_EqualsAndRange_ChecksMembership(long value, bool expected)
    {
        var descriptor = AgeDescriptor();
        var equalsRow = Row("equals", Condition.Equals("age", DecisionValue.Integer(value)));
        var rangeRow = Row("range", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), true, true));

        OverlapAnalyzer.RowsOverlap(equalsRow, rangeRow, descriptor).Should().Be(expected);
        // Symmetric: the same facts with the Range on the left must agree.
        OverlapAnalyzer.RowsOverlap(rangeRow, equalsRow, descriptor).Should().Be(expected);
    }

    [Fact]
    public void RowsOverlap_OverlappingDateRanges_IsTrue()
    {
        var descriptor = BaseDescriptor("test.overlap_date_fixture", new FactDescriptor("when", FactType.Date));
        var a = Row("a", Condition.Range("when", DecisionValue.Date(new DateOnly(2026, 1, 1)), DecisionValue.Date(new DateOnly(2026, 6, 30)), true, true));
        var b = Row("b", Condition.Range("when", DecisionValue.Date(new DateOnly(2026, 6, 1)), DecisionValue.Date(new DateOnly(2026, 12, 31)), true, true));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    [Theory]
    [InlineData("a", "b")] // In/Equals orientation (Allowed, Excluded)
    [InlineData("b", "a")] // Excluded/Allowed orientation
    public void RowsOverlap_EqualsAndNotEquals_RegardlessOfArgumentOrder_DifferentValue_IsTrue(string first, string second)
    {
        var descriptor = CodeDescriptor();
        var equalsRow = Row("equals", Condition.Equals("code", DecisionValue.Code("X")));
        var notEqualsRow = Row("notEquals", Condition.NotEquals("code", DecisionValue.Code("Y")));
        var byName = new Dictionary<string, Row> { ["a"] = equalsRow, ["b"] = notEqualsRow };

        OverlapAnalyzer.RowsOverlap(byName[first], byName[second], descriptor).Should().BeTrue();
    }

    [Theory]
    [InlineData("a", "b")] // StartsWith/In orientation (Prefix, Allowed)
    [InlineData("b", "a")] // In/StartsWith orientation (Allowed, Prefix)
    public void RowsOverlap_StartsWithAndIn_RegardlessOfArgumentOrder_Matching_IsTrue(string first, string second)
    {
        var descriptor = CodeDescriptor();
        var startsWithRow = Row("prefix", Condition.StartsWith("code", "AB"));
        var inRow = Row("in", Condition.In("code", [DecisionValue.Code("ABC"), DecisionValue.Code("XYZ")]));
        var byName = new Dictionary<string, Row> { ["a"] = startsWithRow, ["b"] = inRow };

        OverlapAnalyzer.RowsOverlap(byName[first], byName[second], descriptor).Should().BeTrue();
    }

    /// <summary>StartsWith/StartsWith subsumption is not modeled by IsSubsetOf; the conservative default is "not proven", never a crash or a false positive.</summary>
    [Fact]
    public void IsSubsumedByEarlierRow_AnUnmodeledCombination_IsNotProvenSubsumed()
    {
        var earlier = Row("earlier", Condition.StartsWith("code", "AB"));
        var later = Row("later", Condition.StartsWith("code", "ABC"));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeFalse();
    }

    [Fact]
    public void IsSubsumedByEarlierRow_EarlierHasNoLowerBound_LaterIsSubsumedOnThatSide()
    {
        var earlier = Row("earlier", Condition.Range("age", null, DecisionValue.Integer(100), true, true));
        var later = Row("later", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), true, true));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeTrue();
    }

    [Fact]
    public void IsSubsumedByEarlierRow_LaterHasNoLowerBoundButEarlierDoes_IsFalse()
    {
        var earlier = Row("earlier", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(100), true, true));
        var later = Row("later", Condition.Range("age", null, DecisionValue.Integer(20), true, true));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeFalse();
    }

    [Fact]
    public void IsSubsumedByEarlierRow_EarlierHasNoUpperBound_LaterIsSubsumedOnThatSide()
    {
        var earlier = Row("earlier", Condition.Range("age", DecisionValue.Integer(0), null, true, true));
        var later = Row("later", Condition.Range("age", DecisionValue.Integer(10), DecisionValue.Integer(20), true, true));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeTrue();
    }

    [Fact]
    public void IsSubsumedByEarlierRow_LaterHasNoUpperBoundButEarlierDoes_IsFalse()
    {
        var earlier = Row("earlier", Condition.Range("age", DecisionValue.Integer(0), DecisionValue.Integer(100), true, true));
        var later = Row("later", Condition.Range("age", DecisionValue.Integer(10), null, true, true));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeFalse();
    }

    /// <summary>D-11: a Money Range never matches a fact of another currency, so two Money Ranges of different currencies never overlap even when their amounts line up.</summary>
    [Fact]
    public void RowsOverlap_MoneyRangesInDifferentCurrencies_IsFalse()
    {
        var descriptor = BaseDescriptor("test.overlap_money_currency_fixture", new FactDescriptor("amount", FactType.Money));
        var usd = Row("usd", Condition.Range("amount", DecisionValue.Money(0m, "USD"), DecisionValue.Money(100m, "USD"), true, true));
        var cop = Row("cop", Condition.Range("amount", DecisionValue.Money(0m, "COP"), DecisionValue.Money(100m, "COP"), true, true));

        OverlapAnalyzer.RowsOverlap(usd, cop, descriptor).Should().BeFalse();
    }

    /// <summary>D-11: a later Money Range in a different currency is never subsumed by an earlier row's Money Range, no matter how the amounts compare.</summary>
    [Fact]
    public void IsSubsumedByEarlierRow_MoneyRangeInADifferentCurrencyThanTheEarlierRow_IsFalse()
    {
        var earlier = Row("usd", Condition.Range("amount", DecisionValue.Money(0m, "USD"), DecisionValue.Money(100m, "USD"), true, true));
        var later = Row("cop", Condition.Range("amount", DecisionValue.Money(0m, "COP"), DecisionValue.Money(100m, "COP"), true, true));

        OverlapAnalyzer.IsSubsumedByEarlierRow(later, earlier).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_MoneyLowerOpenRangesInDifferentCurrencies_IsFalse()
    {
        var descriptor = BaseDescriptor("test.overlap_money_lower_open_fixture", new FactDescriptor("amount", FactType.Money));
        var usd = Row("usd", Condition.Range("amount", DecisionValue.Money(0m, "USD"), null, true, true));
        var cop = Row("cop", Condition.Range("amount", DecisionValue.Money(0m, "COP"), null, true, true));

        OverlapAnalyzer.RowsOverlap(usd, cop, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_MoneyUpperOpenRangesInDifferentCurrencies_IsFalse()
    {
        var descriptor = BaseDescriptor("test.overlap_money_upper_open_fixture", new FactDescriptor("amount", FactType.Money));
        var usd = Row("usd", Condition.Range("amount", null, DecisionValue.Money(50m, "USD"), true, true));
        var cop = Row("cop", Condition.Range("amount", null, DecisionValue.Money(80m, "COP"), true, true));

        OverlapAnalyzer.RowsOverlap(usd, cop, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_MoneyLowerOpenRangesInTheSameCurrency_IsTrue()
    {
        var descriptor = BaseDescriptor("test.overlap_money_same_currency_fixture", new FactDescriptor("amount", FactType.Money));
        var a = Row("a", Condition.Range("amount", DecisionValue.Money(0m, "USD"), null, true, true));
        var b = Row("b", Condition.Range("amount", DecisionValue.Money(10m, "USD"), null, true, true));

        OverlapAnalyzer.RowsOverlap(a, b, descriptor).Should().BeTrue();
    }

    [Fact]
    public void RowsOverlap_MoneyNotEqualsInDifferentCurrencies_IsFalse()
    {
        var descriptor = BaseDescriptor("test.overlap_money_notequals_fixture", new FactDescriptor("amount", FactType.Money));
        var usd = Row("usd", Condition.NotEquals("amount", DecisionValue.Money(5m, "USD")));
        var cop = Row("cop", Condition.NotEquals("amount", DecisionValue.Money(5m, "COP")));

        OverlapAnalyzer.RowsOverlap(usd, cop, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_MoneyEqualsAndNotEqualsInDifferentCurrencies_IsFalseInBothOrders()
    {
        var descriptor = BaseDescriptor("test.overlap_money_eq_noteq_fixture", new FactDescriptor("amount", FactType.Money));
        var usd = Row("usd", Condition.Equals("amount", DecisionValue.Money(5m, "USD")));
        var cop = Row("cop", Condition.NotEquals("amount", DecisionValue.Money(5m, "COP")));

        OverlapAnalyzer.RowsOverlap(usd, cop, descriptor).Should().BeFalse();
        OverlapAnalyzer.RowsOverlap(cop, usd, descriptor).Should().BeFalse();
    }

    [Fact]
    public void RowsOverlap_MoneyEqualsAndNotEqualsInTheSameCurrencyAndValue_IsFalse_ButDifferentValueIsTrue()
    {
        var descriptor = BaseDescriptor("test.overlap_money_same_noteq_fixture", new FactDescriptor("amount", FactType.Money));
        var eq = Row("eq", Condition.Equals("amount", DecisionValue.Money(5m, "USD")));

        OverlapAnalyzer.RowsOverlap(eq, Row("same", Condition.NotEquals("amount", DecisionValue.Money(5m, "USD"))), descriptor).Should().BeFalse();
        OverlapAnalyzer.RowsOverlap(eq, Row("other", Condition.NotEquals("amount", DecisionValue.Money(6m, "USD"))), descriptor).Should().BeTrue();
    }

    private static Row Row(string key, Condition condition) => new(key, [condition], "{}");

    private static DecisionKindDescriptor TwoFactDescriptor() => new()
    {
        Key = DecisionKindKey.From("test.overlap_two_fact_fixture"),
        SchemaVersion = 1,
        Facts = [new FactDescriptor("code", FactType.Code), new FactDescriptor("age", FactType.Integer)],
        OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "p",
        AuthorPermission = "p",
        ApprovePermission = "p",
        ContainsPersonalData = false,
        ReadTracePermission = "test.decisions.read_trace",
    };

    private static DecisionKindDescriptor AgeDescriptor() => BaseDescriptor("test.overlap_age_fixture", new FactDescriptor("age", FactType.Integer));

    private static DecisionKindDescriptor CodeDescriptor() => BaseDescriptor("test.overlap_code_fixture", new FactDescriptor("code", FactType.Code, Nullable: true));

    private static DecisionKindDescriptor BooleanDescriptor() => BaseDescriptor("test.overlap_bool_fixture", new FactDescriptor("flag", FactType.Boolean));

    private static DecisionKindDescriptor BaseDescriptor(string key, FactDescriptor fact) => new()
    {
        Key = DecisionKindKey.From(key),
        SchemaVersion = 1,
        Facts = [fact],
        OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "p",
        AuthorPermission = "p",
        ApprovePermission = "p",
        ContainsPersonalData = false,
        ReadTracePermission = "test.decisions.read_trace",
    };
}
