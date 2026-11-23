using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Goldens;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench;

/// <summary>One test case and what the engine made of it.</summary>
/// <param name="Name">The case name.</param>
/// <param name="FromDocument">True for a golden case of the rule document; false for an extra case.</param>
/// <param name="Passed">Whether the engine's outcome matched the expectation.</param>
/// <param name="Facts">The fact values of the case.</param>
/// <param name="Expected">What the case expects.</param>
/// <param name="Actual">The engine's outcome; null when the engine refused the case.</param>
/// <param name="MatchedRowKeys">The row keys of a decided outcome, in table order; empty otherwise.</param>
/// <param name="Failure">The engine's refusal; null when the engine evaluated the case.</param>
public sealed record TestCaseView(
    string Name,
    bool FromDocument,
    bool Passed,
    IReadOnlyDictionary<string, DecisionValue> Facts,
    GoldenExpectation Expected,
    DecisionOutcome? Actual,
    IReadOnlyList<string> MatchedRowKeys,
    SimulationFailure? Failure);

/// <summary>The result of running every test case of a rule.</summary>
/// <param name="Cases">One entry per case: the document's goldens in order, then the extra cases.</param>
/// <param name="UncoveredRowKeys">Every row key, the default row last, that no passing case matched.</param>
public sealed record TestRunView(IReadOnlyList<TestCaseView> Cases, IReadOnlyList<string> UncoveredRowKeys)
{
    /// <summary>The number of cases that passed.</summary>
    public int PassedCount => Cases.Count(testCase => testCase.Passed);

    /// <summary>The number of cases that did not pass.</summary>
    public int FailedCount => Cases.Count(testCase => !testCase.Passed);

    /// <summary>True when no case failed and every row is covered by a passing case.</summary>
    public bool IsOk => FailedCount == 0 && UncoveredRowKeys.Count == 0;
}

/// <summary>Runs the golden cases of a rule, and extra cases, on the engine's golden runner.</summary>
public static class RuleTester
{
    /// <summary>
    /// Runs the golden cases of the rule document and then <paramref name="extraCases"/>, each on its own through
    /// <c>GoldenRunner.Run</c>. A case the engine refuses fails with the engine's code and does not stop the run. With no case
    /// at all the run is empty and every row is uncovered.
    /// </summary>
    /// <param name="rule">The loaded rule.</param>
    /// <param name="extraCases">Cases that are not part of the document.</param>
    /// <param name="resolver">Resolves composed facts; needed only when the kind declares any.</param>
    public static TestRunView Run(BrainRule rule, IReadOnlyList<GoldenCase>? extraCases = null, IComposedFactResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var cases = rule.Goldens.Select(golden => RunCase(rule, golden, true, resolver))
            .Concat((extraCases ?? []).Select(golden => RunCase(rule, golden, false, resolver)))
            .ToList();

        var covered = cases.Where(testCase => testCase.Passed).SelectMany(testCase => testCase.MatchedRowKeys).ToHashSet(StringComparer.Ordinal);
        var keys = rule.Table.Rows.Select(row => row.Key);
        if (rule.Table.DefaultRow is { } fallback)
        {
            keys = keys.Append(fallback.Key);
        }

        return new TestRunView(cases, [.. keys.Where(key => !covered.Contains(key))]);
    }

    private static TestCaseView RunCase(BrainRule rule, GoldenCase golden, bool fromDocument, IComposedFactResolver? resolver)
    {
        try
        {
            var result = GoldenRunner.Run(rule.Compile(), [golden], DateOnly.MinValue, resolver).Results[0];
            var keys = (result.Actual as Decided)?.MatchedRowKeys ?? [];
            return new TestCaseView(golden.Name, fromDocument, result.Passed, golden.Facts, golden.Expected, result.Actual, keys, null);
        }
        catch (DecisionEngineException error)
        {
            return new TestCaseView(
                golden.Name, fromDocument, false, golden.Facts, golden.Expected, null, [], new SimulationFailure(error.ErrorCode, error.Message));
        }
    }
}
