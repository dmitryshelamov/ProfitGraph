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
}