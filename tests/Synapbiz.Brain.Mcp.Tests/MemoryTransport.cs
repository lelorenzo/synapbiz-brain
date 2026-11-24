using System.Threading.Channels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Synapbiz.Brain.Mcp.Tests;

/// <summary>One end of an in-memory connection: messages written here are read at the other end. No serialization, no process.</summary>
internal sealed class MemoryTransport : ITransport
{
    private readonly Channel<JsonRpcMessage> incoming;
    private readonly Channel<JsonRpcMessage> outgoing;

    private MemoryTransport(Channel<JsonRpcMessage> incoming, Channel<JsonRpcMessage> outgoing)
    {
        this.incoming = incoming;
        this.outgoing = outgoing;
    }

    public string? SessionId => null;

    public ChannelReader<JsonRpcMessage> MessageReader => incoming.Reader;

    /// <summary>The two ends: what the first sends the second reads, and the other way round.</summary>
    internal static (MemoryTransport Server, MemoryTransport Client) Pair()
    {
        var toServer = Channel.CreateUnbounded<JsonRpcMessage>();
        var toClient = Channel.CreateUnbounded<JsonRpcMessage>();
        return (new MemoryTransport(toServer, toClient), new MemoryTransport(toClient, toServer));
    }

    public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        outgoing.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        // Closing one end closes the connection both ways, as closing a pipe or a process does.
        outgoing.Writer.TryComplete();
        incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>The client side of a <see cref="MemoryTransport"/> pair, for <c>McpClient.CreateAsync</c>.</summary>
internal sealed class MemoryClientTransport(MemoryTransport end) : IClientTransport
{
    public string Name => "memory";

    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITransport>(end);
}
