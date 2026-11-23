namespace Synapbiz.Brain.Workbench;

/// <summary>The result of drafting: the text of the draft revision, or the error that stopped the run.</summary>
/// <param name="DraftJson">The draft document; null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">The error that stopped the run: one error, or none.</param>
public sealed record RuleDraftResult(string? DraftJson, IReadOnlyList<WorkbenchError> Errors)
{
    /// <summary>True when a draft was written.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Applies <see cref="RuleEdit"/>s to the exact text of a <c>brain.rule@1</c> document and returns the text of a draft revision.
/// It is a pure text-to-text function: it neither loads, validates nor evaluates. The host loads the draft with
/// <c>BrainRuleLoader.Load</c> and its own contracts, so a draft is judged by the same code as production.
/// </summary>
public static class RuleDrafter
{
    /// <summary>
    /// Applies the edits in order to a copy of the document. An edit that cannot be applied stops the run with one error and no
    /// draft: <c>workbench.draft.baseNotReadable</c>, <c>rowNotFound</c>, <c>rowKeyTaken</c>, <c>rowKeyNotValid</c>,
    /// <c>conditionNotFound</c>, <c>noDefaultRow</c>, <c>caseNotFound</c>, <c>caseNameTaken</c>, <c>caseNotValid</c>,
    /// <c>moveOntoItself</c> or <c>labelNotValid</c>, with the position of the edit as the path (<c>/edits/2</c>).
    /// <para>
    /// The draft's document <c>version</c> is the base version plus 1. A row touched by <see cref="SetCondition"/>,
    /// <see cref="ClearCondition"/>, <see cref="SetOutcome"/> or <see cref="SetDefaultOutcome"/> gets its <c>version</c> plus 1,
    /// once however many edits touch it; a new row has version 1 and a moved row keeps its version. Everything an edit does not
    /// name is kept as it is, in value and order. The text is indented with two spaces and <c>\n</c> line ends.
    /// </para>
    /// </summary>
    /// <param name="ruleJson">The text of the base rule document.</param>
    /// <param name="edits">The edits; an empty list gives a draft that differs from the base only by the version.</param>
    public static RuleDraftResult Apply(string ruleJson, IReadOnlyList<RuleEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(ruleJson);
        ArgumentNullException.ThrowIfNull(edits);

        for (var index = 0; index < edits.Count; index++)
        {
            Guard(edits[index], index);
        }

        var (workspace, error) = DraftWorkspace.Open(ruleJson);
        if (workspace is null)
        {
            return Failed(error!);
        }

        for (var index = 0; index < edits.Count; index++)
        {
            if (workspace.Apply(edits[index], index) is { } refused)
            {
                return Failed(refused);
            }
        }

        var (text, finishError) = workspace.Finish();
        return text is null ? Failed(finishError!) : new RuleDraftResult(text, []);
    }

    private static RuleDraftResult Failed(WorkbenchError error) => new(null, [error]);

    private static void Guard(RuleEdit? edit, int index)
    {
        var members = edit switch
        {
            SetCondition e => [e.RowKey, e.Condition],
            ClearCondition e => [e.RowKey, e.Fact],
            SetOutcome e => [e.RowKey, e.Field],
            AddRow e => [e.RowKey, e.Conditions, e.Outcome],
            RemoveRow e => [e.RowKey],
            MoveRow e => [e.RowKey],
            SetDefaultOutcome e => [e.Field],
            SetCaseExpectation e => [e.CaseName, e.Outcome],
            AddCase e => [e.Case, e.Case?.Name, e.Case?.Facts],
            RemoveCase e => [e.CaseName],
            SetRowLabel e => [e.RowKey],
            null => [null],
            _ => Array.Empty<object?>(),
        };

        if (members.Any(member => member is null))
        {
            throw new ArgumentNullException(nameof(edit), $"The edit at position {index} or one of its required members is null.");
        }
    }
}
