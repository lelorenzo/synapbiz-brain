using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Conditions;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Facts;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench;

/// <summary>An evaluation or fact set the engine refused.</summary>
/// <param name="Code">The engine's stable error code, for example <c>decisions.facts.missing</c>.</param>
/// <param name="Message">The engine's English message.</param>
public sealed record SimulationFailure(string Code, string Message);

/// <summary>Whether one condition of a row held for the facts of a simulation.</summary>
/// <param name="Index">Position of the condition in the row, from 0.</param>
/// <param name="FactName">The fact the condition reads.</param>
/// <param name="Passed">Whether the engine's matcher accepts the fact's value.</param>
/// <param name="Actual">The fact's value in the simulation; null when the fact was left out.</param>
public sealed record ConditionCheck(int Index, string FactName, bool Passed, DecisionValue? Actual);

/// <summary>What happened to one row in a simulation.</summary>
/// <param name="Key">The engine row key.</param>
/// <param name="IsDefault">True for the default row.</param>
/// <param name="Matched">Whether every condition of the row passed; true for a row with no conditions.</param>
/// <param name="Selected">Whether the engine reports the row among the matched rows of a decided outcome.</param>
/// <param name="Conditions">The check of each condition, in order.</param>
public sealed record RowExplanation(string Key, bool IsDefault, bool Matched, bool Selected, IReadOnlyList<ConditionCheck> Conditions);

/// <summary>The result of a simulation: the engine's decision and trace, or the engine's refusal, and a per-row explanation.</summary>
/// <param name="Outcome">The engine's outcome; null on a failure.</param>
/// <param name="Trace">The engine's trace; null on a failure.</param>
/// <param name="Failure">The engine's refusal; null when the simulation succeeded.</param>
/// <param name="Rows">One explanation per row in table order, then the default row; empty when the fact set was refused or the table has composed facts.</param>
public sealed record SimulationResult(
    DecisionOutcome? Outcome,
    DecisionTrace? Trace,
    SimulationFailure? Failure,
    IReadOnlyList<RowExplanation> Rows)
{
    /// <summary>True when the engine evaluated the facts.</summary>
    public bool Succeeded => Failure is null;
}

/// <summary>Evaluates a rule for example facts on the real engine and explains the result row by row.</summary>
public static class RuleSimulator
{
    /// <summary>
    /// Evaluates <paramref name="rule"/> with <c>BrainRule.Evaluate</c> and, for every row, reports which conditions passed
    /// according to the engine's own condition matcher. A fact set or evaluation the engine refuses is a
    /// <see cref="SimulationFailure"/> carrying the engine's code. No other exception is caught.
    /// </summary>
    /// <param name="rule">The loaded rule.</param>
    /// <param name="facts">The fact values by fact name.</param>
    /// <param name="asOf">The date the decision applies to, forwarded to a composed-fact resolver.</param>
    /// <param name="resolver">Resolves composed facts; needed only when the kind declares any.</param>
    public static SimulationResult Simulate(
        BrainRule rule, IReadOnlyDictionary<string, DecisionValue> facts, DateOnly asOf, IComposedFactResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(facts);

        List<RowExplanation> rows;
        try
        {
            var compiled = rule.Compile();
            _ = FactSet.Build(rule.Kind, facts);
            rows = compiled.ComposedFacts.Count > 0 ? [] : Explain(rule, facts);
        }
        catch (DecisionEngineException error)
        {
            return Failed(error, []);
        }

        try
        {
            var result = rule.Evaluate(facts, asOf, resolver);
            var matched = (result.Outcome as Decided)?.MatchedRowKeys ?? [];
            var explained = rows.Select(row => row with { Selected = matched.Contains(row.Key, StringComparer.Ordinal) }).ToList();
            return new SimulationResult(result.Outcome, result.Trace, null, explained);
        }
        catch (DecisionEngineException error)
        {
            return Failed(error, rows);
        }
    }

    private static SimulationResult Failed(DecisionEngineException error, IReadOnlyList<RowExplanation> rows) =>
        new(null, null, new SimulationFailure(error.ErrorCode, error.Message), rows);

    private static List<RowExplanation> Explain(BrainRule rule, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        var types = rule.Kind.Facts.ToDictionary(fact => fact.Name, fact => fact.Type, StringComparer.Ordinal);
        var rows = rule.Table.Rows.Select(row => ExplainRow(row, false, types, facts)).ToList();
        if (rule.Table.DefaultRow is { } fallback)
        {
            rows.Add(ExplainRow(fallback, true, types, facts));
        }

        return rows;
    }

    private static RowExplanation ExplainRow(
        Row row, bool isDefault, Dictionary<string, FactType> types, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        var checks = row.Conditions.Select((condition, index) =>
        {
            facts.TryGetValue(condition.FactName, out var actual);
            var passed = ConditionMatcher.Create(condition, types[condition.FactName]).Matches(actual);
            return new ConditionCheck(index, condition.FactName, passed, actual);
        }).ToList();

        return new RowExplanation(row.Key, isDefault, checks.All(check => check.Passed), false, checks);
    }
}
