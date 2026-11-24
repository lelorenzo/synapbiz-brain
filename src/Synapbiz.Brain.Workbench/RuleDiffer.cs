using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench;

/// <summary>How a row or test case differs between two revisions of a rule.</summary>
public enum ChangeKind
{
    /// <summary>The same in both revisions.</summary>
    Unchanged = 0,

    /// <summary>Only in the later revision.</summary>
    Added = 1,

    /// <summary>Only in the earlier revision.</summary>
    Removed = 2,

    /// <summary>In both revisions, with different content.</summary>
    Changed = 3,
}

/// <summary>The change of one row between two revisions.</summary>
/// <param name="Key">The engine row key.</param>
/// <param name="Kind">How the row changed.</param>
/// <param name="IndexBefore">Position in the earlier revision; null when the row is added or is the default row.</param>
/// <param name="IndexAfter">Position in the later revision; null when the row is removed or is the default row.</param>
/// <param name="ConditionsChanged">Whether the conditions differ; false for an added or removed row.</param>
/// <param name="OutcomeChanged">Whether the outcome differs; false for an added or removed row.</param>
public sealed record RowChange(string Key, ChangeKind Kind, int? IndexBefore, int? IndexAfter, bool ConditionsChanged, bool OutcomeChanged);

/// <summary>The change of one golden case between two revisions.</summary>
/// <param name="Name">The case name.</param>
/// <param name="Kind">How the case changed.</param>
public sealed record CaseChange(string Name, ChangeKind Kind);

/// <summary>What differs between two revisions of a rule.</summary>
/// <param name="VersionBefore">The earlier revision's version.</param>
/// <param name="VersionAfter">The later revision's version.</param>
/// <param name="KindChanged">Whether the decision kind differs.</param>
/// <param name="HitPolicyChanged">Whether the hit policy differs.</param>
/// <param name="RowOrderChanged">Whether the rows present in both revisions are in a different order.</param>
/// <param name="Rows">The rows of the later revision in order, then the rows only the earlier revision had.</param>
/// <param name="DefaultRow">The default row's change; null when neither revision has one.</param>
/// <param name="Cases">The golden cases of the later revision in order, then the cases only the earlier revision had.</param>
/// <param name="ClaimsChanged">Whether what the rule says about its sources (<c>meta.sources</c>) or its authority (<c>meta.authority</c>) differs.</param>
public sealed record RuleDiff(
    long VersionBefore,
    long VersionAfter,
    bool KindChanged,
    bool HitPolicyChanged,
    bool RowOrderChanged,
    IReadOnlyList<RowChange> Rows,
    RowChange? DefaultRow,
    IReadOnlyList<CaseChange> Cases,
    bool ClaimsChanged = false)
{
    /// <summary>True when anything but the version differs.</summary>
    public bool HasChanges =>
        KindChanged
        || HitPolicyChanged
        || RowOrderChanged
        || ClaimsChanged
        || Rows.Any(row => row.Kind != ChangeKind.Unchanged)
        || (DefaultRow is not null && DefaultRow.Kind != ChangeKind.Unchanged)
        || Cases.Any(testCase => testCase.Kind != ChangeKind.Unchanged);
}

/// <summary>An example fact set to run on both revisions.</summary>
/// <param name="Name">The sample name.</param>
/// <param name="Facts">The fact values by fact name.</param>
public sealed record BehaviourSample(string Name, IReadOnlyDictionary<string, DecisionValue> Facts);

/// <summary>What both revisions decide for one sample.</summary>
/// <param name="Name">The sample name.</param>
/// <param name="Before">The simulation on the earlier revision.</param>
/// <param name="After">The simulation on the later revision.</param>
/// <param name="Differs">Whether the two revisions decide differently.</param>
public sealed record BehaviourChange(string Name, SimulationResult Before, SimulationResult After, bool Differs);

