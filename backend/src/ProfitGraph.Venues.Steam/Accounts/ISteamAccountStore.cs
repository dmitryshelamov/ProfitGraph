namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     The Steam state of stored credentials (ADR 0022).
/// </summary>
internal interface ISteamAccountStore
{
    Task<SteamAccount?> Get(Guid credentialId, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether any stored account has one of the fingerprints, which a Steam ID has one of per configured master key.
    /// </summary>
    Task<bool> AnyFingerprint(IReadOnlyList<string> fingerprints, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores a new account; false when the same Steam account is stored already.
    /// </summary>
    Task<bool> Add(SteamAccount account, CancellationToken cancellationToken);

    /// <summary>
    ///     The accounts in <see cref="SteamGuardStatus.Checking" />, or <see cref="SteamGuardStatus.CoolingDown" /> after their
    ///     expected aging, whose next check is due.
    /// </summary>
    Task<IReadOnlyList<SteamAccount>> Due(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    ///     Records the account's Guard status when it is still <paramref name="expected" />; false when it changed meanwhile.
    /// </summary>
    Task<bool> Record(
        Guid credentialId,
        SteamGuardStatus expected,
        SteamGuardStatus status,
        DateTimeOffset? nextCheckAt,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}