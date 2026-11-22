using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.Decisions.Contracts.Evaluation;

/// <summary>
/// Evaluates the Active ruleset of a decision kind for the current tenant (P2-20.4, D-05). Only OnDemand
/// kinds are evaluated by a back-office command or consumer; nothing in a shopper request path may depend
/// on this interface (AT-DEC-05).
/// </summary>
public interface IDecisionEvaluator
{
    /// <summary>
    /// Selects the version whose effective window contains <paramref name="asOf"/> and evaluates its table
    /// against <paramref name="facts"/>. The evaluator never reads a clock: the caller passes <paramref name="asOf"/>.
    /// With no such version the result is <c>NoDecision("no_active_version")</c> and a trace without a version.
    /// </summary>
    /// <param name="kind">The registered decision kind.</param>
    /// <param name="facts">The caller's fact values, validated against the kind's descriptor.</param>
    /// <param name="asOf">The instant the decision applies to.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<DecisionResult> EvaluateAsync(
        DecisionKindKey kind,
        IReadOnlyDictionary<string, DecisionValue> facts,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}
