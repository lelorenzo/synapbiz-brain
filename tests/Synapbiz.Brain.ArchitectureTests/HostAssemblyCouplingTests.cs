using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// Freezes what <c>Synapbiz.Brain.Host.Postgres</c> may depend on: the base class library, the Brain libraries it can see
/// (<c>Synapbiz.Brain.Process</c> by project reference, the others through it), <c>Npgsql</c> and the four
/// <c>Microsoft.Extensions.*</c> abstractions packages, with the assemblies they bring listed explicitly as the restore graph
/// holds them. The host is deliberately not in the purity scan: it reads the clock through <see cref="TimeProvider"/> but owns
/// the side effects (database, time, tasks) the pure libraries must not have.
/// </summary>
public sealed class HostAssemblyCouplingTests
{
    private const string Host = "Synapbiz.Brain.Host.Postgres";

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The Brain libraries: the host sees Process directly, and Spec, Contracts and Engine through it.</summary>
    private static readonly string[] Libraries = ["Synapbiz.Brain.Contracts", "Synapbiz.Brain.Engine", "Synapbiz.Brain.Process", "Synapbiz.Brain.Spec"];

    /// <summary>The assemblies of JsonSchema.Net 8.0.5 and its packages, which the Process library brings.</summary>
    private static readonly string[] JsonSchemaAssemblies = ["JsonSchema.Net", "JsonPointer.Net", "Json.More", "Humanizer"];

    /// <summary>The four abstractions packages of the host, the assemblies they bring, and Npgsql.</summary>
    private static readonly string[] HostPackageAssemblies =
    [
        "Npgsql",
        "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Diagnostics.Abstractions",
        "Microsoft.Extensions.FileProviders.Abstractions",
    ];

    /// <summary>The packages the restore graph of the host project holds, runtime packages and build-only analyzers.</summary>
    private static readonly string[] ExpectedPackages =
    [
        "Humanizer.Core/3.0.1",
        "Json.More.Net/2.2.0",
        "JsonPointer.Net/6.0.1",
        "JsonSchema.Net/8.0.5",
        "Microsoft.CodeAnalysis.NetAnalyzers/10.0.401",
        "Microsoft.Extensions.Configuration.Abstractions/9.0.0",
        "Microsoft.Extensions.DependencyInjection.Abstractions/9.0.0",
        "Microsoft.Extensions.Diagnostics.Abstractions/9.0.0",
        "Microsoft.Extensions.FileProviders.Abstractions/9.0.0",
        "Microsoft.Extensions.Hosting.Abstractions/9.0.0",
        "Microsoft.Extensions.Logging.Abstractions/9.0.0",
        "Microsoft.Extensions.Options/9.0.0",
        "Microsoft.Extensions.Primitives/9.0.0",
        "Npgsql/9.0.4",
        "SonarAnalyzer.CSharp/10.8.0.113526",
    ];

    /// <summary>The classes (with everything compiler-generated inside them) that may use the Npgsql namespace.</summary>
    private static readonly string[] NpgsqlUsers =
    [
        "Synapbiz.Brain.Host.Postgres.PostgresProcessStore",
        "Synapbiz.Brain.Host.Postgres.PostgresProcessStoreMigrator",
    ];

