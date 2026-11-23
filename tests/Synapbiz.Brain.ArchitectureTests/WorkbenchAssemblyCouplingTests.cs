using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// Freezes what <c>Synapbiz.Brain.Workbench</c> may depend on: the base class library and the three Brain libraries, with no
/// package of its own. The workbench adds presentation and comparison to the engine and must stay free of anything else.
/// </summary>
public sealed class WorkbenchAssemblyCouplingTests
{
    private const string Workbench = "Synapbiz.Brain.Workbench";

    private static readonly string[] Libraries = ["Synapbiz.Brain.Contracts", "Synapbiz.Brain.Engine", "Synapbiz.Brain.Spec"];

    /// <summary>
    /// The packages the restore graph of the workbench project holds. The project declares none: the two build analyzers come
    /// from <c>Directory.Build.props</c> and the other four flow through the project reference to <c>Synapbiz.Brain.Spec</c>
    /// (JsonSchema.Net 8.0.5 with its transitive packages). The assembly itself references none of them, which the first test
    /// checks, so no package is added by the workbench.
    /// </summary>
    private static readonly string[] ExpectedPackages =
    [
        "Humanizer.Core/3.0.1",
        "Json.More.Net/2.2.0",
        "JsonPointer.Net/6.0.1",
        "JsonSchema.Net/8.0.5",
        "Microsoft.CodeAnalysis.NetAnalyzers/10.0.401",
        "SonarAnalyzer.CSharp/10.8.0.113526",
    ];

    [Fact]
    public void TheWorkbenchAssemblyReferencesOnlyTheBclAndTheThreeLibraries()
    {
        var bcl = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var allowed = Libraries.ToHashSet(StringComparer.Ordinal);
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, Workbench + ".dll"));
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        Assert.NotEmpty(metadata.TypeReferences);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in metadata.AssemblyReferences)
        {
            var owner = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (!bcl.Contains(owner) && !allowed.Contains(owner))
            {
                offenders.Add($"{Workbench} references the assembly {owner}");
            }
        }

        foreach (var handle in metadata.TypeReferences)
        {
            var (scope, name) = ResolveType(metadata, handle);
            if (scope.Kind != HandleKind.AssemblyReference)
            {
                continue;
            }

            var owner = metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
            if (!bcl.Contains(owner) && !allowed.Contains(owner))
            {
                offenders.Add($"{Workbench} uses {name} from {owner}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheWorkbenchProjectDeclaresNoPackageAndReferencesExactlyTheThreeLibraries()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", Workbench, Workbench + ".csproj");
        var project = XDocument.Load(path);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var item in project.Descendants("PackageReference"))
        {
            offenders.Add($"{Workbench} declares PackageReference {(string?)item.Attribute("Include") ?? "?"}");
        }

        foreach (var item in project.Descendants("Reference"))
        {
            offenders.Add($"{Workbench} declares Reference {(string?)item.Attribute("Include") ?? "?"}");
        }

        var projects = project.Descendants("ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(((string?)item.Attribute("Include"))!.Replace('\\', '/')))
            .Order(StringComparer.Ordinal).ToList();
        if (!projects.SequenceEqual(Libraries))
        {
            offenders.Add($"{Workbench} must reference exactly [{string.Join(", ", Libraries)}] but references [{string.Join(", ", projects)}]");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheRestoreGraphOfTheWorkbenchProjectHoldsNoPackageBeyondTheAnalyzersAndWhatTheSpecLibraryBrings()
    {
        var assets = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", Workbench, "obj", "project.assets.json")))!;
        var targets = assets["targets"]!.AsObject();
        var packages = targets.SelectMany(framework => framework.Value!.AsObject())
            .Where(entry => (string?)entry.Value!["type"] == "package")
            .Select(entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(targets);
        Assert.Equal(ExpectedPackages, packages);
    }

    private static (EntityHandle Scope, string Name) ResolveType(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        var name = metadata.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var (scope, declaringType) = ResolveType(metadata, (TypeReferenceHandle)reference.ResolutionScope);
            return (scope, declaringType + "+" + name);
        }

        var typeNamespace = metadata.GetString(reference.Namespace);
        return (reference.ResolutionScope, typeNamespace.Length == 0 ? name : typeNamespace + "." + name);
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
