using Microsoft.AspNetCore.Components;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

public partial class RuleWorkbench
{
    private const string Separator = " · ";

    private sealed record ListEntry(WorkbenchRule Rule, int Index);

    private sealed record ListGroup(int Number, string? Heading, List<ListEntry> Entries);

    /// <summary>The rules in the order given, in runs of the same group; a run gets a heading when its group has a name.</summary>
    private List<ListGroup> RuleGroups()
    {
        var groups = new List<ListGroup>();
        var rules = Rules ?? [];
        for (var index = 0; index < rules.Count; index++)
        {
            if (groups.Count == 0 || !string.Equals(groups[^1].Heading, rules[index].Group, StringComparison.Ordinal))
            {
                groups.Add(new ListGroup(groups.Count + 1, rules[index].Group, []));
            }

            groups[^1].Entries.Add(new ListEntry(rules[index], index));
        }

        return groups;
    }

    /// <summary>The counts of one rule's review, over its whole-rule target, its rows and its cases.</summary>
    private sealed record ReviewCounts(int ReviewedRows, int RowCount, int Questions, int Fixes, int Confirmed, bool RuleConfirmed, int Exported, int Cases);

    private static ReviewCounts CountReview(WorkbenchRule rule, RuleWork work, RuleView view)
    {
        var rowStates = view.Rows.Select(row => work.Review(RowTarget(row)).State).ToList();
        var goldenStates = rule.Rule!.Goldens.Select(golden => work.Review(CaseTarget(golden.Name)).State);
        var states = rowStates.Append(work.Review("rule").State).Concat(goldenStates).ToList();
        var exported = work.Reviews.Values.Count(entry => entry.IsExported);
        return new ReviewCounts(
            rowStates.Count(state => state != ReviewState.Unreviewed),
            rowStates.Count,
            states.Count(state => state == ReviewState.Question),
            states.Count(state => state == ReviewState.NeedsFix),
            states.Count(state => state == ReviewState.Confirmed),
            work.Review("rule").State == ReviewState.Confirmed,
            exported,
            work.Cases.Count);
    }

    private string ReviewLine(ReviewCounts counts)
    {
        if (counts.Questions > 0 || counts.Fixes > 0)
        {
            var parts = new List<string>();
            if (counts.Questions > 0)
            {
                parts.Add(PluralText("list.review.questions", counts.Questions));
            }

            if (counts.Fixes > 0)
            {
                parts.Add(PluralText("list.review.fixes", counts.Fixes));
            }

            return string.Join(", ", parts);
        }

        if (counts.RuleConfirmed || (counts.RowCount > 0 && counts.ReviewedRows == counts.RowCount))
        {
            return Plain("list.review.done");
        }

        return Plain(counts.Confirmed > 0 ? "list.review.partial" : "list.review.none");
    }

    /// <summary>The three facts under a rule's title in the list; a failing test makes the test part bold.</summary>
    private RenderFragment ListSummary(WorkbenchRule rule) => builder =>
    {
        var work = Session.Work(rule.Key);
        var view = work.View(rule, Locale);
        var run = work.Tests(rule.Rule!);
        var total = run.Cases.Count;
        var failed = run.FailedCount;

        builder.AddContent(0, PluralText("list.rows", view.Rows.Count) + Separator);
        if (total == 0)
        {
            builder.AddContent(1, Plain("list.noCases"));
        }
        else if (failed > 0)
        {
            var key = total == 1 ? "list.casesFail.single" : UiText.Plural("list.casesFail", failed);
            builder.OpenElement(2, "strong");
            builder.AddContent(3, Plain(key, ("f", OutcomeFormat.Number(failed)), ("t", OutcomeFormat.Number(total))));
            builder.CloseElement();
        }
        else
        {
            builder.AddContent(4, PluralText("list.casesPass", total, ("p", OutcomeFormat.Number(run.PassedCount)), ("t", OutcomeFormat.Number(total))));
        }

        builder.AddContent(5, Separator + ReviewLine(CountReview(rule, work, view)));
    };

