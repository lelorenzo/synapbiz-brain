using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Contracts.Validation;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Facts;
using Nuvora.Decisions.Engine.Goldens;
using Nuvora.Decisions.Engine.Validation;

namespace Synapbiz.Brain.Spec;

/// <summary>
/// A loaded <c>brain.rule@1</c> document: the decision table, its golden cases and the decision kind it was checked
/// against, ready to validate and evaluate with the engine. Instances are created by <see cref="BrainRuleLoader"/>.
/// </summary>
public sealed class BrainRule
{
    private readonly Lazy<CompiledTable> compiled;

    internal BrainRule(
        string id,
        long version,
        DecisionKindDescriptor kind,
        DecisionTable table,
        IReadOnlyList<GoldenCase> goldens,
        IReadOnlyDictionary<string, string> rowIdsByKey,
        JsonObject meta)
    {
        Id = id;
        Version = version;
        Kind = kind;
        Table = table;
        Goldens = goldens;
        RowIdsByKey = rowIdsByKey;
        Meta = meta;
        compiled = new Lazy<CompiledTable>(() => TableCompiler.Compile(Table, Kind), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The stable id of the rule document.</summary>
    public string Id { get; }

    /// <summary>The version of the rule document.</summary>
    public long Version { get; }

    /// <summary>The decision kind, from the module contract, that the rule was checked against.</summary>
    public DecisionKindDescriptor Kind { get; }

    /// <summary>The decision table; rows are keyed by their <c>legacyKey</c>.</summary>
    public DecisionTable Table { get; }

    /// <summary>The golden cases of the document, in document order.</summary>
    public IReadOnlyList<GoldenCase> Goldens { get; }

    /// <summary>The row id of every engine row key (the row's <c>legacyKey</c>), the default row included.</summary>
    public IReadOnlyDictionary<string, string> RowIdsByKey { get; }

    /// <summary>A deep clone of the <c>meta</c> object of the document.</summary>
    public JsonObject Meta { get; }

    /// <summary>The table compiled against <see cref="Kind"/>; compiled once and cached.</summary>
    public CompiledTable Compile() => compiled.Value;

    /// <summary>
    /// Evaluates the rule for the given facts. The trace of the result carries this rule's version in
    /// <see cref="DecisionTrace.VersionNumber"/>, unless the version does not fit an <see cref="int"/>.
    /// </summary>
    public DecisionResult Evaluate(
        IReadOnlyDictionary<string, DecisionValue> facts, DateOnly asOf, IComposedFactResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var (outcome, trace) = TableEvaluator.Evaluate(Compile(), FactSet.Build(Kind, facts), asOf, resolver);
        return new DecisionResult(outcome, trace with { VersionNumber = Version <= int.MaxValue ? (int)Version : null });
    }

    /// <summary>The engine's static validation of the table: overlap, coverage, reachability, cell types.</summary>
    public ValidationReport ValidateTable() => TableValidator.Validate(Table, Kind);

    /// <summary>
    /// Runs the golden cases of the document. Goldens carry their own dates as facts, so the as-of date that is
    /// forwarded to a composed-fact resolver is <see cref="DateOnly.MinValue"/>.
    /// </summary>
    public GoldenRunResult RunGoldens(IComposedFactResolver? resolver = null) =>
        GoldenRunner.Run(Compile(), Goldens, DateOnly.MinValue, resolver);
}

/// <summary>The outcome of loading a <c>brain.rule@1</c> document.</summary>
/// <param name="Rule">The loaded rule; null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">Every error of the first validation step that found any, ordered by path then code.</param>
/// <param name="DeferredToEngine">The checks this loader leaves to the engine: <c>overlap</c>, <c>coverage</c>, <c>goldens</c>, <c>outcome-policy</c>.</param>
public sealed record BrainRuleLoadResult(
    BrainRule? Rule,
    IReadOnlyList<BrainSpecError> Errors,
    IReadOnlyList<string> DeferredToEngine)
{
    /// <summary>True when the document loaded without errors.</summary>
    public bool IsValid => Errors.Count == 0;
}