    [Fact]
    public void TheHostAssemblyReferencesOnlyTheBclTheBrainLibrariesNpgsqlAndTheExtensionsAbstractions()
    {
        var bcl = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var allowed = Libraries.Concat(JsonSchemaAssemblies).Concat(HostPackageAssemblies).ToHashSet(StringComparer.Ordinal);
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, Host + ".dll"));
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        Assert.NotEmpty(metadata.TypeReferences);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        var referenced = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in metadata.AssemblyReferences)
        {
            var owner = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            referenced.Add(owner);
            if (!bcl.Contains(owner) && !allowed.Contains(owner))
            {
                offenders.Add($"{Host} references the assembly {owner}");
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
                offenders.Add($"{Host} uses {name} from {owner}");
            }
        }

        Assert.Contains("Npgsql", referenced);
        Assert.Contains("Synapbiz.Brain.Process", referenced);
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheHostProjectDeclaresExactlyTheFivePackagesAndTheProcessProject()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", Host, Host + ".csproj");
        var project = XDocument.Load(path);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        var packages = project.Descendants("PackageReference").Select(item => (string?)item.Attribute("Include")).Order(StringComparer.Ordinal).ToList();
        var expected = new[]
        {
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Options",
            "Npgsql",
        };
        if (!packages.SequenceEqual(expected))
        {
            offenders.Add($"{Host} must declare exactly [{string.Join(", ", expected)}] but declares [{string.Join(", ", packages)}]");
        }

        foreach (var item in project.Descendants("PackageReference").Where(item => item.Attribute("Version") is not null || item.Attribute("VersionOverride") is not null))
        {
            offenders.Add($"{Host} sets a version on {(string?)item.Attribute("Include")}; versions belong in Directory.Packages.props");
        }

        foreach (var item in project.Descendants("Reference"))
        {
            offenders.Add($"{Host} declares Reference {(string?)item.Attribute("Include") ?? "?"}");
        }

        var projects = project.Descendants("ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(((string?)item.Attribute("Include"))!.Replace('\\', '/')))
            .Order(StringComparer.Ordinal).ToList();
        if (!projects.SequenceEqual(["Synapbiz.Brain.Process"]))
        {
            offenders.Add($"{Host} must reference exactly [Synapbiz.Brain.Process] but references [{string.Join(", ", projects)}]");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheRestoreGraphOfTheHostProjectHoldsExactlyTheExpectedPackages()
    {
        var assets = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", Host, "obj", "project.assets.json")))!;
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

    [Fact]
    public void NoTypeOutsideThePostgreSqlStoreAndTheMigratorUsesTheNpgsqlNamespace()
    {
        var assembly = typeof(Synapbiz.Brain.Host.Postgres.ServiceCollectionExtensions).Assembly;
        var users = assembly.GetTypes()
            .Where(type => UsesNpgsql(type))
            .Select(type => TopLevel(type).FullName!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Positive control: the scan must see both allowed users, or it has stopped working and the check below proves nothing.
        Assert.Equal(NpgsqlUsers.Order(StringComparer.Ordinal), users);
        Assert.True(assembly.GetTypes().Length > 40, "the host assembly has dozens of types, including compiler-generated ones");
    }

    [Fact]
    public void ThePurityScanStillCoversTheFivePureLibrariesAndDoesNotCoverTheHost()
    {
        var field = typeof(LibraryPurityArchTests).GetField("LibraryList", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        var libraries = (string[])field.GetValue(null)!;
        Assert.Equal(["Contracts", "Engine", "Process", "Spec", "Workbench"], libraries.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(libraries, name => name.Contains("Host", StringComparison.Ordinal));
    }

    private static Type TopLevel(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    private static bool UsesNpgsql(Type type)
    {
        if (type.GetFields(AllDeclared).Any(field => Mentions(field.FieldType)) || Mentions(type.BaseType) || type.GetInterfaces().Any(Mentions))
        {
            return true;
        }

        return type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared)).Any(UsesNpgsql);
    }

    private static bool UsesNpgsql(MethodBase method)
    {
        if (method is MethodInfo info && Mentions(info.ReturnType))
        {
            return true;
        }

        if (method.GetParameters().Any(parameter => Mentions(parameter.ParameterType)))
        {
            return true;
        }

        var body = method.GetMethodBody();
        if (body is null)
        {
            return false;
        }

        if (body.LocalVariables.Any(local => Mentions(local.LocalType)))
        {
            return true;
        }

        var il = body.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        var module = method.Module;
        var typeArguments = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
        for (var index = 0; index < il.Length - 4; index++)
        {
            // call, callvirt, newobj, ldfld, ldflda, stfld, ldsfld, stsfld, ldtoken, isinst, castclass, box, newarr, unbox.any
            if (il[index] is not (0x28 or 0x6F or 0x73 or 0x7B or 0x7C or 0x7D or 0x7E or 0x80 or 0xD0 or 0x75 or 0x74 or 0x8C or 0x8D or 0xA5))
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, index + 1);
            if (TryResolve(module, token, typeArguments, methodArguments) is { } resolved && Mentions(resolved))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves a metadata token; a byte of the IL that only looks like an opcode does not resolve, which is not a finding.</summary>
    private static MemberInfo? TryResolve(Module module, int token, Type[] typeArguments, Type[] methodArguments)
    {
        try
        {
            return module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static bool Mentions(MemberInfo member) => member switch
    {
        Type type => Mentions(type),
        MethodBase method => (method.DeclaringType is { } declaring && Mentions(declaring)) || (method is MethodInfo info && Mentions(info.ReturnType)) || method.GetParameters().Any(parameter => Mentions(parameter.ParameterType)),
        FieldInfo field => (field.DeclaringType is { } declaring && Mentions(declaring)) || Mentions(field.FieldType),
        _ => false,
    };

    private static bool Mentions(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.HasElementType)
        {
            return Mentions(type.GetElementType());
        }

        if ((type.Namespace ?? string.Empty).StartsWith("Npgsql", StringComparison.Ordinal))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(argument => Mentions(argument));
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