    /// <summary>The count line under the list title: rules, approved and drafts.</summary>
    private string CountLine()
    {
        var rules = Rules ?? [];
        var approved = rules.Count(rule => rule.State == RuleApprovalState.Approved);
        var drafts = rules.Count - approved;
        return string.Join(
            Separator,
            PluralText("list.count.rules", rules.Count),
            PluralText("list.count.approved", approved),
            PluralText("list.count.drafts", drafts));
    }

    private string TitleOf(WorkbenchRule rule)
    {
        if (rule.Rule is null)
        {
            return string.IsNullOrWhiteSpace(rule.FallbackTitle) ? Plain("load.untitled") : rule.FallbackTitle;
        }

        return rule.Labels.Title ?? WorkbenchText.Humanize(rule.Rule.Id);
    }

    private string StateLineOf(WorkbenchRule rule) =>
        rule.StateLine ?? Plain(rule.State == RuleApprovalState.Approved ? "state.approvedLine" : "state.draftLine");

    private static string TypeKey(FactType type) => type switch
    {
        FactType.Code => "read.type.code",
        FactType.Enum => "read.type.enum",
        FactType.Boolean => "read.type.boolean",
        FactType.Integer => "read.type.integer",
        FactType.Decimal => "read.type.decimal",
        FactType.Date => "read.type.date",
        FactType.Money => "read.type.money",
        _ => "read.type.reference",
    };

    private static string PolicyKey(HitPolicy policy) => policy switch
    {
        HitPolicy.First => "read.lead.first",
        HitPolicy.Unique => "read.lead.unique",
        _ => "read.lead.collect",
    };

    private string TypeWord(FactView fact)
    {
        var word = Plain(TypeKey(fact.Type));
        return fact.Type == FactType.Enum && fact.AllowedValues.Count > 0
            ? word + ": " + string.Join(", ", fact.AllowedValues.Select(option => option.Label))
            : word;
    }

    private string TestSummary(TestRunView run)
    {
        var parts = new List<string> { PluralText("tests.pass", run.Cases.Count, ("p", OutcomeFormat.Number(run.PassedCount)), ("t", OutcomeFormat.Number(run.Cases.Count))) };
        if (run.FailedCount > 0)
        {
            parts.Add(PluralText("fail.count", run.FailedCount));
        }

        return string.Join(Separator, parts);
    }

    private string ActualText(RuleView view, RuleLabels labels, TestCaseView testCase)
    {
        if (testCase.Failure is { } failure)
        {
            return RefusalText.For(text, view, testCase.Facts, failure.Code).Line;
        }

        return testCase.Actual is Nuvora.Decisions.Contracts.Evaluation.Decided decided
            ? OutcomeFormat.LabelledSummary(OutcomeFormat.Groups(view, labels, Locale, decided.OutcomeJson))
            : Plain("result.none");
    }

    private string ExpectedText(RuleView view, RuleLabels labels, Nuvora.Decisions.Contracts.Goldens.GoldenExpectation expected) =>
        expected is Nuvora.Decisions.Contracts.Goldens.ExpectedDecided decided
            ? OutcomeFormat.LabelledSummary(OutcomeFormat.Groups(view, labels, Locale, decided.OutcomeJson))
            : Plain("result.none");

    private string DecidedByText(RuleView view, TestCaseView testCase) =>
        testCase.MatchedRowKeys.Count switch
        {
            0 => string.Empty,
            1 when SimulationPresenter.RowByKey(view, testCase.MatchedRowKeys[0]) is { IsDefault: true } => Plain("result.default"),
            1 => Plain("result.decidedRow", ("n", SimulationPresenter.RowNumbers(text, view, testCase.MatchedRowKeys))),
            _ => Plain("why.rows", ("rows", SimulationPresenter.RowNumbers(text, view, testCase.MatchedRowKeys))),
        };
}
