using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The reference page includes its complete example from the fixture files; the page and the fixtures may not drift apart.</summary>
public sealed class DocumentationTests
{
    private const string ReferencePage = "docs/reference/brain-process-v1.md";

    [Theory]
    [InlineData("tests/fixtures/brain-process/outreach.contract.json")]
    [InlineData("tests/fixtures/brain-process/outreach.rule.json")]
    [InlineData("tests/fixtures/brain-process/outreach.process.json")]
    public void TheReferencePageIncludesTheFixtureVerbatim(string fixture)
    {
        var page = Normalize(TestFiles.Read(ReferencePage));
        var example = Normalize(TestFiles.Read(fixture)).Trim();

        page.Should().Contain(example, because: $"{ReferencePage} must show {fixture} exactly");
    }

    [Fact]
    public void TheExampleOnThePageLoadsAndItsScenariosPass()
    {
        var process = TestFiles.OutreachProcess();

        var result = process.RunScenarios();

        result.IsOk.Should().BeTrue();
        result.Results.Should().HaveCount(5);
    }

    [Fact]
    public void TheRuleOfTheExampleRunsItsOwnGoldens()
    {
        TestFiles.OutreachRule().RunGoldens().IsOk.Should().BeTrue();
    }

    [Fact]
    public void ThePageNamesEveryCodeTheLoaderCanReport()
    {
        var page = TestFiles.Read(ReferencePage);
        var codes = TestFiles.ReadJson(TestFiles.Fixtures + "conformance.json").AsArray()
            .Where(item => item!["expect"] is System.Text.Json.Nodes.JsonObject)
            .SelectMany(item => item!["expect"]!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()))
            .Where(code => code.StartsWith("process.", StringComparison.Ordinal))
            .Distinct();

        foreach (var code in codes)
        {
            page.Should().Contain("`" + code + "`");
        }
    }

    [Fact]
    public void ThePageNamesEveryRunTimeCodeOfTheInterpreter()
    {
        var page = TestFiles.Read(ReferencePage);
        var codes = new[]
        {
            "process.input.unknown", "process.variable.type_mismatch", "process.definition.mismatch", "process.trigger.stale",
            "process.event.not_accepted", "process.task.outcome_unknown", "process.effect.output_malformed", "process.ai.input_invalid",
            "process.ai.output_invalid", "process.route.unmatched", "process.timer.until_missing", "process.wait.correlation_missing",
            "process.loop.not_a_list", "process.limits.max_steps", "process.state.malformed",
        };

        foreach (var code in codes)
        {
            page.Should().Contain("`" + code + "`");
        }
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
