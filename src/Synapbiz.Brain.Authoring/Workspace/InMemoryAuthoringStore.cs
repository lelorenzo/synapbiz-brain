namespace Synapbiz.Brain.Authoring;

/// <summary>
/// A workspace held in memory, for tests and for hosts that keep their documents elsewhere. The approved documents are given at
/// construction and never change; drafts and proposals live in dictionaries.
/// </summary>
public sealed class InMemoryAuthoringStore : IAuthoringStore
{
    private readonly Dictionary<string, string> flows;
    private readonly Dictionary<string, string> rules;
    private readonly string? catalog;
    private readonly Dictionary<(DocumentKind Kind, string Id), StoredDraft> drafts = [];
    private readonly Dictionary<string, string> proposals = new(StringComparer.Ordinal);

    /// <summary>Creates a workspace.</summary>
    /// <param name="flows">The approved flows by id: the text of each <c>brain.process@1</c> document.</param>
    /// <param name="rules">The approved rules by id: the text of each <c>brain.rule@1</c> document.</param>
    /// <param name="catalogJson">The text of the <c>brain.catalog@1</c> document, or null for none.</param>
    public InMemoryAuthoringStore(
        IReadOnlyDictionary<string, string>? flows = null,
        IReadOnlyDictionary<string, string>? rules = null,
        string? catalogJson = null)
    {
        this.flows = Copy(flows);
        this.rules = Copy(rules);
        catalog = catalogJson;
    }

    /// <summary>The names of the proposals written so far.</summary>
    public IReadOnlyCollection<string> ProposalNames => proposals.Keys;

    /// <inheritdoc />
    public IReadOnlyList<string> ListApproved(DocumentKind kind) => [.. Approved(kind).Keys.Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public string? ReadApproved(DocumentKind kind, string id)
    {
        StoreIds.Require(id, nameof(id));
        return Approved(kind).GetValueOrDefault(id);
    }

    /// <inheritdoc />
    public string? ReadCatalog() => catalog;

    /// <inheritdoc />
    public IReadOnlyList<string> ListDrafts(DocumentKind kind) =>
        [.. drafts.Keys.Where(key => key.Kind == kind).Select(key => key.Id).Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public StoredDraft? ReadDraft(DocumentKind kind, string id)
    {
        StoreIds.Require(id, nameof(id));
        return drafts.GetValueOrDefault((kind, id));
    }

    /// <inheritdoc />
    public void WriteDraft(DocumentKind kind, string id, StoredDraft draft)
    {
        StoreIds.Require(id, nameof(id));
        ArgumentNullException.ThrowIfNull(draft);
        drafts[(kind, id)] = draft;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListProposals() => [.. proposals.Keys.Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public string? ReadProposal(string name)
    {
        StoreIds.Require(name, nameof(name));
        return proposals.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    public string WriteProposal(string name, string json)
    {
        StoreIds.Require(name, nameof(name));
        ArgumentNullException.ThrowIfNull(json);
        if (!proposals.TryAdd(name, json))
        {
            throw new AuthoringStoreException("A proposal with that name exists already.");
        }

        return "proposals/" + name + ".json";
    }

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string>? source)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, text) in source ?? new Dictionary<string, string>())
        {
            StoreIds.Require(id, nameof(source));
            copy[id] = text;
        }

        return copy;
    }

    private Dictionary<string, string> Approved(DocumentKind kind) => kind == DocumentKind.Flow ? flows : rules;
}
