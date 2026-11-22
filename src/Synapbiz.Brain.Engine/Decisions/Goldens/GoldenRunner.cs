using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Validation;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Facts;
using Nuvora.Decisions.Engine.Serialization;

namespace Nuvora.Decisions.Engine.Goldens;

/// <summary>One golden case's actual result (D-12): whether it passed, the actual outcome, its trace, and the row key the case pinned (null when it pinned none).</summary>
public sealed record GoldenCaseResult(string Name, bool Passed, DecisionOutcome Actual, DecisionTrace Trace, string? ExpectedRowKey = null);

/// <summary>
/// The result of <see cref="GoldenRunner.Run"/> (D-12): every case's actual
/// outcome, and which rows (including the default row) no PASSING case
/// matched.
/// </summary>
public sealed record GoldenRunResult
{
    public required IReadOnlyList<GoldenCaseResult> Results { get; init; }

    public required IReadOnlyList<string> UncoveredRowKeys { get; init; }

    /// <summary>Cases that did not match their expectation.</summary>
    public IReadOnlyList<GoldenCaseResult> Failed => [.. Results.Where(r => !r.Passed)];

    /// <summary>True when every case passed and every row (including the default) is covered by a passing case.</summary>
    public bool IsOk => Failed.Count == 0 && UncoveredRowKeys.Count == 0;

    /// <summary>Renders <see cref="Failed"/> and <see cref="UncoveredRowKeys"/> as <see cref="ValidationIssue"/>s, for composing with a <see cref="ValidationReport"/>.</summary>
    public IReadOnlyList<ValidationIssue> ToIssues()
    {
        var issues = new List<ValidationIssue>();

        foreach (var failure in Failed)
        {
            var actualDescription = failure.Actual is Decided decided ? $"row '{decided.RowKey}'" : "no decision";
            issues.Add(new ValidationIssue(
                GoldenRunner.ErrorGoldenFailed,
                $"Golden case '{failure.Name}' failed: actual outcome was {actualDescription}.",
                [.. new[] { (failure.Actual as Decided)?.RowKey, failure.ExpectedRowKey }.OfType<string>().Distinct(StringComparer.Ordinal)]));
        }

        if (UncoveredRowKeys.Count > 0)
        {
            issues.Add(new ValidationIssue(
                GoldenRunner.ErrorRowUncovered,
                $"Row(s) not matched by any passing golden case: {string.Join(", ", UncoveredRowKeys)}.",
                UncoveredRowKeys));
        }

        return issues;
    }
}

/// <summary>
/// Runs a kind's golden cases against a compiled table (D-12): every case's
/// actual outcome is compared to its expectation, and row coverage is
/// computed from PASSING cases only — a case that happens to hit a row while
/// asserting the WRONG outcome does not count as proof that row is correct.
/// An empty golden set is itself an error, never a vacuous pass
/// (GUARD-VERIFICATION-STANDARD §2, empty-set pass).
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public static class GoldenRunner
{
    /// <summary>Stable error code: a golden case's actual outcome did not match its expectation.</summary>
    public const string ErrorGoldenFailed = "decisions.validation.golden_failed";

    /// <summary>Stable error code: a row (or the default row) was never the matched row of any passing case.</summary>
    public const string ErrorRowUncovered = "decisions.validation.golden_row_uncovered";

    /// <summary>Stable error code: the golden set is empty.</summary>
    public const string ErrorEmptySet = "decisions.validation.golden_set_empty";

    /// <summary>
    /// Evaluates every one of <paramref name="cases"/> against
    /// <paramref name="table"/> as of <paramref name="asOf"/>. Throws
    /// <see cref="DecisionEngineException"/> (<see cref="ErrorEmptySet"/>)
    /// when <paramref name="cases"/> is empty. <paramref name="resolver"/> is
    /// required only when the table declares composed facts, exactly as for
    /// <see cref="TableEvaluator.Evaluate"/>.
    /// </summary>
    public static GoldenRunResult Run(
        CompiledTable table, IReadOnlyList<GoldenCase> cases, DateOnly asOf, IComposedFactResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(cases);

        if (cases.Count == 0)
        {
            throw new DecisionEngineException(
                ErrorEmptySet,
                $"Kind '{table.Descriptor.Key}' has an empty golden set; an empty set is an error, never a pass (D-12).",
                new Dictionary<string, object?> { ["kind"] = table.Descriptor.Key.Value });
        }

        var results = new List<GoldenCaseResult>(cases.Count);
        foreach (var goldenCase in cases)
        {
            var facts = FactSet.Build(table.Descriptor, goldenCase.Facts);
            var (actual, trace) = TableEvaluator.Evaluate(table, facts, asOf, resolver);
            results.Add(new GoldenCaseResult(
                goldenCase.Name, Matches(goldenCase.Expected, actual), actual, trace, (goldenCase.Expected as ExpectedDecided)?.RowKey));
        }

        var coveredByAPassingCase = results
            .Where(r => r.Passed && r.Actual is Decided)
            .SelectMany(r => ((Decided)r.Actual).MatchedRowKeys)
            .ToHashSet(StringComparer.Ordinal);

        var allRowKeys = table.DefaultRow is null
            ? table.Rows.Select(r => r.Key)
            : table.Rows.Select(r => r.Key).Append(table.DefaultRow.Key);

        var uncovered = allRowKeys.Where(k => !coveredByAPassingCase.Contains(k)).ToList();

        return new GoldenRunResult { Results = results, UncoveredRowKeys = uncovered };
    }

    private static bool Matches(GoldenExpectation expected, DecisionOutcome actual) => (expected, actual) switch
    {
        (ExpectedNoDecision, NoDecision) => true,
        (ExpectedDecided expectedDecided, Decided actualDecided) =>
            string.Equals(CanonicalOutcomeJson.Canonicalize(expectedDecided.OutcomeJson), CanonicalOutcomeJson.Canonicalize(actualDecided.OutcomeJson), StringComparison.Ordinal)
            && (expectedDecided.RowKey is null || string.Equals(expectedDecided.RowKey, actualDecided.RowKey, StringComparison.Ordinal)),
        _ => false,
    };
}
