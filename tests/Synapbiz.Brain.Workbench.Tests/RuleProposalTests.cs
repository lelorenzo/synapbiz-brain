using System.Text.Json.Nodes;
using FluentAssertions;
using static Synapbiz.Brain.Workbench.Tests.DraftKit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleProposalTests
{
    private static readonly DateTimeOffset Moment = new(2026, 11, 23, 9, 30, 15, TimeSpan.FromHours(-5));

    private static RuleProposal Sample(string? draft = null) => new(
        "intake-routing",
        1,
        RuleReviewJson.ContentHash(FirstText),
        "Author One",
        Moment,
        "Billing goes to the desk: ¿revisar?",
        draft ?? Draft(FirstText, new SetOutcome("billing-topic", "queue", Nuvora.Decisions.Contracts.Tables.DecisionValue.Code("billing-desk"))));

    private static string Valid(Action<JsonObject>? change = null)
    {
        var document = Parse(RuleProposalJson.Serialize(Sample()));
        change?.Invoke(document);
        return document.ToJsonString();
    }

    [Fact]
    public void TheSpecNameIsPinned() => RuleProposalJson.Spec.Should().Be("brain.rule-proposal@1");

    [Fact]
    public void AProposalIsWrittenInTheDocumentedShapeWithTheDraftAsAnObject()
    {
        var text = RuleProposalJson.Serialize(Sample());
        var document = Parse(text);

        document.Select(member => member.Key).Should().Equal("$spec", "rule", "author", "createdAt", "note", "draft");
        document["$spec"]!.GetValue<string>().Should().Be("brain.rule-proposal@1");
        document["rule"]!.AsObject().Select(member => member.Key).Should().Equal("id", "baseVersion", "baseContentHash");
        document["rule"]!["id"]!.GetValue<string>().Should().Be("intake-routing");
        document["rule"]!["baseVersion"]!.GetValue<long>().Should().Be(1);
        document["rule"]!["baseContentHash"]!.GetValue<string>().Should().Be(RuleReviewJson.ContentHash(FirstText));
        document["createdAt"]!.GetValue<string>().Should().Be("2026-11-23T09:30:15-05:00");
        document["draft"].Should().BeOfType<JsonObject>();
        document["draft"]!["$spec"]!.GetValue<string>().Should().Be("brain.rule@1");
        text.Should().Contain("¿revisar?").And.Contain("\n  \"draft\": {\n    \"$spec\"").And.NotContain("\r");
    }

    [Fact]
    public void AProposalReadBackIsTheProposalWrittenWithTheDraftInTheDraftersFormatting()
    {
        var proposal = Sample();

        var parsed = RuleProposalJson.Parse(RuleProposalJson.Serialize(proposal));

        parsed.IsValid.Should().BeTrue();
        parsed.Errors.Should().BeEmpty();
        parsed.Proposal.Should().Be(proposal);
        parsed.Proposal!.DraftJson.Should().Be(proposal.DraftJson);
        RuleProposalJson.Serialize(parsed.Proposal).Should().Be(RuleProposalJson.Serialize(proposal));
    }

    [Fact]
    public void ADraftWrittenWithOtherFormattingIsReWrittenInTheDraftersFormatting()
    {
        var compact = Parse(Sample().DraftJson).ToJsonString();

        var parsed = RuleProposalJson.Parse(RuleProposalJson.Serialize(Sample(compact)));

        parsed.Proposal!.DraftJson.Should().Be(Sample().DraftJson);
    }

    [Fact]
    public void ADraftIsKeptAsAnObjectEvenWithUnknownMembersAndRawNumbers()
    {
        var draft = Parse(Sample().DraftJson);
        draft["extra"] = JsonNode.Parse("1.50");
        var proposal = Sample(draft.ToJsonString());

        var text = RuleProposalJson.Serialize(proposal);

        Parse(text)["draft"]!["extra"]!.ToJsonString().Should().Be("1.50");
        RuleProposalJson.Parse(text).Proposal!.DraftJson.Should().Contain("\"extra\": 1.50");
    }

    [Fact]
    public void ACreationTimeIsWrittenToWholeSecondsWithItsOffsetAndReadBackFromZ()
    {
        var utc = Sample() with { CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero) };

        var text = RuleProposalJson.Serialize(utc);

        Parse(text)["createdAt"]!.GetValue<string>().Should().Be("2026-01-02T03:04:05+00:00");
        var zulu = Valid(document => document["createdAt"] = "2026-01-02T03:04:05Z");
        RuleProposalJson.Parse(zulu).Proposal!.CreatedAt.Should().Be(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("{\"a\":1,\"a\":2}")]
    public void TextThatIsNotAJsonObjectIsNotJson(string text)
    {
        var result = RuleProposalJson.Parse(text);

        result.IsValid.Should().BeFalse();
        result.Proposal.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("workbench.proposal.notJson");
    }

    [Fact]
    public void AnotherFormatIsNotSupportedAndAMissingOrNotTextSpecIsAFieldError()
    {
        RuleProposalJson.Parse(Valid(document => document["$spec"] = "brain.rule-proposal@2")).Errors
            .Should().Equal(new WorkbenchError("workbench.proposal.specNotSupported", "/$spec", "The format 'brain.rule-proposal@2' is not 'brain.rule-proposal@1'."));
        RuleProposalJson.Parse(Valid(document => document.Remove("$spec"))).Errors.Single().Should().Be(new WorkbenchError("workbench.proposal.fieldMissing", "/$spec", "The member is missing."));
        RuleProposalJson.Parse(Valid(document => document["$spec"] = 1)).Errors.Single().Should().Be(new WorkbenchError("workbench.proposal.fieldNotValid", "/$spec", "Expected a string."));
    }

    public static TheoryData<string, string, string> FieldDefects() => new()
    {
        { "rule", "/rule", "workbench.proposal.fieldMissing" },
        { "rule.id", "/rule/id", "workbench.proposal.fieldMissing" },
        { "rule.baseVersion", "/rule/baseVersion", "workbench.proposal.fieldMissing" },
        { "rule.baseContentHash", "/rule/baseContentHash", "workbench.proposal.fieldMissing" },
        { "author", "/author", "workbench.proposal.fieldMissing" },
        { "createdAt", "/createdAt", "workbench.proposal.fieldMissing" },
        { "note", "/note", "workbench.proposal.fieldMissing" },
        { "draft", "/draft", "workbench.proposal.fieldMissing" },
    };

    [Theory]
    [MemberData(nameof(FieldDefects))]
    public void EveryMissingMemberIsReportedByItsPath(string member, string path, string code)
    {
        var text = Valid(document =>
        {
            var parts = member.Split('.');
            var owner = parts.Length == 2 ? document[parts[0]]!.AsObject() : document;
            owner.Remove(parts[^1]);
        });

        var result = RuleProposalJson.Parse(text);

        result.Proposal.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Should().Match<WorkbenchError>(error => error.Code == code && error.Path == path);
    }

    private static readonly string[] NotAText = ["7", "true", "[]", "{}", "null"];

    private static readonly string[] NotAnObject = ["7", "true", "[]", "\"text\"", "null"];

    public static TheoryData<string, string, string[]> WrongKinds() => new()
    {
        { "rule", "/rule", NotAnObject },
        { "rule.id", "/rule/id", NotAText },
        { "rule.baseVersion", "/rule/baseVersion", ["\"1\"", "true", "[]", "{}", "null", "1.5"] },
        { "rule.baseContentHash", "/rule/baseContentHash", NotAText },
        { "author", "/author", NotAText },
        { "createdAt", "/createdAt", NotAText },
        { "note", "/note", NotAText },
        { "draft", "/draft", NotAnObject },
    };

    [Theory]
    [MemberData(nameof(WrongKinds))]
    public void EveryMemberOfTheWrongKindOrNullIsNotValid(string member, string path, string[] wrongValues)
    {
        foreach (var wrong in wrongValues)
        {
            var text = Valid(document =>
            {
                var parts = member.Split('.');
                var owner = parts.Length == 2 ? document[parts[0]]!.AsObject() : document;
                owner[parts[^1]] = JsonNode.Parse(wrong);
            });

            var result = RuleProposalJson.Parse(text);

            result.Proposal.Should().BeNull();
            result.Errors.Should().ContainSingle($"{member} = {wrong}").Which.Should()
                .Match<WorkbenchError>(error => error.Code == "workbench.proposal.fieldNotValid" && error.Path == path);
        }
    }

    [Fact]
    public void ADraftThatIsAnEmptyObjectIsAcceptedAndANonObjectDraftIsNot()
    {
        RuleProposalJson.Parse(Valid(document => document["draft"] = new JsonObject())).Proposal!.DraftJson.Should().Be("{}");
        RuleProposalJson.Parse(Valid(document => document["draft"] = "text")).Errors.Single().Code.Should().Be("workbench.proposal.fieldNotValid");
    }

    [Fact]
    public void ADateThatIsNotInTheDocumentedFormsIsNotValid()
    {
        foreach (var text in new[] { "yesterday", "2026-11-23", "2026-11-23 09:30:15", "2026-11-23T09:30:15" })
        {
            var result = RuleProposalJson.Parse(Valid(document => document["createdAt"] = text));
            result.Errors.Should().ContainSingle().Which.Should().Match<WorkbenchError>(error => error.Code == "workbench.proposal.fieldNotValid" && error.Path == "/createdAt", text);
        }
    }

    [Fact]
    public void ABaseVersionThatIsNotAWholeNumberIsNotValid()
    {
        foreach (var version in new[] { "1.5", "\"1\"", "1e400" })
        {
            var result = RuleProposalJson.Parse(Valid(document => document["rule"]!["baseVersion"] = JsonNode.Parse(version)));
            result.Errors.Should().ContainSingle().Which.Path.Should().Be("/rule/baseVersion", version);
        }
    }

    [Fact]
    public void EveryDefectIsCollectedAndOrderedByPath()
    {
        var text = Valid(document =>
        {
            document.Remove("note");
            document["author"] = 4;
            document["rule"]!.AsObject().Remove("id");
            document["draft"] = 1;
        });

        var result = RuleProposalJson.Parse(text);

        result.Errors.Select(error => (error.Code, error.Path)).Should().Equal(
            ("workbench.proposal.fieldNotValid", "/author"),
            ("workbench.proposal.fieldNotValid", "/draft"),
            ("workbench.proposal.fieldMissing", "/note"),
            ("workbench.proposal.fieldMissing", "/rule/id"));
    }

    [Fact]
    public void UnknownMembersAreIgnored()
    {
        RuleProposalJson.Parse(Valid(document => document["extra"] = "x")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void SerializeRefusesADraftThatIsNotAJsonObject()
    {
        foreach (var draft in new[] { "not json", "[]", "5", "null", "{\"a\":1,\"a\":2}", "" })
        {
            var call = () => RuleProposalJson.Serialize(Sample(draft));
            call.Should().Throw<ArgumentException>(draft);
        }
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        new Action[]
        {
            () => RuleProposalJson.Serialize(null!),
            () => RuleProposalJson.Parse(null!),
            () => RuleProposalJson.Serialize(Sample(null) with { DraftJson = null! }),
        }.Should().AllSatisfy(call => call.Should().Throw<ArgumentException>());
    }
}
