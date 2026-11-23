using System.Globalization;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Validation;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>One reason a candidate draft was refused, in the terms of a form.</summary>
/// <param name="Code">The code of the loader, of the validator or of the drafter. It is never shown.</param>
/// <param name="Path">The JSON pointer the loader reported; empty for the validator.</param>
/// <param name="Place">The row, fact, outcome field or case the path is under.</param>
/// <param name="RowKeys">The keys of the rows the validator named; empty for the loader.</param>
/// <param name="Op">The operator the refused condition has in the document; null when the path names no condition.</param>
/// <param name="Value">The value at the path as the document writes it; null when there is none.</param>
/// <param name="Fact">The fact the path names: the fact of a condition or the fact of a test case.</param>
internal sealed record DraftProblem(
    string Code, string Path, DraftLocation Place, IReadOnlyList<string> RowKeys, string? Op, string? Value, string? Fact);

/// <summary>The result of reading a candidate draft: the text and the rule when the loader read it, and the reasons when it did not.</summary>
/// <param name="Text">The candidate text; null when the drafter refused the edits.</param>
/// <param name="Rule">The candidate as loaded; null when the loader refused it.</param>
/// <param name="Findings">The validator's findings of the candidate; null when the loader refused it.</param>
/// <param name="Problems">Why the candidate is refused; empty when it is accepted.</param>
internal sealed record RebuildResult(string? Text, BrainRule? Rule, DraftFindings? Findings, IReadOnlyList<DraftProblem> Problems)
{
    /// <summary>True when the candidate can become the draft.</summary>
    internal bool IsOk => Problems.Count == 0;
}

/// <summary>
/// Builds a draft from the base text and a list of steps and reads it with the host's loader, so that the draft is never left in
/// a state the loader refuses. It adds no rule of its own: the drafter writes the text, the loader and the engine's validator judge it.
/// </summary>
internal static class DraftEngine
{
    private static readonly DraftLocation Nowhere = new(null, null, false, null, null, null);

    /// <summary>Applies the edits of every step to the base and loads the result.</summary>
    internal static RebuildResult Rebuild(WorkbenchRule host, DraftState draft, IReadOnlyList<DraftStep> steps)
    {
        var drafted = RuleDrafter.Apply(draft.BaseText, [.. steps.SelectMany(step => step.Edits)]);
        if (!drafted.IsValid)
        {
            var error = drafted.Errors[0];
            return new RebuildResult(null, null, null, [new DraftProblem(error.Code, error.Path, Nowhere, [], null, null, null)]);
        }

        var text = drafted.DraftJson!;
        var loaded = host.LoadDraft!(text);
        if (!loaded.IsValid)
        {
            return new RebuildResult(text, null, null, [.. loaded.Errors.Select(error => Problem(text, error))]);
        }

        var rule = loaded.Rule!;
        var findings = DraftFindings.Of(rule);
        var problems = new List<DraftProblem>();
        if (rule.Table.HitPolicy == HitPolicy.Unique)
        {
            foreach (var pair in findings.Overlaps.Where(pair => !draft.BaseFindings.Overlaps.Contains(pair)))
            {
                problems.Add(new DraftProblem(TableValidator.CodeOverlap, string.Empty, Nowhere, pair.Split('|'), null, null, null));
            }
        }

        return new RebuildResult(text, rule, findings, problems);
    }

    /// <summary>The steps without the ones the predicate names.</summary>
    internal static List<DraftStep> Without(DraftState draft, Func<DraftStep, bool> remove) => [.. draft.Steps.Where(step => !remove(step))];

    /// <summary>
    /// Starts a draft of a rule: the base with no edit. When the host's loader refuses even that, the draft keeps the reasons and
    /// has no rule.
    /// </summary>
    internal static DraftState Start(WorkbenchRule host)
    {
        var draft = new DraftState(host.RuleText!, host.Rule!);
        var result = Rebuild(host, draft, []);
        if (result.IsOk)
        {
            draft.Commit([], result);
        }
        else
        {
            draft.Unreadable(result);
        }

        return draft;
    }

    /// <summary>
    /// Tries the steps as the draft: when the loader reads them (and no new overlap shows under a unique policy) they become the
    /// draft and the test cases run again; otherwise the draft is unchanged.
    /// </summary>
    internal static RebuildResult Apply(WorkbenchRule host, DraftState draft, IReadOnlyList<DraftStep> steps)
    {
        var result = Rebuild(host, draft, steps);
        if (result.IsOk)
        {
            draft.Commit(steps, result);
        }

        return result;
    }

    private static DraftProblem Problem(string text, BrainSpecError error)
    {
        var place = DraftErrorLocator.Locate(text, error.Path);
        var node = NodeAt(text, error.Path);
        var op = place.Fact is null ? null : OpAt(text, error.Path);
        var fact = place.Fact ?? FactOfCase(error.Path);
        return new DraftProblem(error.Code, error.Path, place, [], op, ValueOf(node), fact);
    }

    /// <summary>The fact a path under a test case names: <c>/body/goldens/{k}/facts/{name}</c>.</summary>
    private static string? FactOfCase(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 6 && parts[1] == "body" && parts[2] == "goldens" && parts[4] == "facts"
            ? parts[5].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)
            : null;
    }

    private static JsonNode? NodeAt(string text, string path)
    {
        JsonNode? node = JsonNode.Parse(text);
        foreach (var part in path.Split('/').Skip(1))
        {
            var name = part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            node = node switch
            {
                JsonObject container => container[name],
                JsonArray list when int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) => list.ElementAtOrDefault(index),
                _ => null,
            };
        }

        return node;
    }

    private static string? OpAt(string text, string path)
    {
        var parts = path.Split('/');
        var at = Array.IndexOf(parts, "conditions");
        var condition = NodeAt(text, string.Join('/', parts.Take(at + 2)));
        return condition is JsonObject container && container["op"] is JsonValue op && op.TryGetValue<string>(out var word) ? word : null;
    }

    private static string? ValueOf(JsonNode? node) => node switch
    {
        null => null,
        JsonObject tagged when tagged["value"] is { } inner => OutcomeValues.TextOf(inner),
        _ => OutcomeValues.TextOf(node),
    };
}

/// <summary>Reads an outcome as typed values and as text, the way the form shows and compares it.</summary>
internal static class OutcomeValues
{
    /// <summary>The fields of an outcome (an object, or the first element of an array) as typed values; a field that does not read is left out.</summary>
    internal static Dictionary<string, DecisionValue> Typed(DecisionKindDescriptor kind, string outcomeJson)
    {
        var values = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
        var node = JsonNode.Parse(outcomeJson);
        if ((node is JsonArray list ? list.FirstOrDefault() : node) is not JsonObject fields)
        {
            return values;
        }

        foreach (var field in kind.OutcomeFields)
        {
            if (fields[field.Name] is { } value
                && FactInput.Parse(new FactDescriptor(field.Name, field.Type, true), TextOf(value)) is { Value: { } typed })
            {
                values[field.Name] = typed;
            }
        }

        return values;
    }

    /// <summary>The text of a JSON value as <see cref="FactInput"/> reads it: a string as it is, a flag as true or false, a money object as amount and currency.</summary>
    internal static string TextOf(JsonNode node) => node switch
    {
        JsonObject money when money["amount"] is { } amount && money["currency"] is { } currency => TextOf(amount) + " " + TextOf(currency),
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString().Trim('"'),
    };
}
