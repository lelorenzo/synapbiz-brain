using System.Security.Claims;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Mcp;

/// <summary>What a host gives the adapter: its workspace, the settings of the tools, a default language and its own authorization.</summary>
public sealed class BrainMcpOptions
{
    /// <summary>
    /// The workspace the tools read and write: the approved documents, the catalog the host offers, the drafts and the proposals.
    /// A host that keeps its documents elsewhere implements <see cref="IAuthoringStore"/>; a folder is a <see cref="FileAuthoringStore"/>.
    /// </summary>
    public required IAuthoringStore Store { get; init; }

    /// <summary>The settings of the tools (clock, simulation lifetime and number, largest document). The defaults work.</summary>
    public AuthoringOptions Authoring { get; init; } = new();

    /// <summary>
    /// The language of the <c>say</c> texts and of the prompt when a call does not say: <c>en</c> (default) or <c>es</c>.
    /// A call that carries <c>language</c> always wins.
    /// </summary>
    public string DefaultLanguage { get; init; } = "en";

    /// <summary>
    /// The host's authorization, asked before every tool call, resource read and prompt. Return true to allow. A call that is not allowed
    /// is a normal tool result with <c>isError</c> true and the code <c>mcp.not_authorized</c>; the tool does not run. Null allows every call
    /// (for a program that serves one person on their own machine).
    /// </summary>
    public Func<BrainMcpRequest, CancellationToken, ValueTask<bool>>? Authorize { get; init; }
}

/// <summary>What the host's authorization sees about one request.</summary>
/// <param name="Kind">What is asked: <c>tool</c>, <c>resource</c> or <c>prompt</c>.</param>
/// <param name="Name">The tool name, the resource URI or the prompt name.</param>
/// <param name="User">The authenticated user of the connection, when the transport has one; null on stdio.</param>
public sealed record BrainMcpRequest(string Kind, string Name, ClaimsPrincipal? User);
