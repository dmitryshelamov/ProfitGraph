using Microsoft.AspNetCore.SignalR;

namespace ProfitGraph.Api.Common.Authentication;

/// <summary>
///     The open realtime connections of each account in this process, so blocking an account can close them (ADR 0043).
/// </summary>
public sealed class AccountConnections
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<Guid, Dictionary<string, HubCallerContext>> _connections = new Dictionary<Guid, Dictionary<string, HubCallerContext>>();

    /// <summary>
    ///     Closes every open realtime connection of the account.
    /// </summary>
    public void Abort(Guid userId)
    {
        HubCallerContext[] connections;

        lock (_gate)
        {
            connections = _connections.Remove(userId, out Dictionary<string, HubCallerContext>? open) ? [.. open.Values] : [];
        }

        foreach (HubCallerContext connection in connections)
        {
            connection.Abort();
        }
    }

    internal void Add(Guid userId, HubCallerContext connection)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(userId, out Dictionary<string, HubCallerContext>? open))
            {
                open = new Dictionary<string, HubCallerContext>(StringComparer.Ordinal);
                _connections[userId] = open;
            }

            open[connection.ConnectionId] = connection;
        }
    }

    internal void Remove(Guid userId, string connectionId)
    {
        lock (_gate)
        {
            if (_connections.TryGetValue(userId, out Dictionary<string, HubCallerContext>? open) && open.Remove(connectionId) && open.Count == 0)
            {
                _connections.Remove(userId);
            }
        }
    }
}