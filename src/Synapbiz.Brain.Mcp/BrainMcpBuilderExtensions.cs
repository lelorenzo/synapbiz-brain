using Microsoft.Extensions.DependencyInjection;
using Synapbiz.Brain.Mcp;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Brain's authoring tools to an MCP server a host already builds.</summary>
public static class BrainMcpBuilderExtensions
{
    /// <summary>
    /// Adds the Brain authoring tools, the guide resources and the prompt <c>model_a_process_with_me</c> to the server, over the host's store, clock
    /// and authorization. The host's own tools, resources and prompts stay. The host decides the transport and who may connect.
    /// </summary>
    /// <param name="builder">The MCP server builder (<c>services.AddMcpServer()</c>).</param>
    /// <param name="options">What the host supplies.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IMcpServerBuilder WithBrainAuthoring(this IMcpServerBuilder builder, BrainMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var brain = BrainMcp.Create(options);
        return builder.WithTools(brain.Tools).WithResources(brain.Resources).WithPrompts(brain.Prompts);
    }
}
