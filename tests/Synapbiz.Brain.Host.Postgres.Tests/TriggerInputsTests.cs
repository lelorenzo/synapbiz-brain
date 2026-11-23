using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>The pure helpers that read a payload and build the keys and inputs of the instances a trigger starts.</summary>
public sealed class TriggerInputsTests
{
    [Theory]
    [InlineData("{\"k\":\"\\ud800\"}")]
    [InlineData("{\"k\":\"\\udc00\"}")]
    [InlineData("{\"k\":\"a\\u0000b\"}")]
    [InlineData("{\"k\":\"a\\tb\"}")]
    [InlineData("{\"k\":\"   \"}")]
    [InlineData("{\"k\":\"@x\"}")]
    [InlineData("{\"k\":17}")]
    [InlineData("{\"k\":null}")]
    [InlineData("{\"k\":{\"x\":1}}")]
    [InlineData("{\"other\":\"c-1\"}")]
    public void ABadKeyIsNullAndReadingItNeverThrowsEvenOnAnObjectThatWasNotChecked(string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();

        var read = () => TriggerInputs.BusinessKey(payload, "k");

        read.Should().NotThrow();
        read().Should().BeNull();
    }

    [Fact]
    public void AMemberThatTheObjectHoldsTwiceIsNoKeyAndNeverThrows()
    {
        var payload = JsonNode.Parse("{\"k\":\"a\",\"k\":\"b\"}")!.AsObject();

        var read = () => TriggerInputs.BusinessKey(payload, "k");

        read.Should().NotThrow();
        read().Should().BeNull();
    }

    [Theory]
    [InlineData("c-1", true)]
    [InlineData("a b", true)]
    [InlineData(" a ", true)]
    [InlineData("a@b", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("\u00a0", false)]
    [InlineData("@a", false)]
    [InlineData(" @a", false)]
    [InlineData("a\0b", false)]
    [InlineData("a\u0085b", false)]
    public void ABusinessKeyIsNotBlankHasNoControlCharacterNoUnpairedSurrogateAndDoesNotStartWithTheReservedPrefix(string key, bool expected)
    {
        TriggerInputs.IsBusinessKey(key).Should().Be(expected);
    }

    [Fact]
    public void ASurrogateIsOnlyAllowedAsOneHalfOfAPairInTheRightOrder()
    {
        // Built from code units: a test parameter would lose an unpaired surrogate on its way through the test runner.
        var high = ((char)0xD83D).ToString();
        var low = ((char)0xDE00).ToString();

        TriggerInputs.IsBusinessKey("a" + high + low + "b").Should().BeTrue();
        TriggerInputs.IsBusinessKey(high + low).Should().BeTrue();
        TriggerInputs.IsBusinessKey(high).Should().BeFalse();
        TriggerInputs.IsBusinessKey(low).Should().BeFalse();
        TriggerInputs.IsBusinessKey("a" + high).Should().BeFalse();
        TriggerInputs.IsBusinessKey(high + "a").Should().BeFalse();
        TriggerInputs.IsBusinessKey(low + high).Should().BeFalse();
        TriggerInputs.IsBusinessKey(high + high + low).Should().BeFalse();
        TriggerInputs.IsBusinessKey(high + low + low).Should().BeFalse();
    }

    [Fact]
    public void TheLengthLimitIsTwoHundredCharacters()
    {
        TriggerInputs.IsBusinessKey(new string('k', 200)).Should().BeTrue();
        TriggerInputs.IsBusinessKey(new string('k', 201)).Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"a\":1}", true)]
    [InlineData("[1]", false)]
    [InlineData("\"text\"", false)]
    [InlineData("{ not json", false)]
    [InlineData("{\"a\":1,\"a\":2}", false)]
    [InlineData("{\"a\":\"\\ud800\"}", false)]
    [InlineData("{\"\\udc00\":1}", false)]
    [InlineData("{\"a\":[{\"b\":\"\\ud83d\"}]}", false)]
    [InlineData("{\"a\":\"\\ud83d\\ude00\"}", true)]
    [InlineData("{\"a\":\"\\u0000\"}", true)]
    public void ThePayloadIsAnObjectWithoutRepeatedMembersAndWithEveryStringReadable(string json, bool readable)
    {
        var parse = () => TriggerInputs.ParsePayload(json);

        parse.Should().NotThrow();
        (parse() is not null).Should().Be(readable);
    }

    [Fact]
    public void ATextThatCannotBeEncodedIsNoPayloadAndNeverThrows()
    {
        var parse = () => TriggerInputs.ParsePayload("{\"a\":\"\ud800\"}");

        parse.Should().NotThrow();
        parse().Should().BeNull();
    }

    [Fact]
    public void TheKeyOfAScheduleStartIsReservedAndNamesTheScheduleAndTheOccurrenceInUtc()
    {
        var occurrence = new DateTimeOffset(2027, 11, 7, 1, 30, 0, TimeSpan.FromHours(-4));

        var key = TriggerInputs.ScheduleBusinessKey("daily", occurrence);

        key.Should().Be("@schedule/daily/20271107T0530Z");
        TriggerInputs.IsBusinessKey(key).Should().BeFalse("a key a payload carries can never be the key of a schedule start");
    }
}
