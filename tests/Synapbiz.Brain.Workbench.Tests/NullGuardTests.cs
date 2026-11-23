using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>Every public static method refuses a null reference argument that is not optional.</summary>
public sealed class NullGuardTests
{
    private static readonly BrainRule Rule = TestFiles.LoadIntake("intake.first.rule.json");

    private static readonly IReadOnlyDictionary<string, DecisionValue> Facts = Samples.Intake("Web", "x", false, 1);

    private static DecisionKindDescriptor Kind => Rule.Kind;

    public static TheoryData<string> Cases =>
    [
        "Humanize",
        "FormatValue",
        "FromMeta",
        "Describe",
        "Parse fact",
        "ParseAll kind",
        "ParseAll texts",
        "Simulate rule",
        "Simulate facts",
        "Run",
        "Diff before",
        "Diff after",
        "SamplesFromGoldens before",
        "SamplesFromGoldens after",
        "CompareBehaviour before",
        "CompareBehaviour after",
        "CompareBehaviour samples",
        "ContentHash",
        "Serialize",
        "ReviewParse",
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void ANullArgumentIsRefused(string call)
    {
        Action act = call switch
        {
            "Humanize" => () => WorkbenchText.Humanize(null!),
            "FormatValue" => () => WorkbenchText.FormatValue(null, null!, WorkbenchLocale.En),
            "FromMeta" => () => RuleLabels.FromMeta(null!, WorkbenchLocale.En),
            "Describe" => () => RuleDescriber.Describe(null!, WorkbenchLocale.En),
            "Parse fact" => () => FactInput.Parse(null!, "x"),
            "ParseAll kind" => () => FactInput.ParseAll(null!, new Dictionary<string, string?>()),
            "ParseAll texts" => () => FactInput.ParseAll(Kind, null!),
            "Simulate rule" => () => RuleSimulator.Simulate(null!, Facts, Samples.AsOf),
            "Simulate facts" => () => RuleSimulator.Simulate(Rule, null!, Samples.AsOf),
            "Run" => () => RuleTester.Run(null!),
            "Diff before" => () => RuleDiffer.Diff(null!, Rule),
            "Diff after" => () => RuleDiffer.Diff(Rule, null!),
            "SamplesFromGoldens before" => () => RuleDiffer.SamplesFromGoldens(null!, Rule),
            "SamplesFromGoldens after" => () => RuleDiffer.SamplesFromGoldens(Rule, null!),
            "CompareBehaviour before" => () => RuleDiffer.CompareBehaviour(null!, Rule, [], Samples.AsOf),
            "CompareBehaviour after" => () => RuleDiffer.CompareBehaviour(Rule, null!, [], Samples.AsOf),
            "CompareBehaviour samples" => () => RuleDiffer.CompareBehaviour(Rule, Rule, null!, Samples.AsOf),
            "ContentHash" => () => RuleReviewJson.ContentHash(null!),
            "Serialize" => () => RuleReviewJson.Serialize(null!),
            "ReviewParse" => () => RuleReviewJson.Parse(null!),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, "Unknown case."),
        };

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EveryGuardedCallIsListed()
    {
        var publicStatics = new[] { typeof(WorkbenchText), typeof(RuleDescriber), typeof(FactInput), typeof(RuleSimulator), typeof(RuleTester), typeof(RuleDiffer), typeof(RuleReviewJson) }
            .Concat([typeof(RuleLabels)])
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        publicStatics.Should().Equal(
            "CompareBehaviour", "ContentHash", "Describe", "Diff", "FormatValue", "FromMeta", "Humanize", "Parse", "ParseAll", "Run", "SamplesFromGoldens", "Serialize", "Simulate");
    }
}
