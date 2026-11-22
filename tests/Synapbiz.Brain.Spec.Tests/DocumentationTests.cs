using FluentAssertions;

namespace Synapbiz.Brain.Spec.Tests;

/// <summary>The reference page includes its complete example from the fixture files; the page and the fixtures may not drift apart.</summary>
public sealed class DocumentationTests
{
    private const string ReferencePage = "docs/reference/brain-rule-v1.md";

    [Theory]
    [InlineData("tests/fixtures/brain-spec/readiness.contract.json")]
    [InlineData("tests/fixtures/brain-spec/readiness.rule.json")]
    public void TheReferencePageIncludesTheFixtureVerbatim(string fixture)
    {
        var page = Normalize(TestFiles.Read(ReferencePage));
        var example = Normalize(TestFiles.Read(fixture)).Trim();

        page.Should().Contain(example, because: $"{ReferencePage} must show {fixture} exactly");
    }

    [Fact]
    public void TheExampleOnThePageLoadsAsARuleWithItsValidTable()
    {
        var contract = BrainContract.Parse(TestFiles.Read("tests/fixtures/brain-spec/readiness.contract.json"));
        var result = BrainRuleLoader.Load(TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json"), [contract]);

        result.Errors.Should().BeEmpty();
        result.Rule!.ValidateTable().Errors.Should().BeEmpty();
        result.Rule.RunGoldens().IsOk.Should().BeTrue();
    }

    [Fact]
    public void TheMigrationPageLinksTheReferencePageAndThatPageExists()
    {
        TestFiles.Read("docs/migration/interim-rule-shape.md").Should().Contain("brain-rule-v1.md");
        TestFiles.Locate(ReferencePage).Should().NotBeNullOrEmpty();
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
