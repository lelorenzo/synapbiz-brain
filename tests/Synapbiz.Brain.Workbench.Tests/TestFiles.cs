using System.Text.Json.Nodes;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>Finds and reads the shared fixture files above the test assembly, and loads the workbench fixtures.</summary>
internal static class TestFiles
{
    internal const string Folder = "tests/fixtures/workbench/";

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

    internal static BrainContract IntakeContract() => BrainContract.Parse(Read(Folder + "intake.contract.json"));

    internal static BrainContract ReadinessContract() => BrainContract.Parse(Read("tests/fixtures/brain-spec/readiness.contract.json"));

    /// <summary>The text of a workbench rule fixture, for example <c>intake.first.rule.json</c>.</summary>
    internal static string IntakeText(string file) => Read(Folder + file);

    internal static BrainRule LoadIntake(string file) => Load(IntakeText(file), IntakeContract());

    internal static BrainRule LoadIntakeText(string text) => Load(text, IntakeContract());

    internal static BrainRule LoadReadiness() => Load(Read("tests/fixtures/brain-spec/readiness.rule.json"), ReadinessContract());

    internal static BrainRule Load(string text, BrainContract contract)
    {
        var result = BrainRuleLoader.Load(text, [contract]);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => $"{error.Code}@{error.Path}")));
        return result.Rule!;
    }

    /// <summary>The fixture text with the <c>goldens</c> array emptied.</summary>
    internal static string WithoutGoldens(string text)
    {
        var document = JsonNode.Parse(text)!;
        document["body"]!["goldens"] = new JsonArray();
        return document.ToJsonString();
    }

    internal static BrainRule LoadByName(string name) => name switch
    {
        "readiness" => LoadReadiness(),
        "intake.first" => LoadIntake("intake.first.rule.json"),
        "intake.first.v2" => LoadIntake("intake.first.v2.rule.json"),
        "intake.unique" => LoadIntake("intake.unique.rule.json"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown fixture."),
    };
}

/// <summary>The names of the rule fixtures, as theory data.</summary>
public static class FixtureNames
{
    /// <summary>Every rule fixture the workbench tests load: the collect example of the Spec tests and the three intake rules.</summary>
    public static TheoryData<string> RuleNames => ["readiness", "intake.first", "intake.first.v2", "intake.unique"];
}