/// <summary>Compares two revisions of a rule: structure, test cases and behaviour on example facts.</summary>
public static class RuleDiffer
{
    /// <summary>
    /// Compares the structure of two revisions. Rows and golden cases are paired by row key and by case name (the first case
    /// of a name counts). A version difference alone is not a change.
    /// </summary>
    /// <param name="before">The earlier revision.</param>
    /// <param name="after">The later revision.</param>
    public static RuleDiff Diff(BrainRule before, BrainRule after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var rowsBefore = IndexOf(before.Table.Rows);
        var rowsAfter = IndexOf(after.Table.Rows);
        var rows = after.Table.Rows.Select((row, index) => rowsBefore.TryGetValue(row.Key, out var earlier)
                ? Paired(row.Key, earlier.Row, row, earlier.Index, index)
                : new RowChange(row.Key, ChangeKind.Added, null, index, false, false))
            .Concat(before.Table.Rows.Select((row, index) => (row, index)).Where(entry => !rowsAfter.ContainsKey(entry.row.Key))
                .Select(entry => new RowChange(entry.row.Key, ChangeKind.Removed, entry.index, null, false, false)))
            .ToList();

        var sharedBefore = before.Table.Rows.Select(row => row.Key).Where(rowsAfter.ContainsKey);
        var sharedAfter = after.Table.Rows.Select(row => row.Key).Where(rowsBefore.ContainsKey);

        return new RuleDiff(
            before.Version,
            after.Version,
            !string.Equals(before.Kind.Key.Value, after.Kind.Key.Value, StringComparison.Ordinal),
            before.Table.HitPolicy != after.Table.HitPolicy,
            !sharedBefore.SequenceEqual(sharedAfter, StringComparer.Ordinal),
            rows,
            DefaultChange(before.Table.DefaultRow, after.Table.DefaultRow),
            CaseChanges(before.Goldens, after.Goldens),
            RuleClaims.Differ(before.Meta, after.Meta));
    }

    /// <summary>
    /// The example fact sets to compare two revisions on: the golden cases of the later revision in order, then those of the
    /// earlier revision whose name the later one lacks. The first case of a name counts.
    /// </summary>
    /// <param name="before">The earlier revision.</param>
    /// <param name="after">The later revision.</param>
    public static IReadOnlyList<BehaviourSample> SamplesFromGoldens(BrainRule before, BrainRule after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return [.. after.Goldens.Concat(before.Goldens)
            .DistinctBy(golden => golden.Name, StringComparer.Ordinal)
            .Select(golden => new BehaviourSample(golden.Name, golden.Facts))];
    }

    /// <summary>
    /// Simulates every sample on both revisions with <see cref="RuleSimulator"/> and flags the samples the revisions decide
    /// differently: one refused and the other not, different refusal codes, a decision against no decision, or a different
    /// outcome or matched rows.
    /// </summary>
    /// <param name="before">The earlier revision.</param>
    /// <param name="after">The later revision.</param>
    /// <param name="samples">The example fact sets.</param>
    /// <param name="asOf">The date the decisions apply to.</param>
    public static IReadOnlyList<BehaviourChange> CompareBehaviour(
        BrainRule before, BrainRule after, IReadOnlyList<BehaviourSample> samples, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(samples);

        return [.. samples.Select(sample =>
        {
            var earlier = RuleSimulator.Simulate(before, sample.Facts, asOf);
            var later = RuleSimulator.Simulate(after, sample.Facts, asOf);
            return new BehaviourChange(sample.Name, earlier, later, Differ(earlier, later));
        })];
    }

    private static bool Differ(SimulationResult before, SimulationResult after)
    {
        if (!before.Succeeded || !after.Succeeded)
        {
            return before.Succeeded != after.Succeeded
                || !string.Equals(before.Failure!.Code, after.Failure!.Code, StringComparison.Ordinal);
        }

        return (before.Outcome, after.Outcome) switch
        {
            (Decided earlier, Decided later) =>
                !string.Equals(earlier.OutcomeJson, later.OutcomeJson, StringComparison.Ordinal)
                || !earlier.MatchedRowKeys.SequenceEqual(later.MatchedRowKeys, StringComparer.Ordinal),
            (Decided, _) or (_, Decided) => true,
            _ => false,
        };
    }

