using System.Diagnostics.CodeAnalysis;
using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.Decisions.Contracts.Goldens;

/// <summary>
/// What a <see cref="GoldenCase"/> expects a table to decide (D-12). Named
/// <c>ExpectedDecided</c>/<c>ExpectedNoDecision</c> rather than
/// <c>Decided</c>/<c>NoDecision</c> (the engine's own evaluation outcome,
/// <see cref="Evaluation.DecisionOutcome"/>) so a file comparing expectation
/// to actual outcome can import both namespaces without an ambiguous
/// reference.
/// </summary>
[SuppressMessage(
    "Design",
    "S1694:An abstract class should have both abstract and concrete methods",
    Justification = "A closed, pattern-matchable hierarchy (ExpectedDecided | ExpectedNoDecision) is the point — mirrors DecisionOutcome's own Decided/NoDecision design.")]
public abstract record GoldenExpectation;

/// <summary>The case expects a decided row.</summary>
/// <param name="OutcomeJson">The canonical expected object for UNIQUE/FIRST, or ordered array of outcome objects for COLLECT.</param>
/// <param name="RowKey">Optionally pins which row is expected to match, for auditing row coverage precisely (two rows could otherwise yield the same outcome).</param>
public sealed record ExpectedDecided(string OutcomeJson, string? RowKey = null) : GoldenExpectation;

/// <summary>The case expects no row to match (and no default row to apply).</summary>
public sealed record ExpectedNoDecision : GoldenExpectation;

/// <summary>
/// One named fact set and its expected outcome (D-12): the unit the engine's
/// golden runner evaluates a compiled table against.
/// </summary>
public sealed record GoldenCase(string Name, IReadOnlyDictionary<string, DecisionValue> Facts, GoldenExpectation Expected);
