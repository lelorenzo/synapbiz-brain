namespace Synapbiz.Brain.Authoring;

/// <summary>The approved document a draft was started from: its id, version and content hash. A draft of a new flow has none.</summary>
/// <param name="Id">The id of the approved document.</param>
/// <param name="Version">Its version.</param>
/// <param name="ContentHash">Its content hash, as the library of its kind writes it (bare hex for a flow, <c>sha256:</c> and hex for a rule).</param>
public sealed record DraftBase(string Id, long Version, string ContentHash);

/// <summary>
/// A route or a wait the author knowingly leaves without a test or without a timeout, with the reason. It is kept beside the draft,
/// not inside the <c>brain.process@1</c> document, which has no place for it.
/// </summary>
/// <param name="Check">What it excuses: <see cref="KnownGap.RouteUntested"/> or <see cref="KnownGap.WaitWithoutTimeout"/>.</param>
/// <param name="Step">The step id.</param>
/// <param name="Route">The route label (for example <c>onError</c> or <c>on:signed</c>) for an untested route; null otherwise.</param>
/// <param name="Reason">Why, in the author's words. It must not identify a person.</param>
public sealed record KnownGap(string Check, string Step, string? Route, string Reason)
{
    /// <summary>The route of a step is not taken by any test and the author says why.</summary>
    public const string RouteUntested = "route_untested";

    /// <summary>A <c>wait</c> step has no timeout and the author says why.</summary>
    public const string WaitWithoutTimeout = "wait_without_timeout";

    /// <summary>The shortest reason a gap may give, in characters; a shorter one is refused.</summary>
    public const int MinimumReasonLength = 10;
}

/// <summary>A draft as the store keeps it: the full document text, what it came from and what its author said about it.</summary>
/// <param name="Text">The document text exactly as saved; it may be a document the loader refuses.</param>
/// <param name="Base">The approved document it was started from, or null for a new document.</param>
/// <param name="KnownGaps">The gaps the author accepted knowingly (flows only).</param>
/// <param name="EditsJson">For a rule draft, the JSON array of every guided edit applied to the base, in order; null for a flow.</param>
public sealed record StoredDraft(string Text, DraftBase? Base, IReadOnlyList<KnownGap> KnownGaps, string? EditsJson = null);

/// <summary>
/// The workspace behind the tools: approved documents the tools can read and never change, and two areas they may write, the drafts and the
/// proposals. The tools reach no other storage, so an implementation is the only place where a file or a database is touched.
/// An implementation refuses an id that does not match <see cref="StoreIds"/> with <see cref="ArgumentException"/> and anything that would
/// reach outside the drafts or proposals with <see cref="AuthoringStoreException"/>.
/// </summary>
public interface IAuthoringStore
{
    /// <summary>The ids of the approved documents of a kind, in ordinal order.</summary>
    /// <param name="kind">The kind of document.</param>
    IReadOnlyList<string> ListApproved(DocumentKind kind);

    /// <summary>The text of an approved document, or null when there is none.</summary>
    /// <param name="kind">The kind of document.</param>
    /// <param name="id">The id.</param>
    string? ReadApproved(DocumentKind kind, string id);

    /// <summary>The text of the catalog document (<c>brain.catalog@1</c>) the host supplied, or null when it supplied none.</summary>
    string? ReadCatalog();

    /// <summary>The ids of the drafts of a kind, in ordinal order.</summary>
    /// <param name="kind">The kind of document.</param>
    IReadOnlyList<string> ListDrafts(DocumentKind kind);

    /// <summary>The draft with an id, or null.</summary>
    /// <param name="kind">The kind of document.</param>
    /// <param name="id">The id.</param>
    StoredDraft? ReadDraft(DocumentKind kind, string id);

    /// <summary>Creates or replaces a draft.</summary>
    /// <param name="kind">The kind of document.</param>
    /// <param name="id">The id.</param>
    /// <param name="draft">The draft.</param>
    void WriteDraft(DocumentKind kind, string id, StoredDraft draft);

    /// <summary>The names of the proposals, in ordinal order.</summary>
    IReadOnlyList<string> ListProposals();

    /// <summary>The text of a proposal, or null.</summary>
    /// <param name="name">The name of the proposal.</param>
    string? ReadProposal(string name);

    /// <summary>Writes a new proposal and returns where it is, as text for a person (for a folder, the relative path). An existing name is refused with <see cref="AuthoringStoreException"/>.</summary>
    /// <param name="name">The name of the proposal.</param>
    /// <param name="json">The text of the proposal.</param>
    string WriteProposal(string name, string json);
}

/// <summary>The store refused an operation: a name that is not an id, a path that would leave the drafts or proposals, a link that points outside, or a proposal that already exists.</summary>
public sealed class AuthoringStoreException : Exception
{
    /// <summary>Creates the exception.</summary>
    public AuthoringStoreException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What was refused.</param>
    public AuthoringStoreException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    /// <param name="message">What was refused.</param>
    /// <param name="innerException">The cause.</param>
    public AuthoringStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