    private static Dictionary<string, (Row Row, int Index)> IndexOf(IReadOnlyList<Row> rows)
    {
        var index = new Dictionary<string, (Row, int)>(StringComparer.Ordinal);
        for (var position = 0; position < rows.Count; position++)
        {
            index.TryAdd(rows[position].Key, (rows[position], position));
        }

        return index;
    }

    private static RowChange Paired(string key, Row before, Row after, int? indexBefore, int? indexAfter)
    {
        var conditions = !SameConditions(before.Conditions, after.Conditions);
        var outcome = !string.Equals(before.Outcome, after.Outcome, StringComparison.Ordinal);
        return new RowChange(key, conditions || outcome ? ChangeKind.Changed : ChangeKind.Unchanged, indexBefore, indexAfter, conditions, outcome);
    }

    private static RowChange? DefaultChange(Row? before, Row? after)
    {
        if (before is null && after is null)
        {
            return null;
        }

        if (before is null)
        {
            return new RowChange(after!.Key, ChangeKind.Added, null, null, false, false);
        }

        if (after is null)
        {
            return new RowChange(before.Key, ChangeKind.Removed, null, null, false, false);
        }

        var paired = Paired(after.Key, before, after, null, null);
        return paired.Kind == ChangeKind.Unchanged && !string.Equals(before.Key, after.Key, StringComparison.Ordinal)
            ? paired with { Kind = ChangeKind.Changed }
            : paired;
    }

    private static bool SameConditions(IReadOnlyList<Condition> before, IReadOnlyList<Condition> after) =>
        before.Count == after.Count && before.Zip(after).All(pair => SameCondition(pair.First, pair.Second));

    private static bool SameCondition(Condition before, Condition after) =>
        string.Equals(before.FactName, after.FactName, StringComparison.Ordinal)
        && before.Operator == after.Operator
        && Equals(before.Value, after.Value)
        && Equals(before.UpperValue, after.UpperValue)
        && before.LowerInclusive == after.LowerInclusive
        && before.UpperInclusive == after.UpperInclusive
        && before.Values.SequenceEqual(after.Values);

    private static List<CaseChange> CaseChanges(IReadOnlyList<GoldenCase> before, IReadOnlyList<GoldenCase> after)
    {
        var earlier = FirstByName(before);
        var later = FirstByName(after);
        var changes = later.Select(entry => new CaseChange(entry.Key, CaseKind(earlier, entry.Key, entry.Value))).ToList();
        changes.AddRange(earlier.Where(entry => !later.ContainsKey(entry.Key)).Select(entry => new CaseChange(entry.Key, ChangeKind.Removed)));
        return changes;
    }

    private static ChangeKind CaseKind(Dictionary<string, GoldenCase> earlier, string name, GoldenCase later)
    {
        if (!earlier.TryGetValue(name, out var match))
        {
            return ChangeKind.Added;
        }

        return SameCase(match, later) ? ChangeKind.Unchanged : ChangeKind.Changed;
    }

    private static Dictionary<string, GoldenCase> FirstByName(IReadOnlyList<GoldenCase> cases)
    {
        var byName = new Dictionary<string, GoldenCase>(StringComparer.Ordinal);
        foreach (var golden in cases)
        {
            byName.TryAdd(golden.Name, golden);
        }

        return byName;
    }

    private static bool SameCase(GoldenCase before, GoldenCase after) =>
        before.Facts.Count == after.Facts.Count
        && before.Facts.All(fact => after.Facts.TryGetValue(fact.Key, out var value) && Equals(fact.Value, value))
        && SameExpectation(before.Expected, after.Expected);

    private static bool SameExpectation(GoldenExpectation before, GoldenExpectation after) => (before, after) switch
    {
        (ExpectedNoDecision, ExpectedNoDecision) => true,
        (ExpectedDecided earlier, ExpectedDecided later) =>
            string.Equals(earlier.OutcomeJson, later.OutcomeJson, StringComparison.Ordinal)
            && string.Equals(earlier.RowKey, later.RowKey, StringComparison.Ordinal),
        _ => false,
    };
}
