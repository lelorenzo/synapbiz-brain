using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.Decisions.Engine.Conditions;

/// <summary>
/// Which <see cref="Operator"/> values a <see cref="Condition"/> on a given
/// <see cref="FactType"/> may use (D-04): <see cref="Operator.StartsWith"/>
/// only on <see cref="FactType.Code"/>; <see cref="Operator.Range"/> only on
/// Integer, Decimal, Money, Date; <see cref="Operator.In"/> /
/// <see cref="Operator.NotIn"/> everywhere except <see cref="FactType.Boolean"/>.
/// Pure, stateless lookup — no dependency on a specific fact value.
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public static class OperatorMatrix
{
    /// <summary>True when <paramref name="op"/> is a legal operator for a condition on a fact of <paramref name="type"/>.</summary>
    public static bool IsAllowed(Operator op, FactType type) => op switch
    {
        Operator.Any or Operator.Equals or Operator.NotEquals or Operator.IsNull or Operator.IsNotNull => true,
        Operator.In or Operator.NotIn => type != FactType.Boolean,
        Operator.Range => type is FactType.Integer or FactType.Decimal or FactType.Money or FactType.Date,
        Operator.StartsWith => type == FactType.Code,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator."),
    };
}
