using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Conditions;

namespace Synapbiz.Brain.Parity;

/// <summary>Every fact type x operator cell of the matrix, plus decimal scale, money currency and range bound cases.</summary>
internal static class MatcherScenarios
{
    private const string Fact = "f";

    private static readonly Guid ReferenceA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReferenceB = new("22222222-2222-2222-2222-222222222222");

    private static readonly Operator[] Operators =
    [
        Operator.Any, Operator.Equals, Operator.NotEquals, Operator.In, Operator.NotIn,
        Operator.IsNull, Operator.IsNotNull, Operator.Range, Operator.StartsWith,
    ];

    private sealed record TypeSpec(FactType Type, DecisionValue Hit, DecisionValue Miss, DecisionValue Lower, DecisionValue Upper);

    private static readonly TypeSpec[] Specs =
    [
        new(FactType.Code, DecisionValue.Code("A-1"), DecisionValue.Code("B-2"), DecisionValue.Code("A-1"), DecisionValue.Code("B-2")),
        new(FactType.Enum, DecisionValue.Enum("A"), DecisionValue.Enum("B"), DecisionValue.Enum("A"), DecisionValue.Enum("B")),
        new(FactType.Boolean, DecisionValue.Boolean(true), DecisionValue.Boolean(false), DecisionValue.Boolean(false), DecisionValue.Boolean(true)),
        new(FactType.Integer, DecisionValue.Integer(10), DecisionValue.Integer(20), DecisionValue.Integer(5), DecisionValue.Integer(15)),
        new(FactType.Decimal, DecisionValue.Decimal(1.50m), DecisionValue.Decimal(2.5m), DecisionValue.Decimal(1.0m), DecisionValue.Decimal(2.0m)),
        new(FactType.Money, DecisionValue.Money(10.00m, "USD"), DecisionValue.Money(20.00m, "USD"), DecisionValue.Money(5m, "USD"), DecisionValue.Money(15m, "USD")),
        new(FactType.Date, DecisionValue.Date(new DateOnly(2026, 1, 10)), DecisionValue.Date(new DateOnly(2026, 2, 10)), DecisionValue.Date(new DateOnly(2026, 1, 1)), DecisionValue.Date(new DateOnly(2026, 1, 31))),
        new(FactType.Reference, DecisionValue.Reference(ReferenceA), DecisionValue.Reference(ReferenceB), DecisionValue.Reference(ReferenceA), DecisionValue.Reference(ReferenceB)),
    ];

    internal static void Register(ScenarioSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        foreach (var spec in Specs)
        {
            foreach (var op in Operators)
            {
                var condition = Build(op, spec);
                set.Add($"matcher/{spec.Type}/{op}/match", () => Evaluate(condition, spec.Type, spec.Hit));
                set.Add($"matcher/{spec.Type}/{op}/nomatch", () => Evaluate(condition, spec.Type, spec.Miss));
                set.Add($"matcher/{spec.Type}/{op}/null", () => Evaluate(condition, spec.Type, null));
            }
        }

        RegisterDecimalScale(set);
        RegisterForeignCurrency(set);
        RegisterRangeBounds(set);
    }

    private static Condition Build(Operator op, TypeSpec spec) => op switch
    {
        Operator.Any => Condition.Any(Fact),
        Operator.Equals => Condition.Equals(Fact, spec.Hit),
        Operator.NotEquals => Condition.NotEquals(Fact, spec.Hit),
        Operator.In => Condition.In(Fact, [spec.Hit]),
        Operator.NotIn => Condition.NotIn(Fact, [spec.Hit]),
        Operator.IsNull => Condition.IsNull(Fact),
        Operator.IsNotNull => Condition.IsNotNull(Fact),
        Operator.Range => Condition.Range(Fact, spec.Lower, spec.Upper, true, true),
        Operator.StartsWith => Condition.StartsWith(Fact, "A-"),
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator."),
    };

    private static string Evaluate(Condition condition, FactType type, DecisionValue? fact) =>
        Format.Bool(ConditionMatcher.Create(condition, type).Matches(fact));

    private static void RegisterDecimalScale(ScenarioSet set)
    {
        set.Add("matcher/Decimal/Equals/scale_operand_1_5_fact_1_50", () =>
            Evaluate(Condition.Equals(Fact, DecisionValue.Decimal(1.5m)), FactType.Decimal, DecisionValue.Decimal(1.50m)));
        set.Add("matcher/Decimal/Equals/scale_operand_1_50_fact_1_5", () =>
            Evaluate(Condition.Equals(Fact, DecisionValue.Decimal(1.50m)), FactType.Decimal, DecisionValue.Decimal(1.5m)));
        set.Add("matcher/Decimal/In/scale_operand_1_50_fact_1_5", () =>
            Evaluate(Condition.In(Fact, [DecisionValue.Decimal(1.50m)]), FactType.Decimal, DecisionValue.Decimal(1.5m)));
    }

    private static void RegisterForeignCurrency(ScenarioSet set)
    {
        var spec = Specs.Single(s => s.Type == FactType.Money);
        var foreign = DecisionValue.Money(10.00m, "EUR");
        foreach (var op in Operators)
        {
            var condition = Build(op, spec);
            set.Add($"matcher/Money/{op}/foreign_currency", () => Evaluate(condition, FactType.Money, foreign));
        }
    }

    private static void RegisterRangeBounds(ScenarioSet set)
    {
        foreach (var spec in Specs.Where(s => s.Type is FactType.Integer or FactType.Decimal or FactType.Money or FactType.Date))
        {
            var type = spec.Type;
            set.Add($"matcher/{type}/Range/inclusive_at_lower", () =>
                Evaluate(Condition.Range(Fact, spec.Lower, spec.Upper, true, true), type, spec.Lower));
            set.Add($"matcher/{type}/Range/inclusive_at_upper", () =>
                Evaluate(Condition.Range(Fact, spec.Lower, spec.Upper, true, true), type, spec.Upper));
            set.Add($"matcher/{type}/Range/exclusive_at_lower", () =>
                Evaluate(Condition.Range(Fact, spec.Lower, spec.Upper, false, false), type, spec.Lower));
            set.Add($"matcher/{type}/Range/exclusive_at_upper", () =>
                Evaluate(Condition.Range(Fact, spec.Lower, spec.Upper, false, false), type, spec.Upper));
            set.Add($"matcher/{type}/Range/open_upper_inside", () =>
                Evaluate(Condition.Range(Fact, spec.Lower, null, true, true), type, spec.Miss));
            set.Add($"matcher/{type}/Range/open_lower_outside", () =>
                Evaluate(Condition.Range(Fact, null, spec.Lower, true, true), type, spec.Miss));
        }
    }
}
