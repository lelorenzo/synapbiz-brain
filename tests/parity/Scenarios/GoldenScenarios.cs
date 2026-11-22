using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Goldens;

namespace Synapbiz.Brain.Parity;

/// <summary>The golden runner: all pass, a failure, an uncovered row, no-decision expectations and an empty set.</summary>
internal static class GoldenScenarios
{
    internal static void Register(ScenarioSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        set.Add("goldens/all_pass", () => Run(
            Table(withDefault: true),
            [
                new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A","n":1}""", "web")),
                new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B","n":2}""", "pos")),
                new("fallback_case", Facts("B2B"), new ExpectedDecided("""{"class":"FALLBACK","n":0}""", "default")),
            ]));

        set.Add("goldens/all_pass_key_order_insensitive", () => Run(
            Table(withDefault: true),
            [
                new("web_case", Facts("Web"), new ExpectedDecided("""{"n":1,"class":"A"}""")),
                new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B","n":2}""")),
                new("fallback_case", Facts("B2B"), new ExpectedDecided("""{"class":"FALLBACK","n":0}""")),
            ]));

        set.Add("goldens/one_failing", () => Run(
            Table(withDefault: true),
            [
                new("web_case_wrong", Facts("Web"), new ExpectedDecided("""{"class":"WRONG"}""", "web")),
                new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B","n":2}""", "pos")),
                new("fallback_case", Facts("B2B"), new ExpectedDecided("""{"class":"FALLBACK","n":0}""", "default")),
            ]));

        set.Add("goldens/wrong_expected_row_key", () => Run(
            Table(withDefault: true),
            [
                new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A","n":1}""", "pos")),
                new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B","n":2}""", "pos")),
                new("fallback_case", Facts("B2B"), new ExpectedDecided("""{"class":"FALLBACK","n":0}""", "default")),
            ]));

        set.Add("goldens/one_row_uncovered", () => Run(
            Table(withDefault: true),
            [new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A","n":1}""", "web"))]));

        set.Add("goldens/default_row_uncovered", () => Run(
            Table(withDefault: true),
            [
                new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A","n":1}""", "web")),
                new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B","n":2}""", "pos")),
            ]));

        set.Add("goldens/no_decision_expected_and_given", () => Run(
            Table(withDefault: false),
            [
                new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A","n":1}""", "web")),
                new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B","n":2}""", "pos")),
                new("nothing", Facts("B2B"), new ExpectedNoDecision()),
            ]));

        set.Add("goldens/no_decision_expected_but_decided", () => Run(
            Table(withDefault: true),
            [
                new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A","n":1}""", "web")),
                new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B","n":2}""", "pos")),
                new("fallback_case", Facts("B2B"), new ExpectedNoDecision()),
            ]));

        set.Add("goldens/decided_expected_but_no_decision", () => Run(
            Table(withDefault: false),
            [
                new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A","n":1}""", "web")),
                new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B","n":2}""", "pos")),
                new("nothing", Facts("B2B"), new ExpectedDecided("""{"class":"A","n":1}""")),
            ]));

        set.Add("goldens/empty_set", () => Run(Table(withDefault: true), []));

        set.Add("goldens/case_with_a_wrong_typed_fact", () => Run(
            Table(withDefault: true),
            [new("typed", new Dictionary<string, DecisionValue> { ["channel"] = DecisionValue.Code("Web") }, new ExpectedNoDecision())]));
    }

    private static CompiledTable Table(bool withDefault) => TableCompiler.Compile(
        new DecisionTable(
            HitPolicy.Unique,
            [
                new Row("web", [Condition.Equals("channel", DecisionValue.Enum("Web"))], """{"n":1,"class":"A"}"""),
                new Row("pos", [Condition.Equals("channel", DecisionValue.Enum("Pos"))], """{"n":2,"class":"B"}"""),
            ],
            withDefault ? new Row("default", [], """{"n":0,"class":"FALLBACK"}""") : null),
        Fixtures.ChannelKind("synthetic.golden_channel", "Web", "Pos", "B2B"));

    private static Dictionary<string, DecisionValue> Facts(string channel) =>
        new() { ["channel"] = DecisionValue.Enum(channel) };

    private static string Run(CompiledTable table, IReadOnlyList<GoldenCase> cases)
    {
        var result = GoldenRunner.Run(table, cases, Fixtures.AsOf);
        return string.Join(
            "|",
            Format.Bool(result.IsOk),
            Format.Join(result.Failed.Select(f => f.Name)),
            Format.Join(result.UncoveredRowKeys),
            Format.Join(result.ToIssues().Select(i => i.Code)));
    }
}
