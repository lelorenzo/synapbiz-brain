using System.Text;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>One try-out: a simulated case, and the texts of the controls that answer what it waits for.</summary>
internal sealed class TryFrame
{
    internal TryFrame(FlowSimulation simulation, string? parentChildKey = null)
    {
        Simulation = simulation;
        ParentChildKey = parentChildKey;
    }

    internal FlowSimulation Simulation { get; }

    /// <summary>For a child opened from its parent, the key of the child the parent waits for; null for the first frame.</summary>
    internal string? ParentChildKey { get; }

    /// <summary>The texts of the controls of the waiting cards, by a key that names the card and the field.</summary>
    internal Dictionary<string, string> Inputs { get; } = new(StringComparer.Ordinal);

    /// <summary>The field names of a card whose text could not be read, by card key.</summary>
    internal Dictionary<string, List<string>> Invalid { get; } = new(StringComparer.Ordinal);

    /// <summary>The code of the last command the flow refused, with the card it was for; null when the last command was accepted.</summary>
    internal (string Card, string Code)? Refusal { get; set; }
}

/// <summary>Everything one reader's session holds for one flow. It lives in memory and nowhere else.</summary>
internal sealed class FlowWork
{
    internal Dictionary<string, ReviewEntry> Reviews { get; } = new(StringComparer.Ordinal);

    internal List<FlowProposedScenario> Proposed { get; } = [];

    /// <summary>Targets whose note field the reader opened by hand.</summary>
    internal HashSet<string> OpenNotes { get; } = new(StringComparer.Ordinal);

    /// <summary>The try-outs, the first one for this flow and each next one for a child opened from the one before it.</summary>
    internal List<TryFrame> Frames { get; } = [];

    /// <summary>The scenario the first try-out replays, command by command; null for a try-out made by hand. Once it is finished the reader goes on by hand.</summary>
    internal FlowReplay? Replay { get; set; }

    /// <summary>What each proposed scenario does to the flow, by name (null when it is not valid), kept so that it is played once.</summary>
    internal Dictionary<string, FlowScenarioResult?> ProposedResults { get; } = new(StringComparer.Ordinal);

    /// <summary>True while a replay still has commands to play: the cards of the try-out are then read only.</summary>
    internal bool IsReplaying => Replay is { IsFinished: false };

    /// <summary>The texts of the start form.</summary>
    internal Dictionary<string, string> StartValues { get; } = new(StringComparer.Ordinal);

    /// <summary>The text of the start time control; empty until the form is first drawn.</summary>
    internal string StartAt { get; set; } = string.Empty;

    /// <summary>The scenario the start form was filled from, as the select shows it; empty when none.</summary>
    internal string StartLike { get; set; } = string.Empty;

    /// <summary>The fields of the start form that could not be read.</summary>
    internal List<string> StartInvalid { get; } = [];

    /// <summary>The code the flow gave when it refused the start; null when it did not.</summary>
    internal string? StartRefusal { get; set; }

    /// <summary>The name typed for "save as a proposed test".</summary>
    internal string SaveName { get; set; } = string.Empty;

    /// <summary>The string key of the reason the proposed test could not be saved; null when there is none.</summary>
    internal string? SaveError { get; set; }

    /// <summary>True while the panel asks whether to discard the try-out.</summary>
    internal bool ConfirmReset { get; set; }

    /// <summary>The sentence of the last thing a command did, for the live region.</summary>
    internal string Live { get; set; } = string.Empty;

    /// <summary>What the last export contained, so a later change can be told from none.</summary>
    internal string ExportedSignature { get; set; } = string.Empty;

    internal ReviewEntry Review(string target) => Reviews.TryGetValue(target, out var entry) ? entry : new ReviewEntry();

    internal ReviewEntry ReviewForEdit(string target)
    {
        if (!Reviews.TryGetValue(target, out var entry))
        {
            entry = new ReviewEntry();
            Reviews[target] = entry;
        }

        return entry;
    }

    /// <summary>The exported part of the work, as one text; two equal texts mean the same export.</summary>
    internal string Signature()
    {
        var text = new StringBuilder();
        foreach (var (target, entry) in Reviews.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (entry.IsExported)
            {
                text.Append(target).Append('|').Append((int)entry.State).Append('|').Append(entry.Note).Append('\n');
            }
        }

        foreach (var scenario in Proposed)
        {
            text.Append("scenario|").Append(scenario.Name).Append('|').Append(scenario.Json).Append('\n');
        }

        return text.ToString();
    }

    internal bool HasExportable => Reviews.Values.Any(entry => entry.IsExported) || Proposed.Count > 0;

    internal bool HasUnexportedChanges => !string.Equals(Signature(), ExportedSignature, StringComparison.Ordinal);
}
