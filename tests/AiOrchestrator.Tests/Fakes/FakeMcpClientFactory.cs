using AiOrchestrator.Core.Abstractions;

namespace AiOrchestrator.Tests.Fakes;

public sealed class FakeMcpClientFactory : IMcpClientFactory
{
    private readonly Dictionary<string, IMcpClient> _clients;

    public FakeMcpClientFactory(params IMcpClient[] clients)
    {
        _clients = clients.ToDictionary(c => c.ServerId);
    }

    public IReadOnlyCollection<IMcpClient> GetAllClients() => _clients.Values.ToList();

    public IMcpClient GetClient(string serverId) =>
        _clients.TryGetValue(serverId, out var client)
            ? client
            : throw new KeyNotFoundException($"No fake MCP client registered for '{serverId}'.");

    public Task StartAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
