using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.UnitTests.Decisions;

/// <summary>
/// P2-20.2 step 1 (paquete 0.A369): <see cref="DecisionKindKey"/> shape and
/// the <see cref="HitPolicy"/> ordinal pin, both covered here
/// in one test file.
/// </summary>
public sealed class DecisionKindKeyTests
{
    [Fact]
    public void From_AcceptsAValidModuleDotKindKey()
    {
        var key = DecisionKindKey.From("accounting.channel_settlement");

        key.Value.Should().Be("accounting.channel_settlement");
        key.Module.Should().Be("accounting");
    }

    [Theory]
    [InlineData("Accounting.x")]
    [InlineData("accounting")]
    [InlineData("a.b.c")]
    [InlineData("accounting.x\n")]
    [InlineData("")]
    public void From_RefusesAnythingNotShapedAsModuleDotKind(string value)
    {
        var act = () => DecisionKindKey.From(value);

        act.Should().Throw<DecisionEngineException>()
            .Where(e => e.ErrorCode == DecisionKindKey.ErrorInvalidFormat);
    }

    /// <summary>
    /// MUTATION (performed and reverted at gate time, 0.A369): relaxing
    /// <c>KeyRegex</c> to <c>(?i)</c> (allow uppercase) turns
    /// <c>From_RefusesAnythingNotShapedAsModuleDotKind("Accounting.x")</c> RED,
    /// because the theory's only remaining failure channel for that case is
    /// the regex rejecting the leading capital — proving the fact actually
    /// pins case-sensitivity rather than passing for an unrelated reason.
    /// </summary>
    [Fact]
    public void HitPolicy_OrdinalsArePinned()
    {
        ((int)HitPolicy.Unique).Should().Be(1);
        ((int)HitPolicy.First).Should().Be(2);
    }
}
