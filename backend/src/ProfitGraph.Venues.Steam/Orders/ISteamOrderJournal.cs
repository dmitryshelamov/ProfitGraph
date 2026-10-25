namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     The durable record of the module's Steam orders (ADR 0022). Each change is a compare-and-set on the entry's state, so
///     two workers never move one entry twice.
/// </summary>
internal interface ISteamOrderJournal
{
    Task<SteamOrderJournalEntry?> Get(string clientOrderId, CancellationToken cancellationToken);

    /// <summary>
    ///     Records a new entry; false when one with its client order ID exists already.
    /// </summary>
    Task<bool> Add(SteamOrderJournalEntry entry, CancellationToken cancellationToken);

    /// <summary>
    ///     Replaces the entry when its state is still <paramref name="expected" />. False when it changed meanwhile, or when
    ///     the new state would take an item's buy-order slot that another entry of the account holds.
    /// </summary>
    Task<bool> Transition(SteamOrderJournalEntry next, SteamOrderJournalState expected, CancellationToken cancellationToken);

    /// <summary>
    ///     The account's entries that are not done, oldest first.
    /// </summary>
    Task<IReadOnlyList<SteamOrderJournalEntry>> Open(Guid credentialId, CancellationToken cancellationToken);

    /// <summary>
    ///     A bot of each account that has entries not done, so the order worker knows whose accounts to move along.
    /// </summary>
    Task<IReadOnlyList<(Guid CredentialId, Guid BotId)>> OpenAccounts(CancellationToken cancellationToken);

    /// <summary>
    ///     Records a sell with the assets it reserves, all or nothing. False when the entry exists already or an asset is
    ///     reserved by another open sell of the account.
    /// </summary>
    Task<bool> AddSell(SteamOrderJournalEntry entry, IReadOnlyList<SteamAssetReservation> reservations, CancellationToken cancellationToken);

    Task<IReadOnlyList<SteamAssetReservation>> Reservations(string clientOrderId, CancellationToken cancellationToken);

    /// <summary>
    ///     The account's open reservations of every sell.
    /// </summary>
    Task<IReadOnlyList<SteamAssetReservation>> Reserved(Guid credentialId, CancellationToken cancellationToken);

    /// <summary>
    ///     Replaces a reservation when its state is still <paramref name="expected" />.
    /// </summary>
    Task<bool> Transition(SteamAssetReservation next, SteamReservationState expected, CancellationToken cancellationToken);
}