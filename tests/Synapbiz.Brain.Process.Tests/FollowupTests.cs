using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The follow-up example of the reference page: triggers, a delivery window, a contact action and a poll in one document.</summary>
public sealed class FollowupTests
{
    private const string ReferencePage = "docs/reference/brain-process-v1.md";

    [Theory]
    [InlineData("tests/fixtures/brain-process/followup.contract.json")]
    [InlineData("tests/fixtures/brain-process/followup.rule.json")]
    [InlineData("tests/fixtures/brain-process/followup.process.json")]
    public void TheReferencePageIncludesTheFixtureVerbatim(string fixture)
    {
        var page = TestFiles.Read(ReferencePage).Replace("\r\n", "\n", StringComparison.Ordinal);
        var example = TestFiles.Read(fixture).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        page.Should().Contain(example, because: $"{ReferencePage} must show {fixture} exactly");
    }

    [Fact]
    public void TheScenariosOfTheExamplePass()
    {
        var result = TestFiles.FollowupProcess().RunScenarios();

        result.IsOk.Should().BeTrue(because: string.Join("; ", result.Results.Where(item => !item.Passed).Select(item => item.Name + ": " + item.Failure)));
        result.Results.Should().HaveCount(5);
    }

    [Fact]
    public void TheRuleOfTheExampleRunsItsOwnGoldens()
    {
        TestFiles.FollowupRule().RunGoldens().IsOk.Should().BeTrue();
    }

    [Fact]
    public void TheExampleExposesItsTriggers()
    {
        var triggers = TestFiles.FollowupProcess().Triggers;

        triggers.Should().HaveCount(2);
        triggers[0].Should().BeOfType<EventTriggerDeclaration>().Which.BusinessKeyMember.Should().Be("case_ref");
        var schedule = triggers[1].Should().BeOfType<ScheduleTriggerDeclaration>().Subject;
        schedule.Id.Should().Be("morning");
        schedule.Misfire.Should().Be(ProcessMisfire.Skip);
    }

    [Fact]
    public void TheOutreachExampleHasNoTriggers()
    {
        TestFiles.OutreachProcess().Triggers.Should().BeEmpty();
    }
}
