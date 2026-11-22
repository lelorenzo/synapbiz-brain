using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Facts;

namespace Synapbiz.Brain.Parity;

/// <summary>Compile, build facts, evaluate: the three hit policies across the default row, null and missing facts.</summary>
internal static class TableScenarios
{
    private static readonly HitPolicy[] Policies = [HitPolicy.Unique, HitPolicy.First, HitPolicy.Collect];

    internal static void Register(ScenarioSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        foreach (var policy in Policies)
        {
            Add(set, policy, "single_match", withDefault: false, Facts("C-3", ("count", DecisionValue.Integer(100))));
            Add(set, policy, "single_match_with_default", withDefault: true, Facts("C-3", ("count", DecisionValue.Integer(100))));
            Add(set, policy, "two_matching_rows", withDefault: false, Facts("A-1", ("count", DecisionValue.Integer(10))));
            Add(set, policy, "two_matching_rows_with_default", withDefault: true, Facts("A-1", ("count", DecisionValue.Integer(10))));
            Add(set, policy, "no_match_no_default", withDefault: false, Facts("Z-9"));
            Add(set, policy, "no_match_with_default", withDefault: true, Facts("Z-9"));
            Add(set, policy, "nullable_fact_missing", withDefault: false, Facts("N-1"));
            Add(set, policy, "nullable_fact_present", withDefault: false, Facts("N-1", ("day", DecisionValue.Date(new DateOnly(2026, 1, 1)))));
            Add(set, policy, "nullable_fact_present_with_default", withDefault: true, Facts("N-1", ("day", DecisionValue.Date(new DateOnly(2026, 1, 1)))));
            Add(set, policy, "money_same_currency", withDefault: false, Facts("M-1", ("amount", DecisionValue.Money(10m, "USD"))));
            Add(set, policy, "money_foreign_currency", withDefault: false, Facts("M-1", ("amount", DecisionValue.Money(10m, "EUR"))));
            Add(set, policy, "every_fact_type_supplied", withDefault: false, AllFactTypes("C-3"));
            Add(set, policy, "missing_required_fact", withDefault: false, new Dictionary<string, DecisionValue>());
            Add(set, policy, "unknown_fact", withDefault: false, Facts("C-3", ("bogus", DecisionValue.Code("x"))));
            Add(set, policy, "wrong_type_fact", withDefault: false, new Dictionary<string, DecisionValue> { ["code"] = DecisionValue.Integer(1) });
            Add(set, policy, "wrong_type_nullable_fact", withDefault: false, Facts("C-3", ("count", DecisionValue.Code("ten"))));
        }
    }

    private static void Add(
        ScenarioSet set, HitPolicy policy, string name, bool withDefault, IReadOnlyDictionary<string, DecisionValue> facts) =>
        set.Add($"table/{policy}/{name}", () => Run(policy, withDefault, facts));

    private static string Run(HitPolicy policy, bool withDefault, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        var descriptor = Fixtures.ParityKind();
        var compiled = TableCompiler.Compile(Table(policy, withDefault), descriptor);
        var factSet = FactSet.Build(descriptor, facts);
        var (outcome, trace) = TableEvaluator.Evaluate(compiled, factSet, Fixtures.AsOf);
        return Summarize(outcome, trace);
    }

    internal static string Summarize(DecisionOutcome outcome, DecisionTrace trace)
    {
        var decided = outcome as Decided;
        return string.Join(
            "|",
            Format.Bool(outcome.IsDecided),
            decided?.RowKey ?? string.Empty,
            decided is null ? string.Empty : Format.Join(decided.MatchedRowKeys),
            decided?.OutcomeJson ?? string.Empty,
            trace.FactsJson,
            trace.FactsHash);
    }

    private static DecisionTable Table(HitPolicy policy, bool withDefault) => new(
        policy,
        [
            new Row("r_a", [Condition.Equals("code", DecisionValue.Code("A-1"))], """{"class":"A","score":1}"""),
            new Row(
                "r_b",
                [Condition.StartsWith("code", "A-"), Condition.Range("count", DecisionValue.Integer(5), DecisionValue.Integer(15), true, true)],
                """{"class":"B","score":2}"""),
            new Row("r_c", [Condition.Equals("code", DecisionValue.Code("C-3"))], """{"score":3,"class":"C"}"""),
            new Row("r_null", [Condition.StartsWith("code", "N-"), Condition.IsNull("day")], """{"class":"N","score":4}"""),
            new Row(
                "r_money",
                [
                    Condition.Equals("code", DecisionValue.Code("M-1")),
                    Condition.Range("amount", DecisionValue.Money(5m, "USD"), DecisionValue.Money(15m, "USD"), true, true),
                ],
                """{"class":"M","score":5}"""),
        ],
        withDefault ? new Row("default", [], """{"class":"D","score":0}""") : null);

    private static Dictionary<string, DecisionValue> Facts(string code, params (string Name, DecisionValue Value)[] extra)
    {
        var facts = new Dictionary<string, DecisionValue> { ["code"] = DecisionValue.Code(code) };
        foreach (var (name, value) in extra)
        {
            facts[name] = value;
        }

        return facts;
    }

    private static Dictionary<string, DecisionValue> AllFactTypes(string code) => new()
    {
        ["code"] = DecisionValue.Code(code),
        ["enm"] = DecisionValue.Enum("B"),
        ["flag"] = DecisionValue.Boolean(true),
        ["count"] = DecisionValue.Integer(7),
        ["ratio"] = DecisionValue.Decimal(2.50m),
        ["amount"] = DecisionValue.Money(12.50m, "USD"),
        ["day"] = DecisionValue.Date(new DateOnly(2026, 11, 18)),
        ["partner"] = DecisionValue.Reference(new Guid("33333333-3333-3333-3333-333333333333")),
    };
}
