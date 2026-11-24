using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Workbench;

/// <summary>How a rule relates to a source it cites in <c>meta.sources[].relation</c>.</summary>
public enum SourceRelation
{
    /// <summary>The rule implements the source as it is written.</summary>
    Implements = 0,

    /// <summary>The rule adapts the source: it differs from it where <see cref="RuleSource.Differences"/> says so.</summary>
    Adapts = 1,

    /// <summary>The source informed the rule without being implemented.</summary>
    InformedBy = 2,
}

/// <summary>One way a rule differs from its source, in both languages as the document writes it.</summary>
/// <param name="En">The English text.</param>
/// <param name="Es">The Spanish text.</param>
public sealed record RuleSourceDifference(string En, string Es)
{
    /// <summary>The text in a language.</summary>
    /// <param name="locale">The language.</param>
    public string Text(WorkbenchLocale locale) => locale == WorkbenchLocale.Es ? Es : En;
}

/// <summary>A source a rule cites, read from one entry of <c>meta.sources</c>.</summary>
/// <param name="Source">The id of the source, as the document writes it (the host may have a label for it).</param>
/// <param name="Version">The version of the source the rule follows.</param>
/// <param name="Relation">How the rule relates to the source.</param>
/// <param name="Differences">How the rule differs from the source, as the document lists them; empty when it lists none.</param>
public sealed record RuleSource(string Source, string Version, SourceRelation Relation, IReadOnlyList<RuleSourceDifference> Differences);

/// <summary>An entry of <c>meta.sources</c>: the source it states, or null when the entry has an unknown relation or a wrong shape.</summary>
/// <param name="Index">The position of the entry in <c>meta.sources</c>.</param>
/// <param name="Source">The source; null when the entry cannot be read.</param>
public sealed record RuleSourceEntry(int Index, RuleSource? Source)
{
    /// <summary>True when the entry could be read.</summary>
    public bool IsReadable => Source is not null;
}

/// <summary>
/// What a rule document claims about itself in <c>meta</c>: the sources it cites (<c>meta.sources[]</c>) and its authority
/// (<c>meta.authority</c>, a code such as <c>payment_condition</c> or <c>recommendation</c>). Brain renders what the document states and
/// hard-codes no clinical source. <c>meta</c> stays an open object; the reader never throws, whatever shape it has: an entry of the
/// wrong shape is reported as unreadable and the rest is read.
/// </summary>
public sealed record RuleClaims
{
    private static readonly string[] RelationNames = ["implements", "adapts", "informed_by"];

    /// <summary>Claims with nothing in them.</summary>
    public static RuleClaims Empty { get; } = new() { Sources = [], Authority = null };

    /// <summary>One entry for each element of <c>meta.sources</c>, in document order. A <c>sources</c> member that is not an array gives one unreadable entry.</summary>
    public required IReadOnlyList<RuleSourceEntry> Sources { get; init; }

    /// <summary>The authority code; null when the rule states none or the member is not a text.</summary>
    public required string? Authority { get; init; }

    /// <summary>True when the rule cites a source or states an authority.</summary>
    public bool HasAny => Sources.Count > 0 || Authority is not null;

    /// <summary>The readable sources, in document order.</summary>
    public IEnumerable<RuleSource> Readable => Sources.Where(entry => entry.IsReadable).Select(entry => entry.Source!);

    /// <summary>Reads the claims of the <c>meta</c> object of a rule document.</summary>
    /// <param name="meta">The <c>meta</c> object, for example <c>BrainRule.Meta</c>.</param>
    public static RuleClaims From(JsonObject meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        return new RuleClaims { Sources = ReadSources(meta["sources"]), Authority = ReadAuthority(meta["authority"]) };
    }

