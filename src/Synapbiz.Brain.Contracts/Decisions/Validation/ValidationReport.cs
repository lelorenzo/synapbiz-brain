namespace Nuvora.Decisions.Contracts.Validation;

/// <summary>
/// One finding of <see cref="IDecisionOutcomeValidator"/> or the engine's
/// table validator (D-12): a stable code, a human message and the row keys
/// it names. <paramref name="Args"/> carries interpolation arguments for
/// client-side localization, mirroring <c>DomainException</c>'s contract.
/// </summary>
public sealed record ValidationIssue(
    string Code,
    string Message,
    IReadOnlyList<string> RowKeys,
    IReadOnlyDictionary<string, object?>? Args = null);

/// <summary>
/// The result of validating a decision table against its kind (D-12):
/// errors block activation; warnings do not. Stored on the version and
/// shown by the UI (D-12).
/// </summary>
public sealed record ValidationReport
{
    public required IReadOnlyList<ValidationIssue> Errors { get; init; }

    public IReadOnlyList<ValidationIssue> Warnings { get; init; } = [];

    /// <summary>True when <see cref="Errors"/> is empty.</summary>
    public bool IsValid => Errors.Count == 0;
}
