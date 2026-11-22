using System.Text.Json;
using FluentAssertions;
using Synapbiz.Brain.Parity;

namespace Synapbiz.Brain.Engine.Tests;

/// <summary>
/// Runs the shared parity scenarios against this library and compares them with the baseline recorded
/// from the source repository's engine at the commit pinned in <c>tools/parity/SOURCE.lock</c>.
/// </summary>
public sealed class ParityBaselineTests
{
    private const int MaxReportedDifferences = 20;
    private const int MinimumScenarioCount = 200;

    [Fact]
    public void TheLibraryReproducesTheCommittedBaseline()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Parity", "decisions.parity.json")));
        var baseline = document.RootElement.GetProperty("scenarios").EnumerateArray()
            .Select(s => new KeyValuePair<string, string>(s.GetProperty("name").GetString()!, s.GetProperty("result").GetString()!))
            .ToList();
        var actual = ParityRunner.Run();

        baseline.Count.Should().BeGreaterThanOrEqualTo(MinimumScenarioCount);

        var differences = new List<string>();
        for (var index = 0; index < Math.Max(baseline.Count, actual.Count); index++)
        {
            var expected = index < baseline.Count ? baseline[index] : (KeyValuePair<string, string>?)null;
            var current = index < actual.Count ? actual[index] : (KeyValuePair<string, string>?)null;
            if (expected is null || current is null || expected.Value.Key != current.Value.Key || expected.Value.Value != current.Value.Value)
            {
                differences.Add(
                    $"{expected?.Key ?? current!.Value.Key}: expected [{expected?.Value ?? "<missing>"}] actual [{current?.Value ?? "<missing>"}]");
            }
        }

        differences.Should().BeEmpty(
            "the library must reproduce the source repository's recorded behaviour; first {0} differing scenarios: {1}",
            MaxReportedDifferences,
            string.Join(Environment.NewLine, differences.Take(MaxReportedDifferences)));
    }
}
