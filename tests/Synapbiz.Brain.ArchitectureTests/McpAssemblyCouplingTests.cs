using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// Freezes what the two assemblies of Brain MCP may depend on, and that the official MCP SDK is referenced by these two and by nothing
/// else in the repository: <c>Synapbiz.Brain.Mcp</c> (the adapter) and <c>Synapbiz.Brain.Mcp.Tool</c> (the <c>brain-mcp</c> program). Neither reaches the network
/// or starts a process; the adapter touches no file (only the store it is given does) and the program reads one file, the catalog.
/// </summary>
public sealed class McpAssemblyCouplingTests
{
    private const string Adapter = "Synapbiz.Brain.Mcp";
    private const string Program = "Synapbiz.Brain.Mcp.Tool";
    private const string SdkPackage = "ModelContextProtocol";

    /// <summary>Assemblies of the SDK and of the extension packages it brings, as the restore graph holds them (not part of the base class library).</summary>
    private static readonly string[] SdkAssemblies =
    [
        "ModelContextProtocol", "ModelContextProtocol.Core", "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.Options", "Microsoft.Extensions.AI.Abstractions", "Microsoft.Extensions.Primitives",
        "Microsoft.Extensions.Caching.Abstractions", "Microsoft.Extensions.Configuration.Abstractions", "Microsoft.Extensions.Diagnostics.Abstractions",
        "Microsoft.Extensions.FileProviders.Abstractions",
    ];

    /// <summary>The packages of the restore graph of both projects: the SDK with what it brings, JsonSchema.Net with what it brings (through the authoring library), and the build analyzers.</summary>
    private static readonly string[] ExpectedPackages =
    [
        "Humanizer.Core/3.0.1",
        "Json.More.Net/2.2.0",
        "JsonPointer.Net/6.0.1",
        "JsonSchema.Net/8.0.5",
        "Microsoft.CodeAnalysis.NetAnalyzers/10.0.401",
        "Microsoft.Extensions.AI.Abstractions/10.11.0",
        "Microsoft.Extensions.Caching.Abstractions/10.0.14",
        "Microsoft.Extensions.Configuration.Abstractions/10.0.14",
        "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.14",
        "Microsoft.Extensions.Diagnostics.Abstractions/10.0.14",
        "Microsoft.Extensions.FileProviders.Abstractions/10.0.14",
        "Microsoft.Extensions.Hosting.Abstractions/10.0.14",
        "Microsoft.Extensions.Logging.Abstractions/10.0.14",
        "Microsoft.Extensions.Options/10.0.14",
        "Microsoft.Extensions.Primitives/10.0.14",
        "ModelContextProtocol.Core/2.4.0",
        "ModelContextProtocol/2.4.0",
        "SonarAnalyzer.CSharp/10.8.0.113526",
        "System.Diagnostics.DiagnosticSource/10.0.14",
        "System.IO.Pipelines/10.0.13",
        "System.Net.ServerSentEvents/10.0.14",
        "System.Text.Encodings.Web/10.0.13",
        "System.Text.Json/10.0.13",
    ];

    /// <summary>The Brain libraries each assembly may reference: the authoring library, and for the program also the adapter. The pure libraries come only through the authoring API.</summary>
    private static readonly string[] BrainLibraries =
        ["Synapbiz.Brain.Authoring", "Synapbiz.Brain.Contracts", "Synapbiz.Brain.Engine", "Synapbiz.Brain.Process", "Synapbiz.Brain.Spec", "Synapbiz.Brain.Workbench"];

