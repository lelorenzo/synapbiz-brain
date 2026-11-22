namespace Nuvora.Decisions.Contracts.Labels;

/// <summary>
/// The display label of one referenced entity (D-24d). Display-only: never stored in a version, a trace, a diff or a
/// golden.
/// </summary>
/// <param name="Id">The referenced entity id, as the caller asked for it.</param>
/// <param name="Label">The label to show, or null when the entity is unknown to the caller's tenant (the UI then shows the raw id).</param>
/// <param name="IsDeleted">True when the entity is soft-deleted: an old version may still name it, so it keeps its label.</param>
/// <param name="IsPlaceholder">
/// True when the id is a <see cref="Goldens.GoldenPlaceholders"/> id: an example value of a platform golden that names no
/// real entity. Its label is null and the UI shows it as an example value, not as a missing entity.
/// </param>
public sealed record ReferenceLabel(Guid Id, string? Label, bool IsDeleted, bool IsPlaceholder = false);
