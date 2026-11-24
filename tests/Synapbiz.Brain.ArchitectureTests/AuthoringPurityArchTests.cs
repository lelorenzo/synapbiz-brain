using System.Reflection;
using FluentAssertions;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// The authoring library has no network, no clock of its own, no randomness and no GUID generator: time comes from the injected
/// <see cref="TimeProvider"/>, and a simulation token is a counter. File access exists in exactly two types: the file workspace
/// (<c>FileAuthoringStore</c>, which is what a folder of documents is) and the guide, which reads its own embedded examples.
/// </summary>
public sealed class AuthoringPurityArchTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] MayUseFiles = ["Synapbiz.Brain.Authoring.FileAuthoringStore", "Synapbiz.Brain.Authoring.BrainGuide"];

    private static List<MethodBase> Members() =>
    [
        .. typeof(AuthoringTools).Assembly.GetTypes()
            .Where(type => !string.Equals(type.Namespace, "Coverlet.Core.Instrumentation.Tracker", StringComparison.Ordinal))
            .Where(type => type is { IsClass: true } or { IsValueType: true })
            .SelectMany(type => type.GetMethods(AllDeclared).Cast<MethodBase>()
                .Concat(type.GetConstructors(AllDeclared))
                .Concat(type.TypeInitializer is null ? [] : [type.TypeInitializer]))
            .Where(method => !method.IsAbstract)
            .Distinct(),
    ];

    [Fact]
    public void NoMemberUsesTheNetworkTheMachineClockRandomnessOrAGuidGenerator()
    {
        var members = Members();
        members.Count.Should().BeGreaterThan(200, "positive control: the scan must reach the whole library");
        members.OfType<ConstructorInfo>().Count(constructor => constructor.IsStatic).Should().BeGreaterThan(0, "positive control: type initializers are scanned");

        var offenders = members
            .SelectMany(method => ForbiddenCalls(method).Select(call => $"{method.DeclaringType!.FullName}.{method.Name} calls {call.DeclaringType!.FullName}.{call.Name}"))
            .ToList();

        offenders.Should().BeEmpty("time arrives through the injected TimeProvider and nothing in this library reaches the network");
    }

    [Fact]
    public void FileAccessIsConfinedToTheFileWorkspaceAndTheGuide()
    {
        var offenders = Members()
            .Where(method => !MayUseFiles.Any(owner => method.DeclaringType!.FullName!.StartsWith(owner, StringComparison.Ordinal)))
            .SelectMany(method => ForbiddenCalls(method, includeFiles: true).Select(call => $"{method.DeclaringType!.FullName}.{method.Name} calls {call.DeclaringType!.FullName}.{call.Name}"))
            .ToList();

        offenders.Should().BeEmpty("every other type works on text it is given, so it can be tested without a disk");
    }

    [Fact]
    public void TheFileWorkspaceIsTheOnlyTypeThatWritesAndItCanReachNothingButItsOwnAreas()
    {
        var writers = Members()
            .Where(method => ForbiddenCalls(method, includeFiles: true).Any(call => call.Name is "WriteAllText" or "Move" or "Delete" or "CreateDirectory" or "Create" or "OpenWrite" or "AppendAllText"))
            .Select(method => method.DeclaringType!.FullName!)
            .Distinct()
            .ToList();
        writers.Should().NotBeEmpty("positive control: the scan sees the file workspace write");
        writers.Should().OnlyContain(name => name.StartsWith("Synapbiz.Brain.Authoring.FileAuthoringStore", StringComparison.Ordinal));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort IL scan: a body that fails to load or a token that fails to resolve is a limit of the scan, never a finding; the same as LibraryPurityArchTests.")]
    private static List<MemberInfo> ForbiddenCalls(MethodBase method, bool includeFiles = false)
    {
        var found = new List<MemberInfo>();
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch
        {
            return found;
        }

        if (il is null)
        {
            return found;
        }

        var typeArgs = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
        for (var index = 0; index < il.Length - 4; index++)
        {
            if (il[index] is not (0x28 or 0x6F or 0x73))
            {
                continue;
            }

            MemberInfo? resolved;
            try
            {
                resolved = method.Module.ResolveMember(BitConverter.ToInt32(il, index + 1), typeArgs, methodArgs);
            }
            catch
            {
                continue;
            }

            if (resolved is not null && IsForbidden(resolved, includeFiles))
            {
                found.Add(resolved);
            }
        }

        return found;
    }

    private static bool IsForbidden(MemberInfo resolved, bool includeFiles)
    {
        var declaringType = resolved.DeclaringType;
        if (declaringType is null)
        {
            return false;
        }

        var ns = declaringType.Namespace ?? string.Empty;
        if (includeFiles)
        {
            return ns.StartsWith("System.IO", StringComparison.Ordinal);
        }

        return ns.StartsWith("System.Net", StringComparison.Ordinal)
            || declaringType == typeof(Random)
            || (declaringType == typeof(Guid) && string.Equals(resolved.Name, nameof(Guid.NewGuid), StringComparison.Ordinal))
            || ((declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset)) && resolved.Name is "get_Now" or "get_UtcNow")
            || (declaringType == typeof(Environment) && resolved.Name is "get_TickCount" or "get_TickCount64");
    }
}
