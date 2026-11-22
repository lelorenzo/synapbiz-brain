using System.Globalization;
using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Facts;

namespace Nuvora.UnitTests.Decisions.Engine;

/// <summary>
/// P2-20.2 step 2 (paquete 0.A369): canonical JSON + hashing determinism
/// (key order, decimal scale, ambient culture) and the 4 KB fact-set cap.
/// </summary>
public sealed class CanonicalFactsTests
{
    /// <summary>
    /// MUTATION (performed and reverted at gate time): dropping the
    /// <c>OrderBy(..., StringComparer.Ordinal)</c> in
    /// <c>CanonicalFacts.ToCanonicalJson</c> (iterating <c>values.Keys</c>
    /// directly) turns this RED, because a <see cref="Dictionary{TKey,TValue}"/>
    /// does not guarantee enumeration order matches insertion order across
    /// the two differently-ordered fixtures below.
    /// </summary>
    [Fact]
    public void ToCanonicalJson_IsInvariantToKeyInsertionOrder()
    {
        var insertedBThenA = new Dictionary<string, DecisionValue>
        {
            ["b"] = DecisionValue.Code("second"),
            ["a"] = DecisionValue.Code("first"),
        };
        var insertedAThenB = new Dictionary<string, DecisionValue>
        {
            ["a"] = DecisionValue.Code("first"),
            ["b"] = DecisionValue.Code("second"),
        };

        CanonicalFacts.ToCanonicalJson(insertedBThenA).Should().Be(CanonicalFacts.ToCanonicalJson(insertedAThenB));
        CanonicalFacts.Hash(insertedBThenA).Should().Be(CanonicalFacts.Hash(insertedAThenB));
        CanonicalFacts.ToCanonicalJson(insertedAThenB).Should().Be("""{"a":"first","b":"second"}""");
    }

    [Fact]
    public void ToCanonicalJson_NormalizesDecimalScale_SoTrailingZerosDoNotChangeTheHash()
    {
        var withTrailingZero = new Dictionary<string, DecisionValue> { ["amount"] = DecisionValue.Decimal(1.50m) };
        var withoutTrailingZero = new Dictionary<string, DecisionValue> { ["amount"] = DecisionValue.Decimal(1.5m) };

        CanonicalFacts.ToCanonicalJson(withTrailingZero).Should().Be(CanonicalFacts.ToCanonicalJson(withoutTrailingZero));
        CanonicalFacts.Hash(withTrailingZero).Should().Be(CanonicalFacts.Hash(withoutTrailingZero));
        CanonicalFacts.ToCanonicalJson(withoutTrailingZero).Should().Be("""{"amount":1.5}""");
    }

    [Fact]
    public void ToCanonicalJson_NormalizesIntegralDecimalsToTheBareInteger()
    {
        var facts = new Dictionary<string, DecisionValue> { ["amount"] = DecisionValue.Decimal(100.00m) };

        CanonicalFacts.ToCanonicalJson(facts).Should().Be("""{"amount":100}""");
    }

    [Fact]
    public void ToCanonicalJson_NormalizesZeroRegardlessOfSign()
    {
        var positiveZero = new Dictionary<string, DecisionValue> { ["amount"] = DecisionValue.Decimal(0.00m) };
        var negativeZero = new Dictionary<string, DecisionValue> { ["amount"] = DecisionValue.Decimal(-0.00m) };

        CanonicalFacts.ToCanonicalJson(positiveZero).Should().Be("""{"amount":0}""");
        CanonicalFacts.ToCanonicalJson(negativeZero).Should().Be("""{"amount":0}""");
    }

    [Fact]
    public void ToCanonicalJson_FormatsMoneyAsAmountAndCurrency()
    {
        var facts = new Dictionary<string, DecisionValue> { ["price"] = DecisionValue.Money(99.90m, "USD") };

        CanonicalFacts.ToCanonicalJson(facts).Should().Be("""{"price":{"amount":99.9,"currency":"USD"}}""");
    }

    [Fact]
    public void ToCanonicalJson_FormatsDateAsIsoDate()
    {
        var facts = new Dictionary<string, DecisionValue> { ["when"] = DecisionValue.Date(new DateOnly(2026, 3, 7)) };

        CanonicalFacts.ToCanonicalJson(facts).Should().Be("""{"when":"2026-03-07"}""");
    }

