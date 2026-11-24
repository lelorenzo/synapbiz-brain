using System.Text.RegularExpressions;
using FluentAssertions;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The owner's wording (2026-11-23): what a person reads says "process", never "flow" ("proceso", never "flujo").</summary>
public sealed partial class ProcessWordingTests
{
    [GeneratedRegex(@"\b(flows?|flujos?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Forbidden();

    [GeneratedRegex(@"\{\w+\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    private static readonly WorkbenchLocale[] Locales = [WorkbenchLocale.En, WorkbenchLocale.Es];

    // A placeholder name such as {flows} is code, not a word a person reads.
    private static string Words(string template) => Placeholder().Replace(template, string.Empty);

    [Fact]
    public void No_sentence_template_of_the_rules_says_flow_or_flujo()
    {
        SentenceTemplates.EnglishKeys.Should().NotBeEmpty();
        foreach (var locale in Locales)
        {
            foreach (var key in SentenceTemplates.EnglishKeys)
            {
                Forbidden().IsMatch(Words(SentenceTemplates.Template(locale, key))).Should().BeFalse(key + " (" + locale + ")");
            }
        }
    }

    [Fact]
    public void No_sentence_template_of_the_processes_says_flow_or_flujo()
    {
        FlowTemplates.EnglishKeys.Should().NotBeEmpty();
        foreach (var locale in Locales)
        {
            foreach (var key in FlowTemplates.EnglishKeys)
            {
                Forbidden().IsMatch(Words(FlowTemplates.Template(locale, key))).Should().BeFalse(key + " (" + locale + ")");
            }
        }
    }

    [Fact]
    public void The_words_for_the_kinds_of_process_are_the_owners()
    {
        FlowKinds.Word(FlowKind.Main, WorkbenchLocale.En).Should().Be("Main process");
        FlowKinds.Word(FlowKind.Subprocess, WorkbenchLocale.En).Should().Be("Subprocess");
        FlowKinds.Word(FlowKind.Scheduled, WorkbenchLocale.En).Should().Be("Scheduled task");
        FlowKinds.Word(FlowKind.Main, WorkbenchLocale.Es).Should().Be("Proceso principal");
        FlowKinds.Word(FlowKind.Subprocess, WorkbenchLocale.Es).Should().Be("Subproceso");
        FlowKinds.Word(FlowKind.Scheduled, WorkbenchLocale.Es).Should().Be("Tarea programada");
    }
}
