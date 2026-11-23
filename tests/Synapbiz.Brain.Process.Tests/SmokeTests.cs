using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void TheOutreachScenariosPass()
    {
        var result = TestFiles.OutreachProcess().RunScenarios();

        result.Results.Should().HaveCount(5);
        result.Results.Where(r => !r.Passed).Select(r => r.Name + ": " + r.Failure).Should().BeEmpty();
    }
}
