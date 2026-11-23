using System.Globalization;
using Nuvora.Decisions.Contracts.Evaluation;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench;

/// <summary>How a row differs between a base revision and a draft.</summary>
public enum RowChangeTag
{
    /// <summary>Only in the draft.</summary>
    Added = 0,

    /// <summary>Only in the base.</summary>
    Removed = 1,

    /// <summary>In both, with other conditions or another outcome (it may also have moved).</summary>
    Changed = 2,

    /// <summary>In both, with the same content, in another place among the rows both have.</summary>
    Moved = 3,
}

/// <summary>One row that differs between two revisions.</summary>
/// <param name="Key">The row key.</param>
/// <param name="Label">The row's title from the labels, or its humanised key.</param>
/// <param name="Tag">How the row differs.</param>
/// <param name="NumberBefore">The row's 1-based position in the base; null when it was added.</param>
/// <param name="NumberAfter">The row's 1-based position in the draft; null when it was removed.</param>
/// <param name="SentenceBefore">The row as a sentence in the base; null when it was added.</param>
/// <param name="SentenceAfter">The row as a sentence in the draft; null when it was removed.</param>
/// <param name="NameBefore">The row's name from the labels of the base; null when the row has none there (or did not exist). Set for every row, so a name that is set, changed or cleared reads off the two names.</param>
/// <param name="NameAfter">The row's name from the labels of the draft; null when the row has none there (or was removed).</param>
public sealed record RowChangeView(
    string Key,
    string Label,
    RowChangeTag Tag,
    int? NumberBefore,
    int? NumberAfter,
    string? SentenceBefore,
    string? SentenceAfter,
    string? NameBefore,
    string? NameAfter);

/// <summary>A sample the two revisions decide differently.</summary>
/// <param name="Name">The sample name.</param>
/// <param name="Before">The outcome in the base, written as a row's outcome reads in a sentence; <c>no result</c> when it decided nothing or refused the sample.</param>
/// <param name="After">The outcome in the draft, in the same words.</param>
/// <param name="RowKeyBefore">The key of the row that decided in the base; null when none did (default row, no match, refused sample).</param>
/// <param name="RowNumberBefore">The 1-based position of that row; null with <paramref name="RowKeyBefore"/>.</param>
/// <param name="RowKeyAfter">The key of the row that decided in the draft; null when none did.</param>
/// <param name="RowNumberAfter">The 1-based position of that row; null with <paramref name="RowKeyAfter"/>.</param>
public sealed record CaseOutcomeChange(
    string Name,
    string Before,
    string After,
    string? RowKeyBefore,
    int? RowNumberBefore,
    string? RowKeyAfter,
    int? RowNumberAfter);

/// <summary>What a draft changes in a rule, in words.</summary>
/// <param name="Summary">One or two sentences: how many rows change, are added, are removed or move.</param>
/// <param name="Rows">The rows that differ, in the draft's order with removed rows where they were. A row whose only change is its name is listed as <see cref="RowChangeTag.Changed"/>, with the same sentence on both sides.</param>
/// <param name="HitPolicyChanged">Whether the hit policy differs.</param>
/// <param name="DefaultRowChanged">Whether the default row was added, removed or changed.</param>
/// <param name="CasesThatChange">The samples whose outcome differs between the two revisions.</param>
public sealed record RuleChangeView(
    string Summary,
    IReadOnlyList<RowChangeView> Rows,
    bool HitPolicyChanged,
    bool DefaultRowChanged,
    IReadOnlyList<CaseOutcomeChange> CasesThatChange);

