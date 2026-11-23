using System.Globalization;
using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

public sealed partial class FlowWorkbench
{
    private static readonly (ReviewState State, string Key, string PressedKey)[] ReviewParts =
    [
        (ReviewState.Confirmed, "flow.review.confirm", "flow.review.confirmed"),
        (ReviewState.NeedsFix, "flow.review.fix", "flow.review.fixState"),
        (ReviewState.Question, "flow.review.question", "flow.review.questionState"),
    ];

    private bool nameError;
    private bool exportDone;
    private bool notesHeld;
    private string? exportPlace;

    private void ToggleReview(FlowWork work, string target, ReviewState state)
    {
        var entry = work.ReviewForEdit(target);
        entry.State = entry.State == state ? ReviewState.Unreviewed : state;
        if (entry.State is ReviewState.NeedsFix or ReviewState.Question)
        {
            work.OpenNotes.Add(target);
        }

        exportDone = false;
        Session.Touch();
    }

    /// <summary>The targets of a flow's review: those the document has and the proposed scenarios of this review.</summary>
    private static List<string> TargetsOf(FlowData d, FlowWork work) =>
        [.. d.Targets, .. work.Proposed.Select(item => "scenario:" + item.Name)];

    /// <summary>Opens the note field of an item and moves focus to it. The Review tab has a note field for every item it lists, so the reader stays there.</summary>
    private void OpenNote(FlowWork work, string target)
    {
        work.OpenNotes.Add(target);
        pendingFocus = NoteRef(target);
        Session.Touch();
    }

    private void SetNote(FlowWork work, string target, string? note)
    {
        var value = (note ?? string.Empty).Trim();
        work.ReviewForEdit(target).Note = value.Length > NoteMax ? value[..NoteMax] : value;
        exportDone = false;
        notesHeld = notesHeld && MissingNoteCount() > 0;
        Session.Touch();
    }

    private static bool NoteVisible(FlowWork work, string target)
    {
        var entry = work.Review(target);
        return entry.State is ReviewState.NeedsFix or ReviewState.Question || entry.Note.Length > 0 || work.OpenNotes.Contains(target);
    }

    private string StateWord(ReviewState state) => state switch
    {
        ReviewState.Confirmed => Plain("flow.review.confirmed"),
        ReviewState.NeedsFix => Plain("flow.review.fixState"),
        ReviewState.Question => Plain("flow.review.questionState"),
        _ => Plain("flow.review.unreviewed"),
    };

    private static string StateTone(ReviewState state) => state switch
    {
        ReviewState.Confirmed => "ok",
        ReviewState.NeedsFix or ReviewState.Question => "warn",
        _ => "neutral",
    };

    private static string NoteRef(string target) => "note:" + target;

    private string NoteId(string target) => idp + "-note-" + Number(TargetNumber(target));

    private int TargetNumber(string target)
    {
        if (!noteIds.TryGetValue(target, out var number))
        {
            number = noteIds.Count + 1;
            noteIds[target] = number;
        }

        return number;
    }

    /// <summary>What a target is called on screen, for the lines of the Review tab.</summary>
    private string TargetName(FlowData d, string target)
    {
        if (target == "process")
        {
            return Plain("flow.review.flowTarget");
        }

        var colon = target.IndexOf(':', StringComparison.Ordinal);
        var kind = target[..colon];
        var name = target[(colon + 1)..];
        return kind switch
        {
            "trigger" => Plain("flow.review.triggerTarget", ("name", TriggerSentence(d, name))),
            "stage" => Plain("flow.review.stageTarget", ("name", d.Words.Stage(name))),
            "step" => Plain("flow.review.stepTarget", ("name", d.Words.Step(name))),
            "route" => Plain("flow.review.routeTarget", ("name", RouteName(d, name))),
            _ => Plain("flow.review.scenarioTarget", ("name", name)),
        };
    }

    private static string TriggerSentence(FlowData d, string id)
    {
        var at = d.TriggerIds.ToList().IndexOf(id);
        return at >= 0 && at < d.Entry.Triggers.Count ? WithoutCode(d.Entry.Triggers[at]) : id;
    }

    private string RouteName(FlowData d, string name)
    {
        var slash = name.IndexOf('/', StringComparison.Ordinal);
        var step = name[..slash];
        var label = name[(slash + 1)..];
        var route = d.View.Steps.First(item => item.Id == step).Routes.FirstOrDefault(item => item.Label == label);
        return Plain("flow.review.routeOf", ("step", d.Words.Step(step)), ("route", WithoutCode(route?.Phrase ?? label)));
    }

