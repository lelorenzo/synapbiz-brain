using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The draft view, its forms, its menus and its refusals show no identifier either.</summary>
public sealed partial class IdentifierGuardDraftTests
{
    [GeneratedRegex("row-[0-9a-f]{6}")]
    private static partial Regex GeneratedKey();

    private static void Check(Harness harness, WorkbenchRule rule, string where)
    {
        var brain = rule.Rule!;
        var forbidden = new List<string> { brain.Id, brain.Kind.Key.Value, "workbench.", "decisions.", "brain.", "/body", "billing-topic", "urgent-phone", "email-invoice", "fresh-request", "high-rating", "mid-amount", "old-request", "no-account" };
        var markup = harness.Cut.Markup;
        foreach (var golden in brain.Goldens)
        {
            markup = markup.Replace(System.Net.WebUtility.HtmlEncode(golden.Name), string.Empty, StringComparison.Ordinal).Replace(golden.Name, string.Empty, StringComparison.Ordinal);
        }

        foreach (var word in forbidden)
        {
            markup.Should().NotContain(word, where);
        }

        GeneratedKey().IsMatch(markup).Should().BeFalse(where + ": no generated row key");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void NoStateOfTheDraftViewShowsAnIdentifier(WorkbenchLocale locale)
    {
        var rule = Fx.Editable("intake.first", locale);
        using var harness = Ed.Open(locale, rule);
        Check(harness, rule, "the draft view");

        Ed.OpenMenu(harness, 2, locale);
        Check(harness, rule, "a menu");
        Ed.Button(harness.Find(".wb-menu"), Ed.IsEs(locale) ? "Quitar la fila" : "Remove the row").Click();
        Check(harness, rule, "a refusal under a row");

        Ed.Edit(harness, 1, locale);
        harness.Find(".wb-rowform .wb-link").Click();
        Check(harness, rule, "a form with every fact");
        Ed.Apply(harness, locale);

        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.Apply(harness, locale);
        Check(harness, rule, "a form with field errors");
        Ed.Cancel(harness, locale);

        Ed.OpenMenu(harness, 1, locale);
        Ed.Button(harness.Find(".wb-menu"), Ed.IsEs(locale) ? "Agregar una fila debajo" : "Add a row below").Click();
        Ed.OutcomeInput(harness, "queue").Change("late");
        Ed.Apply(harness, locale);
        Check(harness, rule, "a new row");
    }
}