/// <summary>Turns the difference between a rule and its draft into sentences a person reads.</summary>
public static class RuleChangeDescriber
{
    /// <summary>
    /// Describes what <paramref name="after"/> changes in <paramref name="before"/>. It is built on <see cref="RuleDiffer"/> and
    /// <see cref="RuleDescriber"/>. Every sample is simulated with the as-of date <see cref="DateOnly.MinValue"/>, as the golden
    /// cases of a rule are. The summary counts the default row like any other row. A row present on both sides whose name (its
    /// label in <paramref name="labelsBefore"/> and <paramref name="labelsAfter"/>) differs, and that <see cref="RuleDiffer"/> does
    /// not report, is added to the rows as changed, so the rows, the summary and every count made from them agree.
    /// </summary>
    /// <param name="before">The base revision.</param>
    /// <param name="after">The draft.</param>
    /// <param name="labelsBefore">The labels of the base.</param>
    /// <param name="labelsAfter">The labels of the draft.</param>
    /// <param name="locale">The language of the sentences.</param>
    /// <param name="extraSamples">Samples to compare besides the test cases of both revisions; none when null.</param>
    public static RuleChangeView Describe(
        BrainRule before,
        BrainRule after,
        RuleLabels labelsBefore,
        RuleLabels labelsAfter,
        WorkbenchLocale locale,
        IReadOnlyList<BehaviourSample>? extraSamples = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(labelsBefore);
        ArgumentNullException.ThrowIfNull(labelsAfter);

        var diff = RuleDiffer.Diff(before, after);
        var viewBefore = RuleDescriber.Describe(before, locale, labelsBefore);
        var viewAfter = RuleDescriber.Describe(after, locale, labelsAfter);
        var rows = RowChanges(diff, viewBefore, viewAfter);
        var defaultChange = diff.DefaultRow is { Kind: not ChangeKind.Unchanged } changed ? changed.Kind : (ChangeKind?)null;

        var samples = RuleDiffer.SamplesFromGoldens(before, after).Concat(extraSamples ?? []).ToList();
        var cases = RuleDiffer.CompareBehaviour(before, after, samples, DateOnly.MinValue)
            .Where(change => change.Differs)
            .Select(change => CaseChange(change, viewBefore, viewAfter, locale))
            .Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal))
            .ToList();

        return new RuleChangeView(
            Summary(rows, defaultChange, diff.HitPolicyChanged, locale),
            rows,
            diff.HitPolicyChanged,
            defaultChange is not null,
            cases);
    }

    // ---- rows --------------------------------------------------------------------------------

    private static List<RowChangeView> RowChanges(RuleDiff diff, RuleView before, RuleView after)
    {
        var moved = MovedKeys(diff);
        var viewsBefore = ByKey(before);
        var viewsAfter = ByKey(after);
        var removedAfter = RemovedByAnchor(diff, before);
        var result = new List<RowChangeView>(removedAfter.GetValueOrDefault(string.Empty) ?? []);

        foreach (var change in diff.Rows.Where(change => change.Kind != ChangeKind.Removed))
        {
            if ((TagOf(change, moved) ?? RenamedOnly(change, viewsBefore, viewsAfter)) is { } tag)
            {
                result.Add(View(change, tag, viewsBefore, viewsAfter));
            }

            result.AddRange(removedAfter.GetValueOrDefault(change.Key) ?? []);
        }

        return result;
    }

    private static RowChangeTag? TagOf(RowChange change, HashSet<string> moved) => change.Kind switch
    {
        ChangeKind.Added => RowChangeTag.Added,
        ChangeKind.Changed => RowChangeTag.Changed,
        _ => moved.Contains(change.Key) ? RowChangeTag.Moved : null,
    };

    /// <summary>Changed, for a row both revisions have, in the same content and place, whose name differs; otherwise null.</summary>
    private static RowChangeTag? RenamedOnly(RowChange change, Dictionary<string, RowView> before, Dictionary<string, RowView> after) =>
        change.Kind == ChangeKind.Unchanged
        && before.TryGetValue(change.Key, out var earlier)
        && after.TryGetValue(change.Key, out var later)
        && !string.Equals(NameOf(earlier), NameOf(later), StringComparison.Ordinal)
            ? RowChangeTag.Changed
            : null;

    /// <summary>The name of a row from the labels; null when the row has none or the name is only white space.</summary>
    private static string? NameOf(RowView? row) => string.IsNullOrWhiteSpace(row?.Title) ? null : row!.Title;

    private static RowChangeView View(RowChange change, RowChangeTag tag, Dictionary<string, RowView> before, Dictionary<string, RowView> after)
    {
        before.TryGetValue(change.Key, out var earlier);
        after.TryGetValue(change.Key, out var later);
        var title = (later ?? earlier)?.Title ?? WorkbenchText.Humanize(change.Key);
        return new RowChangeView(
            change.Key,
            title,
            tag,
            change.IndexBefore + 1,
            change.IndexAfter + 1,
            earlier?.Sentence,
            later?.Sentence,
            NameOf(earlier),
            NameOf(later));
    }

    /// <summary>The removed rows, by the key of the row they followed in the base (among the rows the draft keeps); the empty key is the start.</summary>
    private static Dictionary<string, List<RowChangeView>> RemovedByAnchor(RuleDiff diff, RuleView before)
    {
        var kept = diff.Rows.Where(change => change.Kind != ChangeKind.Removed).Select(change => change.Key).ToHashSet(StringComparer.Ordinal);
        var removed = diff.Rows.Where(change => change.Kind == ChangeKind.Removed).ToDictionary(change => change.Key, StringComparer.Ordinal);
        var views = ByKey(before);
        var result = new Dictionary<string, List<RowChangeView>>(StringComparer.Ordinal);
        var anchor = string.Empty;
        foreach (var key in before.Rows.Select(row => row.Key))
        {
            if (kept.Contains(key))
            {
                anchor = key;
            }
            else if (removed.TryGetValue(key, out var change))
            {
                if (!result.TryGetValue(anchor, out var list))
                {
                    list = [];
                    result[anchor] = list;
                }

                list.Add(View(change, RowChangeTag.Removed, views, []));
            }
        }

        return result;
    }

    private static Dictionary<string, RowView> ByKey(RuleView view)
    {
        var byKey = new Dictionary<string, RowView>(StringComparer.Ordinal);
        foreach (var row in view.Rows.Concat(view.DefaultRow is null ? [] : [view.DefaultRow]))
        {
            byKey.TryAdd(row.Key, row);
        }

        return byKey;
    }

    /// <summary>
    /// The rows both revisions have that left their place: those outside a longest run of rows that keep their relative order, so
    /// one row moved from the front to the back is the only one reported.
    /// </summary>
    private static HashSet<string> MovedKeys(RuleDiff diff)
    {
        var moved = new HashSet<string>(StringComparer.Ordinal);
        if (!diff.RowOrderChanged)
        {
            return moved;
        }

        var shared = diff.Rows.Where(change => change.Kind is ChangeKind.Unchanged or ChangeKind.Changed).ToList();
        var inBefore = shared.OrderBy(change => change.IndexBefore).Select(change => change.Key).ToList();
        var inAfter = shared.Select(change => change.Key).ToList();

        var length = new int[inBefore.Count + 1][];
        for (var i = 0; i <= inBefore.Count; i++)
        {
            length[i] = new int[inAfter.Count + 1];
        }

        for (var i = inBefore.Count - 1; i >= 0; i--)
        {
            for (var j = inAfter.Count - 1; j >= 0; j--)
            {
                length[i][j] = inBefore[i] == inAfter[j] ? length[i + 1][j + 1] + 1 : Math.Max(length[i + 1][j], length[i][j + 1]);
            }
        }

        int row = 0, column = 0;
        while (column < inAfter.Count)
        {
            if (row < inBefore.Count && inBefore[row] == inAfter[column])
            {
                row++;
                column++;
            }
            else if (row < inBefore.Count && length[row + 1][column] >= length[row][column + 1])
            {
                row++;
            }
            else
            {
                moved.Add(inAfter[column++]);
            }
        }

        return moved;
    }

    // ---- summary -----------------------------------------------------------------------------

    private static string Summary(IReadOnlyList<RowChangeView> rows, ChangeKind? defaultRow, bool hitPolicyChanged, WorkbenchLocale locale)
    {
        var changed = rows.Count(row => row.Tag == RowChangeTag.Changed) + (defaultRow == ChangeKind.Changed ? 1 : 0);
        var added = rows.Count(row => row.Tag == RowChangeTag.Added) + (defaultRow == ChangeKind.Added ? 1 : 0);
        var removed = rows.Count(row => row.Tag == RowChangeTag.Removed) + (defaultRow == ChangeKind.Removed ? 1 : 0);
        var moved = rows.Count(row => row.Tag == RowChangeTag.Moved);

        if (changed + added + removed + moved == 0 && !hitPolicyChanged)
        {
            return SentenceTemplates.Text(locale, "change.none");
        }

        var parts = new List<string>
        {
            SentenceTemplates.Text(
                locale,
                "change.summary",
                ("changes", Count(locale, "change.changes", changed)),
                ("adds", Count(locale, "change.adds", added)),
                ("removes", Count(locale, "change.removes", removed))),
        };
        if (moved > 0)
        {
            parts.Add(Count(locale, "change.moves", moved));
        }

        if (hitPolicyChanged)
        {
            parts.Add(SentenceTemplates.Text(locale, "change.hitPolicy"));
        }

        return string.Join(' ', parts);
    }

    private static string Count(WorkbenchLocale locale, string key, int count) => count switch
    {
        0 => SentenceTemplates.Text(locale, key + ".zero"),
        1 => SentenceTemplates.Text(locale, key + ".one"),
        _ => SentenceTemplates.Text(locale, key + ".many", ("n", count.ToString(CultureInfo.InvariantCulture))),
    };

    // ---- samples -----------------------------------------------------------------------------

    private static CaseOutcomeChange CaseChange(BehaviourChange change, RuleView before, RuleView after, WorkbenchLocale locale)
    {
        var earlier = Side(change.Before, before, locale);
        var later = Side(change.After, after, locale);
        return new CaseOutcomeChange(change.Name, earlier.Text, later.Text, earlier.RowKey, earlier.RowNumber, later.RowKey, later.RowNumber);
    }

    /// <summary>The outcome of a simulation in words, and the key and 1-based position of the first row that decided, when it is not the default row.</summary>
    private static (string Text, string? RowKey, int? RowNumber) Side(SimulationResult result, RuleView view, WorkbenchLocale locale)
    {
        if (result.Outcome is not Decided decided)
        {
            return (SentenceTemplates.Text(locale, "change.noResult"), null, null);
        }

        var views = ByKey(view);
        var keys = decided.MatchedRowKeys.Count > 0 ? decided.MatchedRowKeys : [decided.RowKey];
        var text = string.Join(
            SentenceTemplates.Text(locale, "change.outcomes"),
            keys.Select(key => RuleDescriber.OutcomeSentence(views[key].Outcome, locale)).Distinct(StringComparer.Ordinal));
        var first = views[keys[0]];
        return first.IsDefault ? (text, null, null) : (text, first.Key, first.Index + 1);
    }
}
