namespace Nuvora.Decisions.Contracts.Goldens;

/// <summary>
/// Well-known reference ids that a platform golden case uses when a <see cref="Kinds.FactType.Reference"/> fact is
/// required but the case must not point at a real entity (a platform default is decided by its kind rows and its
/// default row, never by one tenant's record). None of them is ever the id of a real entity.
/// </summary>
/// <remarks>
/// The label service answers these ids itself, with <see cref="Labels.ReferenceLabel.IsPlaceholder"/> set, so the
/// views show them as an example value instead of a broken reference. The values are already stored in tenant
/// versions and goldens: they are append-only, never changed or removed.
/// </remarks>
public static class GoldenPlaceholders
{
    /// <summary>The placeholder sales channel of the <c>accounting.channel_settlement</c> platform default goldens.</summary>
    public static readonly Guid ChannelSettlementPlatformDefault = new("5e7713b0-0000-4000-8000-00000000c0de");

    /// <summary>The placeholder sales channel of the <c>accounting.channel_settlement</c> v2 policy goldens.</summary>
    public static readonly Guid ChannelSettlementPolicyV2 = new("5e7713b0-0000-4000-8000-00000000c0d2");

    /// <summary>Every placeholder id.</summary>
    public static IReadOnlySet<Guid> All { get; } = new HashSet<Guid> { ChannelSettlementPlatformDefault, ChannelSettlementPolicyV2 };

    /// <summary>True when <paramref name="id"/> is a placeholder id, never a real entity.</summary>
    public static bool IsPlaceholder(Guid id) => All.Contains(id);
}