    [Theory]
    [InlineData(Adapter)]
    [InlineData(Program)]
    public void TheAssemblyReferencesOnlyTheBclTheBrainLibrariesAndTheSdkWithItsExtensionPackages(string assemblyName)
    {
        var bcl = BaseClassLibrary();
        var allowed = BrainLibraries.Concat(SdkAssemblies).Append(Adapter).ToHashSet(StringComparer.Ordinal);
        var metadata = Read(assemblyName, out var disposable);
        using (disposable)
        {
            var offenders = new SortedSet<string>(StringComparer.Ordinal);
            var owners = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var handle in metadata.AssemblyReferences)
            {
                var owner = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
                owners.Add(owner);
                if (!bcl.Contains(owner) && !allowed.Contains(owner))
                {
                    offenders.Add($"{assemblyName} references the assembly {owner}");
                }
            }

            Assert.Contains("ModelContextProtocol.Core", owners);
            Assert.Contains("Synapbiz.Brain.Authoring", owners);
            Assert.Equal(assemblyName == Program, owners.Contains(Adapter));
            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }
    }

    [Theory]
    [InlineData(Adapter, false)]
    [InlineData(Program, true)]
    public void TheAssemblyUsesNoNetworkStartsNoProcessAndTouchesFilesOnlyWhereItMust(string assemblyName, bool mayReadTheCatalogFile)
    {
        var metadata = Read(assemblyName, out var disposable);
        using (disposable)
        {
            var offenders = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var handle in metadata.TypeReferences)
            {
                var reference = metadata.GetTypeReference(handle);
                var name = metadata.GetString(reference.Namespace) + "." + metadata.GetString(reference.Name);
                var network = name.StartsWith("System.Net.", StringComparison.Ordinal) && !name.StartsWith("System.Net.WebUtility", StringComparison.Ordinal);
                var process = name is "System.Diagnostics.Process" or "System.Diagnostics.ProcessStartInfo";
                var files = name is "System.IO.File" or "System.IO.Directory" or "System.IO.FileInfo" or "System.IO.DirectoryInfo" or "System.IO.FileStream";
                if (network || process || (files && !(mayReadTheCatalogFile && name == "System.IO.File")))
                {
                    offenders.Add($"{assemblyName} uses {name}");
                }
            }

            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }
    }

    [Theory]
    [InlineData(Adapter, new[] { "Synapbiz.Brain.Authoring" })]
    [InlineData(Program, new[] { "Synapbiz.Brain.Authoring", "Synapbiz.Brain.Mcp" })]
    public void TheProjectDeclaresOnlyTheSdkAndTheLibrariesItAdapts(string project, string[] projects)
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), "src", project, project + ".csproj"));
        var packages = document.Descendants("PackageReference").Select(item => (string?)item.Attribute("Include")).ToList();
        var references = document.Descendants("ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(((string?)item.Attribute("Include"))!.Replace('\\', '/')))
            .Order(StringComparer.Ordinal).ToList();

        Assert.Equal([SdkPackage], packages);
        Assert.Equal(projects.Order(StringComparer.Ordinal), references);
        Assert.Empty(document.Descendants("Reference"));
    }

    [Theory]
    [InlineData(Adapter)]
    [InlineData(Program)]
    public void TheRestoreGraphHoldsExactlyTheExpectedPackages(string project)
    {
        var assets = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", project, "obj", "project.assets.json")))!;
        var targets = assets["targets"]!.AsObject();
        var packages = targets.SelectMany(framework => framework.Value!.AsObject())
            .Where(entry => (string?)entry.Value!["type"] == "package")
            .Select(entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(targets);
        Assert.Equal(ExpectedPackages.Order(StringComparer.Ordinal), packages);
    }

    [Fact]
    public void OnlyTheAdapterAndTheProgramReferenceTheSdkInTheProjectsTheAssetsAndTheAssemblies()
    {
        var root = RepositoryRoot();
        var allowedProjects = new[] { Adapter, Program };
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        // Every project file of the repository other than the two declares no SDK package.
        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.True(projects.Count > 20, "positive control: the scan reaches every project");
        foreach (var path in projects)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var includes = XDocument.Load(path).Descendants("PackageReference").Select(item => (string?)item.Attribute("Include") ?? string.Empty);
            var hasSdk = includes.Any(include => include.StartsWith(SdkPackage, StringComparison.Ordinal));
            var isTest = name.EndsWith(".Tests", StringComparison.Ordinal);
            if (hasSdk && !allowedProjects.Contains(name, StringComparer.Ordinal) && !name.StartsWith(Adapter, StringComparison.Ordinal) && !isTest)
            {
                offenders.Add($"{name} declares the MCP SDK");
            }

            if (hasSdk && isTest && name != Adapter + ".Tests")
            {
                offenders.Add($"{name} declares the MCP SDK; only the tests of the adapter drive it");
            }
        }

        // No restore graph of a library holds the SDK except those of the two (the authoring library is the one that matters most).
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "project.assets.json", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))!;
            var restored = JsonNode.Parse(File.ReadAllText(path))!["targets"]!.AsObject()
                .SelectMany(framework => framework.Value!.AsObject())
                .Select(entry => entry.Key);
            if (!allowedProjects.Contains(name, StringComparer.Ordinal) && restored.Any(package => package.StartsWith(SdkPackage, StringComparison.Ordinal)))
            {
                offenders.Add($"the restore graph of {name} holds the MCP SDK");
            }
        }

        // No other Brain assembly references an SDK assembly.
        var brainAssemblies = Directory.EnumerateFiles(AppContext.BaseDirectory, "Synapbiz.Brain.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !name.EndsWith(".Tests", StringComparison.Ordinal) && !name.EndsWith("ArchitectureTests", StringComparison.Ordinal) && !allowedProjects.Contains(name, StringComparer.Ordinal))
            .Select(name => name!)
            .ToList();
        Assert.True(brainAssemblies.Count >= 8, "positive control: the scan reaches the libraries");
        foreach (var assembly in brainAssemblies)
        {
            var metadata = Read(assembly, out var disposable);
            using (disposable)
            {
                foreach (var handle in metadata.AssemblyReferences)
                {
                    var owner = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
                    if (owner.StartsWith(SdkPackage, StringComparison.Ordinal))
                    {
                        offenders.Add($"{assembly} references {owner}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static HashSet<string?> BaseClassLibrary() => Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
        .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);

    private static MetadataReader Read(string assemblyName, out IDisposable owner)
    {
        var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll"));
        var reader = new PEReader(stream);
        owner = reader;
        var metadata = reader.GetMetadataReader();
        Assert.NotEmpty(metadata.TypeReferences);
        return metadata;
    }

    private static string RepositoryRoot()
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
