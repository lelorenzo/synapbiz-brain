using System.Diagnostics.CodeAnalysis;

namespace Nuvora.Decisions.Contracts.Kinds;

/// <summary>Scalar type of a decision fact or outcome field (D-04).</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Code/Enum/Boolean/Integer/Decimal/Money/Date/Reference is the fact-type vocabulary pinned by decision D-04; renaming 'Decimal' would break that shared terminology.")]
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (mirrors Nuvora.Modules.Accounting/Devices' documented CA1008 suppression): default(FactType) == 0 is deliberately not a valid fact type, catching an uninitialized descriptor instead of silently treating it as Code.")]
public enum FactType
{
    /// <summary>An opaque string code (e.g. a channel code). The only type <see cref="Tables.Operator.StartsWith"/> is allowed on.</summary>
    Code = 1,

    /// <summary>A closed value set declared by the kind (<see cref="FactDescriptor.AllowedValues"/>).</summary>
    Enum = 2,

    /// <summary>True/false.</summary>
    Boolean = 3,

    /// <summary>A whole number.</summary>
    Integer = 4,

    /// <summary>An exact decimal number with no currency.</summary>
    Decimal = 5,

    /// <summary>A decimal amount with an ISO 4217 currency; matches only a fact of the same currency, never converts (D-11).</summary>
    Money = 6,

    /// <summary>A calendar date, no time-of-day.</summary>
    Date = 7,

    /// <summary>An opaque identifier (e.g. a party id).</summary>
    Reference = 8,
}

/// <summary>Evaluation mode of a decision kind (D-07).</summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (mirrors Nuvora.Modules.Accounting/Devices' documented CA1008 suppression): default(EvaluationMode) == 0 is deliberately not a valid mode, catching an uninitialized descriptor instead of silently treating it as Materialized.")]
public enum EvaluationMode
{
    /// <summary>Evaluated over a finite domain at activation / domain change / reconciliation and read as decided rows; never evaluated in a shopper request path (AT-DEC-05).</summary>
    Materialized = 1,

    /// <summary>Evaluated synchronously inside the back-office command or consumer that needs it.</summary>
    OnDemand = 2,
}

/// <summary>
/// Says which module resolves the display label of a <see cref="FactType.Reference"/> fact or outcome field, and for
/// which entity type (D-24d, D-25 ii). Labels are display-only: they are looked up when a screen needs them and are
/// never stored in a version, a trace, a diff or a golden.
/// </summary>
/// <param name="OwnerModule">Id of the module that owns the referenced entity (e.g. <c>sales</c>), or <c>platform</c> for the kernel's own sources.</param>
/// <param name="EntityType">Stable entity type name, unique within the owner module's resolvers (e.g. <c>sales.sales_channel</c>).</param>
public sealed record ReferenceLabelSource(string OwnerModule, string EntityType);

/// <summary>
/// Declares one fact a decision kind's tables may condition on (D-04).
/// </summary>
/// <param name="Name">Stable fact name, unique within the kind.</param>
/// <param name="Type">The fact's scalar type.</param>
/// <param name="Nullable">Whether a caller may omit this fact (it then evaluates as absent).</param>
/// <param name="AllowedValues">The closed value set when <paramref name="Type"/> is <see cref="FactType.Enum"/>; otherwise null.</param>
/// <param name="ComposedFromKind">
/// When set, this fact is never supplied directly by the caller and
/// <c>FactSet.Build</c> does not require its presence: it is resolved by
/// evaluating <paramref name="ComposedFromKind"/> first, with the same
/// <c>asOf</c>, through the caller-supplied <c>IComposedFactResolver</c>
/// (D-14, wired by the engine's compiler/evaluator in P2-20.2 steps 3/4).
/// </param>
/// <param name="LabelSource">
/// Where the display label of a <see cref="FactType.Reference"/> fact comes from (D-24d). Required on a Reference
/// fact and refused on any other type; the kind registry enforces both at startup.
/// </param>
/// <param name="CatalogFactId">Optional shared catalogue identity; the persisted kind-local Name remains unchanged.</param>
public sealed record FactDescriptor(
    string Name,
    FactType Type,
    bool Nullable = false,
    IReadOnlyList<string>? AllowedValues = null,
    DecisionKindKey? ComposedFromKind = null,
    ReferenceLabelSource? LabelSource = null,
    string? CatalogFactId = null);

/// <summary>Declares one field of a decision kind's outcome (D-04).</summary>
/// <param name="Name">Stable field name, unique within the kind.</param>
/// <param name="Type">The field's scalar type.</param>
/// <param name="Nullable">Whether a row may leave this field out.</param>
/// <param name="LabelSource">Where the display label of a <see cref="FactType.Reference"/> field comes from (D-24d); required on a Reference field and refused on any other type.</param>
public sealed record OutcomeFieldDescriptor(
    string Name,
    FactType Type,
    bool Nullable = false,
    ReferenceLabelSource? LabelSource = null);

/// <summary>
/// Declares a decision kind: its fact schema, outcome shape, evaluation mode
/// and the permissions that gate reading, authoring and approving its
/// rulesets (D-02, D-04, D-05).
/// </summary>
public sealed record DecisionKindDescriptor
{
    /// <summary>Default cap on the canonical fact-set size, in UTF-8 bytes (decisions.facts.too_large, D-22).</summary>
    public const int DefaultMaxFactsBytes = 4096;

    /// <summary>Stable kind identifier; its <see cref="DecisionKindKey.Module"/> MUST match the registering module.</summary>
    public required DecisionKindKey Key { get; init; }

    /// <summary>Schema version of the fact/outcome shape itself (not a ruleset version — that is P2-20.3).</summary>
    public required int SchemaVersion { get; init; }

    public required IReadOnlyList<FactDescriptor> Facts { get; init; }

    public required IReadOnlyList<OutcomeFieldDescriptor> OutcomeFields { get; init; }

    public required EvaluationMode EvaluationMode { get; init; }

    /// <summary>
    /// When true, the kind's tables must cover every combination of its
    /// enumerable (Enum/Boolean) columns or declare a default row
    /// (TableValidator, P2-20.2.4) — a materialized kind's whole domain must
    /// be decided before activation (D-07).
    /// </summary>
    public bool RequiresTotalCoverage { get; init; }

    public required string ReadPermission { get; init; }

    public required string AuthorPermission { get; init; }

    public required string ApprovePermission { get; init; }

    /// <summary>Cap on the canonical fact-set size, in UTF-8 bytes (decisions.facts.too_large).</summary>
    public int MaxFactsBytes { get; init; } = DefaultMaxFactsBytes;

    /// <summary>
    /// Whether the kind's facts or outcomes can carry personal data (D-22). Nullable only so a descriptor that never
    /// stated it can be refused at registration instead of silently defaulting to "no personal data".
    /// </summary>
    public bool? ContainsPersonalData { get; init; }

    /// <summary>Permission that allows reading the evaluation traces of this kind (<c>&lt;module&gt;.decisions.read_trace</c>).</summary>
    public required string ReadTracePermission { get; init; }
}
