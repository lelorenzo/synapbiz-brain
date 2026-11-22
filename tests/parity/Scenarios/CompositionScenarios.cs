using System.Text.Json;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Facts;

namespace Synapbiz.Brain.Parity;

/// <summary>A fact composed from a second kind, with a resolver that evaluates the child table, and the composition graph limits.</summary>
internal static class CompositionScenarios
{
    internal static void Register(ScenarioSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        set.Add("composition/resolved_gold", () => ResolveRegion("NA"));
        set.Add("composition/resolved_silver", () => ResolveRegion("EU"));
        set.Add("composition/resolved_child_default", () => ResolveRegion("ASIA"));
        set.Add("composition/no_resolver_supplied", NoResolver);
        set.Add("composition/chain_of_three_hops_is_accepted", () => CompileChain(hops: 3));
        set.Add("composition/chain_of_four_hops_is_refused", () => CompileChain(hops: 4));
        set.Add("composition/two_kind_cycle_is_refused", Cycle);
        set.Add("composition/self_cycle_is_refused", SelfCycle);
        set.Add("composition/unregistered_child_is_accepted", UnregisteredChild);
    }

    private static DecisionKindDescriptor ChildKind() => Fixtures.Descriptor(
        "synthetic.parity_child", [new FactDescriptor("region", FactType.Code)]);

    private static DecisionKindDescriptor ParentKind() => Fixtures.Descriptor(
        "synthetic.parity_parent", [new FactDescriptor("tier", FactType.Code, ComposedFromKind: ChildKind().Key)]);

    private static CompiledTable ChildTable() => TableCompiler.Compile(
        new DecisionTable(
            HitPolicy.Unique,
            [
                new Row("na", [Condition.Equals("region", DecisionValue.Code("NA"))], """{"tier":"gold"}"""),
                new Row("eu", [Condition.Equals("region", DecisionValue.Code("EU"))], """{"tier":"silver"}"""),
            ],
            new Row("other", [], """{"tier":"bronze"}""")),
        ChildKind());

    private static CompiledTable ParentTable() => TableCompiler.Compile(
        new DecisionTable(
            HitPolicy.Unique,
            [
                new Row("gold_tier", [Condition.Equals("tier", DecisionValue.Code("gold"))], """{"result":"R1"}"""),
                new Row("silver_tier", [Condition.Equals("tier", DecisionValue.Code("silver"))], """{"result":"R2"}"""),
            ],
            new Row("fallback_tier", [], """{"result":"R3"}""")),
        ParentKind());

    private static string ResolveRegion(string region)
    {
        var child = ChildKind();
        var childFacts = FactSet.Build(child, new Dictionary<string, DecisionValue> { ["region"] = DecisionValue.Code(region) });
        var parent = ParentKind();
        var parentFacts = FactSet.Build(parent, new Dictionary<string, DecisionValue>());

        var (outcome, trace) = TableEvaluator.Evaluate(
            ParentTable(), parentFacts, Fixtures.AsOf, new ChildTableResolver(ChildTable(), childFacts));

        var children = trace.Children.Select(c => string.Join(
            ";", c.KindKey.Value, c.MatchedRowKey ?? string.Empty, c.OutcomeJson ?? string.Empty, c.FactsJson, c.FactsHash));
        return TableScenarios.Summarize(outcome, trace) + "|children=" + Format.Int(trace.Children.Count) + "|" + string.Join("/", children);
    }

    private static string NoResolver()
    {
        var parent = ParentKind();
        var facts = FactSet.Build(parent, new Dictionary<string, DecisionValue>());
        var (outcome, trace) = TableEvaluator.Evaluate(ParentTable(), facts, Fixtures.AsOf);
        return TableScenarios.Summarize(outcome, trace);
    }

    private static string CompileChain(int hops)
    {
        // Kind 0 composes kind 1, which composes kind 2, and so on; the last kind composes nothing.
        var descriptors = new List<DecisionKindDescriptor>();
        for (var index = hops; index >= 0; index--)
        {
            var key = $"synthetic.chain_{Format.Int(index)}";
            var facts = index == hops
                ? Array.Empty<FactDescriptor>()
                : [new FactDescriptor("upstream", FactType.Code, ComposedFromKind: descriptors[^1].Key)];
            descriptors.Add(Fixtures.Descriptor(key, facts));
        }

        var registry = descriptors.ToDictionary(d => d.Key);
        var root = descriptors[^1];
        _ = TableCompiler.Compile(new DecisionTable(HitPolicy.Unique, [new Row("r", [], """{"out":"x"}""")]), root, registry);
        return "compiled";
    }

    private static string Cycle()
    {
        var aKey = DecisionKindKey.From("synthetic.cycle_a");
        var bKey = DecisionKindKey.From("synthetic.cycle_b");
        var a = Fixtures.Descriptor("synthetic.cycle_a", [new FactDescriptor("fromB", FactType.Code, ComposedFromKind: bKey)]);
        var b = Fixtures.Descriptor("synthetic.cycle_b", [new FactDescriptor("fromA", FactType.Code, ComposedFromKind: aKey)]);
        var registry = new Dictionary<DecisionKindKey, DecisionKindDescriptor> { [aKey] = a, [bKey] = b };
        _ = TableCompiler.Compile(new DecisionTable(HitPolicy.Unique, [new Row("r", [], """{"out":"x"}""")]), a, registry);
        return "compiled";
    }

    private static string SelfCycle()
    {
        var key = DecisionKindKey.From("synthetic.self_cycle");
        var descriptor = Fixtures.Descriptor("synthetic.self_cycle", [new FactDescriptor("me", FactType.Code, ComposedFromKind: key)]);
        var registry = new Dictionary<DecisionKindKey, DecisionKindDescriptor> { [key] = descriptor };
        _ = TableCompiler.Compile(new DecisionTable(HitPolicy.Unique, [new Row("r", [], """{"out":"x"}""")]), descriptor, registry);
        return "compiled";
    }

    private static string UnregisteredChild()
    {
        var parent = ParentKind();
        _ = TableCompiler.Compile(new DecisionTable(HitPolicy.Unique, [new Row("r", [], """{"out":"x"}""")]), parent);
        return "compiled";
    }

    private sealed class ChildTableResolver(CompiledTable childTable, FactSet childFacts) : IComposedFactResolver
    {
        public ComposedFactResolution Resolve(DecisionKindKey childKind, DateOnly asOf)
        {
            var (outcome, trace) = TableEvaluator.Evaluate(childTable, childFacts, asOf);
            var decided = (Decided)outcome;
            using var document = JsonDocument.Parse(decided.OutcomeJson);
            var tier = document.RootElement.GetProperty("tier").GetString()!;
            return new ComposedFactResolution(DecisionValue.Code(tier), trace);
        }
    }
}
