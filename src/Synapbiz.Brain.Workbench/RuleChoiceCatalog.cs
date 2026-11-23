using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Conditions;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench;

/// <summary>One value a form may offer, with the text to show for it.</summary>
/// <param name="Value">The typed value an edit would carry.</param>
/// <param name="Label">The text to show for it.</param>
public sealed record ValueChoice(DecisionValue Value, string Label);

/// <summary>What a form may offer for a condition on one fact.</summary>
/// <param name="Fact">The fact name.</param>
/// <param name="Label">The text to show for the name.</param>
/// <param name="ValueType">The fact's type, as the <c>FactType</c> member name (<c>Enum</c>, <c>Integer</c>, and so on).</param>
/// <param name="Operators">The operators a condition on the fact may use, as <see cref="Operator"/> member names in the engine's order.</param>
/// <param name="Values">The members of an enum fact, in declaration order; empty for any other type.</param>
public sealed record FactChoices(string Fact, string Label, string ValueType, IReadOnlyList<string> Operators, IReadOnlyList<ValueChoice> Values);

/// <summary>What a form may offer for one outcome field.</summary>
/// <param name="Field">The outcome field name.</param>
/// <param name="Label">The text to show for the name.</param>
/// <param name="ValueType">The field's type, as the <c>FactType</c> member name.</param>
/// <param name="Required">True when a row must set the field; false when the kind declares it nullable.</param>
/// <param name="Values">The allowed members of the field; empty, because a kind descriptor does not carry them for outcome fields.</param>
public sealed record OutcomeChoices(string Field, string Label, string ValueType, bool Required, IReadOnlyList<ValueChoice> Values);

/// <summary>What a form may offer for one rule.</summary>
/// <param name="Facts">The facts of the rule's kind, in the descriptor's order.</param>
/// <param name="Outcomes">The outcome fields of the rule's kind, in the descriptor's order.</param>
/// <param name="HasDefaultRow">Whether the rule has a default row, so <see cref="SetDefaultOutcome"/> applies.</param>
/// <param name="HitPolicy">The table's hit policy as a document writes it: <c>unique</c>, <c>first</c> or <c>collect</c>.</param>
public sealed record RuleChoices(IReadOnlyList<FactChoices> Facts, IReadOnlyList<OutcomeChoices> Outcomes, bool HasDefaultRow, string HitPolicy);

/// <summary>Lists, for a loaded rule, the facts, operators and value types a form may offer, so a form cannot build an edit the contract would refuse.</summary>
public static class RuleChoiceCatalog
{
    private static readonly Operator[] AllOperators = Enum.GetValues<Operator>();

    /// <summary>
    /// The choices of a rule, from its kind descriptor. The operators of a fact are those the engine accepts for its type
    /// (<see cref="OperatorMatrix"/>, the check <c>ConditionMatcher.Create</c> makes), so the list can not drift from the engine.
    /// </summary>
    /// <param name="rule">The loaded rule.</param>
    /// <param name="labels">Display names; a missing label falls back to a humanised name.</param>
    /// <param name="locale">The language of the value texts.</param>
    public static RuleChoices For(BrainRule rule, RuleLabels labels, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(labels);

        var facts = rule.Kind.Facts.Select(fact => new FactChoices(
            fact.Name,
            labels.Facts.TryGetValue(fact.Name, out var label) ? label : WorkbenchText.Humanize(fact.Name),
            fact.Type.ToString(),
            [.. AllOperators.Where(op => OperatorMatrix.IsAllowed(op, fact.Type)).Select(op => op.ToString())],
            [.. (fact.AllowedValues ?? []).Select(member => new ValueChoice(DecisionValue.Enum(member), WorkbenchText.FormatValue(DecisionValue.Enum(member), fact.Name, locale, labels)))]));
        var outcomes = rule.Kind.OutcomeFields.Select(field => new OutcomeChoices(
            field.Name,
            labels.OutcomeFields.TryGetValue(field.Name, out var label) ? label : WorkbenchText.Humanize(field.Name),
            field.Type.ToString(),
            !field.Nullable,
            []));

        return new RuleChoices([.. facts], [.. outcomes], rule.Table.DefaultRow is not null, HitPolicyName(rule.Table.HitPolicy));
    }

    private static string HitPolicyName(HitPolicy policy) => policy switch
    {
        HitPolicy.Unique => "unique",
        HitPolicy.First => "first",
        _ => "collect",
    };
}
