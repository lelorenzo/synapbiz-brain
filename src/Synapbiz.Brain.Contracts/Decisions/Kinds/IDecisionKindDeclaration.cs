using Nuvora.Decisions.Contracts.Validation;

namespace Nuvora.Decisions.Contracts.Kinds;

/// <summary>
/// What an owning module declares for one decision kind. A module registers it with plain DI from its own
/// service-collection extension: <c>services.AddSingleton&lt;IDecisionKindDeclaration&gt;(new MyKind())</c>. The
/// declaration (and the fact and outcome types it describes) lives in the owning module's Contracts assembly.
/// </summary>
public interface IDecisionKindDeclaration
{
    /// <summary>Id of the module that owns the kind; it must equal the module segment of the kind key.</summary>
    string OwningModuleId { get; }

    /// <summary>Facts, outcome fields, evaluation mode, permissions and data classification.</summary>
    DecisionKindDescriptor Descriptor { get; }

    /// <summary>Semantic invariants of the kind's outcomes, run at submission; null when the kind has none.</summary>
    IDecisionOutcomeValidator? OutcomeValidator { get; }

    /// <summary>Schema of the owning module that holds this kind's <c>decision_traces</c>; null when it writes no traces.</summary>
    string? TraceSchema { get; }
}
