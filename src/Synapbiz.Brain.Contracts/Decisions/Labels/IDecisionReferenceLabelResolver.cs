namespace Nuvora.Decisions.Contracts.Labels;

/// <summary>
/// Resolves the display labels of the entities a <see cref="Kinds.FactType.Reference"/> fact or outcome field points
/// at (D-24d, D-25 ii). Implemented ONLY by the module that owns the referenced entity, against its own context and its
/// own tenant filter, so the kernel never injects another module's <c>IXxxService</c> (R2/R3); the module implements
/// this Decisions contract, the same inversion as <see cref="Materialization.IDecisionMaterializer"/>. The one
/// exception is the kernel's platform-user source (owner module <c>platform</c>).
/// </summary>
/// <remarks>
/// Register it scoped, with plain DI, from the owning module's own service-collection extension. At most one resolver
/// may exist per (<see cref="OwnerModule"/>, entity type); the host refuses a duplicate, and refuses a kind whose label
/// source names a pair no resolver serves. A resolver that gets an id of another tenant, or an unknown id, returns a
/// <see cref="ReferenceLabel"/> with a null label for it (or omits it); it never names another tenant's entity.
/// </remarks>
public interface IDecisionReferenceLabelResolver
{
    /// <summary>Id of the module that owns the entities this resolver labels (matches <see cref="Kinds.ReferenceLabelSource.OwnerModule"/>).</summary>
    string OwnerModule { get; }

    /// <summary>The entity types this resolver labels (matches <see cref="Kinds.ReferenceLabelSource.EntityType"/>).</summary>
    IReadOnlyCollection<string> EntityTypes { get; }

    /// <summary>
    /// Returns one <see cref="ReferenceLabel"/> per id it knows in the current tenant. An id it does not return is
    /// treated by the caller as an unknown id (null label).
    /// </summary>
    /// <param name="entityType">One of <see cref="EntityTypes"/>.</param>
    /// <param name="ids">The distinct ids to label; at most 200.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<ReferenceLabel>> ResolveAsync(
        string entityType,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}
