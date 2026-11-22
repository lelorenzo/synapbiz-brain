using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;

namespace Synapbiz.Brain.Spec.Tests;

public sealed class BrainContractTests
{
    [Theory]
    [InlineData("accounting", "accounting@1", 1, 5)]
    [InlineData("inventory", "inventory@1", 0, 0)]
    [InlineData("procurement", "procurement@1", 1, 2)]
    [InlineData("sales", "sales@1", 1, 9)]
    [InlineData("synthetic", "synthetic@1", 8, 8)]
    public void TheSharedContractFixturesParse(string name, string address, int kindCount, int factCount)
    {
        var contract = TestFiles.Contract(name);

        contract.Module.Should().Be(name);
        contract.Address.Should().Be(address);
        contract.Kinds.Should().HaveCount(kindCount);
        contract.Kinds.Sum(kind => kind.Facts.Count).Should().Be(factCount);
    }

    [Fact]
    public void ADescriptorCarriesTheKindFactsOutcomesModeAndPermissions()
    {
        var contract = TestFiles.Contract("sales");

        contract.TryGetKind("sales.order_approval_routing", out var kind).Should().BeTrue();
        kind!.Key.Value.Should().Be("sales.order_approval_routing");
        kind.SchemaVersion.Should().Be(2);
        kind.EvaluationMode.Should().Be(EvaluationMode.OnDemand);
        kind.ReadPermission.Should().Be("sales.decisions.read");
        kind.AuthorPermission.Should().Be("sales.decisions.author");
        kind.ApprovePermission.Should().Be("sales.decisions.approve");
        kind.ReadTracePermission.Should().Be("sales.decisions.read_trace");
        kind.Facts.Select(fact => fact.Name).Should().Equal(
            "branch", "channelKind", "creditAction", "currencyCode", "customerSegment", "discountPctMaxLine", "discountPctOrder", "manualPriceLines", "orderTotal");
        kind.OutcomeFields.Should().ContainSingle().Which.Should().Be(new OutcomeFieldDescriptor("approvalState", FactType.Code, Nullable: true));
        kind.RequiresTotalCoverage.Should().BeFalse();
        kind.ContainsPersonalData.Should().BeNull();
        kind.MaxFactsBytes.Should().Be(DecisionKindDescriptor.DefaultMaxFactsBytes);
    }

    [Fact]
    public void AnEmptyAllowedValuesArrayMapsToNullAndALabelSourceIsKept()
    {
        var accounting = TestFiles.Contract("accounting");
        accounting.TryGetKind("accounting.channel_settlement", out var settlement).Should().BeTrue();
        var channel = settlement!.Facts.Single(fact => fact.Name == "ChannelKind");
        channel.AllowedValues.Should().Equal("B2B", "Marketplace", "Pos", "Web");
        var channelId = settlement.Facts.Single(fact => fact.Name == "SalesChannelId");
        channelId.AllowedValues.Should().BeNull();
        channelId.LabelSource.Should().Be(new ReferenceLabelSource("sales", "sales.sales_channel"));
        settlement.EvaluationMode.Should().Be(EvaluationMode.Materialized);

        var synthetic = TestFiles.Contract("synthetic");
        synthetic.TryGetKind("synthetic.code", out var code).Should().BeTrue();
        code!.Facts.Single().AllowedValues.Should().BeNull();
        synthetic.TryGetKind("synthetic.enum", out var enumKind).Should().BeTrue();
        enumKind!.Facts.Single().AllowedValues.Should().Equal("A", "B");
    }

