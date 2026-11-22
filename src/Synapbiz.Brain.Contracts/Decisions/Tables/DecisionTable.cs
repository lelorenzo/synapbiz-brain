using System.Diagnostics.CodeAnalysis;

namespace Nuvora.Decisions.Contracts.Tables;

/// <summary>
/// DMN-style hit policy for a decision table (D-04). Explicit ordinals:
/// persisted and part of a ruleset version's content hash (P2-20.3) — never
/// renumber once shipped.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals are pinned at Unique=1/First=2 by design (D-04, DecisionKindKeyTests.HitPolicy_OrdinalsArePinned) and by project convention (mirrors Nuvora.Modules.Accounting/Devices' documented CA1008 suppression): default(HitPolicy) == 0 is deliberately not a valid hit policy.")]
public enum HitPolicy
{
    /// <summary>Exactly one row may match; more than one is a validated table's defect (decisions.evaluation.multiple_matches, asserted — never expected in production).</summary>
    Unique = 1,

    /// <summary>Row order is intentional: the first matching row wins (overlapping rows are expected, not an error).</summary>
    First = 2,

    /// <summary>Every matching row contributes, with canonical duplicate outcomes removed in row order.</summary>
    Collect = 3,
}

/// <summary>
/// One row of a <see cref="DecisionTable"/>: a stable key, its conditions
/// (one per constrained fact; a fact with no listed condition is implicitly
/// <see cref="Operator.Any"/>) and the outcome it yields when matched.
/// </summary>
/// <param name="Key">Stable row key, unique within the table — named in validation reports, golden results and the trace.</param>
/// <param name="Conditions">The row's conditions.</param>
/// <param name="Outcome">Canonical JSON object of the kind's outcome fields.</param>
public sealed record Row(string Key, IReadOnlyList<Condition> Conditions, string Outcome);

/// <summary>
/// A decision table (D-04): a hit policy, its rows and an optional default
/// row. A kind with <see cref="Kinds.DecisionKindDescriptor.RequiresTotalCoverage"/>
/// set must either declare a default row or have its enumerable (Enum/Boolean)
/// columns fully covered by <see cref="Rows"/> (TableValidator, step P2-20.2.4).
/// </summary>
public sealed record DecisionTable(HitPolicy HitPolicy, IReadOnlyList<Row> Rows, Row? DefaultRow = null);
