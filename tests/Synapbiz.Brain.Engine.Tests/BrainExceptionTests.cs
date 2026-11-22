using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Synapbiz.Brain;

namespace Synapbiz.Brain.Engine.Tests;

public sealed class BrainExceptionTests
{
    [Fact]
    public void DecisionEngineException_IsABrainException_ExposingErrorCodeAndArgs()
    {
        var args = new Dictionary<string, object?> { ["fact"] = "code" };
        var inner = new InvalidOperationException("inner");

        var exception = new DecisionEngineException("decisions.test_code", "A message.", args, inner);

        exception.Should().BeAssignableTo<BrainException>();
        exception.ErrorCode.Should().Be("decisions.test_code");
        exception.Message.Should().Be("A message.");
        exception.Args.Should().BeEquivalentTo(args);
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Args_DefaultToAnEmptyBag()
    {
        var exception = new DecisionEngineException("decisions.test_code", "A message.");

        exception.Args.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrBlankErrorCode_ThrowsArgumentException(string errorCode)
    {
        var act = () => new DecisionEngineException(errorCode, "A message.");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NullErrorCode_ThrowsArgumentException()
    {
        var act = () => new DecisionEngineException(null!, "A message.");

        act.Should().Throw<ArgumentException>();
    }
}
