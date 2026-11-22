using System.Reflection;
using FluentAssertions;
using Nuvora.Decisions.Engine.Evaluation;

namespace Synapbiz.Brain.ArchitectureTests;

/// <summary>
/// The engine is deterministic by construction: no clock, GUID generator, randomness, file or network.
/// Port of nuvora's AT-DEC-02. Time and identity arrive as parameters.
/// </summary>
public sealed class EnginePurityArchTests
{
    private static Assembly EngineAssembly() => typeof(TableEvaluator).Assembly;

    [Fact]
    public void EngineNeverReadsAClockGuidRandomnessOrPerformsIoOrNetwork()
    {
        var engine = EngineAssembly();

        var methods = engine.GetTypes()
            .Where(t => t is { IsClass: true } or { IsValueType: true })
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsAbstract)
            .ToList();

        methods.Should().HaveCountGreaterThan(5,
            "positive control: the engine has dozens of methods across Facts/Conditions/Compilation/Evaluation/"
            + "Validation/Goldens; 5 or fewer means the scan broke, not that the engine shrank to nothing");

        var offenders = methods
            .Where(m => CallsForbiddenApi(m, out _))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .ToList();

        offenders.Should().BeEmpty(
            "no type in Synapbiz.Brain.Engine may call DateTime.Now/UtcNow, DateTimeOffset.Now/UtcNow, "
            + "Guid.NewGuid, a System.Random member, or any System.IO/System.Net member — time and identity "
            + "arrive as parameters. Offenders: {0}",
            string.Join(", ", offenders));
    }

    /// <summary>
    /// Best-effort IL scan (mirrors <c>ProcurementDomainArchTests.ReferencesAny</c>):
    /// reads the method body's IL and resolves every <c>call</c> /
    /// <c>callvirt</c> / <c>newobj</c> member-ref token, flagging the first
    /// one that resolves into <see cref="IsForbidden"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort reflection/IL scan — a method body that fails to load or a token that fails to resolve is a scan limitation, never a test failure signal; mirrors the unsuppressed precedent in ProcurementDomainArchTests.ReferencesAny and SoftDeleteArchTests.SafeGetMethodBody.")]
    private static bool CallsForbiddenApi(MethodInfo method, out MemberInfo? offendingMember)
    {
        offendingMember = null;

        MethodBody? body;
        try
        {
            body = method.GetMethodBody();
        }
        catch
        {
            return false;
        }

        var il = body?.GetILAsByteArray();
        if (il is null)
        {
            return false;
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
                offendingMember = resolved;
                return true;
            }
        }

        return false;
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
