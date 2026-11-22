namespace Nuvora.Decisions.Contracts.Validation;

/// <summary>
/// Semantic validation of one row's outcome, supplied by the module that
/// owns a decision kind (D-12) — the engine knows outcome fields only as
/// canonical JSON and cannot check domain invariants (e.g. "class X requires
/// a non-negative NetDays") on its own.
/// </summary>
public interface IDecisionOutcomeValidator
{
    /// <summary>Returns zero or more issues found in <paramref name="outcomeJson"/> for row <paramref name="rowKey"/>; an empty list means the outcome is valid.</summary>
    IReadOnlyList<ValidationIssue> Validate(string rowKey, string outcomeJson);
}
