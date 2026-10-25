namespace ProfitGraph.Venues.Steam.Protection;

/// <summary>
///     Where open protections are kept, so every process sees a protection another process opened.
/// </summary>
internal interface ISteamAccountProtectionStore
{
    Task<(SteamProtectionReason Reason, DateTimeOffset OpenedAt)?> Get(Guid credentialId, CancellationToken cancellationToken);

    Task<bool> Open(Guid credentialId, SteamProtectionReason reason, DateTimeOffset openedAt, CancellationToken cancellationToken);

    Task Clear(Guid credentialId, CancellationToken cancellationToken);
}