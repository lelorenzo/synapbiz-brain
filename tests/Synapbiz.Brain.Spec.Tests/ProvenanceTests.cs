using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Spec.Tests;

public sealed class ProvenanceTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 18);

    [Fact]
    public void EvaluateReturnsTheMatchedRowKeyTheDocumentVersionAndTheFactsWithTheirHash()
    {
        var rule = LoadFirstCase(version: 7);

        var result = rule.Evaluate(rule.Goldens[0].Facts, AsOf);

        var decided = result.Outcome.Should().BeOfType<Decided>().Subject;
        decided.RowKey.Should().Be("review/step");
        decided.OutcomeJson.Should().Be("""{"approvalState":"manager-approval"}""");
        rule.Id.Should().Be("review-rule");
        rule.Version.Should().Be(7);
        rule.RowIdsByKey["review/step"].Should().Be("review-step");
        result.Trace.KindKey.Value.Should().Be("sales.order_approval_routing");
        result.Trace.VersionNumber.Should().Be(7);
        result.Trace.VersionId.Should().BeNull();
        result.Trace.MatchedRowKey.Should().Be("review/step");
        result.Trace.IsDecided.Should().BeTrue();
        result.Trace.FactsJson.Should().StartWith("{").And.Contain("\"channelKind\":\"Web\"");
        result.Trace.FactsHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void TheSameFactsInAnotherInsertionOrderGiveTheSameJsonAndHash()
    {
        var rule = LoadFirstCase(version: 1);
        var forward = rule.Goldens[0].Facts;
        var backward = new Dictionary<string, DecisionValue>(forward.Reverse(), StringComparer.Ordinal);
        forward.Keys.Should().NotEqual(backward.Keys);

        var first = rule.Evaluate(forward, AsOf).Trace;
        var second = rule.Evaluate(backward, AsOf).Trace;

        second.FactsJson.Should().Be(first.FactsJson);
        second.FactsHash.Should().Be(first.FactsHash);
    }

    [Fact]
    public void ADifferentFactValueGivesADifferentHash()
    {
        var rule = LoadFirstCase(version: 1);
        var changed = new Dictionary<string, DecisionValue>(rule.Goldens[0].Facts, StringComparer.Ordinal)
        {
            ["manualPriceLines"] = DecisionValue.Integer(3),
        };

        rule.Evaluate(changed, AsOf).Trace.FactsHash.Should().NotBe(rule.Evaluate(rule.Goldens[0].Facts, AsOf).Trace.FactsHash);
    }

    [Fact]
    public void AVersionAboveTheIntegerRangeLeavesTheTraceVersionNumberNull()
    {
        var rule = LoadFirstCase(version: 2_147_483_648L);

        rule.Version.Should().Be(2_147_483_648L);
        rule.Evaluate(rule.Goldens[0].Facts, AsOf).Trace.VersionNumber.Should().BeNull();
    }

    [Fact]
    public void TheLargestIntegerVersionIsCarried()
    {
        var rule = LoadFirstCase(version: int.MaxValue);

        rule.Evaluate(rule.Goldens[0].Facts, AsOf).Trace.VersionNumber.Should().Be(int.MaxValue);
    }

    [Fact]
    public void EvaluatingTwiceGivesEqualResultsAndTheRuleIsNotChanged()
    {
        var rule = LoadFirstCase(version: 2);
        var tableBefore = rule.Table;
        var rowsBefore = rule.Table.Rows.ToList();

        var first = rule.Evaluate(rule.Goldens[0].Facts, AsOf);
        var second = rule.Evaluate(rule.Goldens[0].Facts, AsOf);

        second.Outcome.Should().BeEquivalentTo(first.Outcome);
        second.Trace.Should().BeEquivalentTo(first.Trace);
        rule.Table.Should().BeSameAs(tableBefore);
        rule.Table.Rows.Should().Equal(rowsBefore);
        rule.Compile().Should().BeSameAs(rule.Compile());
    }

    [Fact]
    public void AnUnmatchedGoldenFactSetGivesNoDecisionWithoutAMatchedRowKey()
    {
        var rule = LoadFirstCase(version: 2);

        var result = rule.Evaluate(rule.Goldens[1].Facts, AsOf);

        result.Outcome.Should().BeOfType<NoDecision>();
        result.Trace.IsDecided.Should().BeFalse();
        result.Trace.MatchedRowKey.Should().BeNull();
        result.Trace.VersionNumber.Should().Be(2);
    }

    [Fact]
    public void MissingFactsAreRefusedByTheEngine()
    {
        var rule = LoadFirstCase(version: 2);

        var error = Assert.Throws<DecisionEngineException>(() => rule.Evaluate(new Dictionary<string, DecisionValue>(), AsOf));

        error.ErrorCode.Should().Be("decisions.facts.missing");
        Assert.Throws<ArgumentNullException>(() => rule.Evaluate(null!, AsOf));
    }

    [Fact]
    public void TheMetaObjectIsACloneOfTheDocumentsMeta()
    {
        var json = TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json");
        var rule = BrainRuleLoader.Load(json, [BrainContract.Parse(TestFiles.Read("tests/fixtures/brain-spec/readiness.contract.json"))]).Rule!;

        JsonNode.DeepEquals(rule.Meta, JsonNode.Parse(json)!["meta"]).Should().BeTrue();
        rule.Meta["title"]!.GetValue<string>().Should().Be("Readiness notes");
        rule.Meta["reasons"]!["low-score"]!.GetValue<string>().Should().Be("The score is below 50.");
    }

    private static BrainRule LoadFirstCase(long version)
    {
        var fixture = TestFiles.ReadJson("tests/fixtures/spec/brain/conformance.json").AsArray()[0]!.AsObject();
        var document = fixture["document"]!.AsObject();
        document["version"] = version;
        var result = BrainRuleLoader.Load(document.ToJsonString(), [TestFiles.Contract("sales")]);
        result.IsValid.Should().BeTrue();
        return result.Rule!;
    }
}
