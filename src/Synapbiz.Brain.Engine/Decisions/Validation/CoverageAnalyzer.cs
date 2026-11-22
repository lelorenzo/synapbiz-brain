using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Contracts.Validation;
using Nuvora.Decisions.Engine.Conditions;

namespace Nuvora.Decisions.Engine.Validation;

/// <summary>
/// Completeness over a table's enumerable (Enum/Boolean) columns (D-12):
/// enumerates the cartesian product of those columns' finite domains
/// (treating every other column as <see cref="Operator.Any"/>) and checks
/// whether every combination is covered by a row or the default row.
/// Skips the exhaustive walk — reporting nothing — once the product exceeds
/// <see cref="MaxEnumerableProduct"/>, since the design bounds exhaustive
/// checking to a tractable domain size rather than attempting it unsoundly.
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public static class CoverageAnalyzer
{
    /// <summary>D-12: the enumerable domain product above which the exhaustive walk is skipped.</summary>
    public const int MaxEnumerableProduct = 10_000;

    /// <summary>
    /// Finds "incomplete" issues (D-12): an uncovered combination of the
    /// enumerable columns with no default row, or <see cref="DecisionKindDescriptor.RequiresTotalCoverage"/>
    /// set with no default row at all (a materialized kind cannot rely on
    /// enumerable-column coverage alone — a non-enumerable column may still
    /// leave a gap this walk cannot see).
    /// </summary>
    public static IReadOnlyList<ValidationIssue> FindIncompleteness(DecisionTable table, DecisionKindDescriptor descriptor)
    {
        if (table.DefaultRow is not null)
        {
            return [];
        }

        var issues = new List<ValidationIssue>();

        if (descriptor.RequiresTotalCoverage)
        {
            issues.Add(new ValidationIssue(
                TableValidator.CodeIncomplete,
                $"Kind '{descriptor.Key}' requires total coverage and has no default row.",
                []));
        }

        var enumerableFacts = EnumerableFactsOf(descriptor);
        if (enumerableFacts.Count == 0)
        {
            return issues;
        }

        var combinations = EnumerateCombinations(enumerableFacts);
        if (combinations.Count > MaxEnumerableProduct)
        {
            return issues;
        }

        var enumerableTypes = enumerableFacts.ToDictionary(f => f.Name, f => f.Type, StringComparer.Ordinal);
        var uncovered = combinations.Where(c => !table.Rows.Any(r => RowCoversCombination(r, c, enumerableTypes))).ToList();

        if (uncovered.Count > 0)
        {
            issues.Add(new ValidationIssue(
                TableValidator.CodeIncomplete,
                $"Kind '{descriptor.Key}' leaves {uncovered.Count} combination(s) of "
                + $"{string.Join(", ", enumerableFacts.Select(f => f.Name))} uncovered and has no default row: "
                + $"{DescribeCombinations(uncovered)}.",
                [],
                new Dictionary<string, object?> { ["uncovered"] = uncovered.Select(DescribeCombination).ToList() }));
        }

        return issues;
    }

    /// <summary>
    /// Row keys that never win (are never the FIRST match, nor the default)
    /// for any combination of the enumerable columns, when that product is
    /// small enough to enumerate exhaustively; an empty list when there are
    /// no enumerable columns or the product exceeds <see cref="MaxEnumerableProduct"/>
    /// (silence here is "not proven unreachable", never "proven reachable").
    /// </summary>
    public static IReadOnlyList<string> FindUnreachableRowsByEnumeration(DecisionTable table, DecisionKindDescriptor descriptor)
    {
        var enumerableFacts = EnumerableFactsOf(descriptor);
        if (enumerableFacts.Count == 0)
        {
            return [];
        }

        var enumerableNames = enumerableFacts.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var rowsToCheck = table.DefaultRow is null ? table.Rows : [.. table.Rows, table.DefaultRow];
        if (rowsToCheck.Any(r => HasNonTrivialNonEnumerableCondition(r, enumerableNames)))
        {
            // This walk only varies the enumerable columns, holding every other column
            // unconditioned; that makes it sound ONLY when no row actually conditions on
            // a non-enumerable column (Integer/Decimal/Money/Date). Once one does, a row
            // can be the real winner for a value of that column this walk never visits
            // (e.g. "Web AND amount >= 100" vs. an earlier "Web AND amount < 100": both
            // "cover" the single {channel=Web} combination once amount is ignored, so the
            // later row would be wrongly called unreachable even though Web/500 decides
            // it). OverlapAnalyzer.IsSubsumedByEarlierRow already decides subsumption over
            // non-enumerable columns by per-column interval containment and is sound for
            // that case, so this walk stays silent here rather than risk a false positive
            // ("not proven unreachable", never "proven reachable" - the type's own contract).
            return [];
        }

        var combinations = EnumerateCombinations(enumerableFacts);
        if (combinations.Count > MaxEnumerableProduct)
        {
            return [];
        }

        var enumerableTypes = enumerableFacts.ToDictionary(f => f.Name, f => f.Type, StringComparer.Ordinal);
        var winners = new HashSet<string>(StringComparer.Ordinal);

        foreach (var combination in combinations)
        {
            var winner = table.Rows.FirstOrDefault(r => RowCoversCombination(r, combination, enumerableTypes));
            if (winner is not null)
            {
                winners.Add(winner.Key);
            }
            else if (table.DefaultRow is not null)
            {
                winners.Add(table.DefaultRow.Key);
            }
        }

        return [.. table.Rows.Select(r => r.Key).Where(k => !winners.Contains(k))];
    }

    private static List<FactDescriptor> EnumerableFactsOf(DecisionKindDescriptor descriptor) =>
        [.. descriptor.Facts.Where(f => f.Type is FactType.Enum or FactType.Boolean)];

    /// <summary>True when <paramref name="row"/> conditions a column outside <paramref name="enumerableNames"/> with anything other than the implicit <see cref="Operator.Any"/>.</summary>
    private static bool HasNonTrivialNonEnumerableCondition(Row row, HashSet<string> enumerableNames) =>
        row.Conditions.Any(c => !enumerableNames.Contains(c.FactName) && c.Operator != Operator.Any);

    private static bool RowCoversCombination(
        Row row, Dictionary<string, DecisionValue> combination, Dictionary<string, FactType> enumerableTypes)
    {
        foreach (var condition in row.Conditions)
        {
            if (!enumerableTypes.TryGetValue(condition.FactName, out var type))
            {
                continue; // non-enumerable column: not part of this coverage walk.
            }

            if (!ConditionMatcher.Create(condition, type).Matches(combination[condition.FactName]))
            {
                return false;
            }
        }

        return true;
    }

    private static List<Dictionary<string, DecisionValue>> EnumerateCombinations(IReadOnlyList<FactDescriptor> facts)
    {
        List<Dictionary<string, DecisionValue>> result = [[]];

        foreach (var fact in facts)
        {
            var domain = fact.Type == FactType.Boolean
                ? [DecisionValue.Boolean(true), DecisionValue.Boolean(false)]
                : (fact.AllowedValues ?? []).Select(DecisionValue.Enum).ToList();

            var next = new List<Dictionary<string, DecisionValue>>();
            foreach (var partial in result)
            {
                foreach (var value in domain)
                {
                    var copy = new Dictionary<string, DecisionValue>(partial, StringComparer.Ordinal) { [fact.Name] = value };
                    next.Add(copy);
                }
            }

            result = next;
        }

        return result;
    }

    private static string DescribeCombination(IReadOnlyDictionary<string, DecisionValue> combo) => string.Join(
        ", ", combo.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={DescribeValue(kv.Value)}"));

    private static string DescribeValue(DecisionValue value) => value switch
    {
        EnumValue v => v.Value,
        BooleanValue v => v.Value ? "true" : "false",
        _ => value.ToString() ?? string.Empty,
    };

    private static string DescribeCombinations(IEnumerable<IReadOnlyDictionary<string, DecisionValue>> combos) =>
        string.Join("; ", combos.Select(DescribeCombination));
}
