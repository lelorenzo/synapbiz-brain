using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using static Synapbiz.Brain.Workbench.Tests.DraftKit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class DraftErrorLocatorTests
{
    private static readonly DraftLocation Nowhere = new(null, null, false, null, null, null);

    /// <summary>Drafts, asserts that the loader refuses the draft, and returns the draft text with the loader's errors.</summary>
    private static (string Draft, IReadOnlyList<BrainSpecError> Errors) Refused(params RuleEdit[] edits)
    {
        var draft = Draft(FirstText, edits);
        var load = TryLoad(draft);
        load.IsValid.Should().BeFalse("the loader must refuse this draft");
        return (draft, load.Errors);
    }

    private static DraftLocation At((string Draft, IReadOnlyList<BrainSpecError> Errors) refused, string code)
    {
        var error = refused.Errors.Should().ContainSingle(candidate => candidate.Code == code).Subject;
        return DraftErrorLocator.Locate(refused.Draft, error.Path);
    }

    [Fact]
    public void AnEnumValueTheKindDoesNotAllowIsLocatedAtTheRowAndTheFact()
    {
        var refused = Refused(new SetCondition("urgent-phone", Condition.Equals("channel", DecisionValue.Enum("Fax"))));

        refused.Errors.Should().ContainSingle().Which.Path.Should().Be("/body/rows/0/conditions/1/value");
        DraftErrorLocator.Locate(refused.Draft, refused.Errors[0].Path).Should().Be(new DraftLocation("urgent-phone", 1, false, "channel", null, null));
    }

    [Fact]
    public void AnOperatorNotAllowedForTheFactIsLocatedAtTheRowAndTheFact()
    {
        var refused = Refused(new SetCondition("billing-topic", Condition.StartsWith("channel", "W")));

        At(refused, refused.Errors[0].Code).Should().Be(new DraftLocation("billing-topic", 2, false, "channel", null, null));
        refused.Errors[0].Path.Should().Be("/body/rows/1/conditions/1/op");
    }

    [Fact]
    public void AnOutcomeOfTheWrongTypeIsLocatedAtTheRowAndTheField()
    {
        var refused = Refused(new SetOutcome("fresh-request", "queue", DecisionValue.Boolean(true)));

        refused.Errors.Should().ContainSingle();
        At(refused, refused.Errors[0].Code).Should().Be(new DraftLocation("fresh-request", 4, false, null, "queue", null));
    }

    [Fact]
    public void AMissingRequiredOutcomeFieldIsLocatedAtTheRowAndTheFieldEvenThoughTheFieldIsAbsent()
    {
        var refused = Refused(new SetOutcome("high-rating", "queue", null));

        At(refused, refused.Errors[0].Code).Should().Be(new DraftLocation("high-rating", 5, false, null, "queue", null));
    }

    [Fact]
    public void ADefaultRowThatLacksARequiredFieldIsLocatedAsTheDefaultRow()
    {
        var refused = Refused(new SetDefaultOutcome("queue", null));

        refused.Errors[0].Path.Should().Be("/body/defaultRow/outcome/queue");
        At(refused, refused.Errors[0].Code).Should().Be(new DraftLocation(null, null, true, null, "queue", null));
    }

    [Fact]
    public void ACaseThatExpectsARemovedRowIsLocatedByTheCaseName()
    {
        var refused = Refused(new RemoveRow("high-rating"));

        refused.Errors.Should().ContainSingle().Which.Path.Should().Be("/body/goldens/4/expect/row");
        At(refused, refused.Errors[0].Code).Should().Be(new DraftLocation(null, null, false, null, null, "A good rating with a date is followed up"));
    }

    [Fact]
    public void ACaseWhoseOutcomeTheKindRefusesIsLocatedByTheCaseName()
    {
        var refused = Refused(new SetCaseExpectation("An old request goes to the backlog", "old-request", Outcome(("route", DecisionValue.Enum("Teleport")), ("queue", DecisionValue.Code("q")))));

        refused.Errors.Should().ContainSingle();
        DraftErrorLocator.Locate(refused.Draft, refused.Errors[0].Path).CaseName.Should().Be("An old request goes to the backlog");
    }

    [Fact]
    public void EveryErrorOfADraftWithSeveralDefectsIsLocatedAtItsOwnPlace()
    {
        var refused = Refused(
            new SetCondition("urgent-phone", Condition.Equals("channel", DecisionValue.Enum("Fax"))),
            new SetOutcome("fresh-request", "queue", DecisionValue.Boolean(true)),
            new SetOutcome("old-request", "route", null),
            new SetDefaultOutcome("route", null));

        var places = refused.Errors.Select(error => DraftErrorLocator.Locate(refused.Draft, error.Path)).ToList();

        places.Should().BeEquivalentTo(
        [
            new DraftLocation("urgent-phone", 1, false, "channel", null, null),
            new DraftLocation("fresh-request", 4, false, null, "queue", null),
            new DraftLocation("old-request", 7, false, null, "route", null),
            new DraftLocation(null, null, true, null, "route", null),
        ]);
    }

    [Fact]
    public void ADefaultRowConditionIsLocatedWithItsFact()
    {
        var document = Parse(FirstText);
        document["body"]!["defaultRow"]!["conditions"] = new JsonArray(Parse("""{"fact":"topic","op":"any"}"""));

        DraftErrorLocator.Locate(document.ToJsonString(), "/body/defaultRow/conditions/0/value")
            .Should().Be(new DraftLocation(null, null, true, "topic", null, null));
    }

    [Fact]
    public void PathsThatNameLessThanAFieldGiveTheRowOrNothing()
    {
        var text = FirstText;

        DraftErrorLocator.Locate(text, "/body/rows/2").Should().Be(new DraftLocation("email-invoice", 3, false, null, null, null));
        DraftErrorLocator.Locate(text, "/body/rows/2/outcome").Should().Be(new DraftLocation("email-invoice", 3, false, null, null, null));
        DraftErrorLocator.Locate(text, "/body/rows/2/conditions").Should().Be(new DraftLocation("email-invoice", 3, false, null, null, null));
        DraftErrorLocator.Locate(text, "/body/rows/2/legacyKey").Should().Be(new DraftLocation("email-invoice", 3, false, null, null, null));
        DraftErrorLocator.Locate(text, "/body/rows/2/conditions/1").Should().Be(new DraftLocation("email-invoice", 3, false, "channel", null, null));
        DraftErrorLocator.Locate(text, "/body/defaultRow").Should().Be(new DraftLocation(null, null, true, null, null, null));
        DraftErrorLocator.Locate(text, "/body/goldens/1").CaseName.Should().Be("A billing topic on the web goes to billing");
    }

    [Fact]
    public void AFieldNameWithEscapedCharactersIsUnescaped() =>
        DraftErrorLocator.Locate(FirstText, "/body/rows/0/outcome/a~1b~0c").OutcomeField.Should().Be("a/b~c");

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("body/rows/0")]
    [InlineData("/body")]
    [InlineData("/body/rows")]
    [InlineData("/body/rows/99")]
    [InlineData("/body/rows/-1")]
    [InlineData("/body/rows/x")]
    [InlineData("/body/rows/0/conditions/99/value")]
    [InlineData("/body/rows/0/conditions/x")]
    [InlineData("/body/goldens")]
    [InlineData("/body/goldens/99/expect")]
    [InlineData("/body/hitPolicy")]
    [InlineData("/body/kind")]
    [InlineData("/meta/display")]
    [InlineData("/version")]
    [InlineData("/requires/0")]
    public void APathThatResolvesToNothingGivesALocationWithNothingSet(string path)
    {
        DraftErrorLocator.Locate(FirstText, path).Should().Be(Nowhere);
    }

    [Fact]
    public void ADeeperPathStillGivesTheRowAndTheFactOfTheConditionItIsUnder() =>
        DraftErrorLocator.Locate(FirstText, "/body/rows/0/conditions/0/value/extra").Should().Be(new DraftLocation("urgent-phone", 1, false, "urgent", null, null));

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"body\":3}")]
    [InlineData("{\"body\":{\"rows\":3,\"defaultRow\":3,\"goldens\":3}}")]
    [InlineData("{\"a\":1,\"a\":2}")]
    public void ADocumentThatCannotBeReadGivesALocationWithNothingSetForAnyPath(string text)
    {
        foreach (var path in new[] { "/body/rows/0/conditions/0/value", "/body/defaultRow/outcome/queue", "/body/goldens/0/expect", "" })
        {
            DraftErrorLocator.Locate(text, path).Should().Be(Nowhere, path);
        }
    }

    [Fact]
    public void ARowOrCaseOfAnotherKindThanAnObjectGivesNothing()
    {
        var document = Parse(FirstText);
        document["body"]!["rows"]!.AsArray()[0] = 5;
        document["body"]!["goldens"]!.AsArray()[0] = "x";
        var text = document.ToJsonString();

        DraftErrorLocator.Locate(text, "/body/rows/0/outcome/queue").Should().Be(Nowhere);
        DraftErrorLocator.Locate(text, "/body/goldens/0/expect").Should().Be(Nowhere);
        DraftErrorLocator.Locate(text, "/body/rows/1/conditions/0").RowKey.Should().Be("billing-topic");
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        new Action[]
        {
            () => DraftErrorLocator.Locate(null!, "/body"),
            () => DraftErrorLocator.Locate("{}", null!),
        }.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
    }
}
