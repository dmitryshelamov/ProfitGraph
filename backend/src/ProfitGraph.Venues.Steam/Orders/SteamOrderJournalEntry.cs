using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     One engine order of a bot as the Steam module tracks it (ADR 0022): the <c>pg…</c> client order ID the intent knows
///     and the Steam IDs it maps to. A price is per item in whole minor units of the wallet currency; a quantity is whole items.
/// </summary>
internal sealed record SteamOrderJournalEntry(
    string ClientOrderId,
    Guid BotId,
    Guid CredentialId,
    long EngineOrderId,
    string Instrument,
    OrderSide Side,
    OrderType Type,
    int Quantity,
    long PriceMinorUnits,
    SteamOrderJournalState State,
    SteamWaitingCode? WaitingCode,
    string? SteamOrderId,
    string? ConfirmationCreator,
    SteamOrderOutcome? Outcome,
    DateTimeOffset? PlaceStartedAt,
    DateTimeOffset? Deadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    ///     Whether the entry may hold its item's buy-order slot: Steam may have a buy order for it.
    /// </summary>
    public bool HoldsSlot => Side == OrderSide.Buy && State is not (SteamOrderJournalState.Waiting or SteamOrderJournalState.Done);
}