using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Spec.Tests;

public sealed class LoaderStructureTests
{
    private const string Base = """
        {
          "$spec": "brain.rule@1", "id": "structure-base", "version": 1, "uses": [], "requires": ["synthetic@1"], "meta": {},
          "body": {
            "kind": "synthetic.code", "hitPolicy": "first",
            "rows": [ { "id": "row-a", "version": 1, "legacyKey": "A",
              "conditions": [ { "fact": "fact", "op": "equals", "value": { "type": "Code", "value": "A-1" } } ], "outcome": {} } ],
            "goldens": [ { "name": "matches", "facts": { "fact": { "type": "Code", "value": "A-1" } }, "expect": { "row": "row-a", "outcome": {} } } ]
          }
        }
        """;

    /// <summary>The documents the schema refuses: the entries that name one path in <c>errorAt</c>.</summary>
    public static IEnumerable<object[]> StructureCases() =>
        TestFiles.ReadJson("tests/fixtures/brain-spec/structure-documents.json").AsArray()
            .Select((item, index) => (item, index))
            .Where(entry => entry.item!["errorAt"] is not null)
            .Select(entry => new object[] { entry.index });

    [Fact]
    public void TheBaseDocumentLoads()
    {
        var result = Load(Base);

        result.IsValid.Should().BeTrue();
        result.Rule!.Id.Should().Be("structure-base");
        RunsClean(result.Rule);
    }

    [Fact]
    public void TheStructureFixtureHoldsEightSchemaRefusalsAtItsStart() =>
        StructureCases().Select(entry => (int)entry[0]).Should().Equal(Enumerable.Range(0, 8));

    [Theory]
    [MemberData(nameof(StructureCases))]
    public void EveryHandWrittenInvalidDocumentIsRefusedBySchema(int index)
    {
        var fixture = TestFiles.ReadJson("tests/fixtures/brain-spec/structure-documents.json").AsArray()[index]!;
        var expectedPath = fixture["errorAt"]!.GetValue<string>();

        var result = BrainRuleLoader.Load(fixture["document"]!.ToJsonString(), [TestFiles.ContractOf(fixture)]);

        result.Rule.Should().BeNull();
        result.DeferredToEngine.Should().BeEmpty();
        result.Errors.Should().NotBeEmpty();
        result.Errors.Select(error => error.Code).Distinct().Should().Equal("spec.structure.schema");
        result.Errors.Select(error => error.Path).Should().Contain(expectedPath, because: fixture["name"]!.GetValue<string>());
        result.Errors.Should().BeInAscendingOrder(error => error.Path, StringComparer.Ordinal);
    }

