using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Contracts.Validation;
using Nuvora.Decisions.Engine.Validation;

namespace Nuvora.UnitTests.Decisions.Engine;

public sealed class TableRowValidatorTests
{
    [Fact]
    public void RowValidator_RefusesEachInvalidRowIncludingDefault()
    {
        var table = new DecisionTable(HitPolicy.First,
            [new Row("web", [Condition.Equals("channel", DecisionValue.Enum("Web"))], "{}")],
            DefaultRow: new Row("fallback", [], "{}"));

        var report = TableValidator.Validate(table, Descriptor(), new PosOnlyRowValidator());

        report.Errors.Should().HaveCount(2);
        report.Errors.Should().OnlyContain(issue => issue.Code == PosOnlyRowValidator.Code);
        report.Errors.SelectMany(issue => issue.RowKeys).Should().BeEquivalentTo("web", "fallback");
    }

    [Fact]
    public void ConditionalDefault_CannotBypassRowValidation()
    {
        var condition = Condition.Equals("channel", DecisionValue.Enum("Pos"));
        var defaultRow = new Row("not-a-special-key", [condition], "{}");
        var table = new DecisionTable(HitPolicy.First, [], DefaultRow: defaultRow);

        var report = TableValidator.Validate(table, Descriptor(), new PosOnlyRowValidator());

        report.Errors.Should().ContainSingle(issue => issue.Code == PosOnlyRowValidator.Code
            && issue.RowKeys.Contains("not-a-special-key"));
        defaultRow.Conditions.Should().ContainSingle().Which.Should().Be(condition);
    }

    [Fact]
    public void OrdinaryRowNamedDefault_RetainsItsConditions()
    {
        var table = new DecisionTable(HitPolicy.First,
            [new Row("default", [Condition.Equals("channel", DecisionValue.Enum("Pos"))], "{}")]);
        var descriptor = Descriptor() with
        {
            Facts = [new FactDescriptor("channel", FactType.Enum, AllowedValues: ["Pos"])],
        };

        var report = TableValidator.Validate(table, descriptor, new PosOnlyRowValidator());

        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void RowHook_PreservesOutcomeValidation()
    {
        var table = new DecisionTable(HitPolicy.First,
            [new Row("pos", [Condition.Equals("channel", DecisionValue.Enum("Pos"))], "{\"bad\":true}")]);

        var report = TableValidator.Validate(table, Descriptor(), new PosOnlyRowValidator());

        report.Errors.Should().ContainSingle(issue => issue.Code == "test.outcome.invalid");
    }

    private static DecisionKindDescriptor Descriptor() => new()
    {
        Key = DecisionKindKey.From("test.row_validation"),
        SchemaVersion = 1,
        Facts = [new FactDescriptor("channel", FactType.Enum, AllowedValues: ["Web", "Pos"])],
        OutcomeFields = [],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "p",
        AuthorPermission = "p",
        ApprovePermission = "p",
        ContainsPersonalData = false,
        ReadTracePermission = "test.trace",
    };

    private sealed class PosOnlyRowValidator : IDecisionOutcomeValidator, IDecisionRowValidator
    {
        public const string Code = "test.row.requires_pos";

        public IReadOnlyList<ValidationIssue> Validate(string rowKey, string outcomeJson) =>
            outcomeJson.Contains("bad", StringComparison.Ordinal)
                ? [new ValidationIssue("test.outcome.invalid", "Invalid outcome.", [rowKey])]
                : [];

        public IReadOnlyList<ValidationIssue> ValidateRow(Row row) =>
            row.Conditions.Any(condition => condition.FactName == "channel"
                && condition.Operator == Operator.Equals && condition.Value == DecisionValue.Enum("Pos"))
                ? []
                : [new ValidationIssue(Code, "The outcome requires a Pos condition.", [row.Key])];
    }
}
