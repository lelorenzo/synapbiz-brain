using System.Reflection;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>Builders for the synthetic fact sets and rules of the tests.</summary>
internal static class Samples
{
    internal static readonly DateOnly AsOf = new(2026, 11, 22);

    internal static readonly Guid Account = new("3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6b");

    /// <summary>A fact set for the <c>sample.intake</c> kind; the nullable facts are left out when null.</summary>
    internal static Dictionary<string, DecisionValue> Intake(
        string channel,
        string topic,
        bool urgent,
        long ageDays,
        decimal? rating = null,
        decimal? amount = null,
        DateOnly? receivedOn = null,
        bool withAccount = false)
    {
        var facts = new Dictionary<string, DecisionValue>(StringComparer.Ordinal)
        {
            ["channel"] = DecisionValue.Enum(channel),
            ["topic"] = DecisionValue.Code(topic),
            ["urgent"] = DecisionValue.Boolean(urgent),
            ["ageDays"] = DecisionValue.Integer(ageDays),
        };
        if (rating is { } score)
        {
            facts["rating"] = DecisionValue.Decimal(score);
        }

        if (amount is { } money)
        {
            facts["amount"] = DecisionValue.Money(money, "USD");
        }

        if (receivedOn is { } date)
        {
            facts["receivedOn"] = DecisionValue.Date(date);
        }

        if (withAccount)
        {
            facts["account"] = DecisionValue.Reference(Account);
        }

        return facts;
    }

    /// <summary>
    /// A copy of <paramref name="rule"/> with another table and, optionally, another kind. The loader refuses some tables a
    /// test needs (a range with no bound, an outcome with extra members), so the tests build the rule through its internal
    /// constructor and do not need access to the library's internals.
    /// </summary>
    internal static BrainRule WithTable(
        BrainRule rule,
        DecisionTable table,
        DecisionKindDescriptor? kind = null,
        IReadOnlyList<GoldenCase>? goldens = null,
        long? version = null)
    {
        var constructor = typeof(BrainRule).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var rowIds = table.Rows.Concat(table.DefaultRow is null ? [] : [table.DefaultRow])
            .ToDictionary(row => row.Key, row => row.Key, StringComparer.Ordinal);
        return (BrainRule)constructor.Invoke([rule.Id, version ?? rule.Version, kind ?? rule.Kind, table, goldens ?? rule.Goldens, rowIds, new JsonObject()]);
    }

    internal static GoldenCase Case(string name, IReadOnlyDictionary<string, DecisionValue> facts, GoldenExpectation expected) =>
        new(name, facts, expected);

    internal static Row RowOf(string key, string outcome, params Condition[] conditions) => new(key, conditions, outcome);
}