    [Fact]
    public void InvalidJsonIsReportedAtTheDocument()
    {
        var result = Load("{ not json");

        Single(result).Should().Be(("spec.json.invalid", ""));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "$spec": "brain.rule@1", "id": "bad id", "version": 1 }""")]
    [InlineData("""{ "$spec": "brain.rule", "id": "x" }""")]
    public void ADocumentWithoutAValidEnvelopeIsReportedAtTheDocument(string json)
    {
        Load(json).Errors.Should().Contain(error => error.Code == "spec.structure.envelope" && error.Path == "");
    }

    [Theory]
    [InlineData("brain.flow@1")]
    [InlineData("brain.rule@2")]
    [InlineData("other.rule@1")]
    public void AFormatThatIsNotARuleIsReportedAtTheSpecMember(string spec)
    {
        var document = Parse(Base);
        document["$spec"] = spec;

        Single(Load(document.ToJsonString())).Should().Be(("spec.structure.unknown_format", "/$spec"));
    }

    [Fact]
    public void ADecidedGoldenWithoutAnOutcomeIsReportedByTheStructureStep()
    {
        var document = Parse(Base);
        document["body"]!["goldens"]![0]!["expect"]!.AsObject().Remove("outcome");

        Single(Load(document.ToJsonString())).Should().Be(("brain.rule.golden_outcome_missing", "/body/goldens/0/expect/outcome"));
    }

    [Fact]
    public void TheStructureStepReportsTheSchemaAndTheGoldenCheckTogether()
    {
        var document = Parse(Base);
        document["body"]!["goldens"]![0]!["expect"]!.AsObject().Remove("outcome");
        document["extra"] = true;

        var errors = Load(document.ToJsonString()).Errors;

        errors.Select(error => error.Code).Should().Contain(["spec.structure.schema", "brain.rule.golden_outcome_missing"]);
    }

    [Fact]
    public void ASchemaErrorStopsTheLoaderBeforeTheContractsAreChecked()
    {
        var document = Parse(Base);
        document["extra"] = true;
        document["requires"] = new JsonArray("synthetic@9");

        Load(document.ToJsonString()).Errors.Should().OnlyContain(error => error.Code == "spec.structure.schema");
    }

    [Fact]
    public void ARequiredContractThatIsNotSuppliedIsReportedAtItsRequiresEntry()
    {
        var document = Parse(Base);
        document["requires"] = new JsonArray("synthetic@2");

        Single(Load(document.ToJsonString())).Should().Be(("brain.contract.unavailable", "/requires/0"));
    }

    [Fact]
    public void ARuleThatDoesNotRequireItsOwningModuleIsReported()
    {
        var document = Parse(Base);
        document["requires"] = new JsonArray();

        Single(Load(document.ToJsonString())).Should().Be(("brain.contract.module_required", "/requires"));
    }

    [Fact]
    public void ADuplicatedRowIdIsReportedAtTheSecondRow()
    {
        var document = Parse(Base);
        var rows = document["body"]!["rows"]!.AsArray();
        var second = rows[0]!.DeepClone();
        second["legacyKey"] = "B";
        rows.Add(second);

        Single(Load(document.ToJsonString())).Should().Be(("spec.semantics.duplicate_id", "/body/rows/1/id"));
    }

    [Fact]
    public void ADuplicatedLegacyKeyIsReportedAtTheSecondRow()
    {
        var document = Parse(Base);
        var rows = document["body"]!["rows"]!.AsArray();
        var second = rows[0]!.DeepClone();
        second["id"] = "row-b";
        rows.Add(second);

        Single(Load(document.ToJsonString())).Should().Be(("decisions.validation.duplicate_row_key", "/body/rows/1/legacyKey"));
    }

    [Fact]
    public void AnObjectWithAnIdButNoVersionIsANodeError()
    {
        var document = Parse(Base);
        document["meta"] = new JsonObject { ["id"] = "inner" };

        Single(Load(document.ToJsonString())).Should().Be(("spec.semantics.node", "/meta"));
    }

    [Fact]
    public void ABlankTranslationKeyIsReported()
    {
        var document = Parse(Base);
        document["meta"] = new JsonObject { ["title"] = new JsonObject { ["$t"] = " " } };

        Single(Load(document.ToJsonString())).Should().Be(("spec.semantics.translation", "/meta/title/$t"));
    }

    [Fact]
    public void ReservedMetadataMembersAreNotWalked()
    {
        var document = Parse(Base);
        document["$v0"] = new JsonObject { ["id"] = "ignored", ["$ref"] = "nonsense" };
        document["$scope"] = new JsonObject { ["version"] = "ignored" };

        Load(document.ToJsonString()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AUsesReferenceResolvesAgainstTheKnownSectionAddresses()
    {
        var document = Parse(Base);
        document["uses"] = new JsonArray(new JsonObject { ["$ref"] = "brain.rule/shared-rule" });
        var json = document.ToJsonString();
        var contracts = new[] { TestFiles.Contract("synthetic") };

        BrainRuleLoader.Load(json, contracts, ["brain.rule/shared-rule"]).IsValid.Should().BeTrue();
        Single(BrainRuleLoader.Load(json, contracts, ["brain.rule/other"])).Should().Be(("spec.semantics.reference", "/uses/0/$ref"));
        Single(BrainRuleLoader.Load(json, contracts)).Should().Be(("spec.semantics.reference", "/uses/0/$ref"));
        Single(BrainRuleLoader.Load(json, contracts, [])).Should().Be(("spec.semantics.reference", "/uses/0/$ref"));
    }

    [Theory]
    [InlineData("brain.rule/structure-base", "spec.semantics.cycle")]
    [InlineData("not-an-address", "spec.semantics.reference")]
    public void AUsesReferenceToTheDocumentItselfIsACycleAndAMalformedAddressIsUnresolved(string target, string code)
    {
        var document = Parse(Base);
        document["uses"] = new JsonArray(new JsonObject { ["$ref"] = target });

        var result = BrainRuleLoader.Load(document.ToJsonString(), [TestFiles.Contract("synthetic")], [target]);

        Single(result).Should().Be((code, "/uses/0/$ref"));
    }

    [Fact]
    public void LoadingRefusesNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => BrainRuleLoader.Load(null!, []));
        Assert.Throws<ArgumentNullException>(() => BrainRuleLoader.Load(Base, null!));
    }

    [Fact]
    public void ADuplicateContractAddressTakesTheLastContractSupplied()
    {
        var first = BrainContract.Parse("""{ "module": "synthetic", "schemaVersion": 1 }""");
        var result = BrainRuleLoader.Load(Base, [first, TestFiles.Contract("synthetic")]);

        result.IsValid.Should().BeTrue();
    }

    private static BrainRuleLoadResult Load(string json) => BrainRuleLoader.Load(json, [TestFiles.Contract("synthetic")]);

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static (string Code, string Path) Single(BrainRuleLoadResult result)
    {
        var error = result.Errors.Should().ContainSingle().Subject;
        return (error.Code, error.Path);
    }

    private static void RunsClean(BrainRule rule)
    {
        var run = rule.RunGoldens();
        run.Results.Should().ContainSingle().Which.Passed.Should().BeTrue();
    }
}
