using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench;

/// <summary>One permitted value of an enum fact.</summary>
/// <param name="Value">The raw value, as a rule writes it.</param>
/// <param name="Label">The text to show for it.</param>
public sealed record ValueOption(string Value, string Label);

/// <summary>A fact the rule's kind declares.</summary>
/// <param name="Name">The fact name.</param>
/// <param name="Label">The text to show for the name.</param>
/// <param name="Type">The fact's type.</param>
/// <param name="Nullable">Whether the fact may be left out.</param>
/// <param name="AllowedValues">The permitted values of an enum fact, in declaration order; empty when the descriptor has none.</param>
public sealed record FactView(string Name, string Label, FactType Type, bool Nullable, IReadOnlyList<ValueOption> AllowedValues);

/// <summary>An outcome field the rule's kind declares.</summary>
/// <param name="Name">The field name.</param>
/// <param name="Label">The text to show for the name.</param>
/// <param name="Type">The field's type.</param>
/// <param name="Nullable">Whether a row may leave the field out.</param>
public sealed record OutcomeFieldView(string Name, string Label, FactType Type, bool Nullable);

/// <summary>One condition of a row, as a sentence part.</summary>
/// <param name="Index">Position of the condition in the row, from 0.</param>
/// <param name="FactName">The fact the condition reads.</param>
/// <param name="FactLabel">The text to show for the fact.</param>
/// <param name="Operator">The condition's operator.</param>
/// <param name="Text">The condition in plain language, for example <c>score is between 0 and 49</c>.</param>
public sealed record ConditionView(int Index, string FactName, string FactLabel, Operator Operator, string Text);

/// <summary>One field of a row's outcome.</summary>
/// <param name="Field">The outcome field name.</param>
/// <param name="FieldLabel">The text to show for the field.</param>
/// <param name="RawValue">The value as the rule writes it: the string itself, or the JSON text of any other value.</param>
/// <param name="Text">The value in plain language.</param>
public sealed record OutcomeValueView(string Field, string FieldLabel, string RawValue, string Text);

/// <summary>A row of the decision table, with its sentence.</summary>
/// <param name="Key">The engine row key (the row's <c>legacyKey</c>), the one traces report.</param>
/// <param name="RowId">The row id of the rule document.</param>
/// <param name="Index">Position of the row in decision order, from 0; the default row follows the last row.</param>
/// <param name="IsDefault">True for the default row.</param>
/// <param name="Title">The row's title from the labels; null when there is none.</param>
/// <param name="Conditions">Every condition of the row, in order.</param>
/// <param name="Outcome">The outcome values of the row.</param>
/// <param name="Sentence">The row as one sentence, <c>If ..., then ...</c>.</param>
public sealed record RowView(
    string Key,
    string RowId,
    int Index,
    bool IsDefault,
    string? Title,
    IReadOnlyList<ConditionView> Conditions,
    IReadOnlyList<OutcomeValueView> Outcome,
    string Sentence);

/// <summary>A rule as a person reads it.</summary>
/// <param name="Id">The rule id.</param>
/// <param name="Version">The rule version.</param>
/// <param name="Title">The title from the labels, or the rule id.</param>
/// <param name="Kind">The decision kind key.</param>
/// <param name="HitPolicy">The table's hit policy.</param>
/// <param name="Facts">The facts of the kind, in declaration order.</param>
/// <param name="OutcomeFields">The outcome fields of the kind, in declaration order.</param>
/// <param name="Rows">The rows in decision order.</param>
/// <param name="DefaultRow">The default row; null when the table has none.</param>
public sealed record RuleView(
    string Id,
    long Version,
    string Title,
    string Kind,
    HitPolicy HitPolicy,
    IReadOnlyList<FactView> Facts,
    IReadOnlyList<OutcomeFieldView> OutcomeFields,
    IReadOnlyList<RowView> Rows,
    RowView? DefaultRow);
