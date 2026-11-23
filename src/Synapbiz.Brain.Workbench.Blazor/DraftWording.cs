using System.Globalization;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Validation;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>Writes what the loader and the validator refused as sentences, in the words of the person who edits: never a code.</summary>
internal static class DraftWording
{
    private const string OperatorNotAllowed = "decisions.condition.operator_not_allowed";
    private const string RangeTypeMismatch = "decisions.condition.range_type_mismatch";
    private const string CellTypeMismatch = "decisions.validation.cell_type_mismatch";
    private const string MoneyCurrencyMismatch = "decisions.validation.money_currency_mismatch";
    private const string EnumNotAllowed = "brain.fact.enum_not_allowed";
    private const string OutcomeRequired = "brain.outcome.required_field";
    private const string OutcomeTypeMismatch = "brain.outcome.type_mismatch";
    private const string CaseRowUnknown = "brain.golden.unknown_row";
    private const string CaseFactMissing = "decisions.facts.missing";
    private const string CaseFactMismatch = "decisions.facts.type_mismatch";
    private const string CaseOutcomeMissing = "brain.rule.golden_outcome_missing";

    /// <summary>The key of the words of an operator as a document writes it.</summary>
    internal static string OperatorKey(string? op) => op switch
    {
        "equals" => "op.eq",
        "notEquals" => "op.neq",
        "in" => "op.in",
        "notIn" => "op.nin",
        "range" => "op.between",
        "isNull" => "op.isnull",
        "isNotNull" => "op.notnull",
        "startsWith" => "op.starts",
        _ => "op.any",
    };

    /// <summary>
    /// The sentences for the reasons of a refusal, each once. The labels come from the view; a fact the view does not know is
    /// written humanised, never by its name. When the form knows the words the person chose for a fact, they name the operator.
    /// </summary>
    /// <param name="text">The strings of the render.</param>
    /// <param name="problems">The reasons.</param>
    /// <param name="view">The rule as the person reads it, for the labels.</param>
    /// <param name="rule">The candidate when it was read, for the row numbers of an overlap.</param>
    /// <param name="chosen">The words of the operator of each fact line of the form; empty when there is no form.</param>
    internal static List<string> Lines(
        UiText text, IReadOnlyList<DraftProblem> problems, RuleView view, BrainRule? rule, IReadOnlyDictionary<string, string> chosen) =>
        [.. problems.Select(problem => Line(text, problem, view, rule, chosen)).Distinct(StringComparer.Ordinal)];

    /// <summary>True when every reason is one the person cannot cause with what they entered.</summary>
    internal static bool IsInternal(IReadOnlyList<DraftProblem> problems) => problems.All(problem => KeyOf(problem) == "refuse.edit.internal");

    private static string Line(UiText text, DraftProblem problem, RuleView view, BrainRule? rule, IReadOnlyDictionary<string, string> chosen)
    {
        var fact = FactLabel(view, problem.Fact);
        var field = OutcomeLabel(view, problem.Place.OutcomeField);
        var number = problem.Place.RowNumber?.ToString(CultureInfo.InvariantCulture) ?? "–";
        var caseName = problem.Place.CaseName ?? string.Empty;
        var overlap = problem.Code == TableValidator.CodeOverlap;
        var pair = overlap ? problem.RowKeys.Select(key => RowNumber(rule, key)).Order().ToList() : [];
        var operatorWord = problem.Fact is { } name && chosen.TryGetValue(name, out var word) ? word : text.Plain(OperatorKey(problem.Op));
        return text.Plain(
            KeyOf(problem),
            ("operator", operatorWord),
            ("fact", problem.Place.OutcomeField is null ? fact : field),
            ("field", field),
            ("value", problem.Value ?? string.Empty),
            ("n", overlap ? pair[0].ToString(CultureInfo.InvariantCulture) : number),
            ("m", overlap ? pair[1].ToString(CultureInfo.InvariantCulture) : string.Empty),
            ("case", caseName));
    }

    /// <summary>The key of the sentence of a reason; an outcome that a test case lacks is the case's problem, not the row's.</summary>
    private static string KeyOf(DraftProblem problem) =>
        problem.Place.CaseName is not null && problem.Code is OutcomeRequired or OutcomeTypeMismatch ? "refuse.edit.caseOutcome" : KeyOf(problem.Code);

    private static string KeyOf(string code) => code switch
    {
        OperatorNotAllowed => "refuse.edit.operator",
        RangeTypeMismatch => "refuse.edit.rangeType",
        CellTypeMismatch => "refuse.edit.cellType",
        MoneyCurrencyMismatch => "refuse.edit.currency",
        EnumNotAllowed => "refuse.edit.enum",
        OutcomeRequired => "refuse.edit.required",
        OutcomeTypeMismatch => "refuse.edit.outcomeType",
        CaseRowUnknown => "refuse.edit.caseRow",
        CaseFactMissing or CaseFactMismatch => "refuse.edit.caseData",
        CaseOutcomeMissing => "refuse.edit.caseOutcome",
        TableValidator.CodeOverlap => "refuse.edit.overlap",
        _ => "refuse.edit.internal",
    };

    private static int RowNumber(BrainRule? rule, string key) =>
        (rule?.Table.Rows.ToList().FindIndex(row => string.Equals(row.Key, key, StringComparison.Ordinal)) ?? -1) + 1;

    private static string FactLabel(RuleView view, string? name) =>
        name is null ? string.Empty : view.Facts.FirstOrDefault(fact => fact.Name == name)?.Label ?? WorkbenchText.Humanize(name);

    private static string OutcomeLabel(RuleView view, string? name) =>
        name is null ? string.Empty : view.OutcomeFields.FirstOrDefault(field => field.Name == name)?.Label ?? WorkbenchText.Humanize(name);
}
