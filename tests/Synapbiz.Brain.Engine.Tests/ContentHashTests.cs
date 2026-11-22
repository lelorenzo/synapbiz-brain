using System.Text;
using FluentAssertions;
using Synapbiz.Brain;

namespace Synapbiz.Brain.Engine.Tests;

public sealed class ContentHashTests
{
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void FromBytes_ComputesTheLowercaseSha256Hex()
    {
        var hash = ContentHash.FromBytes(Encoding.UTF8.GetBytes("abc"));

        hash.Value.Should().Be(AbcSha256);
        hash.Value.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void FromBytes_OfTheSameBytes_IsEqualByValue()
    {
        var first = ContentHash.FromBytes(Encoding.UTF8.GetBytes("abc"));
        var second = ContentHash.FromBytes(Encoding.UTF8.GetBytes("abc"));
        var other = ContentHash.FromBytes(Encoding.UTF8.GetBytes("abd"));

        first.Equals(second).Should().BeTrue();
        first.Equals((object)second).Should().BeTrue();
        (first == second).Should().BeTrue();
        (first != second).Should().BeFalse();
        first.GetHashCode().Should().Be(second.GetHashCode());

        first.Equals(other).Should().BeFalse();
        (first == other).Should().BeFalse();
        (first != other).Should().BeTrue();
    }

    [Fact]
    public void Equality_HandlesNullOperands()
    {
        var hash = ContentHash.FromBytes(Encoding.UTF8.GetBytes("abc"));

        var absent = Absent();
        object? absentObject = Absent();

        hash.Equals(absent).Should().BeFalse();
        hash.Equals(absentObject).Should().BeFalse();
        hash.Equals("not a hash").Should().BeFalse();
        (hash == absent).Should().BeFalse();
        (absent == hash).Should().BeFalse();
        (absent == Absent()).Should().BeTrue();
    }

    [Fact]
    public void ToString_ReturnsTheValue()
    {
        var hash = ContentHash.FromBytes(Encoding.UTF8.GetBytes("abc"));

        hash.ToString().Should().Be(AbcSha256);
    }

    [Fact]
    public void FromBytes_OfEmptyInput_IsTheSha256OfTheEmptyString()
    {
        ContentHash.FromBytes([]).Value.Should()
            .Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    private static ContentHash? Absent() => null;
}