    /// <summary>The word of a relation in a language: "implements", "adapts", "informed by".</summary>
    /// <param name="relation">The relation.</param>
    /// <param name="locale">The language.</param>
    public static string RelationWord(SourceRelation relation, WorkbenchLocale locale) => SentenceTemplates.Text(locale, relation switch
    {
        SourceRelation.Implements => "claims.relation.implements",
        SourceRelation.Adapts => "claims.relation.adapts",
        SourceRelation.InformedBy => "claims.relation.informed_by",
        _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, "Unknown relation."),
    });

    /// <summary>True when the sources or the authority of the two documents differ. The parsed values are compared, so the order of the members of an entry (<c>source</c>, <c>version</c> and so on) does not matter; the order of the entries and of the differences does.</summary>
    /// <param name="before">The <c>meta</c> of the earlier revision.</param>
    /// <param name="after">The <c>meta</c> of the later revision.</param>
    public static bool Differ(JsonObject before, JsonObject after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return !JsonNode.DeepEquals(before["sources"], after["sources"]) || !JsonNode.DeepEquals(before["authority"], after["authority"]);
    }

    /// <summary>
    /// What changed in the sources and the authority, one sentence each, in plain language: a source added, removed, followed in another
    /// version, related in another way or with other differences, and the authority set, cleared or changed. When the text differs but no
    /// sentence of those fits (an entry that cannot be read changed), one general sentence says so. Empty when nothing changed.
    /// </summary>
    /// <param name="before">The <c>meta</c> of the earlier revision.</param>
    /// <param name="after">The <c>meta</c> of the later revision.</param>
    /// <param name="locale">The language of the sentences.</param>
    public static IReadOnlyList<string> Changes(JsonObject before, JsonObject after, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var sentences = new List<string>();
        var earlier = From(before);
        var later = From(after);
        var oldBySource = earlier.Readable.GroupBy(source => source.Source, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var newBySource = later.Readable.GroupBy(source => source.Source, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var source in later.Readable.Where(source => !oldBySource.ContainsKey(source.Source)))
        {
            sentences.Add(SentenceTemplates.Text(locale, "claims.source.added", ("source", Code(source.Source)), ("version", source.Version), ("relation", RelationWord(source.Relation, locale))));
        }

        foreach (var source in earlier.Readable.Where(source => !newBySource.ContainsKey(source.Source)))
        {
            sentences.Add(SentenceTemplates.Text(locale, "claims.source.removed", ("source", Code(source.Source))));
        }

        foreach (var source in later.Readable.Where(source => oldBySource.ContainsKey(source.Source)))
        {
            var was = oldBySource[source.Source];
            if (!string.Equals(was.Version, source.Version, StringComparison.Ordinal))
            {
                sentences.Add(SentenceTemplates.Text(locale, "claims.source.version", ("source", Code(source.Source)), ("was", was.Version), ("now", source.Version)));
            }

            if (was.Relation != source.Relation)
            {
                sentences.Add(SentenceTemplates.Text(locale, "claims.source.relation", ("source", Code(source.Source)), ("was", RelationWord(was.Relation, locale)), ("now", RelationWord(source.Relation, locale))));
            }

            if (!was.Differences.SequenceEqual(source.Differences))
            {
                sentences.Add(SentenceTemplates.Text(locale, "claims.source.differences", ("source", Code(source.Source))));
            }
        }

        if (!string.Equals(earlier.Authority, later.Authority, StringComparison.Ordinal))
        {
            sentences.Add(AuthorityChange(earlier.Authority, later.Authority, locale));
        }

        if (sentences.Count == 0 && Differ(before, after))
        {
            sentences.Add(SentenceTemplates.Text(locale, "claims.other"));
        }

        return sentences;
    }

    private static string AuthorityChange(string? was, string? now, WorkbenchLocale locale)
    {
        if (was is null)
        {
            return SentenceTemplates.Text(locale, "claims.authority.set", ("authority", Code(now!)));
        }

        return now is null
            ? SentenceTemplates.Text(locale, "claims.authority.cleared", ("authority", Code(was)))
            : SentenceTemplates.Text(locale, "claims.authority.changed", ("was", Code(was)), ("now", Code(now)));
    }

    private static string Code(string value) => "`" + value + "`";

    private static string? ReadAuthority(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static List<RuleSourceEntry> ReadSources(JsonNode? node)
    {
        if (node is null)
        {
            return [];
        }

        if (node is not JsonArray array)
        {
            return [new RuleSourceEntry(0, null)];
        }

        return [.. array.Select((item, index) => new RuleSourceEntry(index, ReadSource(item)))];
    }

    private static RuleSource? ReadSource(JsonNode? node)
    {
        if (node is not JsonObject entry
            || Text(entry["source"]) is not { } source
            || Text(entry["version"]) is not { } version
            || Text(entry["relation"]) is not { } relationText
            || Array.IndexOf(RelationNames, relationText) is var relation && relation < 0)
        {
            return null;
        }

        var differences = new List<RuleSourceDifference>();
        if (entry["differences"] is { } listed)
        {
            if (listed is not JsonArray items)
            {
                return null;
            }

            foreach (var item in items)
            {
                if (item is not JsonObject difference || Text(difference["en"]) is not { } en || Text(difference["es"]) is not { } es)
                {
                    return null;
                }

                differences.Add(new RuleSourceDifference(en, es));
            }
        }

        return new RuleSource(source, version, (SourceRelation)relation, differences);
    }

    // A text member: a string, or a number written as text (a version may be 2024); never empty.
    private static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return value.TryGetValue<long>(out var number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }
}
