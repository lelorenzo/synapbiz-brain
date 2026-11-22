using System.Diagnostics.CodeAnalysis;

namespace Nuvora.Decisions.Contracts.Evaluation;

/// <summary>
/// The result of evaluating a decision table against a fact set (D-04):
/// either a decided row (<see cref="Decided"/>) or a typed, non-exceptional
/// <see cref="NoDecision"/>. The evaluator never throws and never returns a
/// sentinel value for an unmatched fact set — <see cref="IsDecided"/> is the
/// only branch a caller needs (step P2-20.2.3).
/// </summary>
[SuppressMessage(
    "Design",
    "S1694:An abstract class should have both abstract and concrete methods",
    Justification = "A closed, pattern-matchable hierarchy (Decided | NoDecision) is the point (D-04's discriminated-union outcome); an interface would lose record value-equality and the exhaustive switch-expression ergonomics callers rely on.")]
public abstract record DecisionOutcome
{
    /// <summary>True for <see cref="Decided"/>, false for <see cref="NoDecision"/>.</summary>
    public abstract bool IsDecided { get; }
}

/// <summary>Rows matched. <paramref name="RowKey"/> names the first match, or the default row when used.</summary>
/// <param name="OutcomeJson">A canonical outcome object for UNIQUE/FIRST, or an ordered array of distinct outcome objects for COLLECT.</param>
/// <param name="RowKey">The matched row's stable key.</param>
public sealed record Decided(string OutcomeJson, string RowKey) : DecisionOutcome
{
    /// <summary>Every matched row key in stored row order; includes rows whose duplicate outcome was removed.</summary>
    public IReadOnlyList<string> MatchedRowKeys { get; init; } = [];

    /// <inheritdoc />
    public override bool IsDecided => true;
}

/// <summary>No row matched and the table has no default row.</summary>
/// <param name="Reason">Stable reason code (e.g. <c>no_row_matched</c>).</param>
public sealed record NoDecision(string Reason) : DecisionOutcome
{
    /// <inheritdoc />
    public override bool IsDecided => false;
}
