using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>Whether a refusal is cured by correcting a value or is a matter for the rule's maintainer.</summary>
internal enum RefusalWayOut
{
    /// <summary>The reviewer corrects the marked value and simulates again.</summary>
    FixData,

    /// <summary>The reviewer notes it in the review of the rule.</summary>
    AddToReview,
}

/// <summary>What the panel says about an engine refusal. The code itself is never shown.</summary>
/// <param name="Line">The sentence that names the refusal.</param>
/// <param name="WayOut">What the reviewer can do about it.</param>
internal sealed record RefusalText(string Line, RefusalWayOut WayOut)
{
    private const string Missing = "decisions.facts.missing";
    private const string TypeMismatch = "decisions.facts.type_mismatch";
    private const string TooLarge = "decisions.facts.too_large";
    private const string MultipleMatches = "decisions.evaluation.multiple_matches";
    private const string CompositionPrefix = "decisions.composition.";

    /// <summary>Writes the refusal for the code of a <see cref="SimulationFailure"/>.</summary>
    /// <param name="text">The strings of the render.</param>
    /// <param name="view">The rule as read; its facts give the names to show.</param>
    /// <param name="facts">The values that were given to the engine.</param>
    /// <param name="code">The engine's code.</param>
    internal static RefusalText For(UiText text, RuleView view, IReadOnlyDictionary<string, DecisionValue> facts, string code)
    {
        switch (code)
        {
            case Missing:
                return new RefusalText(
                    text.Plain("refuse.missing", ("fact", MissingFacts(view, facts))), RefusalWayOut.FixData);
            case TypeMismatch:
                return new RefusalText(
                    text.Plain("refuse.typeMismatch", ("fact", GivenFacts(view, facts))), RefusalWayOut.FixData);
            case TooLarge:
                return new RefusalText(text.Plain("refuse.tooLarge"), RefusalWayOut.FixData);
            case MultipleMatches:
                return new RefusalText(text.Plain("refuse.multipleMatches"), RefusalWayOut.AddToReview);
            default:
                return code.StartsWith(CompositionPrefix, StringComparison.Ordinal)
                    ? new RefusalText(text.Plain("refuse.composition"), RefusalWayOut.AddToReview)
                    : new RefusalText(text.Plain("refuse.other"), RefusalWayOut.AddToReview);
        }
    }

    /// <summary>The labels of the facts the engine was given, in declaration order: the engine's code does not say which one it refused.</summary>
    private static string GivenFacts(RuleView view, IReadOnlyDictionary<string, DecisionValue> facts) =>
        string.Join(", ", view.Facts.Where(fact => facts.ContainsKey(fact.Name)).Select(fact => fact.Label));

    /// <summary>The labels of the required facts the engine was not given; every required fact when it was given none of them.</summary>
    private static string MissingFacts(RuleView view, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        var missing = view.Facts.Where(fact => !fact.Nullable && !facts.ContainsKey(fact.Name)).Select(fact => fact.Label).ToList();
        if (missing.Count == 0)
        {
            missing = [.. view.Facts.Where(fact => !fact.Nullable).Select(fact => fact.Label)];
        }

        return string.Join(", ", missing);
    }
}
