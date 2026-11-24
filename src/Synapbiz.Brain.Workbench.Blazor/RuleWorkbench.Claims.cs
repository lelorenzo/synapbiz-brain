using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

public partial class RuleWorkbench
{
    /// <summary>
    /// The label of each source a rule cites in <c>meta.sources[].source</c>, in the language of <see cref="Locale"/>. A source with no
    /// label is shown as code. The library names no source of its own.
    /// </summary>
    [Parameter]
    public IReadOnlyDictionary<string, string>? SourceLabels { get; set; }

    /// <summary>
    /// The label of each authority code a rule states in <c>meta.authority</c>, in the language of <see cref="Locale"/>. A rule whose
    /// code has no label here gets no badge.
    /// </summary>
    [Parameter]
    public IReadOnlyDictionary<string, string>? AuthorityLabels { get; set; }

    /// <summary>The host's label for the authority a rule states; null when it states none or the host has no label for it.</summary>
    private string? AuthorityOf(BrainRule brain)
    {
        var code = RuleClaims.From(brain.Meta).Authority;
        return code is not null && AuthorityLabels is not null && AuthorityLabels.TryGetValue(code, out var label) && !string.IsNullOrWhiteSpace(label) ? label.Trim() : null;
    }

    private string? SourceLabelOf(string source) =>
        SourceLabels is not null && SourceLabels.TryGetValue(source, out var label) && !string.IsNullOrWhiteSpace(label) ? label.Trim() : null;
}
