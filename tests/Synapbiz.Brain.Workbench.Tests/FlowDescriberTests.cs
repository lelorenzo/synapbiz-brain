using System.Text.RegularExpressions;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed partial class FlowDescriberTests
{
    [GeneratedRegex(@"\{[a-zA-Z_]+\}")]
    private static partial Regex Placeholder();

    public static TheoryData<string, string> Views()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, _) in FlowDocs.All)
        {
            data.Add(name, "en");
            data.Add(name, "es");
        }

        return data;
    }

    private static WorkbenchLocale Locale(string code) => code == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;

    [Theory]
    [MemberData(nameof(Views))]
    public void The_view_matches_its_golden_text(string name, string language)
    {
        var view = FlowDescriber.Describe(FlowDocs.Process(name), Locale(language));
        var actual = FlowDocs.Render(view);
        FlowDocs.Golden(name + ".view." + language + ".txt", actual).Should().Be(actual);
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void No_placeholder_stays_unfilled(string name, string language)
    {
        var view = FlowDescriber.Describe(FlowDocs.Process(name), Locale(language));
        Placeholder().IsMatch(FlowDocs.Render(view)).Should().BeFalse();
    }

    [Fact]
    public void Describing_is_deterministic()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            FlowDocs.Render(FlowDescriber.Describe(process, WorkbenchLocale.En))
                .Should().Be(FlowDocs.Render(FlowDescriber.Describe(process, WorkbenchLocale.En)), name);
        }
    }

    [Fact]
    public void Both_languages_describe_the_same_steps_with_different_words()
    {
        foreach (var (name, process) in FlowDocs.Reference)
        {
            var en = FlowDescriber.Describe(process, WorkbenchLocale.En);
            var es = FlowDescriber.Describe(process, WorkbenchLocale.Es);
            en.Steps.Select(step => step.Id).Should().Equal(es.Steps.Select(step => step.Id), name);
            en.Steps.Select(step => step.Sentence).Should().NotEqual(es.Steps.Select(step => step.Sentence), name);
        }
    }

    [Fact]
    public void A_deadline_route_reads_as_a_plain_sentence()
    {
        var view = FlowDescriber.Describe(FlowDocs.Process("fixture.reminder"), WorkbenchLocale.En);
        var phrases = view.Steps.SelectMany(step => step.Routes).Select(route => route.Phrase).ToList();
        phrases.Should().Contain(phrase => phrase.Contains("If no answer arrives 2 hours before `visit_at`: go to Call the patient", StringComparison.Ordinal));
    }

    [Fact]
    public void The_ai_view_names_the_models_prompt_schemas_and_the_judging_rule()
    {
        var view = FlowDescriber.Describe(FlowDocs.Process("postvisit.coding_review"), WorkbenchLocale.En);
        var ai = view.Steps.Where(step => step.Ai is not null).Select(step => step.Ai!).ToList();
        ai.Should().NotBeEmpty();
        foreach (var item in ai)
        {
            item.Model.Should().NotBeNullOrEmpty();
            item.PromptId.Should().NotBeNullOrEmpty();
            item.MaxAttempts.Should().BeGreaterThan(0);
            item.Notes.Should().NotBeEmpty();
        }

        FlowDocs.Reference.SelectMany(item => FlowDescriber.Describe(item.Process, WorkbenchLocale.En).Steps)
            .Where(step => step.Ai is not null).Should().Contain(step => step.Ai!.JudgingRule != null);
    }

    [Fact]
    public void A_step_that_is_not_ai_has_no_ai_view()
    {
        foreach (var (_, process) in FlowDocs.All)
        {
            FlowDescriber.Describe(process, WorkbenchLocale.En).Steps
                .Where(step => step.Kind != OutlineStepKind.Ai).Should().OnlyContain(step => step.Ai == null);
        }
    }

    [Fact]
    public void Every_route_target_label_belongs_to_a_step_of_the_view()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            var view = FlowDescriber.Describe(process, WorkbenchLocale.En);
            var ids = view.Steps.Select(step => step.Id).ToHashSet(StringComparer.Ordinal);
            view.Steps.SelectMany(step => step.Routes).All(route => ids.Contains(route.Target)).Should().BeTrue(name);
        }
    }

    [Fact]
    public void Every_kind_of_step_appears_in_at_least_one_document()
    {
        var seen = FlowDocs.All.SelectMany(item => FlowDescriber.Describe(item.Process, WorkbenchLocale.En).Steps).Select(step => step.Kind).ToHashSet();
        seen.Should().BeEquivalentTo(Enum.GetValues<OutlineStepKind>());
    }

    [Fact]
    public void Kind_names_exist_in_both_languages()
    {
        foreach (var kind in Enum.GetValues<OutlineStepKind>())
        {
            FlowDescriber.KindName(kind, WorkbenchLocale.En).Should().NotBeNullOrWhiteSpace();
            FlowDescriber.KindName(kind, WorkbenchLocale.Es).Should().NotBe(FlowDescriber.KindName(kind, WorkbenchLocale.En), kind.ToString());
        }
    }

    [Fact]
    public void Triggers_read_in_both_languages()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            var outline = ProcessOutline.From(process);
            foreach (var trigger in outline.Triggers)
            {
                FlowDescriber.DescribeTrigger(trigger, WorkbenchLocale.En).Should().NotBeNullOrWhiteSpace(name);
                Placeholder().IsMatch(FlowDescriber.DescribeTrigger(trigger, WorkbenchLocale.Es)).Should().BeFalse(name);
            }
        }
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        FluentActions.Invoking(() => FlowDescriber.Describe(null!, WorkbenchLocale.En)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowDescriber.DescribeTrigger(null!, WorkbenchLocale.En)).Should().Throw<ArgumentNullException>();
    }
}