    [Fact]
    public void AnUnknownKindIsNotFound()
    {
        TestFiles.Contract("sales").TryGetKind("sales.unknown", out var kind).Should().BeFalse();
        kind.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "facts": [], "kinds": [] }""", "$.module")]
    [InlineData("""{ "module": "sales", "facts": [], "kinds": [] }""", "$.schemaVersion")]
    [InlineData("""{ "module": "sales", "schemaVersion": 0, "facts": [], "kinds": [] }""", "$.schemaVersion")]
    [InlineData("""[]""", "$")]
    [InlineData("""{ "module": "sales", "schemaVersion": 1, "facts": {}, "kinds": [] }""", "$.facts")]
    [InlineData("not json", "$")]
    public void AMalformedContractRootIsRefusedNamingThePath(string json, string path)
    {
        var error = Assert.Throws<DecisionEngineException>(() => BrainContract.Parse(json));

        error.ErrorCode.Should().Be("brain.contract.malformed");
        error.Message.Should().Contain(path);
        error.Args!["path"].Should().Be(path);
    }

    [Fact]
    public void AFactOfAnUnknownTypeIsRefused()
    {
        const string json = """
            {
              "module": "sales", "schemaVersion": 1,
              "facts": [ { "kind": "sales.rule", "name": "total", "type": "Duration" } ],
              "kinds": [ { "id": "sales.rule", "schemaVersion": 1, "evaluationMode": "OnDemand",
                "permissions": { "read": "r", "author": "a", "approve": "p", "readTrace": "t" }, "outcomes": [] } ]
            }
            """;

        var error = Assert.Throws<DecisionEngineException>(() => BrainContract.Parse(json));

        error.ErrorCode.Should().Be("brain.contract.malformed");
        error.Message.Should().Contain("$.facts[0].type");
    }

    [Fact]
    public void AFactThatPointsToAnUndeclaredKindIsRefused()
    {
        const string json = """
            {
              "module": "sales", "schemaVersion": 1,
              "facts": [ { "kind": "sales.other", "name": "total", "type": "Code" } ],
              "kinds": [ { "id": "sales.rule", "schemaVersion": 1, "evaluationMode": "OnDemand",
                "permissions": { "read": "r", "author": "a", "approve": "p", "readTrace": "t" }, "outcomes": [] } ]
            }
            """;

        var error = Assert.Throws<DecisionEngineException>(() => BrainContract.Parse(json));

        error.ErrorCode.Should().Be("brain.contract.malformed");
        error.Message.Should().Contain("$.facts[0].kind");
    }

    [Theory]
    [InlineData("""{ "id": "Sales Rule", "schemaVersion": 1, "evaluationMode": "OnDemand", "permissions": { "read": "r", "author": "a", "approve": "p", "readTrace": "t" }, "outcomes": [] }""", "$.kinds[0].id")]
    [InlineData("""{ "id": "sales.rule", "schemaVersion": 1, "evaluationMode": "Sometimes", "permissions": { "read": "r", "author": "a", "approve": "p", "readTrace": "t" }, "outcomes": [] }""", "$.kinds[0].evaluationMode")]
    [InlineData("""{ "id": "sales.rule", "schemaVersion": 1, "evaluationMode": "OnDemand", "permissions": { "read": "r", "author": "a", "approve": "p" }, "outcomes": [] }""", "$.kinds[0].permissions.readTrace")]
    [InlineData("""{ "id": "sales.rule", "schemaVersion": 1, "evaluationMode": "OnDemand", "outcomes": [] }""", "$.kinds[0].permissions")]
    [InlineData("""{ "id": "sales.rule", "schemaVersion": 1, "evaluationMode": "OnDemand", "permissions": { "read": "r", "author": "a", "approve": "p", "readTrace": "t" }, "outcomes": [ { "name": "x", "type": "Json" } ] }""", "$.kinds[0].outcomes[0].type")]
    public void AMalformedKindIsRefusedNamingThePath(string kind, string path)
    {
        var json = $$"""{ "module": "sales", "schemaVersion": 1, "facts": [], "kinds": [ {{kind}} ] }""";

        var error = Assert.Throws<DecisionEngineException>(() => BrainContract.Parse(json));

        error.ErrorCode.Should().Be("brain.contract.malformed");
        error.Message.Should().Contain(path);
    }

    [Fact]
    public void ADuplicateKindOrFactIsRefused()
    {
        const string kind = """{ "id": "sales.rule", "schemaVersion": 1, "evaluationMode": "OnDemand", "permissions": { "read": "r", "author": "a", "approve": "p", "readTrace": "t" }, "outcomes": [] }""";
        const string fact = """{ "kind": "sales.rule", "name": "total", "type": "Code" }""";

        Assert.Throws<DecisionEngineException>(() => BrainContract.Parse($$"""{ "module": "sales", "schemaVersion": 1, "facts": [], "kinds": [ {{kind}}, {{kind}} ] }"""))
            .Message.Should().Contain("$.kinds[1].id");
        Assert.Throws<DecisionEngineException>(() => BrainContract.Parse($$"""{ "module": "sales", "schemaVersion": 1, "facts": [ {{fact}}, {{fact}} ], "kinds": [ {{kind}} ] }"""))
            .Message.Should().Contain("$.facts[1].name");
    }

    [Fact]
    public void MalformedFactMembersAreRefused()
    {
        const string kind = """{ "id": "sales.rule", "schemaVersion": 1, "evaluationMode": "OnDemand", "permissions": { "read": "r", "author": "a", "approve": "p", "readTrace": "t" }, "outcomes": [] }""";

        foreach (var (fact, path) in new[]
        {
            ("""{ "kind": "sales.rule", "name": "total", "type": "Code", "nullable": "yes" }""", "$.facts[0].nullable"),
            ("""{ "kind": "sales.rule", "name": "total", "type": "Enum", "allowedValues": "A" }""", "$.facts[0].allowedValues"),
            ("""{ "kind": "sales.rule", "name": "total", "type": "Enum", "allowedValues": [1] }""", "$.facts[0].allowedValues[0]"),
            ("""{ "kind": "sales.rule", "name": "total", "type": "Reference", "labelSource": { "ownerModule": "sales" } }""", "$.facts[0].labelSource.entityType"),
            ("""{ "kind": "sales.rule", "name": "total", "type": "Code", "catalogFactId": 7 }""", "$.facts[0].catalogFactId"),
            ("""{ "kind": "sales.rule", "type": "Code" }""", "$.facts[0].name"),
            ("""7""", "$.facts[0]"),
        })
        {
            var json = $$"""{ "module": "sales", "schemaVersion": 1, "facts": [ {{fact}} ], "kinds": [ {{kind}} ] }""";

            Assert.Throws<DecisionEngineException>(() => BrainContract.Parse(json)).Message.Should().Contain(path);
        }
    }

    [Fact]
    public void AContractWithoutFactsOrKindsArraysIsEmpty()
    {
        var contract = BrainContract.Parse("""{ "module": "sales", "schemaVersion": 4 }""");

        contract.Address.Should().Be("sales@4");
        contract.Kinds.Should().BeEmpty();
    }
}
