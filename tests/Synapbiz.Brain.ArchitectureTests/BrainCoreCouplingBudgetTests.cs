using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// Freezes the external dependencies of the two Brain cores: the base class library and each other,
/// nothing else. Ported from nuvora's <c>BrainCoreCouplingBudgetTests</c> with an empty allowance.
/// </summary>
public sealed class BrainCoreCouplingBudgetTests
{
    private const string Contracts = "Synapbiz.Brain.Contracts";
    private const string Engine = "Synapbiz.Brain.Engine";

    private static readonly string[] Cores = [Contracts, Engine];

    private static readonly HashSet<(string Core, string Assembly, string Type)> AllowedTypes = [];

    [Fact]
    public void CoreAssembliesReferenceOnlyTheBclAndEachOther()
    {
        var bcl = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var core in Cores)
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, core + ".dll"));
            using var assembly = new PEReader(stream);
            var metadata = assembly.GetMetadataReader();
            Assert.NotEmpty(metadata.TypeReferences);

            foreach (var handle in metadata.TypeReferences)
            {
                var (scope, name) = ResolveType(metadata, handle);
                if (scope.Kind != HandleKind.AssemblyReference)
                {
                    continue;
                }

                var reference = metadata.GetAssemblyReference((AssemblyReferenceHandle)scope);
                var owner = metadata.GetString(reference.Name);
                if (!bcl.Contains(owner) && !Cores.Contains(owner, StringComparer.Ordinal)
                    && !AllowedTypes.Contains((core, owner, name)))
                {
                    offenders.Add($"{core} uses {name} from {owner}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void CoreProjectsDeclareNoPackageReference()
    {
        var root = FindRepositoryRoot();
        var contractsProject = CoreProject(root, Contracts);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var core in Cores)
        {
            var path = CoreProject(root, core);
            var project = XDocument.Load(path);

            foreach (var item in project.Descendants("PackageReference").Concat(project.Descendants("Reference")))
            {
                offenders.Add($"{core} declares {item.Name} {(string?)item.Attribute("Include") ?? (string?)item.Attribute("Update") ?? "?"}");
            }

            var projectReferences = project.Descendants("ProjectReference")
                .Select(item => (string?)item.Attribute("Include"))
                .Where(include => include is not null)
                .Select(include => Path.GetFullPath(include!.Replace('\\', '/'), Path.GetDirectoryName(path)!))
                .ToList();

            if (core == Contracts && projectReferences.Count != 0)
            {
                offenders.Add($"{core} declares {projectReferences.Count} ProjectReference(s), expected none");
            }

            if (core == Engine && !(projectReferences.Count == 1 && projectReferences[0] == contractsProject))
            {
                offenders.Add($"{core} must declare exactly one ProjectReference, to {Contracts}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
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

    private static string CoreProject(string root, string core) =>
        Path.Combine(root, "src", core, core + ".csproj");

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
