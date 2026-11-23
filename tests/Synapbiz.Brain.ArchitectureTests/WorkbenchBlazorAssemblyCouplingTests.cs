using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// Freezes what <c>Synapbiz.Brain.Workbench.Blazor</c> may depend on: the base class library, the five Brain libraries and the
/// Blazor component model. It declares the one package that gives it the component model, never touches JavaScript and ships
/// no script, so a host that renders it needs nothing but the host's own tokens.
/// </summary>
public sealed class WorkbenchBlazorAssemblyCouplingTests
{
    private const string Panels = "Synapbiz.Brain.Workbench.Blazor";

    private static readonly string[] Libraries =
        ["Synapbiz.Brain.Contracts", "Synapbiz.Brain.Engine", "Synapbiz.Brain.Process", "Synapbiz.Brain.Spec", "Synapbiz.Brain.Workbench"];

    private static readonly string[] AllowedPrefixes = ["Microsoft.AspNetCore.Components", "Microsoft.JSInterop", "Microsoft.Extensions."];

    private static bool IsAllowed(string owner, HashSet<string?> bcl) =>
        bcl.Contains(owner)
        || Libraries.Contains(owner, StringComparer.Ordinal)
        || AllowedPrefixes.Any(prefix => owner.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void TheAssemblyReferencesOnlyTheBclTheFiveLibrariesAndTheComponentModel()
    {
        var bcl = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, Panels + ".dll"));
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        Assert.NotEmpty(metadata.TypeReferences);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        var owners = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in metadata.AssemblyReferences)
        {
            var owner = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            owners.Add(owner);
            if (!IsAllowed(owner, bcl))
            {
                offenders.Add($"{Panels} references the assembly {owner}");
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
            if (!IsAllowed(owner, bcl))
            {
                offenders.Add($"{Panels} uses {name} from {owner}");
            }
        }

        Assert.Contains("Synapbiz.Brain.Workbench", owners);
        Assert.Contains("Microsoft.AspNetCore.Components", owners);
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheProjectDeclaresExactlyOnePackageAndReferencesExactlyTheFiveLibraries()
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", Panels, Panels + ".csproj"));
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        var packages = project.Descendants("PackageReference").Select(item => (string?)item.Attribute("Include") ?? "?").ToList();
        if (!packages.SequenceEqual(["Microsoft.AspNetCore.Components.Web"]))
        {
            offenders.Add($"{Panels} must declare exactly the PackageReference Microsoft.AspNetCore.Components.Web but declares [{string.Join(", ", packages)}]");
        }

        foreach (var item in project.Descendants("Reference"))
        {
            offenders.Add($"{Panels} declares Reference {(string?)item.Attribute("Include") ?? "?"}");
        }

        var projects = project.Descendants("ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(((string?)item.Attribute("Include"))!.Replace('\\', '/')))
            .Order(StringComparer.Ordinal).ToList();
        if (!projects.SequenceEqual(Libraries))
        {
            offenders.Add($"{Panels} must reference exactly [{string.Join(", ", Libraries)}] but references [{string.Join(", ", projects)}]");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoTypeHoldsAJavaScriptRuntime()
    {
        var types = typeof(Synapbiz.Brain.Workbench.Blazor.RuleWorkbench).Assembly.GetTypes();
        Assert.True(types.Length > 10, "positive control: the scan must reach the types of the panels");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        static bool IsRuntime(Type type) => string.Equals(type.FullName, "Microsoft.JSInterop.IJSRuntime", StringComparison.Ordinal)
            || string.Equals(type.FullName, "Microsoft.JSInterop.IJSInProcessRuntime", StringComparison.Ordinal);

        foreach (var type in types)
        {
            foreach (var field in type.GetFields(all).Where(field => IsRuntime(field.FieldType)))
            {
                offenders.Add($"{type.FullName}.{field.Name} is a field of the JavaScript runtime");
            }

            foreach (var property in type.GetProperties(all).Where(property => IsRuntime(property.PropertyType)))
            {
                offenders.Add($"{type.FullName}.{property.Name} is a property of the JavaScript runtime");
            }

            foreach (var constructor in type.GetConstructors(all).Where(constructor => constructor.GetParameters().Any(parameter => IsRuntime(parameter.ParameterType))))
            {
                offenders.Add($"{type.FullName} takes the JavaScript runtime in a constructor");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ThePackageShipsNoScript()
    {
        var root = Path.Combine(FindRepositoryRoot(), "src", Panels);
        Assert.True(File.Exists(Path.Combine(root, "wwwroot", "workbench.css")), "positive control: the stylesheet is where the scan looks");

        var scripts = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => Path.GetExtension(path) is ".js" or ".mjs" or ".cjs" or ".ts" or ".map" or ".wasm")
            .ToList();
        Assert.True(scripts.Count == 0, string.Join(Environment.NewLine, scripts));
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
