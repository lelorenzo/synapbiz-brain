using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Workbench;

/// <summary>
/// Display names for the facts, outcome fields, values and rows of a rule, in one locale. A rule may carry them under
/// <c>meta.display.&lt;en|es&gt;</c> and a host may supply its own. A missing label falls back to a humanised name.
/// Labels never change a decision.
/// </summary>
public sealed record RuleLabels
{
    private static readonly IReadOnlyDictionary<string, string> NoTexts = new Dictionary<string, string>(0, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NoValues =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(0, StringComparer.Ordinal);

    private IReadOnlyDictionary<string, string> facts = NoTexts;
    private IReadOnlyDictionary<string, string> outcomeFields = NoTexts;
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values = NoValues;
    private IReadOnlyDictionary<string, string> rows = NoTexts;

    /// <summary>Labels with nothing in them.</summary>
    public static RuleLabels Empty { get; } = new();

    /// <summary>The title of the rule; null when none is given.</summary>
    public string? Title { get; init; }

    /// <summary>Fact name to label. Never null.</summary>
    public IReadOnlyDictionary<string, string> Facts
    {
        get => facts;
        init => facts = value ?? NoTexts;
    }

    /// <summary>Outcome field name to label. Never null.</summary>
    public IReadOnlyDictionary<string, string> OutcomeFields
    {
        get => outcomeFields;
        init => outcomeFields = value ?? NoTexts;
    }

    /// <summary>Fact or outcome field name to (raw value to label). Never null.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Values
    {
        get => values;
        init => values = value ?? NoValues;
    }

    /// <summary>Row id to title. Never null.</summary>
    public IReadOnlyDictionary<string, string> Rows
    {
        get => rows;
        init => rows = value ?? NoTexts;
    }

    /// <summary>
    /// Reads the labels of one locale from <c>meta.display.en</c> or <c>meta.display.es</c> of a rule document: <c>title</c>
    /// (a string), <c>facts</c>, <c>outcomes</c> and <c>rows</c> (objects of strings) and <c>values</c> (an object of
    /// objects of strings). A member that is absent or of another JSON kind is ignored, entry by entry; the method never
    /// throws on content.
    /// </summary>
    /// <param name="meta">The <c>meta</c> object of the rule, for example <c>BrainRule.Meta</c>.</param>
    /// <param name="locale">The locale to read.</param>
    public static RuleLabels FromMeta(JsonObject meta, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var tag = locale == WorkbenchLocale.Es ? "es" : "en";
        if (meta["display"] is not JsonObject display || display[tag] is not JsonObject node)
        {
            return new RuleLabels();
        }

        return new RuleLabels
        {
            Title = TextOf(node["title"]),
            Facts = Texts(node["facts"]),
            OutcomeFields = Texts(node["outcomes"]),
            Values = Nested(node["values"]),
            Rows = Texts(node["rows"]),
        };
    }

    /// <summary>The label of a raw value of a fact or outcome field, or null when there is none.</summary>
    internal string? ValueLabel(string name, string raw) =>
        Values.TryGetValue(name, out var byValue) && byValue.TryGetValue(raw, out var label) ? label : null;

    private static string? TextOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static Dictionary<string, string> Texts(JsonNode? node)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is JsonObject entries)
        {
            foreach (var (name, entry) in entries)
            {
                if (TextOf(entry) is { } text)
                {
                    texts[name] = text;
                }
            }
        }

        return texts;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Nested(JsonNode? node)
    {
        var nested = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (node is JsonObject entries)
        {
            foreach (var (name, entry) in entries)
            {
                var texts = Texts(entry);
                if (texts.Count > 0)
                {
                    nested[name] = texts;
                }
            }
        }

        return nested;
    }
}
