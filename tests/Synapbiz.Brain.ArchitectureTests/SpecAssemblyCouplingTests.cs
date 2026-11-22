using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// Freezes what <c>Synapbiz.Brain.Spec</c> may depend on: the base class library, the two Brain libraries and
/// <c>JsonSchema.Net</c> with the packages it brings, listed explicitly as the restore graph holds them.
/// </summary>
public sealed class SpecAssemblyCouplingTests
{
    private const string Spec = "Synapbiz.Brain.Spec";

    private static readonly string[] Libraries = ["Synapbiz.Brain.Contracts", "Synapbiz.Brain.Engine"];

    /// <summary>The assemblies of JsonSchema.Net 8.0.5 and its transitive packages, as the restore graph lists them.</summary>
    private static readonly string[] JsonSchemaAssemblies = ["JsonSchema.Net", "JsonPointer.Net", "Json.More", "Humanizer"];

    /// <summary>The packages the restore graph of the Spec project holds, runtime packages and build-only analyzers.</summary>
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
    public void TheSpecAssemblyReferencesOnlyTheBclTheTwoLibrariesAndJsonSchemaNetWithItsPackages()
    {
        var bcl = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var allowed = Libraries.Concat(JsonSchemaAssemblies).ToHashSet(StringComparer.Ordinal);
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, Spec + ".dll"));
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        Assert.NotEmpty(metadata.TypeReferences);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in metadata.AssemblyReferences)
        {
            var owner = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (!bcl.Contains(owner) && !allowed.Contains(owner))
            {
                offenders.Add($"{Spec} references the assembly {owner}");
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
                offenders.Add($"{Spec} uses {name} from {owner}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheSpecProjectDeclaresOnlyJsonSchemaNetAndTheTwoLibraries()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", Spec, Spec + ".csproj");
        var project = XDocument.Load(path);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        var packages = project.Descendants("PackageReference").Select(item => (string?)item.Attribute("Include")).ToList();
        if (!packages.SequenceEqual(["JsonSchema.Net"]))
        {
            offenders.Add($"{Spec} must declare exactly one PackageReference, JsonSchema.Net, but declares [{string.Join(", ", packages)}]");
        }

        foreach (var item in project.Descendants("Reference"))
        {
            offenders.Add($"{Spec} declares Reference {(string?)item.Attribute("Include") ?? "?"}");
        }

        var projects = project.Descendants("ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(((string?)item.Attribute("Include"))!.Replace('\\', '/')))
            .Order(StringComparer.Ordinal).ToList();
        if (!projects.SequenceEqual(Libraries))
        {
            offenders.Add($"{Spec} must reference exactly [{string.Join(", ", Libraries)}] but references [{string.Join(", ", projects)}]");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheRestoreGraphOfTheSpecProjectHoldsExactlyTheExpectedPackages()
    {
        var assets = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", Spec, "obj", "project.assets.json")))!;
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
