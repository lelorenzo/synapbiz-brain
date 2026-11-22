using System.Diagnostics.CodeAnalysis;

namespace Nuvora.Decisions.Contracts.Tables;

/// <summary>
/// Comparison operator usable in a <see cref="Condition"/> (D-04). Explicit
/// ordinals: a table's rows are part of a ruleset version's content hash
/// (P2-20.3), so these values are never renumbered once shipped.
/// </summary>
/// <remarks>
/// D-11 (Money, one currency rule, applies identically to every operator
/// below): a Money operand only ever compares against a Money fact of the
/// SAME currency. The engine never converts, and a cross-currency comparison
/// is always a non-match — never a thrown exception — regardless of the
/// operator's polarity: <see cref="Equals"/>, <see cref="NotEquals"/>,
/// <see cref="In"/>, <see cref="NotIn"/> and <see cref="Range"/> all treat a
/// foreign-currency operand as "this condition does not speak to that
/// currency", so a different-currency fact never matches any of them, not
/// even the negated ones (negating "incomparable" is still "incomparable",
/// never "matches").
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Operator' is the DMN/condition vocabulary pinned by decision D-04 ('Operator {Any, Equals, NotEquals, In, NotIn, IsNull, IsNotNull, Range, StartsWith}'); it is a reserved word in VB only, not C#, and the type is never consumed from VB.")]
public enum Operator
{
    /// <summary>Matches every value, including an absent (null) fact — the implicit operator for an unlisted column.</summary>
    Any = 0,

    /// <summary>Exact match; refuses a null fact.</summary>
    Equals = 1,

    /// <summary>Anything but the operand; refuses a null fact.</summary>
    NotEquals = 2,

    /// <summary>Membership in a finite set; refuses a null fact. Not allowed on <see cref="Kinds.FactType.Boolean"/>.</summary>
    In = 3,

    /// <summary>Non-membership in a finite set; refuses a null fact. Not allowed on <see cref="Kinds.FactType.Boolean"/>.</summary>
    NotIn = 4,

    /// <summary>Matches only an absent fact.</summary>
    IsNull = 5,

    /// <summary>Matches any present (non-null) fact.</summary>
    IsNotNull = 6,

    /// <summary>Bounded interval, inclusive flags per bound. Only Integer, Decimal, Money, Date (D-04).</summary>
    Range = 7,

    /// <summary>String prefix match. Only <see cref="Kinds.FactType.Code"/> (D-04).</summary>
    StartsWith = 8,
}

/// <summary>
/// A typed scalar value used as a condition operand or an evaluated fact
/// value — one case per <see cref="Kinds.FactType"/>. Immutable; two values
/// of the same case and payload are equal (record value equality; a decimal
/// is equal regardless of its stored scale, e.g. 1.50 == 1.5).
/// </summary>
public abstract record DecisionValue
{
    public static DecisionValue Code(string value) => new CodeValue(value);

    public static DecisionValue Enum(string value) => new EnumValue(value);

    public static DecisionValue Boolean(bool value) => new BooleanValue(value);

    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Names the FactType.Integer case it constructs (D-04); a value-factory method is expected to be named after what it builds.")]
    public static DecisionValue Integer(long value) => new IntegerValue(value);

    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Names the FactType.Decimal case it constructs (D-04); a value-factory method is expected to be named after what it builds.")]
    public static DecisionValue Decimal(decimal value) => new DecimalValue(value);

    public static DecisionValue Money(decimal amount, string currency) => new MoneyValue(amount, currency);

    public static DecisionValue Date(DateOnly value) => new DateValue(value);

    public static DecisionValue Reference(Guid value) => new ReferenceValue(value);
}

/// <summary>A <see cref="Kinds.FactType.Code"/> value.</summary>
public sealed record CodeValue(string Value) : DecisionValue;

/// <summary>A <see cref="Kinds.FactType.Enum"/> value.</summary>
public sealed record EnumValue(string Value) : DecisionValue;

/// <summary>A <see cref="Kinds.FactType.Boolean"/> value.</summary>
public sealed record BooleanValue(bool Value) : DecisionValue;

/// <summary>A <see cref="Kinds.FactType.Integer"/> value.</summary>
public sealed record IntegerValue(long Value) : DecisionValue;

/// <summary>A <see cref="Kinds.FactType.Decimal"/> value.</summary>
public sealed record DecimalValue(decimal Value) : DecisionValue;

/// <summary>A <see cref="Kinds.FactType.Money"/> value — amount plus its ISO 4217 currency code.</summary>
public sealed record MoneyValue(decimal Amount, string Currency) : DecisionValue;

/// <summary>A <see cref="Kinds.FactType.Date"/> value.</summary>
public sealed record DateValue(DateOnly Value) : DecisionValue;

/// <summary>A <see cref="Kinds.FactType.Reference"/> value.</summary>
public sealed record ReferenceValue(Guid Value) : DecisionValue;

/// <summary>
/// One condition of a <see cref="Row"/>: a fact name, an operator (D-04) and
/// the operand value(s) the operator needs. Constructing an instance never
/// validates the operator against a fact TYPE — that is
/// <c>Nuvora.Decisions.Engine.Conditions.OperatorMatrix</c> /
/// <c>ConditionMatcher</c>'s job, because only the engine (given a kind's
/// descriptor) knows each fact's type; this project carries no engine logic
/// (AT-DEC-01).
/// </summary>
public sealed record Condition
{
    public required string FactName { get; init; }

    public required Operator Operator { get; init; }

    /// <summary>Operand for Equals / NotEquals / StartsWith (as a <see cref="CodeValue"/> prefix) / the lower bound of Range.</summary>
    public DecisionValue? Value { get; init; }

    /// <summary>Upper bound of Range.</summary>
    public DecisionValue? UpperValue { get; init; }

    public bool LowerInclusive { get; init; } = true;

    public bool UpperInclusive { get; init; } = true;

    /// <summary>Operand set for In / NotIn.</summary>
    public IReadOnlyList<DecisionValue> Values { get; init; } = [];

    public static Condition Any(string factName) =>
        new() { FactName = factName, Operator = Operator.Any };

    public static Condition Equals(string factName, DecisionValue value) =>
        new() { FactName = factName, Operator = Operator.Equals, Value = value };

    public static Condition NotEquals(string factName, DecisionValue value) =>
        new() { FactName = factName, Operator = Operator.NotEquals, Value = value };

    public static Condition In(string factName, IReadOnlyList<DecisionValue> values) =>
        new() { FactName = factName, Operator = Operator.In, Values = values };

    public static Condition NotIn(string factName, IReadOnlyList<DecisionValue> values) =>
        new() { FactName = factName, Operator = Operator.NotIn, Values = values };

    public static Condition IsNull(string factName) =>
        new() { FactName = factName, Operator = Operator.IsNull };

    public static Condition IsNotNull(string factName) =>
        new() { FactName = factName, Operator = Operator.IsNotNull };

    public static Condition Range(
        string factName,
        DecisionValue? lower,
        DecisionValue? upper,
        bool lowerInclusive,
        bool upperInclusive) =>
        new()
        {
            FactName = factName,
            Operator = Operator.Range,
            Value = lower,
            UpperValue = upper,
            LowerInclusive = lowerInclusive,
            UpperInclusive = upperInclusive,
        };

    public static Condition StartsWith(string factName, string prefix) =>
        new() { FactName = factName, Operator = Operator.StartsWith, Value = new CodeValue(prefix) };
}
