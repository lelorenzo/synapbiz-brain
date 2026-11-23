using FluentAssertions;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using static Synapbiz.Brain.Workbench.Tests.DraftKit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>
/// <see cref="SetCaseExpectation"/> says every expectation a case can hold, on every hit policy, so a panel never needs remove
/// plus add. Each form goes through the real loader and the case is run with <see cref="RuleTester"/>.
/// </summary>
public sealed class SetCaseExpectationTests
{
    private const string FirstCase = "An urgent phone request is escalated";
    private const string Readiness = "tests/fixtures/brain-spec/readiness.rule.json";

    private static DecisionValue Note(string note) => DecisionValue.Code(note);

    private static BrainRule LoadReadinessWith(params RuleEdit[] edits) =>
        TestFiles.Load(Draft(TestFiles.Read(Readiness), edits), TestFiles.ReadinessContract());

    private static BrainRule LoadWith(string file, params RuleEdit[] edits) =>
        TestFiles.LoadIntakeText(Draft(TestFiles.IntakeText(file), edits));

    private static TestCaseView Run(BrainRule rule, string name) => RuleTester.Run(rule).Cases.Single(testCase => testCase.Name == name);

    private static GoldenExpectation Expected(BrainRule rule, string name) => rule.Goldens.Single(golden => golden.Name == name).Expected;

    // ---- no decision, on every hit policy --------------------------------------------------------

    [Theory]
    [InlineData("intake.first.rule.json")]
    [InlineData("intake.unique.rule.json")]
    public void NoDecisionWritesTheNoDecisionExpectationAndTheCaseRunsAgainstIt(string file)
    {
        var name = TestFiles.LoadIntake(file).Goldens[0].Name;

        var rule = LoadWith(file, SetCaseExpectation.NoDecision(name));

        Expected(rule, name).Should().BeOfType<ExpectedNoDecision>();
        var run = Run(rule, name);
        run.Passed.Should().BeFalse("the rule does decide for this case");
        run.Actual.Should().BeOfType<Decided>();
    }

    [Fact]
    public void NoDecisionPassesWhenNoRowDecidesOnAUniqueRule()
    {
        var unique = TestFiles.LoadIntake("intake.unique.rule.json");
        var name = unique.Goldens[2].Name;
        unique.Goldens[2].Expected.Should().BeOfType<ExpectedNoDecision>();

        var rule = LoadWith("intake.unique.rule.json", new SetCaseExpectation(name, null, Outcome(("route", DecisionValue.Enum("Fast")), ("queue", DecisionValue.Code("new")))), SetCaseExpectation.NoDecision(name));

        Expected(rule, name).Should().BeOfType<ExpectedNoDecision>();
        Run(rule, name).Passed.Should().BeTrue();
    }

    [Fact]
    public void NoDecisionOnACollectRuleWritesTheNoDecisionExpectationAndTheCasePasses()
    {
        var name = "A high score at intake matches nothing";

        var rule = LoadReadinessWith(
            new SetCaseExpectation(name, null, Outcome(("note", Note("low-score")))),
            SetCaseExpectation.NoDecision(name));

        Expected(rule, name).Should().BeOfType<ExpectedNoDecision>();
        Run(rule, name).Passed.Should().BeTrue();
    }

    [Fact]
    public void NoDecisionIgnoresTheRowTheOutcomeAndTheResults()
    {
        var edit = new SetCaseExpectation(FirstCase, "urgent-phone", Outcome(("route", DecisionValue.Enum("Escalate")))) { ExpectNoDecision = true, Results = [Outcome()] };

        Expected(LoadFirst(edit), FirstCase).Should().BeOfType<ExpectedNoDecision>();
    }

    [Fact]
    public void NoDecisionBuildsAnEditThatNamesTheCaseAndExpectsNoDecision()
    {
        var edit = SetCaseExpectation.NoDecision("a case");

        edit.CaseName.Should().Be("a case");
        edit.RowKey.Should().BeNull();
        edit.Outcome.Should().BeEmpty();
        edit.ExpectNoDecision.Should().BeTrue();
        edit.Results.Should().BeNull();
    }

    // ---- a decision, on every hit policy ---------------------------------------------------------

    [Theory]
    [InlineData("intake.first.rule.json")]
    [InlineData("intake.unique.rule.json")]
    public void ADecisionByARowKeepsTheExistingBehaviourOnFirstAndUniqueRules(string file)
    {
        var name = TestFiles.LoadIntake(file).Goldens[0].Name;
        var rule = LoadWith(file, new SetCaseExpectation(name, null, Outcome(("route", DecisionValue.Enum("Escalate")), ("queue", DecisionValue.Code("urgent")), ("priority", DecisionValue.Integer(1)))));

        Expected(rule, name).Should().Be(new ExpectedDecided("""{"priority":1,"queue":"urgent","route":"Escalate"}""", null));
    }

