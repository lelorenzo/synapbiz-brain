using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>Helpers shared by the draft, change-view, proposal and locator tests.</summary>
internal static class DraftKit
{
    internal const string First = "intake.first.rule.json";

    internal static string FirstText => TestFiles.IntakeText(First);

    /// <summary>Applies the edits to the text, asserts that a draft came out and returns its text.</summary>
    internal static string Draft(string text, params RuleEdit[] edits)
    {
        var result = RuleDrafter.Apply(text, edits);
        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
        return result.DraftJson!;
    }

    /// <summary>Applies the edits to the text and loads the draft with the loader and the fixture contract; the load must succeed.</summary>
    internal static BrainRule Load(string text, params RuleEdit[] edits) => TestFiles.LoadIntakeText(Draft(text, edits));

    internal static BrainRule LoadFirst(params RuleEdit[] edits) => Load(FirstText, edits);

    internal static BrainRuleLoadResult TryLoad(string draft) => BrainRuleLoader.Load(draft, [TestFiles.IntakeContract()]);

    internal static RuleDraftResult Refused(string text, params RuleEdit[] edits)
    {
        var result = RuleDrafter.Apply(text, edits);
        result.IsValid.Should().BeFalse();
        result.DraftJson.Should().BeNull();
        result.Errors.Should().HaveCount(1);
        return result;
    }

    internal static Row RowOf(BrainRule rule, string key) => rule.Table.Rows.Single(row => row.Key == key);

    internal static Condition ConditionOn(Row row, string fact) => row.Conditions.Single(condition => condition.FactName == fact);

    internal static bool Same(Condition left, Condition right) =>
        left.FactName == right.FactName
        && left.Operator == right.Operator
        && Equals(left.Value, right.Value)
        && Equals(left.UpperValue, right.UpperValue)
        && left.LowerInclusive == right.LowerInclusive
        && left.UpperInclusive == right.UpperInclusive
        && left.Values.SequenceEqual(right.Values);

    internal static JsonObject Parse(string text) => JsonNode.Parse(text)!.AsObject();

    internal static JsonObject RowNode(JsonObject document, string key) =>
        document["body"]!["rows"]!.AsArray().OfType<JsonObject>().Single(row => (string?)row["legacyKey"] == key);

    internal static IReadOnlyDictionary<string, DecisionValue> Outcome(params (string Field, DecisionValue Value)[] fields) =>
        fields.ToDictionary(field => field.Field, field => field.Value, StringComparer.Ordinal);
}
