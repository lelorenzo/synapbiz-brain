using System.Diagnostics.CodeAnalysis;
using Synapbiz.Brain;

namespace Nuvora.Decisions.Contracts;

/// <summary>
/// Typed failure of the decision engine or ruleset lifecycle. Carries a
/// stable <c>decisions.*</c> error code (decisions D-04,
/// D-09, D-12 and the others name the codes) and the interpolation arguments a client
/// localizes the message with (DomainException's contract).
/// </summary>
/// <remarks>
/// Introduced in P2-20.2 (paquete 0.A369) as the single carrier for every
/// "decisions.*" error code (kind-key validation, fact-set size/shape,
/// operator matrix refusals, evaluation defects, validator/golden errors), so
/// a consumer catches one typed exception for all of them.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Domain exceptions are intentionally minimal: a single (code, message, args, innerException) constructor forces every throw site to supply a stable error code (DomainException's contract) — mirrors ValueObjectException and the Accounting module's documented CA1032 convention.")]
public sealed class DecisionEngineException : BrainException
{
    /// <summary>Initializes a new instance of <see cref="DecisionEngineException"/>.</summary>
    public DecisionEngineException(
        string errorCode,
        string message,
        IReadOnlyDictionary<string, object?>? args = null,
        Exception? innerException = null)
        : base(errorCode, message, args, innerException)
    {
    }
}