    private FlowReview BuildReview(FlowData d, FlowWork work) => new()
    {
        ProcessId = d.Outline.Id,
        ProcessVersion = d.Outline.Version,
        ProcessContentHash = d.Outline.ContentHash,
        Reviewer = Session.Reviewer.Trim(),
        CreatedAt = Now(),
        Language = Locale,
        Items = [.. TargetsOf(d, work).Where(target => work.Review(target).IsExported).Select(target => new ReviewItem(target, work.Review(target).State, work.Review(target).Note))],
        ProposedScenarios = [.. work.Proposed],
    };

    /// <summary>How many items, in every readable flow of the session, are a fix or a question with no note.</summary>
    private int MissingNoteCount() => FlowsMissingNotes().Sum(pair => pair.Count);

    /// <summary>The flows, named for the reader, that still have a fix or a question with no note, with how many. A flow whose title is another flow's too is named with its version.</summary>
    private List<(string Label, int Count)> FlowsMissingNotes()
    {
        var found = new List<(string, int)>();
        foreach (var flow in (Flows ?? []).Where(flow => flow.IsReadable))
        {
            var d = Data(flow)!;
            var work = Session.Work(flow.Key);
            var count = TargetsOf(d, work).Count(target => work.Review(target).NeedsNote);
            if (count > 0)
            {
                found.Add((Quoted(TitleOf(flow)) + (VersionLine(flow, d) is { } version ? Plain("flow.join") + version : string.Empty), count));
            }
        }

        return found;
    }

    /// <summary>True when the target is a scenario the reader proposed in this review.</summary>
    private static bool IsProposed(FlowWork work, string target) =>
        target.StartsWith("scenario:", StringComparison.Ordinal) && work.Proposed.Any(item => string.Equals("scenario:" + item.Name, target, StringComparison.Ordinal));

    private bool IsListed(string key) => (Flows ?? []).Any(flow => flow.IsReadable && string.Equals(flow.Key, key, StringComparison.Ordinal));

    /// <summary>How many flows with work that was not exported are no longer in the list: nothing can export them.</summary>
    private int OrphanCount() => Session.UnexportedFlowKeys.Count(key => !IsListed(key));

    /// <summary>How many flows of the list have work that was not exported.</summary>
    private int ListedUnexportedCount() => Session.UnexportedFlowKeys.Count(IsListed);

    private int ExportableFlowCount() =>
        Flows?.Count(flow => flow.IsReadable && Session.Work(flow.Key).HasExportable) ?? 0;

    private FlowWorkbenchExport BuildExport(out List<string> keys)
    {
        keys = [];
        var files = new List<FlowWorkbenchExportFile>();
        foreach (var flow in (Flows ?? []).Where(flow => flow.IsReadable))
        {
            var work = Session.Work(flow.Key);
            if (!work.HasExportable)
            {
                continue;
            }

            var d = Data(flow)!;
            var review = BuildReview(d, work);
            keys.Add(flow.Key);
            files.Add(new FlowWorkbenchExportFile(
                "review-" + d.Outline.Id + "-v" + d.Outline.Version.ToString(CultureInfo.InvariantCulture) + ".json",
                FlowReviewJson.Serialize(review),
                review));
        }

        return new FlowWorkbenchExport(files);
    }

    private async Task ExportAsync()
    {
        if (OnExport is null)
        {
            return;
        }

        notesHeld = MissingNoteCount() > 0;
        if (string.IsNullOrWhiteSpace(Session.Reviewer))
        {
            nameError = true;
            pendingFocus = ReviewerRef;
            return;
        }

        nameError = false;
        if (notesHeld)
        {
            return;
        }

        var export = BuildExport(out var keys);
        if (export.Files.Count == 0)
        {
            return;
        }

        // An exception of the host's save is not caught (as in RuleWorkbench): nothing is marked as exported then.
        var place = await OnExport(export).ConfigureAwait(true);
        exportDone = true;
        exportPlace = string.IsNullOrWhiteSpace(place) ? null : place.Trim();
        Session.MarkExported(keys);
    }

    private const string ReviewerRef = "reviewer";

    private void SetReviewer(ChangeEventArgs args)
    {
        Session.Reviewer = args.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(Session.Reviewer))
        {
            nameError = false;
        }
    }
}
