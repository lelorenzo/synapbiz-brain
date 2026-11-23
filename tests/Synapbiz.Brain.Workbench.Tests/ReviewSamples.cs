using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The review used by the serialisation tests: every state, every value type, both expectation shapes and non-ASCII text.</summary>
internal static class ReviewSamples
{
    internal static readonly Guid Account = new("3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6b");

    internal static RuleReview Full() => new(
        "readiness-notes",
        3,
        RuleReviewJson.ContentHash("{\"id\":\"readiness-notes\"}"),
        "Reviewer One",
        new DateTimeOffset(2026, 11, 22, 21, 0, 0, TimeSpan.Zero),
        [
            new ReviewItem("rule", ReviewState.Unreviewed, string.Empty),
            new ReviewItem("row:low-score", ReviewState.NeedsFix, "La puntuación límite debería ser 50 — ¿revisar?"),
            new ReviewItem("golden:A low score at intake", ReviewState.Confirmed, "Looks right, \"quoted\" and <tagged> & more."),
            new ReviewItem("row:late-stage", ReviewState.Question, "Why does Review count as late?\nSecond line."),
        ],
        [
            new ProposedCase(
                "Every type of fact",
                new Dictionary<string, DecisionValue>
                {
                    ["zeta"] = DecisionValue.Reference(Account),
                    ["code"] = DecisionValue.Code("ab-1"),
                    ["stage"] = DecisionValue.Enum("Review"),
                    ["urgent"] = DecisionValue.Boolean(false),
                    ["score"] = DecisionValue.Integer(-20),
                    ["rate"] = DecisionValue.Decimal(12.50m),
                    ["total"] = DecisionValue.Money(99.9m, "EUR"),
                    ["day"] = DecisionValue.Date(new DateOnly(2026, 11, 22)),
                },
                false,
                """{"note":"low-score","órden":"¿sí?"}""",
                "low-score"),
            new ProposedCase("Nothing matches", new Dictionary<string, DecisionValue>(), true, null, null),
            new ProposedCase(
                "A collect case without a pinned row",
                new Dictionary<string, DecisionValue> { ["flag"] = DecisionValue.Boolean(true) },
                false,
                """[{"note":"a"},{"note":"b"}]""",
                null),
        ]);
}
