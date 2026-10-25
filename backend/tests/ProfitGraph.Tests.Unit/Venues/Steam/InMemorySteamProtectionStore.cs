using System.Collections.Concurrent;

using ProfitGraph.Venues.Steam.Protection;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

internal sealed class InMemorySteamProtectionStore : ISteamAccountProtectionStore
{
    private readonly ConcurrentDictionary<Guid, (SteamProtectionReason Reason, DateTimeOffset OpenedAt)> _open =
        new ConcurrentDictionary<Guid, (SteamProtectionReason Reason, DateTimeOffset OpenedAt)>();

    public Task<(SteamProtectionReason Reason, DateTimeOffset OpenedAt)?> Get(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.FromResult<(SteamProtectionReason Reason, DateTimeOffset OpenedAt)?>(
            _open.TryGetValue(credentialId, out (SteamProtectionReason Reason, DateTimeOffset OpenedAt) open) ? open : null);
    }

    public Task<bool> Open(Guid credentialId, SteamProtectionReason reason, DateTimeOffset openedAt, CancellationToken cancellationToken)
    {
        return Task.FromResult(_open.TryAdd(credentialId, (reason, openedAt)));
    }

    public Task Clear(Guid credentialId, CancellationToken cancellationToken)
    {
        _open.TryRemove(credentialId, out _);

        return Task.CompletedTask;
    }
}