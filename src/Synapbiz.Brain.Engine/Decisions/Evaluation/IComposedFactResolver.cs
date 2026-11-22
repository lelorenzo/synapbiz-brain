using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;

namespace Nuvora.Decisions.Engine.Evaluation;

/// <summary>
/// Resolves one composed fact (D-14) by evaluating the composed-from kind
/// first, with the same <c>asOf</c> date and the facts it declares, and
/// returning the outcome field bound to the composing fact together with
/// the child's own trace, which <see cref="TableEvaluator"/> nests under
/// <see cref="DecisionTrace.Children"/>. Gathering the facts the child kind
/// needs is this implementation's responsibility — the engine passes none,
/// since the child's fact schema is generally not a subset of the composing
/// kind's own facts; the caller that wires up a resolver is the one who
/// knows where the child's facts come from. <see cref="TableCompiler"/>
/// already proved the composition graph is acyclic and no deeper than three
/// hops (<see cref="TableCompiler.MaxCompositionDepth"/>), so an
/// implementation never needs to re-check depth itself.
/// </summary>
public interface IComposedFactResolver
{
    /// <summary>Resolves the value a composed fact takes from evaluating <paramref name="childKind"/> as of <paramref name="asOf"/>.</summary>
    ComposedFactResolution Resolve(DecisionKindKey childKind, DateOnly asOf);
}

/// <summary>The value a composed fact takes, and the child evaluation's trace.</summary>
public sealed record ComposedFactResolution(DecisionValue Value, DecisionTrace ChildTrace);
