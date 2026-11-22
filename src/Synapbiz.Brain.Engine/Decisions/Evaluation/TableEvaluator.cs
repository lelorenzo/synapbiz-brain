using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Facts;
using Nuvora.Decisions.Engine.Serialization;

namespace Nuvora.Decisions.Engine.Evaluation;

/// <summary>
/// Evaluates a <see cref="CompiledTable"/> against a <see cref="FactSet"/>
/// (D-04, D-05, D-14): resolves any composed facts first, matches rows per
/// the table's <see cref="HitPolicy"/>, and returns a typed, total
/// <see cref="DecisionOutcome"/> together with its <see cref="DecisionTrace"/>.
/// Pure and deterministic: <c>asOf</c> arrives as a parameter, no
/// clock/IO/randomness is read (AT-DEC-02); the same inputs always produce a
/// byte-identical trace.
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public static class TableEvaluator
{
    /// <summary>Stable error code: more than one row matched under <see cref="HitPolicy.Unique"/> — a validated table never reaches this; it is a defect.</summary>
    public const string ErrorMultipleMatches = "decisions.evaluation.multiple_matches";

    /// <summary>Stable error code: a composed fact is declared but no resolver was supplied.</summary>
    public const string ErrorResolverRequired = "decisions.composition.resolver_required";

    /// <summary>The reason on a <see cref="NoDecision"/> outcome: no row matched and the table has no default row.</summary>
    public const string NoRowMatchedReason = "no_row_matched";

    /// <summary>
    /// Evaluates <paramref name="table"/> against <paramref name="facts"/> as
    /// of <paramref name="asOf"/>. <paramref name="resolver"/> is required
    /// only when the table declares at least one composed fact
    /// (<see cref="CompiledTable.ComposedFacts"/>); it is never consulted
    /// otherwise.
    /// </summary>
    public static (DecisionOutcome Outcome, DecisionTrace Trace) Evaluate(
        CompiledTable table,
        FactSet facts,
        DateOnly asOf,
        IComposedFactResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(facts);

        var workingFacts = facts;
        var children = new List<DecisionTrace>();

        foreach (var composed in table.ComposedFacts)
        {
            if (resolver is null)
            {
                throw new DecisionEngineException(
                    ErrorResolverRequired,
                    $"Kind '{table.Descriptor.Key}' declares fact '{composed.Name}' composed from "
                    + $"'{composed.ComposedFromKind}', but no {nameof(IComposedFactResolver)} was supplied.",
                    new Dictionary<string, object?>
                    {
                        ["kind"] = table.Descriptor.Key.Value,
                        ["fact"] = composed.Name,
                        ["childKind"] = composed.ComposedFromKind!.Value.Value,
                    });
            }

            var resolution = resolver.Resolve(composed.ComposedFromKind!.Value, asOf);
            workingFacts = workingFacts.With(composed.Name, resolution.Value);
            children.Add(resolution.ChildTrace);
        }

        var (outcome, matchedRowKey) = EvaluateRows(table, workingFacts.Values);

        var trace = new DecisionTrace
        {
            KindKey = table.Descriptor.Key,
            MatchedRowKey = matchedRowKey,
            MatchedRowKeys = outcome is Decided matched ? matched.MatchedRowKeys : [],
            IsDecided = outcome.IsDecided,
            OutcomeJson = outcome is Decided decided ? decided.OutcomeJson : null,
            FactsJson = workingFacts.ToCanonicalJson(),
            FactsHash = workingFacts.Hash().Value,
            Children = children,
        };

        return (outcome, trace);
    }

    private static (DecisionOutcome Outcome, string? MatchedRowKey) EvaluateRows(
        CompiledTable table, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        if (table.HitPolicy == HitPolicy.Collect)
        {
            return EvaluateCollectRows(table, facts);
        }

        CompiledRow? matched = table.HitPolicy switch
        {
            HitPolicy.Unique => MatchUnique(table, facts),
            HitPolicy.First => table.Rows.FirstOrDefault(r => r.Matches(facts)),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table.HitPolicy, "Unknown hit policy."),
        };

        matched ??= table.DefaultRow;

        return matched is null
            ? (new NoDecision(NoRowMatchedReason), null)
            : (new Decided(matched.Outcome, matched.Key) { MatchedRowKeys = [matched.Key] }, matched.Key);
    }

    private static (DecisionOutcome Outcome, string? MatchedRowKey) EvaluateCollectRows(
        CompiledTable table, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        var matches = table.Rows.Where(row => row.Matches(facts)).ToList();
        if (matches.Count == 0 && table.DefaultRow is not null)
        {
            matches.Add(table.DefaultRow);
        }
        if (matches.Count == 0)
        {
            return (new NoDecision(NoRowMatchedReason), null);
        }

        var outcomes = matches.Select(row => CanonicalOutcomeJson.Canonicalize(row.Outcome)).Distinct(StringComparer.Ordinal);
        var outcome = new Decided("[" + string.Join(",", outcomes) + "]", matches[0].Key)
        {
            MatchedRowKeys = matches.Select(row => row.Key).ToList(),
        };
        return (outcome, outcome.RowKey);
    }

    private static CompiledRow? MatchUnique(CompiledTable table, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        var matches = table.Rows.Where(r => r.Matches(facts)).ToList();

        if (matches.Count > 1)
        {
            throw new DecisionEngineException(
                ErrorMultipleMatches,
                $"Kind '{table.Descriptor.Key}' matched {matches.Count} rows under HitPolicy.Unique "
                + $"({string.Join(", ", matches.Select(m => m.Key))}); a validated table never reaches this (it is a defect).",
                new Dictionary<string, object?>
                {
                    ["kind"] = table.Descriptor.Key.Value,
                    ["rowKeys"] = matches.Select(m => m.Key).ToList(),
                });
        }

        return matches.Count == 1 ? matches[0] : null;
    }
}
