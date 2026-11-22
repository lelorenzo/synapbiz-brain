using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Parity;

/// <summary>A rule document as the engine reads it: its table, its golden cases and the kind it was checked against.</summary>
internal sealed record ProjectedRule(DecisionTable Table, IReadOnlyList<GoldenCase> Goldens, DecisionKindDescriptor Kind);

/// <summary>
/// Turns a <c>brain.rule@1</c> document and its contract files into an engine table. The Brain library implements it with
/// its loader; the source repository's side implements it with its validator, bridge and definition codec.
/// </summary>
internal interface IRuleProjector
{
    /// <summary>The projected rule, or null when the document has errors; the errors are always returned.</summary>
    ProjectedRule? Project(string documentJson, IReadOnlyList<string> contractJsons, out IReadOnlyList<(string Code, string Path)> errors);
}

/// <summary>Each assembly that compiles the shared scenarios supplies its own projector in a file of its own.</summary>
internal static partial class RuleProjectorFactory
{
    internal static partial IRuleProjector Create();
}
