using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>Finds and loads the shared fixture files above the test assembly.</summary>
internal static class Fixtures
{
    private const string ProcessFixtures = "tests/fixtures/brain-process/";
    private const string HostFixtures = "tests/fixtures/brain-host/";

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

    /// <summary>The reference outreach process of the interpreter library, loaded against its synthetic contract and rule.</summary>
    internal static BrainProcess Outreach()
    {
        var contract = BrainContract.Parse(Read(ProcessFixtures + "outreach.contract.json"));
        var rule = BrainRuleLoader.Load(Read(ProcessFixtures + "outreach.rule.json"), [contract]).Rule
            ?? throw new InvalidOperationException("The outreach rule does not load.");
        return Load(Read(ProcessFixtures + "outreach.process.json"), [rule]);
    }

    /// <summary>The follow-up process of the interpreter library (a contact action in a delivery window, then a poll), loaded against its synthetic contract and rule.</summary>
    internal static BrainProcess Followup()
    {
        var contract = BrainContract.Parse(Read(ProcessFixtures + "followup.contract.json"));
        var rule = BrainRuleLoader.Load(Read(ProcessFixtures + "followup.rule.json"), [contract]).Rule
            ?? throw new InvalidOperationException("The follow-up rule does not load.");
        return Load(Read(ProcessFixtures + "followup.process.json"), [rule]);
    }

    /// <summary>The text of the follow-up process document.</summary>
    internal static string FollowupText() => Read(ProcessFixtures + "followup.process.json");

    /// <summary>The text of the reference outreach process document.</summary>
    internal static string OutreachText() => Read(ProcessFixtures + "outreach.process.json");

    /// <summary>A process of <c>tests/fixtures/brain-host</c>, by file name without the extension.</summary>
    internal static BrainProcess Host(string name) => Load(Read(HostFixtures + name + ".process.json"), []);

    /// <summary>A process of <c>tests/fixtures/brain-host</c> with its text changed by <paramref name="edit"/> (another version, another cron expression), loaded as a document of its own.</summary>
    internal static BrainProcess HostEdited(string name, Func<string, string> edit) => Load(edit(Read(HostFixtures + name + ".process.json")), []);

    private static BrainProcess Load(string text, IReadOnlyCollection<BrainRule> rules)
    {
        var loaded = BrainProcessLoader.Load(text, new ProcessLoadContext(rules));
        return loaded.Process ?? throw new InvalidOperationException("The fixture does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }
}
