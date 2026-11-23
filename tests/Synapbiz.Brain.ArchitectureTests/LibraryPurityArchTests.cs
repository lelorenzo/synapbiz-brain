using System.Reflection;
using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Engine.Evaluation;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// The Brain libraries are deterministic by construction: no clock, GUID generator, randomness, file or network.
/// Port of nuvora's AT-DEC-02, extended to the Spec library and to constructors and type initializers (a static field
/// initializer such as <c>static readonly DateTime X = DateTime.UtcNow;</c> runs in the type initializer). Time and
/// identity arrive as parameters.
/// </summary>
public sealed class LibraryPurityArchTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Members whose only System.IO use is an in-memory buffer, which is not file or network access. The scan treats the whole
    /// System.IO namespace as forbidden; these members are the exact exceptions, each with the exact System.IO calls it makes
    /// (the schema fingerprint writes the canonical schema into a MemoryStream to hash it).
    /// </summary>
    private static readonly Dictionary<string, string[]> InMemoryOnly = new(StringComparer.Ordinal)
    {
        ["Nuvora.Decisions.Contracts.Kinds.DecisionKindSchemaFingerprint.Compute"] =
            ["System.IO.MemoryStream..ctor", "System.IO.MemoryStream.GetBuffer", "System.IO.Stream.get_Length"],
    };

    private static readonly string[] LibraryList = ["Contracts", "Engine", "Spec", "Workbench", "WorkbenchBlazor", "Process"];

    public static TheoryData<string> LibraryNames => [.. LibraryList];

    [Fact]
    public void EveryInMemoryExceptionIsStillNeededAndTouchesNothingButAMemoryStream()
    {
        var all = LibraryList.SelectMany(name => Library(name).GetTypes())
            .SelectMany(t => t.GetMethods(AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(AllDeclared)))
            .ToList();

        foreach (var (member, allowed) in InMemoryOnly)
        {
            var method = all.Single(m => $"{m.DeclaringType!.FullName}.{m.Name}" == member);
            var calls = ForbiddenCalls(method).Select(call => $"{call.DeclaringType!.FullName}.{call.Name}").Distinct().Order(StringComparer.Ordinal).ToList();

            calls.Should().NotBeEmpty($"{member} is exempt, so the scan must flag it; delete the exemption if it no longer does");
            calls.Should().Equal(allowed.Order(StringComparer.Ordinal), $"{member} may only use an in-memory buffer");
        }
    }

    /// <summary>
    /// Under <c>--collect:"XPlat Code Coverage"</c> coverlet adds a hit-tracker type to each instrumented library, in this
    /// exact namespace, and the tracker writes its hit file with System.IO. That is code of the collector, not of the
    /// library, and it exists only while coverage is collected; every other type is scanned.
    /// </summary>
    private static bool IsInjectedByCoverageCollection(Type type) =>
        string.Equals(type.Namespace, "Coverlet.Core.Instrumentation.Tracker", StringComparison.Ordinal);

    private static Assembly Library(string name) => name switch
    {
        "Contracts" => typeof(DecisionEngineException).Assembly,
        "Engine" => typeof(TableEvaluator).Assembly,
        "Spec" => typeof(BrainRuleLoader).Assembly,
        "Workbench" => typeof(RuleSimulator).Assembly,
        "WorkbenchBlazor" => typeof(RuleWorkbench).Assembly,
        "Process" => typeof(BrainProcessLoader).Assembly,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown library."),
    };

    [Theory]
    [MemberData(nameof(LibraryNames))]
    public void NoLibraryReadsAClockGuidRandomnessOrPerformsIoOrNetwork(string name)
    {
        var library = Library(name);

        var members = library.GetTypes()
            .Where(t => !IsInjectedByCoverageCollection(t))
            .Where(t => t is { IsClass: true } or { IsValueType: true })
            .SelectMany(t => t.GetMethods(AllDeclared).Cast<MethodBase>()
                .Concat(t.GetConstructors(AllDeclared))
                .Concat(t.TypeInitializer is null ? [] : [t.TypeInitializer]))
            .Where(m => !m.IsAbstract)
            .Distinct()
            .ToList();

        members.Should().HaveCountGreaterThan(5,
            "positive control: each library has dozens of methods and constructors; 5 or fewer means the scan broke, not that the library shrank to nothing");
        members.OfType<ConstructorInfo>().Count(c => !c.IsStatic).Should().BeGreaterThan(0,
            "positive control: the scan must reach instance constructors");
        if (name == "Spec")
        {
            members.OfType<ConstructorInfo>().Count(c => c.IsStatic).Should().BeGreaterThan(0,
                "positive control: the Spec library has type initializers (its lazily built schema) and the scan must reach them");
        }

        var offenders = members
            .Where(m => ForbiddenCalls(m).Count != 0)
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .Where(member => !InMemoryOnly.ContainsKey(member))
            .ToList();

        offenders.Should().BeEmpty(
            "no type in Synapbiz.Brain.{0} may call DateTime.Now/UtcNow, DateTimeOffset.Now/UtcNow, "
            + "Guid.NewGuid, a System.Random member, or any System.IO/System.Net member, in a method, a constructor or a type "
            + "initializer: time and identity arrive as parameters. Offenders: {1}",
            name,
            string.Join(", ", offenders));
    }

    /// <summary>
    /// Best-effort IL scan (mirrors <c>ProcurementDomainArchTests.ReferencesAny</c>):
    /// reads the method body's IL and resolves every <c>call</c> /
    /// <c>callvirt</c> / <c>newobj</c> member-ref token, collecting those
    /// that resolve into <see cref="IsForbidden"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort reflection/IL scan — a method body that fails to load or a token that fails to resolve is a scan limitation, never a test failure signal; mirrors the unsuppressed precedent in ProcurementDomainArchTests.ReferencesAny and SoftDeleteArchTests.SafeGetMethodBody.")]
    private static List<MemberInfo> ForbiddenCalls(MethodBase method)
    {
        var found = new List<MemberInfo>();

        MethodBody? body;
        try
        {
            body = method.GetMethodBody();
        }
        catch
        {
            return found;
        }

        var il = body?.GetILAsByteArray();
        if (il is null)
        {
            return found;
        }

        var module = method.Module;
        var typeArgs = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;

        for (var i = 0; i < il.Length - 4; i++)
        {
            // 0x28 = call, 0x6F = callvirt, 0x73 = newobj — each takes a 4-byte member-ref token.
            if (il[i] is not (0x28 or 0x6F or 0x73))
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            MemberInfo? resolved;
            try
            {
                resolved = module.ResolveMember(token, typeArgs, methodArgs);
            }
            catch
            {
                continue;
            }

            if (resolved is not null && IsForbidden(resolved))
            {
                found.Add(resolved);
            }
        }

        return found;
    }

    private static bool IsForbidden(MemberInfo resolved)
    {
        var declaringType = resolved.DeclaringType;
        if (declaringType is null)
        {
            return false;
        }

        var ns = declaringType.Namespace ?? string.Empty;
        if (ns.StartsWith("System.IO", StringComparison.Ordinal) || ns.StartsWith("System.Net", StringComparison.Ordinal))
        {
            return true;
        }

        if (declaringType == typeof(Random))
        {
            return true;
        }

        if (declaringType == typeof(Guid) && string.Equals(resolved.Name, nameof(Guid.NewGuid), StringComparison.Ordinal))
        {
            return true;
        }

        if ((declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset))
            && resolved.Name is "get_Now" or "get_UtcNow")
        {
            return true;
        }

        return false;
    }
}
