using System.Diagnostics.CodeAnalysis;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>The source of process definitions. The host never loads documents itself.</summary>
public interface IProcessCatalog
{
    /// <summary>The latest version of a process; used to start instances and child processes.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The definition, or null when the catalog does not know the process.</returns>
    BrainProcess? Latest(string processId);

    /// <summary>The exact definition an instance is pinned to; used for every later trigger.</summary>
    /// <param name="processId">The process id.</param>
    /// <param name="version">The version.</param>
    /// <param name="contentHash">The content hash of the document.</param>
    /// <returns>The definition, or null when the catalog does not have it (yet).</returns>
    BrainProcess? Find(string processId, long version, string contentHash);
}

/// <summary>A port the effects of a process are sent to: the integration point of the consumer.</summary>
public interface IProcessPort
{
    /// <summary>The name of the port, as the <c>port</c> member of <c>read</c>, <c>action</c> and <c>ai</c> steps writes it.</summary>
    string Name { get; }

    /// <summary>Runs one effect.</summary>
    /// <param name="portCall">The tenant, the instance and the request, including the idempotency key.</param>
    /// <param name="cancellationToken">Fires when the port timeout elapses or the host stops.</param>
    /// <returns>The output or the failure.</returns>
    Task<PortResult> InvokeAsync(PortCall portCall, CancellationToken cancellationToken);
}

/// <summary>One call of a port.</summary>
/// <param name="TenantId">The tenant of the instance.</param>
/// <param name="InstanceId">The instance.</param>
/// <param name="Request">The effect request produced by the interpreter.</param>
public sealed record PortCall(string TenantId, string InstanceId, EffectRequest Request);

/// <summary>The answer of a port.</summary>
[SuppressMessage(
    "Design",
    "S1694:An abstract class should have both abstract and concrete methods",
    Justification = "A closed, pattern-matchable hierarchy of sealed records is the point; an interface would lose record value-equality. Same reason as ProcessRequest in Synapbiz.Brain.Process.")]
public abstract record PortResult;

/// <summary>The port ran the effect.</summary>
/// <param name="OutputJson">The output as the text of a JSON object, or <c>"{}"</c>.</param>
public sealed record PortCompleted(string OutputJson) : PortResult;

/// <summary>The port reports that the effect failed.</summary>
/// <param name="Kind">The kind of failure.</param>
/// <param name="Code">A stable code of the failure.</param>
/// <param name="RetryAfter">The delay a rate-limited port asks for.</param>
public sealed record PortFailed(ProcessErrorKind Kind, string Code, TimeSpan? RetryAfter = null) : PortResult;

/// <summary>Tells the interpreter what day it is for a tenant (the day rules are evaluated for).</summary>
public interface IProcessCalendar
{
    /// <summary>The calendar day of the tenant at an instant.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="now">The instant.</param>
    /// <returns>The date.</returns>
    DateOnly Today(string tenantId, DateTimeOffset now);
}

/// <summary>Protects the payload columns of the store: state, history data, effect input, task data and inbox payloads.</summary>
public interface IPayloadProtector
{
    /// <summary>Protects a text before it is written.</summary>
    /// <param name="tenantId">The tenant that owns the text.</param>
    /// <param name="plaintext">The text.</param>
    /// <returns>The protected text.</returns>
    string Protect(string tenantId, string plaintext);

    /// <summary>Reverses <see cref="Protect"/> after a text is read.</summary>
    /// <param name="tenantId">The tenant that owns the text.</param>
    /// <param name="protectedText">The protected text.</param>
    /// <returns>The text.</returns>
    string Unprotect(string tenantId, string protectedText);
}

/// <summary>One pass of the work the hosted worker repeats; also the unit the tests drive.</summary>
public interface IProcessPump
{
    /// <summary>
    /// Runs one pass: expired effect leases, the inbox, due timers and pending effects, up to the batch size of each.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pass; an effect already claimed stays running and is handled by a later pass.</param>
    /// <returns>The number of work items handled.</returns>
    Task<int> RunOnceAsync(CancellationToken cancellationToken = default);
}
