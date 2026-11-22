using System.Diagnostics.CodeAnalysis;

namespace Nuvora.Decisions.Contracts.Kinds;

/// <summary>The kinds registered by the modules of this host. Registration happens once, at startup.</summary>
public interface IDecisionKindRegistry
{
    /// <summary>Every registered kind, ordered by key.</summary>
    IReadOnlyList<IDecisionKindDeclaration> All { get; }

    /// <summary>Adds a declaration, refusing a duplicate key, a module mismatch, a missing data classification or a missing or foreign permission.</summary>
    void Register(IDecisionKindDeclaration declaration);

    /// <summary>Looks a kind up by key.</summary>
    bool TryGet(DecisionKindKey key, [NotNullWhen(true)] out IDecisionKindDeclaration? declaration);
}
