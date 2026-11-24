using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// Freezes what <c>Synapbiz.Brain.Workbench.Postgres</c> may depend on: the base class library, the headless workbench, the Postgres
/// host and the process library, with no package of its own and no use of Npgsql. It is the one place that knows both the workbench
/// contract (<c>IFlowLiveSource</c>) and the engine of the host, so that neither of them has to know the other:
/// <c>Synapbiz.Brain.Host.Postgres</c> may reference only the process library (<see cref="HostAssemblyCouplingTests"/>), and the
/// workbench only the four libraries (<see cref="WorkbenchAssemblyCouplingTests"/>).
/// </summary>
public sealed class WorkbenchPostgresAssemblyCouplingTests
{
    private const string Adapter = "Synapbiz.Brain.Workbench.Postgres";

    private static readonly string[] Allowed = ["Synapbiz.Brain.Process", "Synapbiz.Brain.Workbench", "Synapbiz.Brain.Host.Postgres"];

    [Fact]
    public void TheAdapterAssemblyReferencesOnlyTheBclTheWorkbenchTheHostAndTheProcessLibrary()
    {
        var bcl = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, Adapter + ".dll"));
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        Assert.NotEmpty(metadata.TypeReferences);
        var referenced = new SortedSet<string>(StringComparer.Ordinal);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in metadata.AssemblyReferences)
        {
            var owner = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            referenced.Add(owner);
            if (!bcl.Contains(owner) && !Allowed.Contains(owner, StringComparer.Ordinal))
            {
                offenders.Add($"{Adapter} references the assembly {owner}");
            }
        }

        foreach (var handle in metadata.TypeReferences)
        {
            var reference = metadata.GetTypeReference(handle);
            if (reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
            {
                continue;
            }

            var owner = metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name);
            if (!bcl.Contains(owner) && !Allowed.Contains(owner, StringComparer.Ordinal))
            {
                offenders.Add($"{Adapter} uses {metadata.GetString(reference.Namespace)}.{metadata.GetString(reference.Name)} from {owner}");
            }
        }

        Assert.Contains("Synapbiz.Brain.Workbench", referenced);
        Assert.Contains("Synapbiz.Brain.Host.Postgres", referenced);
        Assert.DoesNotContain("Npgsql", referenced);
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheAdapterProjectDeclaresNoPackageAndReferencesExactlyTheWorkbenchAndTheHost()
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", Adapter, Adapter + ".csproj"));
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var item in project.Descendants("PackageReference"))
        {
            offenders.Add($"{Adapter} declares PackageReference {(string?)item.Attribute("Include") ?? "?"}");
        }

        foreach (var item in project.Descendants("Reference"))
        {
            offenders.Add($"{Adapter} declares Reference {(string?)item.Attribute("Include") ?? "?"}");
        }

        var projects = project.Descendants("ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(((string?)item.Attribute("Include"))!.Replace('\\', '/')))
            .Order(StringComparer.Ordinal).ToList();
        string[] expected = ["Synapbiz.Brain.Host.Postgres", "Synapbiz.Brain.Workbench"];
        if (!projects.SequenceEqual(expected))
        {
            offenders.Add($"{Adapter} must reference exactly [{string.Join(", ", expected)}] but references [{string.Join(", ", projects)}]");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheRestoreGraphOfTheAdapterHoldsOnlyWhatTheHostAndTheWorkbenchBring()
    {
        var restore = Packages("src", Adapter);
        var host = Packages("src", "Synapbiz.Brain.Host.Postgres");
        var workbench = Packages("src", "Synapbiz.Brain.Workbench");

        Assert.NotEmpty(restore);
        Assert.Equal(host.Concat(workbench).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), restore);
    }

    [Fact]
    public void NeitherTheHostNorTheWorkbenchReferencesTheAdapter()
    {
        foreach (var other in new[] { "Synapbiz.Brain.Host.Postgres", "Synapbiz.Brain.Workbench", "Synapbiz.Brain.Workbench.Blazor" })
        {
            var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", other, other + ".csproj"));
            Assert.DoesNotContain(project.Descendants("ProjectReference"), item => ((string?)item.Attribute("Include"))!.Contains(Adapter, StringComparison.Ordinal));
        }
    }

    private static List<string> Packages(string folder, string project)
    {
        var assets = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), folder, project, "obj", "project.assets.json")))!;
        return [.. assets["targets"]!.AsObject().SelectMany(framework => framework.Value!.AsObject())
            .Where(entry => (string?)entry.Value!["type"] == "package")
            .Select(entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Synapbiz.Brain.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find Synapbiz.Brain.sln above the architecture test assembly.");
    }
}
