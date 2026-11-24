using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>A test as it stood when the proposal was made.</summary>
/// <param name="Name">The name of the test.</param>
/// <param name="Passed">Whether it passed.</param>
/// <param name="Failure">Why it did not, in the words of the player; null when it passed.</param>
public sealed record FlowProposalTest(string Name, bool Passed, string? Failure);

/// <summary>A route that no test takes.</summary>
/// <param name="Step">The step id.</param>
/// <param name="Route">The route label.</param>
public sealed record FlowProposalRoute(string Step, string Route);

/// <summary>The change list in one language.</summary>
/// <param name="Summary">The counts in a sentence.</param>
/// <param name="Items">One sentence per change.</param>
/// <param name="Behaviour">One sentence per test of the base that now behaves differently.</param>
public sealed record FlowProposalChanges(string Summary, IReadOnlyList<string> Items, IReadOnlyList<string> Behaviour);

/// <summary>
/// A draft flow sent to the team that reviews flows: the base it came from, the full new document, what changes in plain language (in both
/// languages), the tests and the route coverage at the time, who asked and when. It changes nothing: a person applies it elsewhere.
/// </summary>
public sealed record FlowProposal
{
    /// <summary>The id of the process.</summary>
    public required string ProcessId { get; init; }

    /// <summary>The version of the new document.</summary>
    public required long Version { get; init; }

    /// <summary>The version of the approved document it came from; null for a new process.</summary>
    public long? BaseVersion { get; init; }

    /// <summary>The content hash of the approved document it came from; null for a new process.</summary>
    public string? BaseContentHash { get; init; }

    /// <summary>The name the person gave. It must not identify a patient.</summary>
    public required string Author { get; init; }

    /// <summary>When the proposal was made, by the host clock.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The language the person asked in.</summary>
    public required WorkbenchLocale Language { get; init; }

    /// <summary>The person's own words about the change. It must not identify a patient.</summary>
    public required string Summary { get; init; }

    /// <summary>Always true for a process: our team reviews every change to a process before it is activated.</summary>
    public bool NeedsReview { get; init; } = true;

    /// <summary>The full new document, formatted as <see cref="FlowProposalJson"/> writes it.</summary>
    public required string DocumentJson { get; init; }

    /// <summary>The hash of <see cref="DocumentJson"/>, which is the content hash the document has once it is applied as written.</summary>
    public required string DocumentHash { get; init; }

    /// <summary>The change list in English.</summary>
    public required FlowProposalChanges ChangesEn { get; init; }

    /// <summary>The change list in Spanish.</summary>
    public required FlowProposalChanges ChangesEs { get; init; }

    /// <summary>The tests at the time of the proposal.</summary>
    public required IReadOnlyList<FlowProposalTest> Tests { get; init; }

    /// <summary>The number of routes of the document.</summary>
    public required int TotalRoutes { get; init; }

    /// <summary>The number of routes at least one test takes.</summary>
    public required int CoveredRoutes { get; init; }

    /// <summary>The routes no test takes.</summary>
    public required IReadOnlyList<FlowProposalRoute> NeverTaken { get; init; }

    /// <summary>What the author accepted knowingly.</summary>
    public required IReadOnlyList<KnownGap> KnownGaps { get; init; }

    /// <summary>The change list in a language.</summary>
    /// <param name="locale">The language.</param>
    public FlowProposalChanges Changes(WorkbenchLocale locale) => locale == WorkbenchLocale.Es ? ChangesEs : ChangesEn;
}

/// <summary>The result of reading a proposal document.</summary>
/// <param name="Proposal">The proposal; null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">Every defect found, ordered by path.</param>
public sealed record FlowProposalParseResult(FlowProposal? Proposal, IReadOnlyList<WorkbenchError> Errors)
{
    /// <summary>True when the document was read without errors.</summary>
    public bool IsValid => Errors.Count == 0;
}
