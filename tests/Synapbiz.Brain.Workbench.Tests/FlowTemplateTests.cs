using System.Text.RegularExpressions;
using FluentAssertions;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed partial class FlowTemplateTests
{
    [GeneratedRegex(@"\{([a-zA-Z_]+)\}")]
    private static partial Regex Placeholder();

    private static string[] Names(string text) => [.. Placeholder().Matches(text).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal)];

    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        FlowTemplates.EnglishKeys.Order(StringComparer.Ordinal).Should().Equal(FlowTemplates.SpanishKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Both_languages_use_the_same_placeholders()
    {
        foreach (var key in FlowTemplates.EnglishKeys)
        {
            Names(FlowTemplates.Template(WorkbenchLocale.Es, key)).Should().Equal(Names(FlowTemplates.Template(WorkbenchLocale.En, key)), key);
        }
    }

    [Fact]
    public void Spanish_differs_from_English_unless_the_key_is_listed()
    {
        foreach (var key in FlowTemplates.EnglishKeys)
        {
            var same = FlowTemplates.Template(WorkbenchLocale.Es, key) == FlowTemplates.Template(WorkbenchLocale.En, key);
            same.Should().Be(FlowTemplates.IntentionallyEqual.Contains(key), key);
        }
    }

    [Fact]
    public void The_listed_exceptions_exist()
    {
        FlowTemplates.IntentionallyEqual.Should().OnlyContain(key => FlowTemplates.Has(WorkbenchLocale.En, key));
    }

    [Fact]
    public void Filling_a_template_replaces_every_placeholder()
    {
        foreach (var key in FlowTemplates.EnglishKeys)
        {
            foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
            {
                var parameters = Names(FlowTemplates.Template(locale, key)).Distinct().Select(name => (name, "VALUE")).ToArray();
                var text = FlowTemplates.Text(locale, key, parameters);
                Placeholder().IsMatch(text).Should().BeFalse(key);
            }
        }
    }

    [Fact]
    public void Every_template_is_used_by_some_document()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        FlowTemplates.Recorder = used;
        try
        {
            foreach (var (_, process) in FlowDocs.All)
            {
                var view = FlowDescriber.Describe(process, WorkbenchLocale.En);
                _ = FlowGraph.From(process, WorkbenchLocale.En);
                _ = FlowLibrary.From([process], WorkbenchLocale.En);
                foreach (var trigger in Synapbiz.Brain.Process.ProcessOutline.From(process).Triggers)
                {
                    _ = FlowDescriber.DescribeTrigger(trigger, WorkbenchLocale.En);
                }

                view.Steps.Should().NotBeNull();
            }

            // The texts of a coverage problem and of a review check.
            var foreign = FlowSimulation.Start(FlowDocs.Process("flow.bare"), new FlowSimulationSetup { At = DateTimeOffset.UnixEpoch, Today = new DateOnly(2027, 1, 4), InputJson = """{"ref":"r"}""" });
            foreign.AdvanceBy(TimeSpan.FromSeconds(31));
            _ = FlowCoverage.Of(FlowDocs.Process("flow.batch"), [foreign]);
            var review = new FlowReview
            {
                ProcessId = "other",
                ProcessVersion = 9,
                ProcessContentHash = "0",
                Reviewer = "r",
                CreatedAt = DateTimeOffset.UnixEpoch,
                Language = WorkbenchLocale.En,
                Items = [new Synapbiz.Brain.Workbench.ReviewItem("step:none", Synapbiz.Brain.Workbench.ReviewState.Question, "?")],
                ProposedScenarios = [new FlowProposedScenario("bad", """{"name":"bad","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04"},"events":[{"complete":"none"}],"expect":{"status":"Completed"}}""")],
            };
            _ = FlowReviewCheck.Against(review, FlowDocs.Process("flow.batch"));
        }
        finally
        {
            FlowTemplates.Recorder = null;
        }

        string.Join(", ", FlowTemplates.EnglishKeys.Except(used).Order(StringComparer.Ordinal)).Should().BeEmpty("a template no document exercises needs a fixture");
    }

    [Fact]
    public void An_unknown_key_is_a_defect_and_not_silent()
    {
        FluentActions.Invoking(() => FlowTemplates.Text(WorkbenchLocale.En, "no.such.key")).Should().Throw<Exception>();
    }
}
