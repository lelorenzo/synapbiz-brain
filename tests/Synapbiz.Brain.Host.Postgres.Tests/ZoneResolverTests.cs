using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>The one seam through which the host turns a zone id into a zone.</summary>
public sealed class ZoneResolverTests
{
    [Fact]
    public void TheDefaultResolverKnowsUtcAndNothingThatIsNotAZone()
    {
        var resolver = new SystemZoneResolver();

        resolver.Find("UTC").Should().NotBeNull();
        resolver.Find("Nowhere/Unknown").Should().BeNull();
        resolver.Find("").Should().BeNull();
        resolver.Find("../../etc/passwd").Should().BeNull();
        resolver.Find("America/New_York\0x").Should().BeNull();
    }

    [Fact]
    public void TheSystemTimeZoneDatabaseIsAskedInExactlyOneClassOfTheHost()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Fixtures.Locate("src/Synapbiz.Brain.Host.Postgres/Synapbiz.Brain.Host.Postgres.csproj")))!;
        var host = Path.Combine(root, "Synapbiz.Brain.Host.Postgres");
        var sources = Directory.EnumerateFiles(host, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();

        sources.Should().NotBeEmpty();
        foreach (var forbidden in new[] { "FindSystemTimeZoneById", "TimeZoneInfo.Local", "GetSystemTimeZones", "TryFindSystemTimeZoneById" })
        {
            var users = sources.Where(path => File.ReadAllText(path).Contains(forbidden, StringComparison.Ordinal)).Select(Path.GetFileName).ToList();
            users.Should().Equal(forbidden == "FindSystemTimeZoneById" ? ["ZoneResolver.cs"] : [], forbidden);
        }
    }

    [Fact]
    public void TheDefaultRegistrationUsesTheSystemResolver()
    {
        var services = new ServiceCollection();
        services.AddBrainProcessHostPostgres(store => store.ConnectionString = "Host=localhost");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IZoneResolver>().Should().BeOfType<SystemZoneResolver>();
    }
}
