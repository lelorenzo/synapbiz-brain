using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Contracts.Validation;
using Nuvora.Decisions.Engine.Validation;

namespace Nuvora.UnitTests.Decisions.Engine;

/// <summary>
/// P2-20.2 step 4 (paquete 0.A369): one fixture per validation code, plus a
/// clean-table positive control (so the suite cannot pass by rejecting
/// everything — GUARD-VERIFICATION-STANDARD §2).
/// </summary>
public sealed class TableValidatorTests
{
    [Fact]
    public void Unique_OverlappingRows_ReportsOverlapNamingBothKeys()
    {
        var descriptor = ChannelDescriptor("test.overlap_fixture", "Web", "Pos");
        var table = new DecisionTable(
            HitPolicy.Unique,
            [
                Row("in_both", Condition.In("channel", [DecisionValue.Enum("Web"), DecisionValue.Enum("Pos")])),
                Row("web_only", Condition.Equals("channel", DecisionValue.Enum("Web"))),
            ]);

        var report = TableValidator.Validate(table, descriptor);

        report.IsValid.Should().BeFalse();
        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeOverlap
            && e.RowKeys.Contains("in_both") && e.RowKeys.Contains("web_only"));
    }

    /// <summary>MUTATION (performed and reverted at gate time): removing the overlap check lets this fixture pass with zero errors.</summary>
    [Fact]
    public void First_SecondRowEqualsTheFirst_ReportsUnreachable()
    {
        var descriptor = ChannelDescriptor("test.unreachable_fixture", "Web", "Pos");
        var table = new DecisionTable(
            HitPolicy.First,
            [
                Row("first", Condition.Equals("channel", DecisionValue.Enum("Web"))),
                Row("second", Condition.Equals("channel", DecisionValue.Enum("Web"))),
            ]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeUnreachable && e.RowKeys.Contains("second"));
    }

    /// <summary>MUTATION (performed and reverted at gate time): treating Enum as unbounded (skip enumeration) lets this fixture pass with zero errors.</summary>
    [Fact]
    public void Enum_ThreeOfFourValuesCovered_NoDefault_ReportsIncompleteNamingTheMissingValue()
    {
        var descriptor = ChannelDescriptor("test.incomplete_fixture", "Pos", "Web", "B2B", "Marketplace");
        var table = new DecisionTable(
            HitPolicy.Unique,
            [
                Row("pos", Condition.Equals("channel", DecisionValue.Enum("Pos"))),
                Row("web", Condition.Equals("channel", DecisionValue.Enum("Web"))),
                Row("b2b", Condition.Equals("channel", DecisionValue.Enum("B2B"))),
            ]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeIncomplete && e.Message.Contains("Marketplace", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiresTotalCoverage_WithNoDefaultRow_ReportsIncomplete_EvenWithoutEnumerableColumns()
    {
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.total_coverage_fixture"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("code", FactType.Code)],
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            RequiresTotalCoverage = true,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = new DecisionTable(HitPolicy.Unique, [Row("any", Condition.Any("code"))]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeIncomplete);
    }

    [Fact]
    public void RangeOperator_OnACodeColumn_ReportsTypeMismatch()
    {
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.type_mismatch_fixture"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("code", FactType.Code)],
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = new DecisionTable(
            HitPolicy.Unique,
            [Row("bad", Condition.Range("code", DecisionValue.Code("A"), DecisionValue.Code("Z"), true, true))]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeTypeMismatch && e.RowKeys.Contains("bad"));
    }

    [Fact]
    public void AnOutcomeValidatorThatRefuses_SurfacesWithTheValidatorsOwnCode()
    {
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.outcome_invalid_fixture"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("code", FactType.Code)],
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = new DecisionTable(HitPolicy.Unique, [Row("bad_outcome", Condition.Any("code"), outcome: """{"class":"BAD"}""")]);

        var report = TableValidator.Validate(table, descriptor, new RefusesBadOutcomeValidator());

        report.Errors.Should().ContainSingle(e => e.Code == RefusesBadOutcomeValidator.Code && e.RowKeys.Contains("bad_outcome"));
    }

    /// <summary>
    /// Positive control (GUARD-VERIFICATION-STANDARD §2): a genuinely
    /// well-formed table — disjoint rows, full Enum coverage, no mismatched
    /// cells — must report zero errors. Without this fixture the suite
    /// could pass by rejecting every table, proving nothing.
    /// </summary>
    [Fact]
    public void AWellFormedTable_ReportsZeroErrors()
    {
        var descriptor = ChannelDescriptor("test.clean_fixture", "Web", "Pos");
        var table = new DecisionTable(
            HitPolicy.Unique,
            [
                Row("web", Condition.Equals("channel", DecisionValue.Enum("Web"))),
                Row("pos", Condition.Equals("channel", DecisionValue.Enum("Pos"))),
            ]);

        var report = TableValidator.Validate(table, descriptor);

        report.IsValid.Should().BeTrue();
        report.Errors.Should().BeEmpty();
    }

    [Fact]
    public void MoneyCondition_MixingCurrenciesWithinOneRow_ReportsMoneyCurrencyMismatch()
    {
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.money_mismatch_fixture"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("amount", FactType.Money)],
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = new DecisionTable(
            HitPolicy.Unique,
            [Row("mixed", Condition.In("amount", [DecisionValue.Money(10m, "USD"), DecisionValue.Money(20m, "COP")]))]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeMoneyCurrencyMismatch && e.RowKeys.Contains("mixed"));
    }

    [Fact]
    public void PartialEnumCoverage_WithADefaultRow_ReportsNoIncompleteIssue()
    {
        var descriptor = ChannelDescriptor("test.partial_with_default_fixture", "Web", "Pos", "B2B");
        var table = new DecisionTable(
            HitPolicy.Unique,
            [Row("web", Condition.Equals("channel", DecisionValue.Enum("Web")))],
            DefaultRow: new Row("default", [], "{}"));

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().NotContain(e => e.Code == TableValidator.CodeIncomplete);
    }

    [Fact]
    public void Boolean_OneOfTwoValuesCovered_NoDefault_ReportsIncompleteNamingFalse()
    {
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.boolean_incomplete_fixture"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("isUrgent", FactType.Boolean)],
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = new DecisionTable(HitPolicy.Unique, [Row("urgent", Condition.Equals("isUrgent", DecisionValue.Boolean(true)))]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeIncomplete && e.Message.Contains("false", StringComparison.Ordinal));
    }

    /// <summary>
    /// D-12: exhaustive enumeration is bounded to CoverageAnalyzer.MaxEnumerableProduct
    /// (10,000) combinations; above that, the check is silently skipped rather
    /// than attempted unsoundly or made to hang. 14 Boolean facts give
    /// 2^14 = 16,384 combinations, and only one of them is covered here, yet
    /// the gate must stay silent rather than report thousands of gaps.
    /// </summary>
    [Fact]
    public void EnumerableProductAboveTheCap_SkipsTheExhaustiveCoverageCheck_WithoutReportingIncomplete()
    {
        var facts = Enumerable.Range(0, 14).Select(i => new FactDescriptor($"flag{i}", FactType.Boolean)).ToList();
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.cap_exceeded_fixture"),
            SchemaVersion = 1,
            Facts = facts,
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var onlyCombinationCovered = facts.Select(f => Condition.Equals(f.Name, DecisionValue.Boolean(true))).ToList();
        var table = new DecisionTable(HitPolicy.Unique, [new Row("one_combo", onlyCombinationCovered, "{}")]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().NotContain(e => e.Code == TableValidator.CodeIncomplete);
    }

    /// <summary>
    /// D-12: a FIRST table with an Enum column AND an Integer
    /// Range column must not call the later row unreachable just because the
    /// exhaustive enumeration walk (CoverageAnalyzer) only varies the Enum
    /// column - evaluating channel=Web, amount=500 genuinely decides
    /// "web_large", so it is reachable.
    /// </summary>
    [Fact]
    public void First_MixedEnumerableAndRangeColumns_LaterRowDistinguishedByRange_IsNotUnreachable()
    {
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.mixed_range_reachable_fixture"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("channel", FactType.Enum, AllowedValues: ["Web"]), new FactDescriptor("amount", FactType.Integer)],
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = new DecisionTable(
            HitPolicy.First,
            [
                new Row(
                    "web_small",
                    [Condition.Equals("channel", DecisionValue.Enum("Web")), Condition.Range("amount", null, DecisionValue.Integer(100), true, false)],
                    "{}"),
                new Row(
                    "web_large",
                    [Condition.Equals("channel", DecisionValue.Enum("Web")), Condition.Range("amount", DecisionValue.Integer(100), null, true, true)],
                    "{}"),
            ]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().NotContain(e => e.Code == TableValidator.CodeUnreachable && e.RowKeys.Contains("web_large"));
    }

    /// <summary>
    /// Positive control: when the ONLY column is a
    /// non-enumerable Integer range, subsumption still has to be caught -
    /// here by the pairwise interval-containment check, since there is no
    /// enumerable column to feed an enumeration walk at all.
    /// </summary>
    [Fact]
    public void First_SameRangeColumnOnly_LaterRowFullySubsumedByEarlierOpenRange_ReportsUnreachable()
    {
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.range_only_unreachable_fixture"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("amount", FactType.Integer)],
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = new DecisionTable(
            HitPolicy.First,
            [
                new Row("amount_nonneg", [Condition.Range("amount", DecisionValue.Integer(0), null, true, true)], "{}"),
                new Row("amount_large", [Condition.Range("amount", DecisionValue.Integer(100), null, true, true)], "{}"),
            ]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeUnreachable && e.RowKeys.Contains("amount_large"));
    }

    [Fact]
    public void DecimalBound_OnAnIntegerColumn_ReportsCellTypeMismatchNamingRowAndFact()
    {
        var descriptor = AmountDescriptor("test.cell_type_fixture", FactType.Integer);
        var table = new DecisionTable(
            HitPolicy.First,
            [Row("bad_bound", Condition.Range("amount", DecisionValue.Decimal(10.5m), null, true, true))]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeCellTypeMismatch
            && e.RowKeys.Contains("bad_bound") && e.Message.Contains("amount", StringComparison.Ordinal));
    }

    [Fact]
    public void InSet_WithOneMistypedMember_ReportsCellTypeMismatch()
    {
        var descriptor = AmountDescriptor("test.cell_type_in_fixture", FactType.Integer);
        var table = new DecisionTable(
            HitPolicy.First,
            [Row("bad_set", Condition.In("amount", [DecisionValue.Integer(1), DecisionValue.Code("2")]))]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeCellTypeMismatch && e.RowKeys.Contains("bad_set"));
    }

    [Fact]
    public void EqualsWithoutAnOperand_ReportsCellTypeMismatch()
    {
        var descriptor = AmountDescriptor("test.cell_missing_operand_fixture", FactType.Integer);
        var table = new DecisionTable(
            HitPolicy.First,
            [Row("no_operand", new Condition { FactName = "amount", Operator = Operator.Equals })]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeCellTypeMismatch && e.RowKeys.Contains("no_operand"));
    }

    [Fact]
    public void WellTypedCells_ReportNoCellTypeMismatch()
    {
        var descriptor = AmountDescriptor("test.cell_type_control_fixture", FactType.Integer);
        var table = new DecisionTable(
            HitPolicy.First,
            [
                Row("low", Condition.Range("amount", DecisionValue.Integer(0), DecisionValue.Integer(9), true, true)),
                Row("high", Condition.Range("amount", DecisionValue.Integer(10), null, true, true)),
            ]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().NotContain(e => e.Code == TableValidator.CodeCellTypeMismatch);
    }

    [Fact]
    public void Unique_ConditionOnAnUndeclaredFact_ReportsUnknownFactInsteadOfThrowing()
    {
        var descriptor = ChannelDescriptor("test.unknown_fact_validator_fixture", "Web", "Pos");
        var table = new DecisionTable(
            HitPolicy.Unique,
            [
                Row("ghost_a", Condition.Equals("ghost", DecisionValue.Code("A"))),
                Row("ghost_b", Condition.Equals("ghost", DecisionValue.Code("B"))),
            ]);

        var act = () => TableValidator.Validate(table, descriptor);

        var report = act.Should().NotThrow().Subject;
        report.Errors.Should().Contain(e => e.Code == TableValidator.CodeUnknownFact && e.RowKeys.Contains("ghost_a"));
        report.Errors.Should().Contain(e => e.Code == TableValidator.CodeUnknownFact && e.RowKeys.Contains("ghost_b"));
    }

    [Fact]
    public void First_MistypedBounds_DoNotThrowFromTheUnreachabilityAnalysis()
    {
        var descriptor = AmountDescriptor("test.cell_type_first_fixture", FactType.Integer);
        var table = new DecisionTable(
            HitPolicy.First,
            [
                Row("a", Condition.Range("amount", DecisionValue.Decimal(1m), null, true, true)),
                Row("b", Condition.Range("amount", DecisionValue.Integer(5), null, true, true)),
            ]);

        var act = () => TableValidator.Validate(table, descriptor);

        act.Should().NotThrow().Subject.Errors.Should().Contain(e => e.Code == TableValidator.CodeCellTypeMismatch && e.RowKeys.Contains("a"));
    }

    [Fact]
    public void TwoRowsWithTheSameKey_ReportsDuplicateRowKey()
    {
        var descriptor = ChannelDescriptor("test.duplicate_key_fixture", "Web", "Pos");
        var table = new DecisionTable(
            HitPolicy.Unique,
            [
                Row("same", Condition.Equals("channel", DecisionValue.Enum("Web"))),
                Row("same", Condition.Equals("channel", DecisionValue.Enum("Pos"))),
            ]);

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeDuplicateRowKey && e.RowKeys.Contains("same"));
    }

    [Fact]
    public void ADefaultRowReusingARowKey_ReportsDuplicateRowKey()
    {
        var descriptor = ChannelDescriptor("test.duplicate_default_key_fixture", "Web", "Pos");
        var table = new DecisionTable(
            HitPolicy.Unique,
            [Row("web", Condition.Equals("channel", DecisionValue.Enum("Web")))],
            DefaultRow: new Row("web", [], "{}"));

        var report = TableValidator.Validate(table, descriptor);

        report.Errors.Should().ContainSingle(e => e.Code == TableValidator.CodeDuplicateRowKey);
    }

    private static DecisionKindDescriptor AmountDescriptor(string key, FactType type) => new()
    {
        Key = DecisionKindKey.From(key),
        SchemaVersion = 1,
        Facts = [new FactDescriptor("amount", type)],
        OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "p",
        AuthorPermission = "p",
        ApprovePermission = "p",
        ContainsPersonalData = false,
        ReadTracePermission = "test.decisions.read_trace",
    };

    private static Row Row(string key, Condition condition, string outcome = "{}") => new(key, [condition], outcome);

    private static DecisionKindDescriptor ChannelDescriptor(string key, params string[] allowedValues) => new()
    {
        Key = DecisionKindKey.From(key),
        SchemaVersion = 1,
        Facts = [new FactDescriptor("channel", FactType.Enum, AllowedValues: allowedValues)],
        OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "p",
        AuthorPermission = "p",
        ApprovePermission = "p",
        ContainsPersonalData = false,
        ReadTracePermission = "test.decisions.read_trace",
    };

    private sealed class RefusesBadOutcomeValidator : IDecisionOutcomeValidator
    {
        public const string Code = "test.outcome.refused";

        public IReadOnlyList<ValidationIssue> Validate(string rowKey, string outcomeJson) =>
            outcomeJson.Contains("BAD", StringComparison.Ordinal)
                ? [new ValidationIssue(Code, $"Row '{rowKey}' has a refused outcome.", [rowKey])]
                : [];
    }
}
