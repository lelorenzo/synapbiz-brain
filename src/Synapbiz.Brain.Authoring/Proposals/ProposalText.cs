using System.Globalization;
using System.Text;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>The text of a proposal as a person reads it, in English or Spanish, built from the stored proposal alone.</summary>
public static class ProposalText
{
    /// <summary>Writes the text of a process proposal.</summary>
    /// <param name="proposal">The proposal.</param>
    /// <param name="locale">The language.</param>
    public static string Flow(FlowProposal proposal, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var text = new StringBuilder();
        var key = proposal.BaseVersion is null ? "proposal.flow.head.new" : "proposal.flow.head";
        text.AppendLine(AuthoringText.Text(locale, key, ("id", FlowChangeDescriber.Quote(proposal.ProcessId)), ("version", AuthoringText.Number(proposal.Version))));
        text.AppendLine(Who(proposal.Author, proposal.CreatedAt, locale));
        text.AppendLine(AuthoringText.Text(locale, "proposal.why", ("summary", proposal.Summary)));
        var changes = proposal.Changes(locale);
        text.AppendLine();
        text.AppendLine(AuthoringText.Text(locale, "proposal.changes"));
        text.AppendLine(changes.Summary);
        foreach (var item in changes.Items)
        {
            text.AppendLine("- " + item);
        }

        if (changes.Behaviour.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(AuthoringText.Text(locale, "proposal.behaviour"));
            foreach (var item in changes.Behaviour)
            {
                text.AppendLine("- " + item);
            }
        }

        text.AppendLine();
        text.AppendLine(AuthoringText.Text(locale, "proposal.tests", ("pass", AuthoringText.Number(proposal.Tests.Count(test => test.Passed))), ("total", AuthoringText.Number(proposal.Tests.Count)), ("covered", AuthoringText.Number(proposal.CoveredRoutes)), ("routes", AuthoringText.Number(proposal.TotalRoutes))));
        var document = AuthoringJson.TryObject(proposal.DocumentJson);
        foreach (var gap in proposal.KnownGaps)
        {
            var step = FlowChangeDescriber.Quote(PathWords.StepLabel(document, locale, gap.Step));
            var what = gap.Route is { } route && gap.Check == KnownGap.RouteUntested
                ? AuthoringText.Text(locale, "proposal.gap.route", ("step", step), ("route", Route(route, locale)))
                : AuthoringText.Text(locale, "proposal.gap.wait", ("step", step));
            text.AppendLine("- " + AuthoringText.Text(locale, "proposal.gap", ("what", what), ("reason", gap.Reason)));
        }

        text.AppendLine();
        text.AppendLine(AuthoringText.Text(locale, "proposal.review.flow"));
        text.Append(AuthoringText.Text(locale, "proposal.nothing"));
        return text.ToString();
    }

    /// <summary>Writes the text of a rule proposal.</summary>
    /// <param name="file">The proposal with its decision.</param>
    /// <param name="locale">The language.</param>
    public static string Rule(RuleProposalFile file, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(file);
        var proposal = file.Proposal;
        var text = new StringBuilder();
        text.AppendLine(AuthoringText.Text(locale, "proposal.rule.head", ("id", FlowChangeDescriber.Quote(proposal.RuleId)), ("version", AuthoringText.Number(proposal.BaseVersion + 1))));
        text.AppendLine(Who(proposal.Author, proposal.CreatedAt, locale));
        text.AppendLine(AuthoringText.Text(locale, "proposal.why", ("summary", proposal.Note)));
        text.AppendLine();
        text.AppendLine(AuthoringText.Text(locale, "proposal.changes"));
        text.AppendLine(file.Summary(locale));
        text.AppendLine();
        if (file.NeedsReview)
        {
            text.AppendLine(AuthoringText.Text(locale, "proposal.review.rule.yes", ("reasons", AuthoringText.Join(locale, [.. file.Reasons.Select(reason => AuthoringText.Text(locale, "rule.review." + reason))]))));
        }
        else
        {
            text.AppendLine(AuthoringText.Text(locale, "proposal.review.rule.no"));
        }

        text.Append(AuthoringText.Text(locale, "proposal.nothing"));
        return text.ToString();
    }

    // A route label of a step in words: the fixed ones by their sentence, a value of the result as the value.
    private static string Route(string label, WorkbenchLocale locale)
    {
        if (label.StartsWith("on:", StringComparison.Ordinal))
        {
            return AuthoringText.Text(locale, "route.on", ("value", FlowChangeDescriber.Quote(WorkbenchText.Humanize(label[3..]))));
        }

        return AuthoringText.Has("route." + label) ? AuthoringText.Text(locale, "route." + label) : AuthoringText.Text(locale, "route.next");
    }

    private static string Who(string author, DateTimeOffset at, WorkbenchLocale locale) =>
        AuthoringText.Text(locale, "proposal.by", ("author", author), ("date", AuthoringText.Moment(locale, at)));
}
