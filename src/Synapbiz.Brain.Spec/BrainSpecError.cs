namespace Synapbiz.Brain.Spec;

/// <summary>
/// A diagnostic reported while loading a <c>brain.rule@1</c> document: a stable code, the RFC 6901 JSON Pointer of the
/// offending value and the message and fix hint in English and Spanish.
/// </summary>
/// <param name="Code">Stable diagnostic code.</param>
/// <param name="Path">JSON Pointer of the offending value in the document ("" for the document itself).</param>
/// <param name="MessageEn">What is wrong, in English.</param>
/// <param name="MessageEs">What is wrong, in Spanish.</param>
/// <param name="FixHint">How to fix it, in English.</param>
public sealed record BrainSpecError(string Code, string Path, string MessageEn, string MessageEs, string FixHint);
