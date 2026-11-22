using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Conditions;

namespace Nuvora.Decisions.Engine.Compilation;

/// <summary>
/// A row whose conditions are pre-parsed into <see cref="ConditionMatcher"/>s
/// (one per condition, resolved against the kind's fact types at compile
/// time — <see cref="TableCompiler"/> is the only place a type mismatch can
/// surface).
/// </summary>
public sealed class CompiledRow
{
    internal CompiledRow(string key, IReadOnlyList<(string FactName, ConditionMatcher Matcher)> matchers, string outcome)
    {
        Key = key;
        Matchers = matchers;
        Outcome = outcome;
    }

    /// <summary>The row's stable key (the source <see cref="Row.Key"/>).</summary>
    public string Key { get; }

    /// <summary>The row's conditions, each paired with the fact name it reads.</summary>
    public IReadOnlyList<(string FactName, ConditionMatcher Matcher)> Matchers { get; }

    /// <summary>The row's outcome (canonical JSON), unchanged from the source <see cref="Row"/>.</summary>
    public string Outcome { get; }

    /// <summary>Whether every one of <see cref="Matchers"/> is satisfied by <paramref name="facts"/> (a fact absent from the dictionary is treated as null/absent).</summary>
    public bool Matches(IReadOnlyDictionary<string, DecisionValue> facts)
    {
        foreach (var (factName, matcher) in Matchers)
        {
            facts.TryGetValue(factName, out var value);
            if (!matcher.Matches(value))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// A <see cref="DecisionTable"/> compiled against a <see cref="DecisionKindDescriptor"/>
/// (D-04/D-14): rows indexed with pre-parsed <see cref="ConditionMatcher"/>s,
/// plus the descriptor's composed-fact declarations the evaluator must
/// resolve before matching rows. Built only by <see cref="TableCompiler"/>.
/// </summary>
public sealed class CompiledTable
{
    internal CompiledTable(
        DecisionKindDescriptor descriptor,
        HitPolicy hitPolicy,
        IReadOnlyList<CompiledRow> rows,
        CompiledRow? defaultRow,
        IReadOnlyList<FactDescriptor> composedFacts)
    {
        Descriptor = descriptor;
        HitPolicy = hitPolicy;
        Rows = rows;
        DefaultRow = defaultRow;
        ComposedFacts = composedFacts;
    }

    /// <summary>The kind this table was compiled for.</summary>
    public DecisionKindDescriptor Descriptor { get; }

    public HitPolicy HitPolicy { get; }

    /// <summary>Rows in their original (significant for <see cref="HitPolicy.First"/>) order.</summary>
    public IReadOnlyList<CompiledRow> Rows { get; }

    public CompiledRow? DefaultRow { get; }

    /// <summary>The descriptor's facts declared with <see cref="FactDescriptor.ComposedFromKind"/> (D-14).</summary>
    public IReadOnlyList<FactDescriptor> ComposedFacts { get; }
}
