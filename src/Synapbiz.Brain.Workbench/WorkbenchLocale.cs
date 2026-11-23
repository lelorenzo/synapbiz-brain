namespace Synapbiz.Brain.Workbench;

/// <summary>The language of the sentences and value texts the workbench writes.</summary>
public enum WorkbenchLocale
{
    /// <summary>English; dates are written <c>MM/dd/yyyy</c> and decimals with a point.</summary>
    En = 0,

    /// <summary>Spanish; dates are written <c>dd/MM/yyyy</c> and decimals with a comma.</summary>
    Es = 1,
}

/// <summary>A defect found while reading a workbench document: a stable code, the JSON Pointer of the offending value and an English message.</summary>
/// <param name="Code">Stable machine-readable code, <c>workbench.&lt;area&gt;.&lt;reason&gt;</c>.</param>
/// <param name="Path">JSON Pointer of the offending value; empty for the whole document.</param>
/// <param name="Message">English text for logs and developers.</param>
public sealed record WorkbenchError(string Code, string Path, string Message);
