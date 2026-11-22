using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.Decisions.Engine.Conditions;

/// <summary>
/// The one <see cref="FactType"/> to <see cref="DecisionValue"/> case mapping (D-04):
/// each fact type is carried by exactly one value case. Used to type-check condition
/// operands when a table is validated and caller-supplied fact values when a
/// fact set is built, so a mismatch is reported at configuration or at the
/// boundary and never first surfaces as an evaluation-time failure.
/// </summary>
internal static class ValueTypes
{
    /// <summary>Whether <paramref name="value"/> is the value case that <paramref name="type"/> declares.</summary>
    internal static bool Matches(FactType type, DecisionValue value) => type switch
    {
        FactType.Code => value is CodeValue,
        FactType.Enum => value is EnumValue,
        FactType.Boolean => value is BooleanValue,
        FactType.Integer => value is IntegerValue,
        FactType.Decimal => value is DecimalValue,
        FactType.Money => value is MoneyValue,
        FactType.Date => value is DateValue,
        FactType.Reference => value is ReferenceValue,
        _ => false,
    };
}
