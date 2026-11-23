using System.Text.Json.Nodes;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Finds and reads the shared fixture files above the test assembly.</summary>
internal static class TestFiles
{
    internal const string Fixtures = "tests/fixtures/brain-process/";

    internal static string Locate(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("Could not locate the fixture file above the test assembly.", relativePath);
    }

    internal static string Read(string relativePath) => File.ReadAllText(Locate(relativePath));

    internal static JsonNode ReadJson(string relativePath) => JsonNode.Parse(Read(relativePath))!;

    /// <summary>The rule of the reference outreach process, loaded against its synthetic contract.</summary>
    internal static BrainRule OutreachRule()
    {
        var contract = BrainContract.Parse(Read(Fixtures + "outreach.contract.json"));
        var loaded = BrainRuleLoader.Load(Read(Fixtures + "outreach.rule.json"), [contract]);
        return loaded.Rule ?? throw new InvalidOperationException("The outreach rule does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }

    /// <summary>The reference outreach process.</summary>
    internal static BrainProcess OutreachProcess()
    {
        var loaded = BrainProcessLoader.Load(Read(Fixtures + "outreach.process.json"), new ProcessLoadContext([OutreachRule()]));
        return loaded.Process ?? throw new InvalidOperationException("The outreach process does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }
}
