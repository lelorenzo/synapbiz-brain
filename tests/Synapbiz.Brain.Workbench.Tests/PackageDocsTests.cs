using System.Text.RegularExpressions;
using FluentAssertions;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The README, the consuming guide and the architecture overview name the same packages the repository packs, and say how many.</summary>
public sealed class PackageDocsTests
{
    private static readonly string[] Words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen"];

    private static IReadOnlyList<string> Packages()
    {
        var root = Path.GetDirectoryName(TestFiles.Locate("Synapbiz.Brain.sln"))!;
        return [.. Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void The_readme_counts_the_packages_and_lists_every_one_with_its_own_line()
    {
        var packages = Packages();
        var readme = TestFiles.Read("README.md");
        var head = readme[..readme.IndexOf("## Build and test", StringComparison.Ordinal)];

        packages.Should().HaveCount(11);
        head.Should().Contain("as " + Words[packages.Count] + " packages (" + Words[packages.Count - 1] + " libraries and one .NET tool)");
        foreach (var package in packages)
        {
            Regex.Matches(head, "^- `" + Regex.Escape(package) + "`:", RegexOptions.Multiline).Should().HaveCount(1, package);
        }

        Regex.Matches(head, "^- `Synapbiz\\.Brain\\.", RegexOptions.Multiline).Should().HaveCount(packages.Count);
        readme.Should().Contain("# " + Words[packages.Count] + " packages");
    }

    [Fact]
    public void The_readme_says_the_blazor_package_has_both_components()
    {
        var line = TestFiles.Read("README.md").Split('\n').Single(item => item.StartsWith("- `Synapbiz.Brain.Workbench.Blazor`:", StringComparison.Ordinal));

        line.Should().Contain("`RuleWorkbench`").And.Contain("`FlowWorkbench`").And.Contain("two Razor components");
        line.Should().NotContain("one Razor component");
    }

    [Fact]
    public void The_consuming_guide_and_the_overview_name_every_package_and_both_components()
    {
        var packages = Packages();
        var consuming = TestFiles.Read("docs/consuming.md");
        var overview = TestFiles.Read("docs/architecture/overview.md");

        consuming.Should().Contain("The " + Words[packages.Count] + " packages");
        foreach (var package in packages)
        {
            consuming.Should().Contain("`" + package + "`", package);
            overview.Should().Contain("| `" + package + "` |", package);
        }

        overview.Should().Contain("two public components: `RuleWorkbench`").And.Contain("`FlowWorkbench`");
    }
}
