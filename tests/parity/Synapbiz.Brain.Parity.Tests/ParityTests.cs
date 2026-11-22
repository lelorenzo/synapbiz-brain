extern alias erp;
extern alias lib;

using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;

namespace Synapbiz.Brain.Parity.Tests;

/// <summary>
/// Behaviour parity between the Brain libraries (<c>lib</c>) and the source repository's engine (<c>erp</c>):
/// one scenario source, compiled twice, must yield identical (name, result) lists, and the source side must
/// equal the committed baseline.
/// </summary>
public sealed class ParityTests
{
    private const string BaselineRelativePath = "tests/parity/baseline/decisions.parity.json";
    private const int MaxReportedDifferences = 20;
    private const int MinimumScenarioCount = 200;

    [Fact]
    public void TheLibraryAndTheSourceRepositoryProduceIdenticalResults()
    {
        var library = lib::Synapbiz.Brain.Parity.ParityRunner.Run().ToList();
        var source = erp::Synapbiz.Brain.Parity.ParityRunner.Run().ToList();

        library.Count.Should().BeGreaterThanOrEqualTo(MinimumScenarioCount);
        var differences = Differences(source, library);

        differences.Should().BeEmpty(
            "the library must behave exactly like the source engine; first {0} differing scenarios (expected = source, actual = library): {1}",
            MaxReportedDifferences,
            string.Join(Environment.NewLine, differences.Take(MaxReportedDifferences)));
    }

    [Fact]
    public void TheSourceRepositoryResultsEqualTheCommittedBaseline()
    {
        var root = FindRepositoryRoot();
        var baselinePath = Path.Combine(root, BaselineRelativePath);
        var source = erp::Synapbiz.Brain.Parity.ParityRunner.Run().ToList();

        if (Environment.GetEnvironmentVariable("PARITY_WRITE_BASELINE") == "1")
        {
            WriteBaseline(baselinePath, root, source);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var baseline = document.RootElement.GetProperty("scenarios").EnumerateArray()
            .Select(s => new KeyValuePair<string, string>(s.GetProperty("name").GetString()!, s.GetProperty("result").GetString()!))
            .ToList();

        baseline.Count.Should().BeGreaterThanOrEqualTo(MinimumScenarioCount);
        document.RootElement.GetProperty("source").GetString().Should().Be(File.ReadAllText(Path.Combine(root, "tools", "parity", "SOURCE.lock")).Trim());

        var differences = Differences(baseline, source);
        differences.Should().BeEmpty(
            "the source engine must reproduce the committed baseline; first {0} differing scenarios (expected = baseline, actual = source): {1}",
            MaxReportedDifferences,
            string.Join(Environment.NewLine, differences.Take(MaxReportedDifferences)));
    }

    private static List<string> Differences(
        List<KeyValuePair<string, string>> expected, List<KeyValuePair<string, string>> actual)
    {
        var differences = new List<string>();
        var count = Math.Max(expected.Count, actual.Count);
        for (var index = 0; index < count; index++)
        {
            var left = index < expected.Count ? expected[index] : (KeyValuePair<string, string>?)null;
            var right = index < actual.Count ? actual[index] : (KeyValuePair<string, string>?)null;
            if (left is null || right is null || left.Value.Key != right.Value.Key || left.Value.Value != right.Value.Value)
            {
                differences.Add(
                    $"{left?.Key ?? right!.Value.Key}: expected [{left?.Value ?? "<missing>"}] actual [{right?.Value ?? "<missing>"}]"
                    + (left is not null && right is not null && left.Value.Key != right.Value.Key ? $" (names differ: {left.Value.Key} / {right.Value.Key})" : string.Empty));
            }
        }

        return differences;
    }

    private static void WriteBaseline(string path, string root, IReadOnlyList<KeyValuePair<string, string>> scenarios)
    {
        var sourceRoot = Environment.GetEnvironmentVariable("NUVORA_ROOT")
            ?? throw new InvalidOperationException("NUVORA_ROOT is not set.");
        var commit = Git(sourceRoot, "rev-parse", "HEAD");
        var pinned = File.ReadAllText(Path.Combine(root, "tools", "parity", "SOURCE.lock")).Trim();
        if (!string.Equals(commit, pinned, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Source HEAD {commit} differs from tools/parity/SOURCE.lock {pinned}.");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("source", commit);
            writer.WriteStartArray("scenarios");
            foreach (var scenario in scenarios)
            {
                writer.WriteStartObject();
                writer.WriteString("name", scenario.Key);
                writer.WriteString("result", scenario.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Encoding.UTF8.GetString(stream.ToArray()) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Git(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(workingDirectory);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"git failed with exit code {process.ExitCode}.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Synapbiz.Brain.Parity.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find Synapbiz.Brain.Parity.sln above the parity test assembly.");
    }
}
