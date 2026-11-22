using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Conditions;

namespace Nuvora.Decisions.Engine.Compilation;

/// <summary>
/// Compiles a <see cref="DecisionTable"/> against its <see cref="DecisionKindDescriptor"/>
/// (D-04): resolves and validates every condition's operator against the
/// fact's declared type (<see cref="ConditionMatcher.Create"/>), and — for a
/// kind that declares composed facts (D-14) — walks the composition graph
/// over an optional kind registry, refusing a cycle or a chain deeper than
/// <see cref="MaxCompositionDepth"/> hops. A single <see cref="Compile"/>
/// call sees only descriptors, never a fact value: the cycle/depth check is
/// therefore a static, deterministic graph walk, not a function of any
/// particular evaluation.
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public static class TableCompiler
{
    /// <summary>Stable error code: the composition graph revisits a kind already on the current path.</summary>
    public const string ErrorCompositionCycle = "decisions.composition.cycle";

    /// <summary>Stable error code: the composition chain exceeds <see cref="MaxCompositionDepth"/> hops.</summary>
    public const string ErrorCompositionTooDeep = "decisions.composition.too_deep";

    /// <summary>Stable error code: a row condition names a fact the descriptor does not declare.</summary>
    public const string ErrorUnknownFact = "decisions.table.unknown_fact";

    /// <summary>D-14: a composition chain longer than this many hops is refused.</summary>
    public const int MaxCompositionDepth = 3;

    /// <summary>
    /// Compiles <paramref name="table"/> for <paramref name="descriptor"/>.
    /// <paramref name="kindRegistry"/>, when supplied, lets the composition
    /// graph check see past the immediate child: without it, only a direct
    /// self-composition (depth 1) can be detected.
    /// </summary>
    public static CompiledTable Compile(
        DecisionTable table,
        DecisionKindDescriptor descriptor,
        IReadOnlyDictionary<DecisionKindKey, DecisionKindDescriptor>? kindRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(descriptor);

        var factTypes = descriptor.Facts.ToDictionary(f => f.Name, f => f.Type, StringComparer.Ordinal);

        CompiledRow CompileRow(Row row) => new(
            row.Key,
            [.. row.Conditions.Select(c => (c.FactName, ConditionMatcher.Create(c, TypeOf(descriptor, factTypes, row.Key, c.FactName))))],
            row.Outcome);

        var rows = table.Rows.Select(CompileRow).ToList();
        var defaultRow = table.DefaultRow is null ? null : CompileRow(table.DefaultRow);

        var composedFacts = descriptor.Facts.Where(f => f.ComposedFromKind is not null).ToList();
        foreach (var composed in composedFacts)
        {
            ValidateCompositionGraph(descriptor.Key, composed.ComposedFromKind!.Value, kindRegistry, [descriptor.Key], depth: 1);
        }

        return new CompiledTable(descriptor, table.HitPolicy, rows, defaultRow, composedFacts);
    }

    private static FactType TypeOf(
        DecisionKindDescriptor descriptor, Dictionary<string, FactType> factTypes, string rowKey, string factName)
    {
        if (factTypes.TryGetValue(factName, out var type))
        {
            return type;
        }

        throw new DecisionEngineException(
            ErrorUnknownFact,
            $"Row '{rowKey}' of kind '{descriptor.Key}' conditions on fact '{factName}', which the kind does not declare.",
            new Dictionary<string, object?> { ["kind"] = descriptor.Key.Value, ["row"] = rowKey, ["fact"] = factName });
    }

    private static void ValidateCompositionGraph(
        DecisionKindKey root,
        DecisionKindKey child,
        IReadOnlyDictionary<DecisionKindKey, DecisionKindDescriptor>? registry,
        IReadOnlyList<DecisionKindKey> pathSoFar,
        int depth)
    {
        List<DecisionKindKey> pathIncludingChild = [.. pathSoFar, child];
        var pathText = string.Join(" -> ", pathIncludingChild.Select(k => k.Value));

        if (pathSoFar.Contains(child))
        {
            throw new DecisionEngineException(
                ErrorCompositionCycle,
                $"Decision kind '{root}' composes a cycle through '{child}' (path: {pathText}).",
                new Dictionary<string, object?>
                {
                    ["root"] = root.Value,
                    ["child"] = child.Value,
                    ["path"] = pathSoFar.Select(k => k.Value).ToList(),
                });
        }

        if (depth > MaxCompositionDepth)
        {
            throw new DecisionEngineException(
                ErrorCompositionTooDeep,
                $"Decision kind '{root}' composes to a depth greater than {MaxCompositionDepth} hops (path: {pathText}).",
                new Dictionary<string, object?> { ["root"] = root.Value, ["maxDepth"] = MaxCompositionDepth });
        }

        if (registry is null || !registry.TryGetValue(child, out var childDescriptor))
        {
            return;
        }

        IReadOnlyList<DecisionKindKey> nextPath = pathIncludingChild;
        foreach (var grandchild in childDescriptor.Facts.Where(f => f.ComposedFromKind is not null))
        {
            ValidateCompositionGraph(root, grandchild.ComposedFromKind!.Value, registry, nextPath, depth + 1);
        }
    }
}
