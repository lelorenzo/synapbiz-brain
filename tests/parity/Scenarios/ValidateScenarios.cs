using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Contracts.Validation;
using Nuvora.Decisions.Engine.Validation;

namespace Synapbiz.Brain.Parity;

/// <summary>One scenario per validation code, plus valid positive controls.</summary>
internal static class ValidateScenarios
{
    internal static void Register(ScenarioSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        set.Add("validate/valid_table", () => Report(
            new DecisionTable(HitPolicy.Unique, [Web(), Pos()]), Channel()));

        set.Add("validate/valid_table_with_default_row", () => Report(
            new DecisionTable(HitPolicy.Unique, [Web()], new Row("default", [], "{}")), Channel()));

        set.Add("validate/duplicate_row_key", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [
                    new Row("same", [Condition.Equals("channel", DecisionValue.Enum("Web"))], "{}"),
                    new Row("same", [Condition.Equals("channel", DecisionValue.Enum("Pos"))], "{}"),
                ]),
            Channel()));

        set.Add("validate/default_row_reuses_a_row_key", () => Report(
            new DecisionTable(HitPolicy.Unique, [Web()], new Row("web", [], "{}")), Channel()));

        set.Add("validate/unknown_fact", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [
                    new Row("ghost_a", [Condition.Equals("ghost", DecisionValue.Code("A"))], "{}"),
                    new Row("ghost_b", [Condition.Equals("ghost", DecisionValue.Code("B"))], "{}"),
                ]),
            Channel()));

        set.Add("validate/cell_type_mismatch_decimal_bound_on_integer_fact", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [new Row("bad_bound", [Condition.Range("amount", DecisionValue.Decimal(10.5m), null, true, true)], "{}")]),
            Fixtures.AmountKind("synthetic.validate_integer", FactType.Integer)));

        set.Add("validate/cell_type_mismatch_mixed_in_set", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [new Row("bad_set", [Condition.In("amount", [DecisionValue.Integer(1), DecisionValue.Code("2")])], "{}")]),
            Fixtures.AmountKind("synthetic.validate_integer", FactType.Integer)));

        set.Add("validate/cell_type_mismatch_missing_operand", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [new Row("no_operand", [new Condition { FactName = "amount", Operator = Operator.Equals }], "{}")]),
            Fixtures.AmountKind("synthetic.validate_integer", FactType.Integer)));

        set.Add("validate/operator_not_allowed_range_on_code", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [new Row("bad", [Condition.Range("amount", DecisionValue.Code("A"), DecisionValue.Code("Z"), true, true)], "{}")]),
            Fixtures.AmountKind("synthetic.validate_code", FactType.Code)));

        set.Add("validate/operator_not_allowed_in_on_boolean", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [new Row("bad", [Condition.In("amount", [DecisionValue.Boolean(true)])], "{}")]),
            Fixtures.AmountKind("synthetic.validate_boolean", FactType.Boolean)));

        set.Add("validate/overlap_under_unique", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [
                    new Row("in_both", [Condition.In("channel", [DecisionValue.Enum("Web"), DecisionValue.Enum("Pos")])], "{}"),
                    Web(),
                ]),
            Channel()));

        set.Add("validate/overlap_of_integer_ranges_under_unique", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [
                    new Row("low", [Condition.Range("amount", DecisionValue.Integer(0), DecisionValue.Integer(10), true, true)], "{}"),
                    new Row("mid", [Condition.Range("amount", DecisionValue.Integer(10), DecisionValue.Integer(20), true, true)], "{}"),
                ],
                new Row("default", [], "{}")),
            Fixtures.AmountKind("synthetic.validate_integer", FactType.Integer)));

        set.Add("validate/adjacent_integer_ranges_under_unique", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [
                    new Row("low", [Condition.Range("amount", DecisionValue.Integer(0), DecisionValue.Integer(9), true, true)], "{}"),
                    new Row("high", [Condition.Range("amount", DecisionValue.Integer(10), null, true, true)], "{}"),
                ]),
            Fixtures.AmountKind("synthetic.validate_integer", FactType.Integer)));

        set.Add("validate/unreachable_row_under_first", () => Report(
            new DecisionTable(
                HitPolicy.First,
                [
                    new Row("first", [Condition.Equals("channel", DecisionValue.Enum("Web"))], "{}"),
                    new Row("second", [Condition.Equals("channel", DecisionValue.Enum("Web"))], "{}"),
                ]),
            Channel()));

        set.Add("validate/reachable_rows_under_first", () => Report(
            new DecisionTable(HitPolicy.First, [Web(), Pos()]), Channel()));

        set.Add("validate/incomplete_enum_coverage", () => Report(
            new DecisionTable(HitPolicy.Unique, [Web(), Pos()]),
            Fixtures.ChannelKind("synthetic.validate_channel_three", "Pos", "Web", "B2B")));

        set.Add("validate/incomplete_total_coverage_required", () => Report(
            new DecisionTable(HitPolicy.Unique, [new Row("any", [Condition.Any("code")], "{}")]),
            Fixtures.Descriptor("synthetic.validate_total", [new FactDescriptor("code", FactType.Code)], requiresTotalCoverage: true)));

        set.Add("validate/total_coverage_required_with_default_row", () => Report(
            new DecisionTable(
                HitPolicy.Unique, [new Row("one", [Condition.Equals("code", DecisionValue.Code("A"))], "{}")], new Row("default", [], "{}")),
            Fixtures.Descriptor("synthetic.validate_total", [new FactDescriptor("code", FactType.Code)], requiresTotalCoverage: true)));

        set.Add("validate/money_currency_mismatch_within_one_row", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [new Row("mixed", [Condition.In("amount", [DecisionValue.Money(10m, "USD"), DecisionValue.Money(20m, "COP")])], "{}")]),
            Fixtures.AmountKind("synthetic.validate_money", FactType.Money)));

        set.Add("validate/money_single_currency_is_valid", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [new Row("usd", [Condition.In("amount", [DecisionValue.Money(10m, "USD"), DecisionValue.Money(20m, "USD")])], "{}")],
                new Row("default", [], "{}")),
            Fixtures.AmountKind("synthetic.validate_money", FactType.Money)));

        set.Add("validate/invalid_outcome_json_under_collect", () => Report(
            new DecisionTable(
                HitPolicy.Collect,
                [new Row("bad", [Condition.Any("code")], "not json")]),
            Fixtures.Descriptor("synthetic.validate_collect", [new FactDescriptor("code", FactType.Code)])));

        set.Add("validate/non_object_outcome_under_collect", () => Report(
            new DecisionTable(
                HitPolicy.Collect,
                [new Row("array", [Condition.Any("code")], "[1,2]")]),
            Fixtures.Descriptor("synthetic.validate_collect", [new FactDescriptor("code", FactType.Code)])));

        set.Add("validate/duplicate_outcome_and_conditions_under_collect", () => Report(
            new DecisionTable(
                HitPolicy.Collect,
                [
                    new Row("one", [Condition.Equals("code", DecisionValue.Code("A"))], """{"a":1,"b":2}"""),
                    new Row("two", [Condition.Equals("code", DecisionValue.Code("A"))], """{"b":2,"a":1}"""),
                ]),
            Fixtures.Descriptor("synthetic.validate_collect", [new FactDescriptor("code", FactType.Code)])));

        set.Add("validate/outcome_validator_refuses", () => Report(
            new DecisionTable(
                HitPolicy.Unique,
                [new Row("bad_outcome", [Condition.Any("code")], """{"class":"BAD"}""")]),
            Fixtures.Descriptor("synthetic.validate_outcome", [new FactDescriptor("code", FactType.Code)]),
            new RefusesBadOutcomeValidator()));

        set.Add("validate/unsupported_hit_policy", () => Report(
            new DecisionTable((HitPolicy)99, [Web(), Pos()]), Channel()));
    }

    private static string Report(
        DecisionTable table, DecisionKindDescriptor descriptor, IDecisionOutcomeValidator? outcomeValidator = null)
    {
        var report = TableValidator.Validate(table, descriptor, outcomeValidator);
        var errors = report.Errors.Select(e => e.Code).OrderBy(c => c, StringComparer.Ordinal);
        var warnings = report.Warnings.Select(w => w.Code).OrderBy(c => c, StringComparer.Ordinal);
        return Format.Join(errors) + "|" + Format.Join(warnings);
    }

    private static DecisionKindDescriptor Channel() => Fixtures.ChannelKind("synthetic.validate_channel", "Web", "Pos");

    private static Row Web() => new("web", [Condition.Equals("channel", DecisionValue.Enum("Web"))], "{}");

    private static Row Pos() => new("pos", [Condition.Equals("channel", DecisionValue.Enum("Pos"))], "{}");

    private sealed class RefusesBadOutcomeValidator : IDecisionOutcomeValidator
    {
        internal const string Code = "synthetic.outcome.refused";

        public IReadOnlyList<ValidationIssue> Validate(string rowKey, string outcomeJson) =>
            outcomeJson.Contains("BAD", StringComparison.Ordinal)
                ? [new ValidationIssue(Code, $"Row '{rowKey}' has a refused outcome.", [rowKey])]
                : [];
    }
}