    [Fact]
    public void ADecisionByTheDefaultRowAfterANoDecisionExpectationWritesRowAndOutcome()
    {
        var outcome = Outcome(("route", DecisionValue.Enum("Standard")), ("queue", DecisionValue.Code("other")), ("priority", DecisionValue.Integer(7)));

        var rule = LoadFirst(SetCaseExpectation.NoDecision(FirstCase), new SetCaseExpectation(FirstCase, "fallback", outcome));

        Expected(rule, FirstCase).Should().Be(new ExpectedDecided("""{"priority":7,"queue":"other","route":"Standard"}""", "fallback"));
    }

    // ---- results under collect -------------------------------------------------------------------

    [Fact]
    public void ZeroResultsUnderCollectWriteAnEmptyList()
    {
        var name = "A low score at intake";

        var rule = LoadReadinessWith(new SetCaseExpectation(name, null, Outcome())
        {
            Results = [],
        });

        Expected(rule, name).Should().Be(new ExpectedDecided("[]", null));
        Run(rule, name).Passed.Should().BeFalse("the rule matches a row for this case");
    }

    [Fact]
    public void OneResultUnderCollectWritesAOneElementListWithOrWithoutTheResultsMember()
    {
        var name = "A low score at intake";
        var single = Outcome(("note", Note("low-score")));

        var viaOutcome = LoadReadinessWith(new SetCaseExpectation(name, "low-score", single));
        var viaResults = LoadReadinessWith(new SetCaseExpectation(name, "low-score", Outcome()) { Results = [single] });

        Expected(viaOutcome, name).Should().Be(new ExpectedDecided("""[{"note":"low-score"}]""", "low-score"));
        Expected(viaResults, name).Should().Be(Expected(viaOutcome, name));
        Run(viaResults, name).Passed.Should().BeTrue();
    }

    [Fact]
    public void SeveralResultsUnderCollectKeepTheirOrderAndTheCaseRunsAgainstThem()
    {
        var name = "A mid score in review matches two rows";
        var results = new[] { Outcome(("note", Note("mid-score"))), Outcome(("note", Note("late-stage"))) };

        var rule = LoadReadinessWith(
            new SetCaseExpectation(name, null, Outcome(("note", Note("other")))),
            new SetCaseExpectation(name, "mid-score", Outcome()) { Results = results });

        Expected(rule, name).Should().Be(new ExpectedDecided("""[{"note":"mid-score"},{"note":"late-stage"}]""", "mid-score"));
        Run(rule, name).Passed.Should().BeTrue();
    }

    [Fact]
    public void ResultsOnARuleThatIsNotCollectAreRefused()
    {
        var edit = new SetCaseExpectation(FirstCase, null, Outcome()) { Results = [Outcome()] };

        var refused = Refused(FirstText, edit);

        refused.Errors[0].Code.Should().Be("workbench.draft.caseNotValid");
        refused.Errors[0].Path.Should().Be("/edits/0");
    }

    [Fact]
    public void ANullResultIsAProgrammingError()
    {
        var edit = new SetCaseExpectation("a", null, Outcome()) { Results = [null!] };

        Action call = () => RuleDrafter.Apply(FirstText, [edit]);

        call.Should().Throw<ArgumentNullException>();
    }

    // ---- the case keeps its place ----------------------------------------------------------------

    [Fact]
    public void TheCaseKeepsItsPositionAndEveryOtherMemberOnEveryForm()
    {
        var before = TestFiles.LoadIntake("intake.first.rule.json");
        var name = before.Goldens[2].Name;
        var forms = new RuleEdit[]
        {
            new SetCaseExpectation(name, "urgent-phone", Outcome(("route", DecisionValue.Enum("Escalate")), ("queue", DecisionValue.Code("urgent")))),
            new SetCaseExpectation(name, null, Outcome(("route", DecisionValue.Enum("Fast")), ("queue", DecisionValue.Code("new")))),
            SetCaseExpectation.NoDecision(name),
        };

        foreach (var form in forms)
        {
            var after = LoadFirst(form);

            after.Goldens.Select(golden => golden.Name).Should().Equal(before.Goldens.Select(golden => golden.Name));
            after.Goldens[2].Facts.Should().BeEquivalentTo(before.Goldens[2].Facts);
            after.Goldens.Where((_, index) => index != 2).Select(golden => golden.Expected)
                .Should().Equal(before.Goldens.Where((_, index) => index != 2).Select(golden => golden.Expected));
        }
    }

    [Fact]
    public void TheCaseKeepsItsPositionOnACollectRule()
    {
        var before = TestFiles.LoadReadiness();
        var name = before.Goldens[1].Name;

        var after = LoadReadinessWith(new SetCaseExpectation(name, null, Outcome()) { Results = [] });

        after.Goldens.Select(golden => golden.Name).Should().Equal(before.Goldens.Select(golden => golden.Name));
        after.Goldens[1].Facts.Should().BeEquivalentTo(before.Goldens[1].Facts);
    }
}
