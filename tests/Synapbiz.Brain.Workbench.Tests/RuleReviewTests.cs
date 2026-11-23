using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleReviewTests
{
    private static string HexOf(string text) =>
        string.Concat(SHA256.HashData(Encoding.UTF8.GetBytes(text)).Select(octet => octet.ToString("x2", CultureInfo.InvariantCulture)));

    [Fact]
    public void TheContentHashIsSha256OfTheUtf8TextExactlyAsGiven()
    {
        const string text = "{\"id\": \"rule\", \"note\": \"ñ — ü\"}\r\n";
        var expected = "sha256:" + HexOf(text);

        RuleReviewJson.ContentHash(text).Should().Be(expected);
        RuleReviewJson.ContentHash(text + " ").Should().NotBe(expected, "a single added space is a different text");
    }

    [Fact]
    public void TheContentHashOfAKnownTextIsTheKnownDigest()
    {
        RuleReviewJson.ContentHash("abc").Should().Be("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        RuleReviewJson.ContentHash(string.Empty).Should().Be("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void TheFormatNameAndErrorCodesAreStable()
    {
        RuleReviewJson.Format.Should().Be("brain.rule-review@1");
        RuleReviewJson.ErrorJsonInvalid.Should().Be("workbench.review.json_invalid");
        RuleReviewJson.ErrorUnknownFormat.Should().Be("workbench.review.unknown_format");
        RuleReviewJson.ErrorMalformed.Should().Be("workbench.review.malformed");
    }

    [Fact]
    public void ASerialisedReviewIsExactlyTheExpectedDocument()
    {
        var hash = HexOf("{\"id\":\"readiness-notes\"}");
        var expected = $$"""
{
  "$spec": "brain.rule-review@1",
  "rule": {
    "id": "readiness-notes",
    "version": 3,
    "contentHash": "sha256:{{hash}}"
  },
  "reviewer": "Reviewer One",
  "createdAt": "2026-11-22T21:00:00+00:00",
  "items": [
    {
      "target": "rule",
      "state": "unreviewed",
      "note": ""
    },
    {
      "target": "row:low-score",
      "state": "needsFix",
      "note": "La puntuación límite debería ser 50 — ¿revisar?"
    },
    {
      "target": "golden:A low score at intake",
      "state": "confirmed",
      "note": "Looks right, \"quoted\" and <tagged> & more."
    },
    {
      "target": "row:late-stage",
      "state": "question",
      "note": "Why does Review count as late?\nSecond line."
    }
  ],
  "proposedCases": [
    {
      "name": "Every type of fact",
      "facts": {
        "code": {
          "type": "Code",
          "value": "ab-1"
        },
        "day": {
          "type": "Date",
          "value": "2026-11-22"
        },
        "rate": {
          "type": "Decimal",
          "value": "12.50"
        },
        "score": {
          "type": "Integer",
          "value": "-20"
        },
        "stage": {
          "type": "Enum",
          "value": "Review"
        },
        "total": {
          "type": "Money",
          "value": {
            "amount": "99.9",
            "currency": "EUR"
          }
        },
        "urgent": {
          "type": "Boolean",
          "value": false
        },
        "zeta": {
          "type": "Reference",
          "value": {
            "guid": "3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6b"
          }
        }
      },
      "expect": {
        "row": "low-score",
        "outcome": {
          "note": "low-score",
          "órden": "¿sí?"
        }
      }
    },
    {
      "name": "Nothing matches",
      "facts": {},
      "expect": {
        "noDecision": true
      }
    },
    {
      "name": "A collect case without a pinned row",
      "facts": {
        "flag": {
          "type": "Boolean",
          "value": true
        }
      },
      "expect": {
        "outcome": [
          {
            "note": "a"
          },
          {
            "note": "b"
          }
        ]
      }
    }
  ]
}
""";

        RuleReviewJson.Serialize(ReviewSamples.Full()).Should().Be(expected.ReplaceLineEndings("\n"));
    }

    // ---- round trip ----------------------------------------------------------------------------

    [Fact]
    public void ASerialisedReviewIsReadBackMemberByMember()
    {
        var review = ReviewSamples.Full();

        var result = RuleReviewJson.Parse(RuleReviewJson.Serialize(review));

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Path)));
        var read = result.Review!;
        read.RuleId.Should().Be(review.RuleId);
        read.RuleVersion.Should().Be(review.RuleVersion);
        read.RuleContentHash.Should().Be(review.RuleContentHash);
        read.Reviewer.Should().Be(review.Reviewer);
        read.CreatedAt.Should().Be(review.CreatedAt);
        read.CreatedAt.Offset.Should().Be(review.CreatedAt.Offset);
        read.Items.Should().Equal(review.Items);
        read.ProposedCases.Should().HaveCount(review.ProposedCases.Count);
        foreach (var (actual, expected) in read.ProposedCases.Zip(review.ProposedCases))
        {
            actual.Name.Should().Be(expected.Name);
            actual.Facts.Should().Equal(expected.Facts);
            actual.ExpectNoDecision.Should().Be(expected.ExpectNoDecision);
            actual.ExpectedOutcomeJson.Should().Be(expected.ExpectedOutcomeJson);
            actual.ExpectedRowId.Should().Be(expected.ExpectedRowId);
        }
    }

    [Fact]
    public void ReadingTheDocumentAgainAfterWritingItGivesTheSameText()
    {
        var text = RuleReviewJson.Serialize(ReviewSamples.Full());

        RuleReviewJson.Serialize(RuleReviewJson.Parse(text).Review!).Should().Be(text);
    }

    [Fact]
    public void AnOutcomeIsReadBackInItsCompactFormWhateverTheInputSpacing()
    {
        var review = ReviewSamples.Full() with
        {
            ProposedCases = [new ProposedCase("Spaced", new Dictionary<string, DecisionValue>(), false, "{ \"note\" :  \"x\" ,\n \"n\": 1 }", null)],
        };

        var read = RuleReviewJson.Parse(RuleReviewJson.Serialize(review)).Review!;

        read.ProposedCases[0].ExpectedOutcomeJson.Should().Be("""{"note":"x","n":1}""");
    }

    [Fact]
    public void AnOffsetIsKeptAndSecondsAreTheFinestUnit()
    {
        var moment = new DateTimeOffset(2026, 3, 4, 5, 6, 7, 999, TimeSpan.FromHours(-5));
        var review = ReviewSamples.Full() with { CreatedAt = moment };

        var text = RuleReviewJson.Serialize(review);
        var read = RuleReviewJson.Parse(text).Review!;

        text.Should().Contain("\"createdAt\": \"2026-03-04T05:06:07-05:00\"");
        read.CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(-5)));
        read.CreatedAt.Offset.Should().Be(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void AReviewWithNoItemsAndNoCasesIsValid()
    {
        var review = ReviewSamples.Full() with { Items = [], ProposedCases = [] };

        var read = RuleReviewJson.Parse(RuleReviewJson.Serialize(review));

        read.IsValid.Should().BeTrue();
        read.Review!.Items.Should().BeEmpty();
        read.Review.ProposedCases.Should().BeEmpty();
    }

    [Fact]
    public void ADecisionWithoutAnOutcomeIsWrittenWithoutOneAndThenReadAsMalformed()
    {
        var review = ReviewSamples.Full() with
        {
            ProposedCases = [new ProposedCase("Incomplete", new Dictionary<string, DecisionValue>(), false, null, "low-score")],
        };

        var text = RuleReviewJson.Serialize(review);
        var result = RuleReviewJson.Parse(text);

        text.Should().NotContain("outcome");
        result.Errors.Select(error => (error.Code, error.Path)).Should().Equal((RuleReviewJson.ErrorMalformed, "/proposedCases/0/expect/outcome"));
    }

    [Fact]
    public void AnUnknownReviewStateIsRefusedWhenWriting()
    {
        var review = ReviewSamples.Full() with { Items = [new ReviewItem("rule", (ReviewState)9, string.Empty)] };

        var act = () => RuleReviewJson.Serialize(review);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AValueOfAnUnknownCaseIsRefusedWhenWriting()
    {
        var facts = new Dictionary<string, DecisionValue> { ["x"] = new Unknown() };
        var review = ReviewSamples.Full() with { ProposedCases = [new ProposedCase("Odd", facts, true, null, null)] };

        var act = () => RuleReviewJson.Serialize(review);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InvalidOutcomeTextIsAJsonExceptionWhenWriting()
    {
        var review = ReviewSamples.Full() with
        {
            ProposedCases = [new ProposedCase("Bad", new Dictionary<string, DecisionValue>(), false, "{not json", null)],
        };

        var act = () => RuleReviewJson.Serialize(review);

        act.Should().Throw<System.Text.Json.JsonException>();
    }

    private sealed record Unknown : DecisionValue;

    // ---- reading errors ------------------------------------------------------------------------

    private static JsonObject Valid() => (JsonObject)JsonNode.Parse(RuleReviewJson.Serialize(ReviewSamples.Full()))!;

    private static List<(string Code, string Path)> ErrorsOf(JsonNode document) => ErrorsOfText(document.ToJsonString());

    private static List<(string Code, string Path)> ErrorsOfText(string text)
    {
        var result = RuleReviewJson.Parse(text);
        result.IsValid.Should().Be(result.Errors.Count == 0);
        (result.Review is null).Should().Be(result.Errors.Count > 0);
        result.Errors.All(error => error.Message.Length > 0).Should().BeTrue();
        return [.. result.Errors.Select(error => (error.Code, error.Path))];
    }

    private static void Malformed(JsonNode document, params string[] paths) =>
        ErrorsOf(document).Should().Equal(paths.Select(path => (RuleReviewJson.ErrorMalformed, path)));

    [Fact]
    public void TheSerialisedSampleIsAValidDocument() => ErrorsOf(Valid()).Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"a\": ")]
    [InlineData("{} trailing")]
    public void TextThatIsNotJsonIsOneJsonInvalidError(string text) =>
        ErrorsOfText(text).Should().Equal((RuleReviewJson.ErrorJsonInvalid, string.Empty));

    [Theory]
    [InlineData("[]")]
    [InlineData("3")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    public void ADocumentThatIsNotAnObjectIsMalformedAtTheRoot(string text) =>
        ErrorsOfText(text).Should().Equal((RuleReviewJson.ErrorMalformed, string.Empty));

    [Fact]
    public void AnotherFormatIsAnUnknownFormatErrorAtTheSpecMember()
    {
        var document = Valid();
        document["$spec"] = "brain.rule@1";
        document.Remove("reviewer");

        ErrorsOf(document).Should().Equal((RuleReviewJson.ErrorUnknownFormat, "/$spec"));
    }

    [Fact]
    public void AMissingOrNonStringSpecIsMalformed()
    {
        var missing = Valid();
        missing.Remove("$spec");
        Malformed(missing, "/$spec");

        var number = Valid();
        number["$spec"] = 1;
        Malformed(number, "/$spec");
    }

    [Theory]
    [InlineData("rule", "/rule")]
    [InlineData("reviewer", "/reviewer")]
    [InlineData("createdAt", "/createdAt")]
    [InlineData("items", "/items")]
    [InlineData("proposedCases", "/proposedCases")]
    public void AMissingTopLevelMemberIsMalformedAtItsPath(string member, string path)
    {
        var document = Valid();
        document.Remove(member);

        Malformed(document, path);
    }

    [Fact]
    public void AMemberOfTheWrongKindIsMalformedAtItsPath()
    {
        var document = Valid();
        document["rule"] = "text";
        document["reviewer"] = 3;
        document["createdAt"] = 4;
        document["items"] = new JsonObject();
        document["proposedCases"] = "none";

        Malformed(document, "/createdAt", "/items", "/proposedCases", "/reviewer", "/rule");
    }

    [Fact]
    public void ABadRuleBlockReportsEachMemberAtItsOwnPath()
    {
        var document = Valid();
        document["rule"] = new JsonObject { ["id"] = 5, ["version"] = "3" };

        Malformed(document, "/rule/contentHash", "/rule/id", "/rule/version");
    }

    [Theory]
    [InlineData("3.5")]
    [InlineData("\"3\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void AVersionThatIsNotAWholeNumberIsMalformed(string version)
    {
        var document = Valid();
        document["rule"]!["version"] = JsonNode.Parse(version);

        Malformed(document, "/rule/version");
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-11-22")]
    [InlineData("2026-11-22T21:00:00")]
    [InlineData("22/11/2026 21:00:00 +00:00")]
    public void ADateTimeThatIsNotRoundTripFormatIsMalformed(string text)
    {
        var document = Valid();
        document["createdAt"] = text;

        Malformed(document, "/createdAt");
    }

    [Theory]
    [InlineData("2026-11-22T21:00:00Z")]
    [InlineData("2026-11-22T21:00:00+02:00")]
    public void AZuluOrOffsetDateTimeIsAccepted(string text)
    {
        var document = Valid();
        document["createdAt"] = text;

        ErrorsOf(document).Should().BeEmpty();
    }

    [Fact]
    public void ABadItemReportsEachDefectAtItsPath()
    {
        var document = Valid();
        var items = (JsonArray)document["items"]!;
        items[0] = "text";
        items[1] = new JsonObject { ["target"] = "row:x", ["state"] = "maybe", ["note"] = "n" };
        items[2] = new JsonObject { ["target"] = 1, ["state"] = 2 };

        Malformed(document, "/items/0", "/items/1/state", "/items/2/note", "/items/2/state", "/items/2/target");
    }

    [Theory]
    [InlineData("unreviewed", ReviewState.Unreviewed)]
    [InlineData("confirmed", ReviewState.Confirmed)]
    [InlineData("needsFix", ReviewState.NeedsFix)]
    [InlineData("question", ReviewState.Question)]
    public void EveryStateNameIsRead(string name, ReviewState expected)
    {
        var document = Valid();
        document["items"]![0]!["state"] = name;

        RuleReviewJson.Parse(document.ToJsonString()).Review!.Items[0].State.Should().Be(expected);
    }

    [Fact]
    public void AStateNameIsCaseSensitive()
    {
        var document = Valid();
        document["items"]![0]!["state"] = "NeedsFix";

        Malformed(document, "/items/0/state");
    }

    [Fact]
    public void ABadProposedCaseReportsEachDefectAtItsPath()
    {
        var document = Valid();
        var cases = (JsonArray)document["proposedCases"]!;
        cases[0] = 7;
        cases[1] = new JsonObject { ["name"] = 1, ["facts"] = "none", ["expect"] = "none" };
        cases[2] = new JsonObject();

        Malformed(
            document,
            "/proposedCases/0",
            "/proposedCases/1/expect",
            "/proposedCases/1/facts",
            "/proposedCases/1/name",
            "/proposedCases/2/expect",
            "/proposedCases/2/facts",
            "/proposedCases/2/name");
    }

    private static JsonObject WithFact(JsonNode fact)
    {
        var document = Valid();
        document["proposedCases"]![0]!["facts"] = new JsonObject { ["fact"] = fact };
        return document;
    }

    public static TheoryData<string, string> BadFacts => new()
    {
        { "3", "/proposedCases/0/facts/fact" },
        { "\"Code\"", "/proposedCases/0/facts/fact" },
        { "{}", "/proposedCases/0/facts/fact/type" },
        { """{"type":"Text","value":"x"}""", "/proposedCases/0/facts/fact/type" },
        { """{"type":"code","value":"x"}""", "/proposedCases/0/facts/fact/type" },
        { """{"type":5,"value":"x"}""", "/proposedCases/0/facts/fact/type" },
        { """{"type":"Code","value":5}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Code"}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Enum","value":true}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Boolean","value":"true"}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Boolean","value":null}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Integer","value":20}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Integer","value":"2.5"}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Integer","value":"x"}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Decimal","value":1.5}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Decimal","value":"1,5"}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Date","value":"2026-13-40"}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Date","value":"22/11/2026"}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Money","value":"5 USD"}""", "/proposedCases/0/facts/fact/value" },
        { """{"type":"Money","value":{"amount":"5"}}""", "/proposedCases/0/facts/fact/value/currency" },
        { """{"type":"Money","value":{"amount":"5","currency":"usd"}}""", "/proposedCases/0/facts/fact/value/currency" },
        { """{"type":"Money","value":{"amount":"5","currency":"USDX"}}""", "/proposedCases/0/facts/fact/value/currency" },
        { """{"type":"Money","value":{"amount":5,"currency":"USD"}}""", "/proposedCases/0/facts/fact/value/amount" },
        { """{"type":"Money","value":{"amount":"x","currency":"USD"}}""", "/proposedCases/0/facts/fact/value/amount" },
        { """{"type":"Reference","value":"3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6b"}""", "/proposedCases/0/facts/fact/value/guid" },
        { """{"type":"Reference","value":{}}""", "/proposedCases/0/facts/fact/value/guid" },
        { """{"type":"Reference","value":{"guid":"not-a-guid"}}""", "/proposedCases/0/facts/fact/value/guid" },
        { """{"type":"Reference","value":{"guid":"3f2b8c1e5a4d4e6f9b7a1c2d3e4f5a6b"}}""", "/proposedCases/0/facts/fact/value/guid" },
    };

    [Theory]
    [MemberData(nameof(BadFacts))]
    public void AnUnreadableTypedValueIsMalformedAtTheOffendingValue(string fact, string path) =>
        Malformed(WithFact(JsonNode.Parse(fact)!), path);

    [Fact]
    public void BothMoneyDefectsAreReported() =>
        Malformed(WithFact(JsonNode.Parse("""{"type":"Money","value":{"amount":"x","currency":"u"}}""")!),
            "/proposedCases/0/facts/fact/value/amount", "/proposedCases/0/facts/fact/value/currency");

    [Fact]
    public void AFactNameWithASlashOrATildeIsEscapedInThePointer()
    {
        var document = Valid();
        document["proposedCases"]![0]!["facts"] = new JsonObject { ["a/b~c"] = new JsonObject { ["type"] = "Nope", ["value"] = "x" } };

        Malformed(document, "/proposedCases/0/facts/a~1b~0c/type");
    }

    [Fact]
    public void AFactSetWithOneBadValueDropsTheCaseButReportsOnlyThatValue()
    {
        var document = Valid();
        document["proposedCases"]![0]!["facts"] = new JsonObject
        {
            ["good"] = new JsonObject { ["type"] = "Code", ["value"] = "x" },
            ["bad"] = new JsonObject { ["type"] = "Integer", ["value"] = "x" },
        };

        Malformed(document, "/proposedCases/0/facts/bad/value");
    }

    [Fact]
    public void ADecidedExpectationNeedsAnOutcomeThatIsAnObjectOrAnArray()
    {
        var document = Valid();
        var cases = (JsonArray)document["proposedCases"]!;
        cases[0]!["expect"] = new JsonObject { ["row"] = "low-score" };
        cases[1]!["expect"] = new JsonObject { ["outcome"] = "text" };
        cases[2]!["expect"] = new JsonObject { ["outcome"] = 3 };

        Malformed(document, "/proposedCases/0/expect/outcome", "/proposedCases/1/expect/outcome", "/proposedCases/2/expect/outcome");
    }

    [Fact]
    public void ARowThatIsNotAStringIsMalformedAndOneThatIsAbsentIsFine()
    {
        var document = Valid();
        var cases = (JsonArray)document["proposedCases"]!;
        cases[0]!["expect"] = new JsonObject { ["row"] = 5, ["outcome"] = new JsonObject() };
        cases[2]!["expect"] = new JsonObject { ["outcome"] = new JsonObject() };

        Malformed(document, "/proposedCases/0/expect/row");
    }

    [Theory]
    [InlineData("false")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void NoDecisionMustBeTheJsonLiteralTrue(string flag)
    {
        var document = Valid();
        document["proposedCases"]![1]!["expect"] = new JsonObject { ["noDecision"] = JsonNode.Parse(flag) };

        Malformed(document, "/proposedCases/1/expect/noDecision");
    }

    [Fact]
    public void ANoDecisionExpectationIgnoresTheOtherExpectMembers()
    {
        var document = Valid();
        document["proposedCases"]![0]!["expect"] = new JsonObject { ["noDecision"] = true, ["row"] = 5, ["outcome"] = 3 };

        var read = RuleReviewJson.Parse(document.ToJsonString()).Review!;

        read.ProposedCases[0].ExpectNoDecision.Should().BeTrue();
        read.ProposedCases[0].ExpectedOutcomeJson.Should().BeNull();
        read.ProposedCases[0].ExpectedRowId.Should().BeNull();
    }

    [Fact]
    public void EveryErrorIsCollectedAndTheListIsOrderedByPath()
    {
        var document = Valid();
        document["reviewer"] = 1;
        document["rule"]!["id"] = 2;
        document["items"]![3]!["state"] = "x";
        document["proposedCases"]![2]!["name"] = 3;
        document["proposedCases"]![0]!["facts"]!["code"]!["value"] = 4;

        var result = RuleReviewJson.Parse(document.ToJsonString());

        result.Review.Should().BeNull();
        result.Errors.Select(error => error.Path).Should().Equal(
            "/items/3/state", "/proposedCases/0/facts/code/value", "/proposedCases/2/name", "/reviewer", "/rule/id");
        result.Errors.Select(error => error.Path).Should().BeInAscendingOrder(StringComparer.Ordinal);
        result.Errors.Should().OnlyContain(error => error.Code == RuleReviewJson.ErrorMalformed);
    }

    [Fact]
    public void UnknownMembersAreIgnored()
    {
        var document = Valid();
        document["extra"] = 1;
        document["rule"]!["more"] = "x";
        document["items"]![0]!["colour"] = "red";
        document["proposedCases"]![0]!["expect"]!["why"] = "because";

        ErrorsOf(document).Should().BeEmpty();
    }

    [Fact]
    public void ADocumentThatRepeatsAMemberNameIsAJsonInvalidErrorNotAnException()
    {
        var text = RuleReviewJson.Serialize(ReviewSamples.Full()).Replace("\"reviewer\": \"Reviewer One\",", "\"reviewer\": \"Reviewer One\", \"reviewer\": \"Reviewer Two\",", StringComparison.Ordinal);

        ErrorsOfText(text).Should().Equal((RuleReviewJson.ErrorJsonInvalid, string.Empty));
    }
}
