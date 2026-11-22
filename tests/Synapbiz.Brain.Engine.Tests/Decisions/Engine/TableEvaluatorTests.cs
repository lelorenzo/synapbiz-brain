using System.Text.Json;
using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Facts;

namespace Nuvora.UnitTests.Decisions.Engine;

/// <summary>
/// P2-20.2 step 3 (paquete 0.A369): UNIQUE/FIRST evaluation, the default
/// row, the typed non-exceptional <see cref="NoDecision"/>, composition
/// (D-14), the composition graph's cycle/depth refusal, and determinism.
/// </summary>
public sealed class TableEvaluatorTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 1);

    [Fact]
    public void Unique_ASingleMatch_ReturnsTheRowAndANamingTrace()
    {
        var descriptor = ChannelDescriptor("test.unique");
        var table = TableCompiler.Compile(
            new DecisionTable(
                HitPolicy.Unique,
                [
                    Row("web", Condition.Equals("channel", DecisionValue.Enum("Web")), """{"class":"A"}"""),
                    Row("pos", Condition.Equals("channel", DecisionValue.Enum("Pos")), """{"class":"B"}"""),
                ]),
            descriptor);
        var facts = FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["channel"] = DecisionValue.Enum("Web") });

        var (outcome, trace) = TableEvaluator.Evaluate(table, facts, AsOf);

        outcome.Should().BeOfType<Decided>();
        ((Decided)outcome).RowKey.Should().Be("web");
        ((Decided)outcome).OutcomeJson.Should().Be("""{"class":"A"}""");
        trace.IsDecided.Should().BeTrue();
        trace.MatchedRowKey.Should().Be("web");
        trace.KindKey.Should().Be(descriptor.Key);
        trace.FactsHash.Should().Be(facts.Hash().Value);
        trace.FactsJson.Should().Be(facts.ToCanonicalJson());
    }

    [Fact]
    public void Unique_MoreThanOneMatch_ThrowsAsADefect()
    {
        var descriptor = ChannelDescriptor("test.unique_defect");
        var table = TableCompiler.Compile(
            new DecisionTable(
                HitPolicy.Unique,
                [
                    Row("any1", Condition.Any("channel"), """{"class":"A"}"""),
                    Row("any2", Condition.Any("channel"), """{"class":"B"}"""),
                ]),
            descriptor);
        var facts = FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["channel"] = DecisionValue.Enum("Web") });

        var act = () => TableEvaluator.Evaluate(table, facts, AsOf);

        act.Should().Throw<DecisionEngineException>().Where(e => e.ErrorCode == TableEvaluator.ErrorMultipleMatches);
    }

    /// <summary>MUTATION (performed and reverted at gate time): making FIRST pick the LAST match turns this RED.</summary>
    [Fact]
    public void First_OverlappingRows_TheEarlierRowWins()
    {
        var descriptor = ChannelDescriptor("test.first");
        var table = TableCompiler.Compile(
            new DecisionTable(
                HitPolicy.First,
                [
                    Row("catch_all", Condition.Any("channel"), """{"class":"CATCH_ALL"}"""),
                    Row("web_specific", Condition.Equals("channel", DecisionValue.Enum("Web")), """{"class":"WEB_SPECIFIC"}"""),
                ]),
            descriptor);
        var facts = FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["channel"] = DecisionValue.Enum("Web") });

        var (outcome, trace) = TableEvaluator.Evaluate(table, facts, AsOf);

        ((Decided)outcome).RowKey.Should().Be("catch_all");
        trace.MatchedRowKey.Should().Be("catch_all");
    }

    [Fact]
    public void Unmatched_WithADefaultRow_DecidesTheDefault()
    {
        var descriptor = ChannelDescriptor("test.default");
        var table = TableCompiler.Compile(
            new DecisionTable(
                HitPolicy.Unique,
                [Row("web", Condition.Equals("channel", DecisionValue.Enum("Web")), """{"class":"A"}""")],
                DefaultRow: Row("default", [], """{"class":"FALLBACK"}""")),
            descriptor);
        var facts = FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["channel"] = DecisionValue.Enum("Pos") });

        var (outcome, trace) = TableEvaluator.Evaluate(table, facts, AsOf);

        outcome.Should().BeOfType<Decided>();
        ((Decided)outcome).RowKey.Should().Be("default");
        trace.MatchedRowKey.Should().Be("default");
    }

    /// <summary>MUTATION (performed and reverted at gate time): returning the default row's outcome when no default exists (a sentinel) turns this RED.</summary>
    [Fact]
    public void Unmatched_WithNoDefaultRow_IsNoDecision_NeverAnException()
    {
        var descriptor = ChannelDescriptor("test.nodefault");
        var table = TableCompiler.Compile(
            new DecisionTable(HitPolicy.Unique, [Row("web", Condition.Equals("channel", DecisionValue.Enum("Web")), """{"class":"A"}""")]),
            descriptor);
        var facts = FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["channel"] = DecisionValue.Enum("Pos") });

        var act = () => TableEvaluator.Evaluate(table, facts, AsOf);

        act.Should().NotThrow();
        var (outcome, trace) = act();
        outcome.Should().BeOfType<NoDecision>();
        outcome.IsDecided.Should().BeFalse();
        ((NoDecision)outcome).Reason.Should().Be(TableEvaluator.NoRowMatchedReason);
        trace.IsDecided.Should().BeFalse();
        trace.MatchedRowKey.Should().BeNull();
        trace.OutcomeJson.Should().BeNull();
    }

    [Fact]
    public void Composition_TheChildKindOutcome_FeedsTheParentColumn_AndItsTraceIsNested()
    {
        var childDescriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.composition_child"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("region", FactType.Code)],
            OutcomeFields = [new OutcomeFieldDescriptor("tier", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var childTable = TableCompiler.Compile(
            new DecisionTable(HitPolicy.Unique, [Row("na", Condition.Equals("region", DecisionValue.Code("NA")), """{"tier":"gold"}""")]),
            childDescriptor);
        var childFacts = FactSet.Build(childDescriptor, new Dictionary<string, DecisionValue> { ["region"] = DecisionValue.Code("NA") });

        var parentDescriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.composition_parent"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("tier", FactType.Code, ComposedFromKind: childDescriptor.Key)],
            OutcomeFields = [new OutcomeFieldDescriptor("result", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var parentTable = TableCompiler.Compile(
            new DecisionTable(HitPolicy.Unique, [Row("gold_tier", Condition.Equals("tier", DecisionValue.Code("gold")), """{"result":"R1"}""")]),
            parentDescriptor);
        var parentFacts = FactSet.Build(parentDescriptor, new Dictionary<string, DecisionValue>());

        var resolver = new FixedChildResolver(childTable, childFacts);

        var (outcome, trace) = TableEvaluator.Evaluate(parentTable, parentFacts, AsOf, resolver);

        outcome.Should().BeOfType<Decided>();
        ((Decided)outcome).RowKey.Should().Be("gold_tier");
        trace.Children.Should().HaveCount(1);
        trace.Children[0].KindKey.Should().Be(childDescriptor.Key);
        trace.Children[0].MatchedRowKey.Should().Be("na");
    }

    [Fact]
    public void Determinism_OneThousandEvaluationsOfTheSameInputs_ProduceByteIdenticalTraces()
    {
        var descriptor = ChannelDescriptor("test.determinism");
        var table = TableCompiler.Compile(
            new DecisionTable(
                HitPolicy.Unique,
                [Row("web", Condition.Equals("channel", DecisionValue.Enum("Web")), """{"class":"A"}""")]),
            descriptor);
        var facts = FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["channel"] = DecisionValue.Enum("Web") });

        var (_, firstTrace) = TableEvaluator.Evaluate(table, facts, AsOf);
        var firstSerialized = SerializeTrace(firstTrace);

        for (var i = 0; i < 1000; i++)
        {
            var (_, trace) = TableEvaluator.Evaluate(table, facts, AsOf);
            SerializeTrace(trace).Should().Be(firstSerialized);
        }
    }

    /// <summary>MUTATION (performed and reverted at gate time): dropping the depth check in TableCompiler turns this RED.</summary>
    [Fact]
    public void Compile_ADepthFourCompositionChain_IsRefused()
    {
        // a -> b -> c -> d -> e is four hops; TableCompiler.MaxCompositionDepth is 3.
        var e = ChildOnlyDescriptor("test.chain_e", composedFrom: null);
        var d = ChildOnlyDescriptor("test.chain_d", composedFrom: e.Key);
        var c = ChildOnlyDescriptor("test.chain_c", composedFrom: d.Key);
        var b = ChildOnlyDescriptor("test.chain_b", composedFrom: c.Key);
        var a = ChildOnlyDescriptor("test.chain_a", composedFrom: b.Key);
        var registry = new[] { e, d, c, b, a }.ToDictionary(k => k.Key);
        var table = new DecisionTable(HitPolicy.Unique, [Row("r", [], """{"out":"x"}""")]);

        var act = () => TableCompiler.Compile(table, a, registry);

        act.Should().Throw<DecisionEngineException>().Where(ex => ex.ErrorCode == TableCompiler.ErrorCompositionTooDeep);
    }

    [Fact]
    public void Compile_AThreeHopCompositionChain_IsAccepted()
    {
        // a -> b -> c -> d is three hops, at the limit.
        var d = ChildOnlyDescriptor("test.ok_chain_d", composedFrom: null);
        var c = ChildOnlyDescriptor("test.ok_chain_c", composedFrom: d.Key);
        var b = ChildOnlyDescriptor("test.ok_chain_b", composedFrom: c.Key);
        var a = ChildOnlyDescriptor("test.ok_chain_a", composedFrom: b.Key);
        var registry = new[] { d, c, b, a }.ToDictionary(k => k.Key);
        var table = new DecisionTable(HitPolicy.Unique, [Row("r", [], """{"out":"x"}""")]);

        var act = () => TableCompiler.Compile(table, a, registry);

        act.Should().NotThrow();
    }

    /// <summary>MUTATION target lives in TableCompiler (pathSoFar.Contains check); reverted at gate time.</summary>
    [Fact]
    public void Compile_ATwoKindCompositionCycle_IsRefused()
    {
        var bKey = DecisionKindKey.From("test.cycle_b");
        var aKey = DecisionKindKey.From("test.cycle_a");

        var a = new DecisionKindDescriptor
        {
            Key = aKey,
            SchemaVersion = 1,
            Facts = [new FactDescriptor("fromB", FactType.Code, ComposedFromKind: bKey)],
            OutcomeFields = [],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var b = new DecisionKindDescriptor
        {
            Key = bKey,
            SchemaVersion = 1,
            Facts = [new FactDescriptor("fromA", FactType.Code, ComposedFromKind: aKey)],
            OutcomeFields = [],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var registry = new Dictionary<DecisionKindKey, DecisionKindDescriptor> { [aKey] = a, [bKey] = b };
        var table = new DecisionTable(HitPolicy.Unique, [Row("r", [], """{"out":"x"}""")]);

        var act = () => TableCompiler.Compile(table, a, registry);

        act.Should().Throw<DecisionEngineException>().Where(ex => ex.ErrorCode == TableCompiler.ErrorCompositionCycle);
    }

    [Fact]
    public void Compile_ARowConditionsOnAnUndeclaredFact_ThrowsUnknownFact()
    {
        var descriptor = ChannelDescriptor("test.unknown_fact_fixture");
        var table = new DecisionTable(HitPolicy.Unique, [Row("bad", Condition.Equals("not_declared", DecisionValue.Code("x")), "{}")]);

        var act = () => TableCompiler.Compile(table, descriptor);

        act.Should().Throw<DecisionEngineException>().Where(ex => ex.ErrorCode == TableCompiler.ErrorUnknownFact);
    }

    [Fact]
    public void Evaluate_AComposedFactWithNoResolverSupplied_ThrowsResolverRequired()
    {
        var childKey = DecisionKindKey.From("test.resolver_required_child");
        var parentDescriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.resolver_required_parent"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("tier", FactType.Code, ComposedFromKind: childKey)],
            OutcomeFields = [],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = TableCompiler.Compile(
            new DecisionTable(HitPolicy.Unique, [Row("r", Condition.Any("tier"), "{}")]), parentDescriptor);
        var facts = FactSet.Build(parentDescriptor, new Dictionary<string, DecisionValue>());

        var act = () => TableEvaluator.Evaluate(table, facts, AsOf);

        act.Should().Throw<DecisionEngineException>().Where(ex => ex.ErrorCode == TableEvaluator.ErrorResolverRequired);
    }

    private static string SerializeTrace(DecisionTrace trace) => JsonSerializer.Serialize(new
    {
        trace.KindKey.Value,
        trace.VersionId,
        trace.VersionNumber,
        trace.MatchedRowKey,
        trace.IsDecided,
        trace.OutcomeJson,
        trace.FactsJson,
        trace.FactsHash,
        Children = trace.Children.Select(SerializeTrace).ToList(),
    });

    private static Row Row(string key, Condition condition, string outcome) => new(key, [condition], outcome);

    private static Row Row(string key, IReadOnlyList<Condition> conditions, string outcome) => new(key, conditions, outcome);

    private static DecisionKindDescriptor ChannelDescriptor(string key) => new()
    {
        Key = DecisionKindKey.From(key),
        SchemaVersion = 1,
        Facts = [new FactDescriptor("channel", FactType.Enum, AllowedValues: ["Web", "Pos"])],
        OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "p",
        AuthorPermission = "p",
        ApprovePermission = "p",
        ContainsPersonalData = false,
        ReadTracePermission = "test.decisions.read_trace",
    };

    private static DecisionKindDescriptor ChildOnlyDescriptor(string key, DecisionKindKey? composedFrom) => new()
    {
        Key = DecisionKindKey.From(key),
        SchemaVersion = 1,
        Facts = composedFrom is null ? [] : [new FactDescriptor("upstream", FactType.Code, ComposedFromKind: composedFrom)],
        OutcomeFields = [],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "p",
        AuthorPermission = "p",
        ApprovePermission = "p",
        ContainsPersonalData = false,
        ReadTracePermission = "test.decisions.read_trace",
    };

    private sealed class FixedChildResolver(CompiledTable childTable, FactSet childFacts) : IComposedFactResolver
    {
        public ComposedFactResolution Resolve(DecisionKindKey childKind, DateOnly asOf)
        {
            var (outcome, trace) = TableEvaluator.Evaluate(childTable, childFacts, asOf);
            var decided = (Decided)outcome;
            using var doc = JsonDocument.Parse(decided.OutcomeJson);
            var tier = doc.RootElement.GetProperty("tier").GetString()!;
            return new ComposedFactResolution(DecisionValue.Code(tier), trace);
        }
    }
}
