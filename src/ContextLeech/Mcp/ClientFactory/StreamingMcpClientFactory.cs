using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ContextLeech.Mcp.ClientFactory;

public class StreamingMcpClientFactory
{
    private readonly Pipe _clientToServerPipe;
    private readonly Pipe _serverToClientPipe;

    public StreamingMcpClientFactory(Pipe clientToServerPipe, Pipe serverToClientPipe)
    {
        ArgumentNullException.ThrowIfNull(clientToServerPipe);
        ArgumentNullException.ThrowIfNull(serverToClientPipe);
        _clientToServerPipe = clientToServerPipe;
        _serverToClientPipe = serverToClientPipe;
    }

    public async Task<IMcpClient> CreateAsync(CancellationToken cancellationToken)
    {
        var mcpClient = await McpClientFactory.CreateAsync(
            new StreamClientTransport(
                _clientToServerPipe.Writer.AsStream(),
                _serverToClientPipe.Reader.AsStream()),
            cancellationToken: cancellationToken);
        return mcpClient;
    }
}
