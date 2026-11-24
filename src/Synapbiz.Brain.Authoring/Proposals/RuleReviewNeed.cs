using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>
/// Decides whether a rule proposal needs our team's review (owner decision of 2026-11-23): a proposal that stays within the facts the rule
/// reads and the outcomes it gives is applied by the clinic that approves it; one that goes beyond them waits for review. The server applies
/// nothing either way.
/// </summary>
public static class RuleReviewNeed
{
    /// <summary>The decision kind of the rule changed.</summary>
    public const string KindChanged = "kind_changed";

    /// <summary>The hit policy changed.</summary>
    public const string HitPolicyChanged = "hit_policy_changed";

    /// <summary>A row reads a fact that no row of the approved rule read.</summary>
    public const string NewFactRead = "new_fact_read";

    /// <summary>A row gives an outcome field that no row of the approved rule gave.</summary>
    public const string NewOutcomeField = "new_outcome_field";

    /// <summary>A row gives a code or a category (an outcome field of type Code or Enum) with a value that no row of the approved rule gave.</summary>
    public const string NewOutcomeValue = "new_outcome_value";

    /// <summary>The sources the rule cites (<c>meta.sources</c>) or its authority (<c>meta.authority</c>) changed: it changes what the rule claims to follow.</summary>
    public const string ClaimsChanged = "claims_changed";

    /// <summary>Every reason code, in the order they are reported.</summary>
    public static IReadOnlyList<string> Codes { get; } = [KindChanged, HitPolicyChanged, NewFactRead, NewOutcomeField, NewOutcomeValue, ClaimsChanged];

    /// <summary>The reasons the proposal needs review; empty when it does not.</summary>
    /// <param name="before">The approved rule.</param>
    /// <param name="after">The draft.</param>
    public static IReadOnlyList<string> Reasons(BrainRule before, BrainRule after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var reasons = new List<string>();
        if (!string.Equals(before.Kind.Key.Value, after.Kind.Key.Value, StringComparison.Ordinal))
        {
            reasons.Add(KindChanged);
        }

        if (before.Table.HitPolicy != after.Table.HitPolicy)
        {
            reasons.Add(HitPolicyChanged);
        }

        if (!FactsRead(after).IsSubsetOf(FactsRead(before)))
        {
            reasons.Add(NewFactRead);
        }

        var fieldsBefore = Outcomes(before);
        var fieldsAfter = Outcomes(after);
        if (!fieldsAfter.Keys.All(fieldsBefore.ContainsKey))
        {
            reasons.Add(NewOutcomeField);
        }

        var categorical = after.Kind.OutcomeFields.Where(field => field.Type is FactType.Code or FactType.Enum).Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        if (fieldsAfter.Any(pair => categorical.Contains(pair.Key) && fieldsBefore.TryGetValue(pair.Key, out var known) && !pair.Value.IsSubsetOf(known)))
        {
            reasons.Add(NewOutcomeValue);
        }

        if (RuleClaims.Differ(before.Meta, after.Meta))
        {
            reasons.Add(ClaimsChanged);
        }

        return reasons;
    }

    private static HashSet<string> FactsRead(BrainRule rule) =>
        [.. rule.Table.Rows.SelectMany(row => row.Conditions).Where(condition => condition.Operator != Operator.Any).Select(condition => condition.FactName)];

    private static Dictionary<string, HashSet<string>> Outcomes(BrainRule rule)
    {
        var fields = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var row in rule.Table.Rows.Concat(rule.Table.DefaultRow is { } fallback ? [fallback] : []))
        {
            if (AuthoringJson.TryObject(row.Outcome) is not { } outcome)
            {
                continue;
            }

            foreach (var (name, value) in outcome)
            {
                if (!fields.TryGetValue(name, out var values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    fields[name] = values;
                }

                values.Add(AuthoringJson.Text(value) ?? AuthoringJson.CompactText(value));
            }
        }

        return fields;
    }
}
