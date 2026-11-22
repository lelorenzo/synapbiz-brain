namespace Nuvora.Decisions.Contracts.Evaluation;

/// <summary>
/// What the runtime evaluator returns (P2-20.4, D-05): the typed <see cref="Outcome"/> and the
/// <see cref="Trace"/> that explains it. The caller persists the trace in its own unit of work.
/// </summary>
/// <param name="Outcome">A decided row, or a typed <see cref="NoDecision"/> (never an exception for "nothing matched").</param>
/// <param name="Trace">The evaluation trace: version, matched row, canonical facts and their hash.</param>
public sealed record DecisionResult(DecisionOutcome Outcome, DecisionTrace Trace);
