namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// The sentences for the codes the interpreter and the simulation give when they refuse a command or a start, in English and Spanish, so
/// that a screen writes a reason in words and never shows a bare code. A code the table does not know is written between backticks.
/// </summary>
public static class FlowCodes
{
    /// <summary>The code of a start whose zones the simulation could not use (a zone id or an offset it does not accept).</summary>
    public const string ZoneInvalid = "workbench.flow.zone_invalid";

    /// <summary>The code of a child process that could not be opened (it is not among the documents the screen knows).</summary>
    public const string ChildUnknown = "workbench.flow.child_unknown";

    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["process.input.unknown"] = "code.inputUnknown",
        ["process.variable.type_mismatch"] = "code.typeMismatch",
        [FlowSimulation.ErrorJsonInvalid] = "code.jsonInvalid",
        [FlowSimulation.ErrorDurationInvalid] = "code.durationInvalid",
        [FlowSimulation.ErrorNotStarted] = "code.notStarted",
        [FlowSimulation.ErrorAnswerSourceFailed] = "code.sourceFailed",
        [FlowSimulation.ErrorEffectUnknown] = "code.effectUnknown",
        ["process.trigger.stale"] = "code.stale",
        ["process.task.outcome_unknown"] = "code.outcomeUnknown",
        ["process.event.not_accepted"] = "code.eventNotAccepted",
        ["process.window.zone_unknown"] = "code.zoneUnknown",
        ["process.effect.output_malformed"] = "code.outputMalformed",
        ["process.ai.output_invalid"] = "code.aiOutputInvalid",
        [ZoneInvalid] = "code.zoneInvalid",
        [ChildUnknown] = "code.childUnknown",
    };

    /// <summary>The reason a code stands for, as a sentence in the asked language, with the code in backticks when the table has no sentence for it.</summary>
    /// <param name="code">The code; null or empty gives the sentence for an unknown reason.</param>
    /// <param name="locale">The language.</param>
    public static string Describe(string? code, WorkbenchLocale locale)
    {
        if (code is not null && Keys.TryGetValue(code, out var key))
        {
            return FlowTemplates.Text(locale, key);
        }

        return string.IsNullOrEmpty(code)
            ? FlowTemplates.Text(locale, "code.unknown")
            : FlowTemplates.Text(locale, "code.other", ("code", code));
    }
}
