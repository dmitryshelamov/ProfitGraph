using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     One <see cref="SteamAccountSession" /> per Steam credential in this process. A session is handed out only to the
///     credential's owner.
/// </summary>
internal sealed class SteamAccountSessions(SteamHttpTransport transport, IServiceScopeFactory scopes, IOptions<SteamOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, SteamAccountSession> _sessions = new ConcurrentDictionary<Guid, SteamAccountSession>();

    public SteamAccountSession For(Guid ownerId, Guid credentialId)
    {
        SteamAccountSession session = _sessions.GetOrAdd(credentialId, id => new SteamAccountSession(ownerId, id, transport, scopes, options, clock));

        return session.OwnerId == ownerId ? session : throw new InvalidOperationException("The Steam credential belongs to another user.");
    }
}