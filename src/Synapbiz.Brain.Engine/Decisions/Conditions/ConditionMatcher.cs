using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.Decisions.Engine.Conditions;

/// <summary>
/// Matches one <see cref="Condition"/> against a fact value, for a known
/// <see cref="FactType"/> (D-04, D-11). <see cref="Create"/> is the only way
/// to obtain one and refuses — at construction, never at match time — an
/// operator/type combination <see cref="OperatorMatrix"/> forbids. Every
/// operator's comparison against a <see cref="MoneyValue"/> of a different
/// currency than the condition's operand simply does not match: it is never
/// an exception, and never flips to a match under a negated operator either
/// (D-11 — the engine never converts; see <see cref="Operator"/>'s remarks
/// for the one rule applied uniformly here).
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public sealed class ConditionMatcher
{
    /// <summary>Stable error code: the condition's operator is not allowed on the fact's declared type.</summary>
    public const string ErrorOperatorNotAllowed = "decisions.condition.operator_not_allowed";

    /// <summary>Stable error code: a Range bound's type does not match the fact's declared type.</summary>
    public const string ErrorRangeTypeMismatch = "decisions.condition.range_type_mismatch";

    private readonly Condition _condition;

    private ConditionMatcher(Condition condition)
    {
        _condition = condition;
    }

    /// <summary>
    /// Validates <paramref name="condition"/> against <paramref name="type"/>
    /// via <see cref="OperatorMatrix"/> and returns a matcher. Throws
    /// <see cref="DecisionEngineException"/> (<see cref="ErrorOperatorNotAllowed"/>)
    /// when the operator is forbidden for that type.
    /// </summary>
    public static ConditionMatcher Create(Condition condition, FactType type)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (!OperatorMatrix.IsAllowed(condition.Operator, type))
        {
            throw new DecisionEngineException(
                ErrorOperatorNotAllowed,
                $"Operator '{condition.Operator}' is not allowed on a fact of type '{type}' (fact '{condition.FactName}').",
                new Dictionary<string, object?>
                {
                    ["operator"] = condition.Operator.ToString(),
                    ["factType"] = type.ToString(),
                    ["fact"] = condition.FactName,
                });
        }

        return new ConditionMatcher(condition);
    }

    /// <summary>Whether <paramref name="factValue"/> (null means the fact is absent) satisfies the condition.</summary>
    public bool Matches(DecisionValue? factValue) => _condition.Operator switch
    {
        Operator.Any => true,
        Operator.IsNull => factValue is null,
        Operator.IsNotNull => factValue is not null,
        Operator.Equals => factValue is not null && ValuesEqual(factValue, _condition.Value!),
        Operator.NotEquals => factValue is not null && CurrenciesCompatible(_condition.Value, factValue) && !ValuesEqual(factValue, _condition.Value!),
        Operator.In => factValue is not null && _condition.Values.Any(v => ValuesEqual(factValue, v)),
        Operator.NotIn => factValue is not null && HasACurrencyCompatibleOperand(factValue, _condition.Values) && !_condition.Values.Any(v => ValuesEqual(factValue, v)),
        Operator.StartsWith => MatchesStartsWith(factValue),
        Operator.Range => MatchesRange(factValue),
        _ => throw new ArgumentOutOfRangeException(nameof(factValue), _condition.Operator, "Unknown operator."),
    };

    private bool MatchesStartsWith(DecisionValue? factValue) =>
        factValue is CodeValue code && _condition.Value is CodeValue prefix
        && code.Value.StartsWith(prefix.Value, StringComparison.Ordinal);

    private bool MatchesRange(DecisionValue? factValue)
    {
        if (factValue is null)
        {
            return false;
        }

        if (!CurrenciesCompatible(_condition.Value, factValue) || !CurrenciesCompatible(_condition.UpperValue, factValue))
        {
            return false;
        }

        if (_condition.Value is not null)
        {
            var cmp = CompareOrdered(factValue, _condition.Value);
            var belowLower = _condition.LowerInclusive ? cmp < 0 : cmp <= 0;
            if (belowLower)
            {
                return false;
            }
        }

        if (_condition.UpperValue is not null)
        {
            var cmp = CompareOrdered(factValue, _condition.UpperValue);
            var aboveUpper = _condition.UpperInclusive ? cmp > 0 : cmp >= 0;
            if (aboveUpper)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// D-11: a Money bound only ever compares against a Money fact of the
    /// SAME currency; a different currency means "does not match", not an
    /// error, and no other case restricts currency. This is the ONE rule for
    /// every operator (chosen over a typed currency-mismatch error): Equals
    /// and In already produce it naturally through <see cref="ValuesEqual"/>
    /// (a cross-currency pair is never "equal", so Equals/In are already a
    /// non-match); NotEquals and NotIn apply it explicitly here, because
    /// negating "not equal to a value this fact's currency cannot even be
    /// compared to" must NOT flip into a match — the condition does not
    /// speak to that currency at all, in either polarity.
    /// </summary>
    private static bool CurrenciesCompatible(DecisionValue? bound, DecisionValue factValue) =>
        bound is not MoneyValue boundMoney
        || factValue is not MoneyValue factMoney
        || string.Equals(boundMoney.Currency, factMoney.Currency, StringComparison.Ordinal);

    /// <summary>
    /// D-11, for <see cref="Operator.NotIn"/>: true when at least one operand shares the
    /// fact's currency (or neither side is Money, where every operand is "compatible").
    /// A NotIn set that is ENTIRELY in a foreign currency never matches — the condition
    /// has nothing comparable to say about this fact — but a set with even one
    /// same-currency operand evaluates normally against it.
    /// </summary>
    private static bool HasACurrencyCompatibleOperand(DecisionValue factValue, IReadOnlyList<DecisionValue> operands) =>
        operands.Count == 0 || operands.Any(v => CurrenciesCompatible(v, factValue));

    /// <summary>
    /// D-11: equal only when both are the same case and payload; two
    /// <see cref="MoneyValue"/>s are equal only when currency AND amount
    /// match — a cross-currency comparison is "not equal", never an error.
    /// </summary>
    private static bool ValuesEqual(DecisionValue a, DecisionValue b) => (a, b) switch
    {
        (MoneyValue x, MoneyValue y) => string.Equals(x.Currency, y.Currency, StringComparison.Ordinal) && x.Amount == y.Amount,
        _ => a.Equals(b),
    };

    private static int CompareOrdered(DecisionValue fact, DecisionValue bound) => (fact, bound) switch
    {
        (IntegerValue f, IntegerValue b) => f.Value.CompareTo(b.Value),
        (DecimalValue f, DecimalValue b) => f.Value.CompareTo(b.Value),
        (DateValue f, DateValue b) => f.Value.CompareTo(b.Value),
        (MoneyValue f, MoneyValue b) => f.Amount.CompareTo(b.Amount),
        _ => throw new DecisionEngineException(
            ErrorRangeTypeMismatch,
            $"Range bound of type '{bound.GetType().Name}' does not match fact value of type '{fact.GetType().Name}'.",
            new Dictionary<string, object?> { ["boundType"] = bound.GetType().Name, ["factType"] = fact.GetType().Name }),
    };
}
