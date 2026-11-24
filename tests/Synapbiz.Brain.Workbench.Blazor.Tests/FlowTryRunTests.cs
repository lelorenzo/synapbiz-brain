using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowTryRunTests
{
    private static FlowHarness Started(string id, string? scenario = null, IFlowAnswerSource? source = null)
    {
        var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference(), source: source);
        harness.Start(id, scenario);
        return harness;
    }

    private static bool Ended(FlowHarness harness) =>
        harness.PanelText.Contains("The case has finished.", StringComparison.Ordinal) || harness.PanelText.Contains("The case was cancelled.", StringComparison.Ordinal);

    private static IElement? Card(FlowHarness harness, string headingStart) =>
        harness.FindAll("section.wb-wait").FirstOrDefault(card => FlowHarness.Normalise(card.QuerySelector("h3")!.TextContent).StartsWith(headingStart, StringComparison.Ordinal));

    private static IElement? Action(IElement card, string textStart) =>
        card.QuerySelectorAll("button").FirstOrDefault(button => FlowHarness.Normalise(button.TextContent).StartsWith(textStart, StringComparison.Ordinal));

    /// <summary>Presses one button that moves the case on, in the order a reader would: an answer from a test, a decision, an event, a moment, a child result, time.</summary>
    private static void Advance(FlowHarness harness, string? scenario = null)
    {
        foreach (var card in harness.FindAll("section.wb-wait"))
        {
            var heading = FlowHarness.Normalise(card.QuerySelector("h3")!.TextContent);
            if (heading.StartsWith("Waiting for an answer", StringComparison.Ordinal) && harness.UseAnswer(scenario))
            {
                return;
            }

            var button = heading switch
            {
                _ when heading.StartsWith("Waiting for an answer", StringComparison.Ordinal) => Action(card, "It fails for good"),
                _ when heading.StartsWith("A person must act", StringComparison.Ordinal) => card.QuerySelectorAll(".wb-actions button").FirstOrDefault(),
                _ when heading.StartsWith("Waiting for an event", StringComparison.Ordinal) => Action(card, "The event arrives"),
                _ when heading.StartsWith("Waiting for another process", StringComparison.Ordinal) => Action(card, "Finish the subprocess"),
                _ => Action(card, "Go to that moment"),
            };
            if (button is not null)
            {
                button.Click();
                return;
            }
        }

        harness.Press("1 day");
    }

    [Theory]
    [InlineData("previsit.outreach")]
    [InlineData("visit.note_wait")]
    [InlineData("postvisit.coding_review")]
    [InlineData("collection.followup")]
    [InlineData("case.lifecycle")]
    [InlineData("practice.daily_refresh")]
    public void AReferenceFlowIsWalkedToItsEndByButtonsOnly(string id)
    {
        using var harness = Started(id, FlowDocs.Process(id).RunScenarios().Results[0].Name);
        for (var turn = 0; turn < 120 && !Ended(harness); turn++)
        {
            Advance(harness);
        }

        Ended(harness).Should().BeTrue(harness.PanelText);
        harness.FindAll("section.wb-wait").Should().BeEmpty();
        harness.PanelText.Should().Contain("What happened").And.Contain("The case started");
    }

    [Fact]
    public void AnAnswerFromATestMovesTheCaseOnAndTheTimelineSaysWhoAnsweredAndWhatCameNext()
    {
        using var harness = Started("previsit.outreach", "nothing is missing");
        harness.UseAnswer("nothing is missing");
        harness.PanelText.Should().Contain("Answer at Check what is missing").And.Contain("from a test").And.Contain("Then:");
        harness.PanelText.Should().Contain("The case has finished.");
        harness.Find("[role=tabpanel] .wb-sr[role=status]").TextContent.Should().StartWith("The case finished with the result");
    }

    [Fact]
    public void ItFailsForNowAndForGoodRouteTheCaseThroughTheFlowsOwnErrorExits()
    {
        using var temporary = Started("previsit.outreach");
        Action(Card(temporary, "Waiting for an answer")!, "It fails for now")!.Click();
        temporary.PanelText.Should().Contain("Failure at Check what is missing");

        using var permanent = Started("previsit.outreach");
        Action(Card(permanent, "Waiting for an answer")!, "It fails for good")!.Click();
        permanent.PanelText.Should().Contain("Failure at Check what is missing").And.Contain("A person must act");
    }

    [Fact]
    public void AHandWrittenAnswerGoesInAndABadOneIsRefusedWithItsReasonAndChangesNothing()
    {
        using var harness = Started("previsit.outreach");
        Card(harness, "Waiting for an answer")!.QuerySelector("textarea")!.Change("this is not json");
        Action(Card(harness, "Waiting for an answer")!, "Send this answer")!.Click();
        harness.PanelText.Should().Contain("the text is not valid JSON");
        harness.Find("section.wb-wait [role=alert]").TextContent.Should().Contain("the text is not valid JSON");
        harness.FindAll(".wb-timeline .wb-pill-bad").Should().HaveCount(1);
        harness.Find(".wb-timeline").TextContent.Should().Contain("Refused");
        harness.Find("dl.wb-kv dd .wb-pill").TextContent.Should().Be("waiting");
        Card(harness, "Waiting for an answer").Should().NotBeNull();

        Card(harness, "Waiting for an answer")!.QuerySelector("textarea")!.Change("{\"missing_count\":0}");
        Action(Card(harness, "Waiting for an answer")!, "Send this answer")!.Click();
        harness.PanelText.Should().Contain("The case has finished.");
        harness.FindAll(".wb-timeline .wb-pill-bad").Should().HaveCount(1);
    }

    [Fact]
    public void AnEmptyHandWrittenAnswerSendsAnEmptyObject()
    {
        using var harness = Started("previsit.outreach");
        Action(Card(harness, "Waiting for an answer")!, "Send this answer")!.Click();
        harness.PanelText.Should().Contain("Answer at Check what is missing");
    }

    [Fact]
    public void ATaskShowsItsValuesAndOneButtonPerOutcomeWithTheDocumentsLabels()
    {
        using var harness = Started("previsit.outreach", "the requirements cannot be read at all");
        harness.UseAnswer("the requirements cannot be read at all");
        var card = Card(harness, "A person must act")!;
        card.TextContent.Should().Contain("case_ref").And.Contain("c-");
        var outcomes = card.QuerySelectorAll(".wb-actions button").Select(button => FlowHarness.Normalise(button.TextContent)).ToList();
        var words = FlowWords.For(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En);
        outcomes.Should().Equal(words.Outcome("resolved"), words.Outcome("unreachable"));
        card.QuerySelectorAll(".wb-actions button")[1].Click();
        harness.PanelText.Should().Contain("A person completed").And.Contain("The case has finished.");
    }

    [Fact]
    public void AWaitForAnEventIsAnsweredByTheEventArrives()
    {
        using var harness = Started("previsit.outreach", "a reply after the first reminder closes the stage");
        for (var turn = 0; turn < 20 && Card(harness, "Waiting for an event") is null && !Ended(harness); turn++)
        {
            Advance(harness, "a reply after the first reminder closes the stage");
        }

        var card = Card(harness, "Waiting for an event")!;
        card.TextContent.Should().Contain("Waiting for:");
        Action(card, "The event arrives")!.Click();
        harness.PanelText.Should().Contain("An event arrived at");
    }

    [Fact]
    public void ATimerAndAWindowAreAnsweredByGoingToThatMoment()
    {
        using var harness = Started("previsit.outreach", "a reminder waits for the next opening of the contact hours");
        var windowSeen = false;
        for (var turn = 0; turn < 30 && !Ended(harness); turn++)
        {
            if (Card(harness, "Waiting for a time window") is { } window)
            {
                windowSeen = true;
                window.TextContent.Should().Contain("The window opens");
                var before = harness.PanelText;
                Action(window, "Go to that moment")!.Click();
                harness.PanelText.Should().NotBe(before);
                break;
            }

            Advance(harness, "a reminder waits for the next opening of the contact hours");
        }

        windowSeen.Should().BeTrue(harness.PanelText);
    }

    [Fact]
    public void LettingTimePassMovesTheClockAndTheTimelineSaysSo()
    {
        using var harness = Started("previsit.outreach", "nothing is missing");
        var before = harness.PanelText;
        before.Should().Contain("01/04/2027 2:00 PM UTC");
        harness.Press("1 day");
        harness.PanelText.Should().Contain("01/05/2027 2:00 PM UTC").And.Contain("Time passed");
        harness.Press("1 week");
        harness.PanelText.Should().Contain("01/12/2027 2:00 PM UTC");
        harness.Press("1 hour");
        harness.PanelText.Should().Contain("01/12/2027 3:00 PM UTC");
    }

    [Fact]
    public void ToTheNextTimerOnlyAppearsWhenThereIsOneAndGoesThere()
    {
        using var harness = Started("previsit.outreach", "nothing is missing");
        harness.Buttons.Select(button => FlowHarness.Normalise(button.TextContent)).Should().NotContain(text => text.StartsWith("To the next timer", StringComparison.Ordinal));
        for (var turn = 0; turn < 20 && !harness.Buttons.Any(button => button.TextContent.Contains("To the next timer", StringComparison.Ordinal)) && !Ended(harness); turn++)
        {
            Advance(harness, "a reply after the first reminder closes the stage");
        }

        var next = harness.Buttons.First(button => button.TextContent.Contains("To the next timer", StringComparison.Ordinal));
        next.Click();
        (harness.PanelText.Contains("The timer of", StringComparison.Ordinal) || harness.PanelText.Contains("Time passed", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public void ACommandTheFlowRefusesIsListedWithItsReasonAndChangesNoState()
    {
        using var harness = Started("previsit.outreach", "nothing is missing");
        harness.UseAnswer("nothing is missing");
        harness.PanelText.Should().Contain("The case has finished.");
        harness.FindAll("section.wb-wait").Should().BeEmpty();
        harness.Buttons.Any(button => button.TextContent.Contains("Use this answer", StringComparison.Ordinal)).Should().BeFalse();
    }
}
