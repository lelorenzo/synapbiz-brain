using FluentAssertions;

namespace Synapbiz.Brain.Spec.Tests;

public sealed class LoadResultTests
{
    private static BrainRuleLoadResult LoadReadiness() => BrainRuleLoader.Load(
        TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json"),
        [BrainContract.Parse(TestFiles.Read("tests/fixtures/brain-spec/readiness.contract.json"))]);

    [Fact]
    public void TheDeferredChecksAreAReadOnlyListThatNoCallerCanChange()
    {
        var deferred = LoadReadiness().DeferredToEngine;

        deferred.Should().NotBeOfType<string[]>();
        var list = deferred.Should().BeAssignableTo<IList<string>>().Subject;
        list.IsReadOnly.Should().BeTrue();
        var add = () => list.Add("changed");
        var replace = () => list[0] = "changed";
        add.Should().Throw<NotSupportedException>();
        replace.Should().Throw<NotSupportedException>();
        LoadReadiness().DeferredToEngine.Should().Equal("overlap", "coverage", "goldens", "outcome-policy");
    }

    [Fact]
    public void TheDeferredChecksOfAFailedLoadAreEmptyAndReadOnlyToo()
    {
        var failed = BrainRuleLoader.Load("{ not json", []);

        failed.DeferredToEngine.Should().BeEmpty();
        failed.Errors.Should().BeAssignableTo<IList<BrainSpecError>>().Subject.IsReadOnly.Should().BeTrue();
    }
}
