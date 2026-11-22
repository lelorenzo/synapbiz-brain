using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Spec.Tests;

/// <summary>The worked example of the migration guide: a synthetic interim file, the contract and the rule it migrates to.</summary>
public sealed class MigrationExampleTests
{
    private const string Interim = "tests/fixtures/brain-spec/migration/interim.rule.json";
    private const string Contract = "tests/fixtures/brain-spec/migration/migrated.contract.json";
    private const string Rule = "tests/fixtures/brain-spec/migration/migrated.rule.json";
    private const string Page = "docs/migration/interim-rule-shape.md";

    [Theory]
    [InlineData(Interim)]
    [InlineData(Contract)]
    [InlineData(Rule)]
    public void TheMigrationPageIncludesTheFixtureVerbatim(string fixture)
    {
        var page = TestFiles.Read(Page).Replace("\r\n", "\n", StringComparison.Ordinal);
        var example = TestFiles.Read(fixture).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        page.Should().Contain(example, because: $"{Page} must show {fixture} exactly");
    }

    [Fact]
    public void TheMigratedRuleLoadsAgainstTheMigratedContractAndItsGoldensRunClean()
    {
        var result = BrainRuleLoader.Load(TestFiles.Read(Rule), [BrainContract.Parse(TestFiles.Read(Contract))]);

        result.Errors.Should().BeEmpty(because: string.Join("; ", result.Errors.Select(error => $"{error.Code}@{error.Path}")));
        var run = result.Rule!.RunGoldens();
        run.IsOk.Should().BeTrue();
        run.Results.Should().HaveCount(3);
    }

    [Fact]
    public void TheInterimFileIsRecognisedByRevisionAndFactsAndNotBySpec()
    {
        var interim = TestFiles.ReadJson(Interim);

        interim["$spec"]!.GetValue<string>().Should().Be("brain.rule@1");
        interim.AsObject().ContainsKey("revision").Should().BeTrue();
        interim["meta"]!["facts"].Should().BeOfType<JsonObject>();
        interim.AsObject().ContainsKey("version").Should().BeFalse();
        interim["body"]!.AsObject().ContainsKey("kind").Should().BeFalse();
    }

    [Fact]
    public void TheDocumentedInterimDocumentIsRefusedAsItStandsWithTheEnvelopeAndShapeErrors()
    {
        var result = BrainRuleLoader.Load(TestFiles.Read(Interim), [BrainContract.Parse(TestFiles.Read(Contract))]);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.Code).Should().OnlyContain(code => code == "spec.structure.schema");
    }

    [Fact]
    public void EachMigratedGoldenCarriesTheOutcomesOfTheRowsTheInterimGoldenNamedInThatOrder()
    {
        var interim = TestFiles.ReadJson(Interim)["body"]!;
        var migrated = TestFiles.ReadJson(Rule)["body"]!;
        var rows = migrated["rows"]!.AsArray().ToDictionary(row => row!["legacyKey"]!.GetValue<string>(), row => row!);

        foreach (var (before, after) in interim["goldens"]!.AsArray().Zip(migrated["goldens"]!.AsArray()))
        {
            var expected = before!["expected"]!;
            var expect = after!["expect"]!;
            after["name"]!.GetValue<string>().Should().Be(before["name"]!.GetValue<string>());
            if (expected["kind"]!.GetValue<string>() != "decided")
            {
                expect["noDecision"]!.GetValue<bool>().Should().BeTrue();
                continue;
            }

            var keys = expected["rowKeys"]!.AsArray().Select(key => key!.GetValue<string>()).ToList();
            expect["row"]!.GetValue<string>().Should().Be(rows[keys[0]]["id"]!.GetValue<string>());
            expect["outcome"]!.AsArray().Select(outcome => outcome!.ToJsonString())
                .Should().Equal(keys.Select(key => rows[key]["outcome"]!.ToJsonString()));
        }
    }
}