    [Fact]
    public void ToCanonicalJson_UnderACommaDecimalCulture_EqualsTheInvariantOutput()
    {
        var facts = new Dictionary<string, DecisionValue>
        {
            ["amount"] = DecisionValue.Decimal(1234.5m),
            ["price"] = DecisionValue.Money(10.25m, "COP"),
            ["when"] = DecisionValue.Date(new DateOnly(2026, 12, 31)),
        };

        var invariantOutput = CanonicalFacts.ToCanonicalJson(facts);

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-CO");
            CanonicalFacts.ToCanonicalJson(facts).Should().Be(invariantOutput);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ToCanonicalJson_FormatsBooleanIntegerAndReference()
    {
        var reference = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var facts = new Dictionary<string, DecisionValue>
        {
            ["flag"] = DecisionValue.Boolean(true),
            ["count"] = DecisionValue.Integer(42),
            ["partyId"] = DecisionValue.Reference(reference),
        };

        CanonicalFacts.ToCanonicalJson(facts).Should().Be(
            """{"count":42,"flag":true,"partyId":"11111111-2222-3333-4444-555555555555"}""");
    }

    [Fact]
    public void ToCanonicalJson_NormalizesAZeroScaleDecimal_ToTheBareInteger()
    {
        var facts = new Dictionary<string, DecisionValue> { ["amount"] = DecisionValue.Decimal(100m) };

        CanonicalFacts.ToCanonicalJson(facts).Should().Be("""{"amount":100}""");
    }

    [Fact]
    public void Build_AnUndeclaredFact_ThrowsMissing()
    {
        var descriptor = CapDescriptor();

        var act = () => FactSet.Build(
            descriptor, new Dictionary<string, DecisionValue> { ["x"] = DecisionValue.Code("a"), ["unknown"] = DecisionValue.Code("b") });

        act.Should().Throw<DecisionEngineException>().Where(e => e.ErrorCode == FactSet.ErrorMissing);
    }

    [Fact]
    public void Build_AValueOfTheWrongCase_ThrowsTypeMismatchNamingTheFact()
    {
        var descriptor = CapDescriptor();

        var act = () => FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["x"] = DecisionValue.Integer(1) });

        act.Should().Throw<DecisionEngineException>()
            .Where(e => e.ErrorCode == FactSet.ErrorTypeMismatch && e.Message.Contains("'x'", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_AValueOfTheDeclaredCase_DoesNotThrow()
    {
        var descriptor = CapDescriptor();

        var act = () => FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["x"] = DecisionValue.Code("ok") });

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_AMissingRequiredFact_ThrowsMissing()
    {
        var descriptor = CapDescriptor();

        var act = () => FactSet.Build(descriptor, new Dictionary<string, DecisionValue>());

        act.Should().Throw<DecisionEngineException>().Where(e => e.ErrorCode == FactSet.ErrorMissing);
    }

    [Fact]
    public void Build_AtExactlyTheCap_Passes()
    {
        var descriptor = CapDescriptor();
        var padded = new string('a', PaddingLengthForTotalBytes(4096));

        var act = () => FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["x"] = DecisionValue.Code(padded) });

        act.Should().NotThrow();
        CanonicalFacts.ToCanonicalJsonBytes(
            new Dictionary<string, DecisionValue> { ["x"] = DecisionValue.Code(padded) }).Length.Should().Be(4096);
    }

    [Fact]
    public void Build_OneByteOverTheCap_ThrowsTooLarge()
    {
        var descriptor = CapDescriptor();
        var padded = new string('a', PaddingLengthForTotalBytes(4097));

        var act = () => FactSet.Build(descriptor, new Dictionary<string, DecisionValue> { ["x"] = DecisionValue.Code(padded) });

        act.Should().Throw<DecisionEngineException>().Where(e => e.ErrorCode == CanonicalFacts.ErrorTooLarge);
    }

    /// <summary>The canonical JSON for a single Code fact named "x" is <c>{"x":"&lt;padding&gt;"}</c> — 8 bytes of structure plus the padding length.</summary>
    private static int PaddingLengthForTotalBytes(int totalBytes) => totalBytes - 8;

    private static DecisionKindDescriptor CapDescriptor() => new()
    {
        Key = DecisionKindKey.From("test.cap"),
        SchemaVersion = 1,
        Facts = [new FactDescriptor("x", FactType.Code)],
        OutcomeFields = [],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "test.decisions.read",
        AuthorPermission = "test.decisions.author",
        ApprovePermission = "test.decisions.approve",
        ContainsPersonalData = false,
        ReadTracePermission = "test.decisions.read_trace",
    };
}
