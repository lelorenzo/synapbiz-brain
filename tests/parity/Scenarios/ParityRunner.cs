using System.Globalization;
using Nuvora.Decisions.Contracts;

namespace Synapbiz.Brain.Parity;

/// <summary>
/// Runs every parity scenario and returns the ordered list of (scenario name, canonical result).
/// The same source is compiled against the Brain libraries and against the source repository's
/// projects; the two lists must be identical. Only API present in both copies is used, hashes are
/// read through <c>.Value</c>, and a <see cref="DecisionEngineException"/> is recorded as
/// <c>error:&lt;ErrorCode&gt;</c>. Any other exception is a defect and is not caught.
/// </summary>
public static class ParityRunner
{
    /// <summary>Runs all scenarios in their fixed order.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Run()
    {
        var set = new ScenarioSet();
        MatcherScenarios.Register(set);
        TableScenarios.Register(set);
        CompositionScenarios.Register(set);
        ValidateScenarios.Register(set);
        GoldenScenarios.Register(set);
        FactsScenarios.Register(set);
        SpecScenarios.Register(set);
        return set.Results;
    }
}

internal sealed class ScenarioSet
{
    private readonly List<KeyValuePair<string, string>> _results = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    internal IReadOnlyList<KeyValuePair<string, string>> Results => _results;

    internal void Add(string name, Func<string> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!_names.Add(name))
        {
            throw new InvalidOperationException($"Duplicate parity scenario name '{name}'.");
        }

        _results.Add(new KeyValuePair<string, string>(name, Capture(body)));
    }

    private static string Capture(Func<string> body)
    {
        try
        {
            return body();
        }
        catch (DecisionEngineException exception)
        {
            return "error:" + exception.ErrorCode;
        }
    }
}

internal static class Format
{
    internal static string Bool(bool value) => value ? "true" : "false";

    internal static string Join(IEnumerable<string> values) => string.Join(",", values);

    internal static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
