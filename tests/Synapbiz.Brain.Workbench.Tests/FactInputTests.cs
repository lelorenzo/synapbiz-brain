using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class FactInputTests
{
    private static readonly FactDescriptor Allowed = new("channel", FactType.Enum, false, ["Web", "Phone"]);

    private static FactDescriptor Of(FactType type, bool nullable = false) => new("fact", type, nullable);

    public static TheoryData<FactType, string, DecisionValue> ValidTexts => new()
    {
        { FactType.Code, "  any text  ", DecisionValue.Code("any text") },
        { FactType.Enum, "Whatever", DecisionValue.Enum("Whatever") },
        { FactType.Boolean, "true", DecisionValue.Boolean(true) },
        { FactType.Boolean, " FALSE ", DecisionValue.Boolean(false) },
        { FactType.Integer, "-42", DecisionValue.Integer(-42) },
        { FactType.Integer, "+7", DecisionValue.Integer(7) },
        { FactType.Decimal, "12.50", DecisionValue.Decimal(12.5m) },
        { FactType.Decimal, "-0.25", DecisionValue.Decimal(-0.25m) },
        { FactType.Date, "2026-11-22", DecisionValue.Date(new DateOnly(2026, 11, 22)) },
        { FactType.Money, "12.50 USD", DecisionValue.Money(12.5m, "USD") },
        { FactType.Reference, "3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6b", DecisionValue.Reference(Samples.Account) },
    };

    public static TheoryData<FactType, string, string> InvalidTexts => new()
    {
        { FactType.Boolean, "yes", FactInput.ErrorNotABoolean },
        { FactType.Boolean, "1", FactInput.ErrorNotABoolean },
        { FactType.Integer, "12.5", FactInput.ErrorNotAnInteger },
        { FactType.Integer, "1,000", FactInput.ErrorNotAnInteger },
        { FactType.Integer, "99999999999999999999", FactInput.ErrorNotAnInteger },
        { FactType.Decimal, "1,5", FactInput.ErrorNotADecimal },
        { FactType.Decimal, "1e3", FactInput.ErrorNotADecimal },
        { FactType.Decimal, "abc", FactInput.ErrorNotADecimal },
        { FactType.Date, "22/11/2026", FactInput.ErrorNotADate },
        { FactType.Date, "2026-13-01", FactInput.ErrorNotADate },
        { FactType.Date, "2026-1-5", FactInput.ErrorNotADate },
        { FactType.Money, "12.50", FactInput.ErrorNotMoney },
        { FactType.Money, "12.50 usd", FactInput.ErrorNotMoney },
        { FactType.Money, "12.50 US", FactInput.ErrorNotMoney },
        { FactType.Money, "12.50  USD", FactInput.ErrorNotMoney },
        { FactType.Money, "abc USD", FactInput.ErrorNotMoney },
        { FactType.Money, "1 2 USD", FactInput.ErrorNotMoney },
        { FactType.Reference, "not a guid", FactInput.ErrorNotAReference },
    };

    [Theory]
    [MemberData(nameof(ValidTexts))]
    public void ATextOfTheRightShapeBecomesTheTypedValue(FactType type, string text, DecisionValue expected)
    {
        var result = FactInput.Parse(Of(type), text);

        result.Should().Be(new FactInputResult(true, expected, null));
    }

    [Theory]
    [MemberData(nameof(InvalidTexts))]
    public void ATextThatDoesNotFitGivesTheTypedErrorCode(FactType type, string text, string code)
    {
        var result = FactInput.Parse(Of(type), text);

        result.Should().Be(new FactInputResult(false, null, code));
    }

    [Fact]
    public void AnEnumTextMustBeOneOfTheAllowedValues()
    {
        FactInput.Parse(Allowed, " Phone ").Value.Should().Be(DecisionValue.Enum("Phone"));
        FactInput.Parse(Allowed, "phone").Should().Be(new FactInputResult(false, null, FactInput.ErrorNotAllowed));
        FactInput.Parse(Allowed, "Fax").ErrorCode.Should().Be(FactInput.ErrorNotAllowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankTextIsValidWithNoValueForANullableFact(string? text)
    {
        foreach (var type in Enum.GetValues<FactType>())
        {
            FactInput.Parse(Of(type, nullable: true), text).Should().Be(new FactInputResult(true, null, null), type.ToString());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\t ")]
    public void ABlankTextIsRequiredForAFactThatIsNotNullable(string? text)
    {
        foreach (var type in Enum.GetValues<FactType>())
        {
            FactInput.Parse(Of(type), text).Should().Be(new FactInputResult(false, null, FactInput.ErrorRequired), type.ToString());
        }
    }

    [Fact]
    public void AnUnknownFactTypeIsRefused()
    {
        var act = () => FactInput.Parse(Of((FactType)99), "x");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheErrorCodesAreStable()
    {
        new[]
        {
            FactInput.ErrorRequired, FactInput.ErrorNotAllowed, FactInput.ErrorNotABoolean, FactInput.ErrorNotAnInteger,
            FactInput.ErrorNotADecimal, FactInput.ErrorNotADate, FactInput.ErrorNotMoney, FactInput.ErrorNotAReference,
        }.Should().Equal(
            "workbench.fact.required", "workbench.fact.value_not_allowed", "workbench.fact.not_a_boolean", "workbench.fact.not_an_integer",
            "workbench.fact.not_a_decimal", "workbench.fact.not_a_date", "workbench.fact.not_money", "workbench.fact.not_a_reference");
    }

    [Fact]
    public void ParseAllReadsEveryFactOfTheKindAndCollectsTheErrors()
    {
        var kind = KindOf();
        var texts = new Dictionary<string, string?>
        {
            ["channel"] = "Phone",
            ["topic"] = "general",
            ["urgent"] = "maybe",
            ["ageDays"] = "12",
            ["rating"] = "",
            ["amount"] = "5 USD",
            ["receivedOn"] = "2026-13-01",
            ["account"] = null,
            ["nothing"] = "ignored",
        };

        var result = FactInput.ParseAll(kind, texts);

        result.IsValid.Should().BeFalse();
        result.Facts.Should().Equal(new Dictionary<string, DecisionValue>
        {
            ["channel"] = DecisionValue.Enum("Phone"),
            ["topic"] = DecisionValue.Code("general"),
            ["ageDays"] = DecisionValue.Integer(12),
            ["amount"] = DecisionValue.Money(5m, "USD"),
        });
        result.Errors.Should().Equal(new Dictionary<string, string>
        {
            ["urgent"] = FactInput.ErrorNotABoolean,
            ["receivedOn"] = FactInput.ErrorNotADate,
        });
    }

    [Fact]
    public void ParseAllTreatsAFactWithNoEntryAsEmpty()
    {
        var result = FactInput.ParseAll(KindOf(), new Dictionary<string, string?> { ["channel"] = "Web" });

        result.Errors.Keys.Should().Equal("topic", "urgent", "ageDays");
        result.Errors.Values.Should().OnlyContain(code => code == FactInput.ErrorRequired);
        result.Facts.Keys.Should().Equal("channel");
    }

    [Fact]
    public void ParseAllOfAValidFactSetIsValidAndFeedsTheEngine()
    {
        var kind = KindOf();
        var texts = new Dictionary<string, string?> { ["channel"] = "Web", ["topic"] = "x", ["urgent"] = "false", ["ageDays"] = "3" };

        var result = FactInput.ParseAll(kind, texts);

        result.IsValid.Should().BeTrue();
        TestFiles.LoadIntake("intake.first.rule.json").Evaluate(result.Facts, Samples.AsOf).Outcome.IsDecided.Should().BeTrue();
    }

    [Fact]
    public void ParseAllSkipsAFactComposedFromAnotherKind()
    {
        var kind = KindOf();
        var composed = kind with { Facts = [.. kind.Facts, new FactDescriptor("derived", FactType.Integer, false, null, DecisionKindKey.From("sample.child"))] };

        var result = FactInput.ParseAll(composed, new Dictionary<string, string?> { ["derived"] = "oops" });

        result.Errors.Keys.Should().NotContain("derived");
        result.Facts.Keys.Should().NotContain("derived");
    }

    private static DecisionKindDescriptor KindOf()
    {
        TestFiles.IntakeContract().TryGetKind("sample.intake", out var kind).Should().BeTrue();
        return kind!;
    }
}
