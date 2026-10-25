using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Turns an account's market history into the fills of its bots' orders (ADR 0022; Profit Fox Designer ADR 0087). Each
///     receipt is attributed once, and only on positive evidence:
///     <list type="bullet">
///         <item>a sale or a cancel by the listing ID of a reserved asset, the sale also by the asset;</item>
///         <item>
///             a purchase to the buy whose own counters (<c>getbuyorderstatus</c>) bought exactly as many items as the
///             unattributed purchases of the item since the buy was placed, none above its price.
///         </item>
///     </list>
///     A count that does not match, such as the owner's own purchase of the item while a bot buys it, attributes nothing
///     and leaves the order to reconciliation. Fills are cumulative; an order completes when all its items are proven, and a
///     cancelled one once its counters and receipts agree on what was bought or sold.
/// </summary>
internal sealed class SteamExecutions(
    ISteamOrderJournal journal,
    ISteamMarketHistoryStore receipts,
    ISteamMarketAttributions attributions,
    SteamMarketHistory history,
    SteamAccountSessions sessions,
    SteamBuyOrderClient buyOrders,
    SteamOrderUpdates updates,
    TimeProvider clock)
{
    /// <summary>
    ///     How much earlier than its place request a purchase may be stamped by Steam and still be the buy's (Profit Fox
    ///     Designer's fixed local tolerance).
    /// </summary>
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     One round for an account. False when its history is unknown now, so nothing was decided.
    /// </summary>
    public async Task<bool> Advance(Guid ownerId, Guid credentialId, SteamCurrency currency, SellerFeeSchedule fees, CancellationToken cancellationToken)
    {
        SteamAccountSession session = sessions.For(ownerId, credentialId);

        if (await history.Read(session, credentialId, currency, fees, cancellationToken) is null)
        {
            return false;
        }

        Dictionary<string, SteamMarketReceipt> all = (await receipts.From(credentialId, position: 0, cancellationToken)).ToDictionary(
            stored => stored.Receipt.Id,
            stored => stored.Receipt,
            StringComparer.Ordinal);
        var attributed = new List<SteamAttribution>(await attributions.Of(credentialId, cancellationToken));
        var round = new SteamExecutionRound(all, attributed);

        await Sells(credentialId, currency, round, cancellationToken);
        await Buys(session, credentialId, currency, round, cancellationToken);

        return true;
    }

    private async Task Sells(Guid credentialId, SteamCurrency currency, SteamExecutionRound round, CancellationToken cancellationToken)
    {
        var touched = new HashSet<string>(StringComparer.Ordinal);

        foreach (SteamAssetReservation reservation in await journal.Reserved(credentialId, cancellationToken))
        {
            if (await journal.Get(reservation.ClientOrderId, cancellationToken) is not { } entry ||
                !SteamInstrument.TryParse(entry.Instrument, out SteamInstrument? instrument))
            {
                continue;
            }

            if (reservation.ListingId is { } listingId)
            {
                SteamMarketReceipt? sold = round.Free.FirstOrDefault(receipt =>
                    receipt.Kind == SteamMarketReceiptKind.Sold && receipt.ListingId == listingId && receipt.AppId == reservation.AppId &&
                    receipt.ContextId == reservation.ContextId && receipt.AssetId == reservation.AssetId && receipt.MarketHashName == instrument.MarketHashName);
                SteamMarketReceipt? cancelled = sold is null
                    ? round.Free.FirstOrDefault(receipt => receipt.Kind == SteamMarketReceiptKind.Cancelled && receipt.ListingId == listingId)
                    : null;

                if ((sold ?? cancelled) is { } evidence && await Attribute(credentialId, round, entry, [evidence], cancellationToken))
                {
                    SteamReservationState next = sold is not null ? SteamReservationState.Sold : SteamReservationState.Released;
                    await journal.Transition(reservation with { State = next, UpdatedAt = clock.GetUtcNow() }, reservation.State, cancellationToken);
                    touched.Add(entry.ClientOrderId);
                }
            }
            else if (reservation.State == SteamReservationState.Unknown)
            {
                // An unclear listing request is resolved by its listed event: the asset's listing, created after it was reserved.
                SteamMarketReceipt[] listed =
                [
                    .. round.Free.Where(receipt =>
                        receipt.Kind == SteamMarketReceiptKind.Listed && receipt.AppId == reservation.AppId && receipt.ContextId == reservation.ContextId &&
                        receipt.AssetId == reservation.AssetId && receipt.At >= reservation.CreatedAt - Skew)
                ];

                if (listed.Length == 1 && await Attribute(credentialId, round, entry, listed, cancellationToken))
                {
                    await journal.Transition(
                        reservation with { State = SteamReservationState.AwaitingConfirmation, ListingId = listed[0].ListingId, UpdatedAt = clock.GetUtcNow() },
                        SteamReservationState.Unknown,
                        cancellationToken);
                }
            }
        }

        foreach (string clientOrderId in touched)
        {
            if (await journal.Get(clientOrderId, cancellationToken) is { State: not SteamOrderJournalState.Done } entry)
            {
                await SettleSell(entry, currency, round, cancellationToken);
            }
        }
    }

    private async Task SettleSell(SteamOrderJournalEntry entry, SteamCurrency currency, SteamExecutionRound round, CancellationToken cancellationToken)
    {
        IReadOnlyList<SteamAssetReservation> reservations = await journal.Reservations(entry.ClientOrderId, cancellationToken);
        SteamMarketReceipt[] sold = [.. round.Of(entry.ClientOrderId).Where(receipt => receipt.Kind == SteamMarketReceiptKind.Sold)];
        int items = sold.Sum(receipt => receipt.Quantity);
        bool cancelledRest = entry.State == SteamOrderJournalState.Cancelling &&
            reservations.All(reservation => reservation.State is SteamReservationState.Sold or SteamReservationState.Released);

        if (items == entry.Quantity)
        {
            await Finish(entry, SteamOrderOutcome.Filled, cancellationToken);
            updates.Publish(entry.BotId, Fill(entry, sold, currency, final: true));

            return;
        }

        if (items > 0)
        {
            updates.Publish(entry.BotId, Fill(entry, sold, currency, final: false));
        }

        if (cancelledRest)
        {
            await Finish(entry, SteamOrderOutcome.Cancelled, cancellationToken);
            updates.Publish(entry.BotId, ExternalOrderUpdate.ConfirmCancel(entry.EngineOrderId));
        }
    }

    private async Task Buys(SteamAccountSession session, Guid credentialId, SteamCurrency currency, SteamExecutionRound round, CancellationToken cancellationToken)
    {
        SteamOrderJournalEntry[] buys =
        [
            .. (await journal.Open(credentialId, cancellationToken)).Where(entry =>
                entry is { Side: OrderSide.Buy, SteamOrderId: not null, PlaceStartedAt: not null } &&
                entry.State is SteamOrderJournalState.Active or SteamOrderJournalState.Cancelling)
        ];

        foreach (SteamOrderJournalEntry entry in buys)
        {
            if (!SteamInstrument.TryParse(entry.Instrument, out SteamInstrument? instrument))
            {
                continue;
            }

            SteamMarketReceipt[] candidates =
            [
                .. round.Free.Where(receipt =>
                    receipt.Kind == SteamMarketReceiptKind.Bought && receipt.AppId == instrument.AppId && receipt.MarketHashName == instrument.MarketHashName &&
                    receipt.At >= entry.PlaceStartedAt!.Value - Skew && receipt.BuyerPaysMinorUnits <= entry.PriceMinorUnits * receipt.Quantity)
            ];

            if (candidates.Length == 0 && entry.State != SteamOrderJournalState.Cancelling)
            {
                continue;
            }

            SteamBuyOrderStatus status;

            try
            {
                status = await buyOrders.Status(session, entry.SteamOrderId!, cancellationToken);
            }
            catch (SteamOrderUnresolvedException)
            {
                continue;
            }

            if (status.Quantity != entry.Quantity)
            {
                continue;
            }

            int before = round.Of(entry.ClientOrderId).Where(receipt => receipt.Kind == SteamMarketReceiptKind.Bought).Sum(receipt => receipt.Quantity);
            int missing = status.Purchased - before;

            if (missing > 0 && candidates.Sum(receipt => receipt.Quantity) == missing)
            {
                await Attribute(credentialId, round, entry, candidates, cancellationToken);
            }

            SteamMarketReceipt[] bought = [.. round.Of(entry.ClientOrderId).Where(receipt => receipt.Kind == SteamMarketReceiptKind.Bought)];
            int items = bought.Sum(receipt => receipt.Quantity);

            if (items == entry.Quantity)
            {
                await Finish(entry, SteamOrderOutcome.Filled, cancellationToken);
                updates.Publish(entry.BotId, Fill(entry, bought, currency, final: true));

                continue;
            }

            if (items > before)
            {
                updates.Publish(entry.BotId, Fill(entry, bought, currency, final: false));
            }

            // An order Steam closed with nothing more to prove is cancelled for its remainder, whoever closed it.
            if (!status.Active && items == status.Purchased)
            {
                await Finish(entry, SteamOrderOutcome.Cancelled, cancellationToken);
                updates.Publish(entry.BotId, ExternalOrderUpdate.ConfirmCancel(entry.EngineOrderId));
            }
        }
    }

    private async Task<bool> Attribute(
        Guid credentialId,
        SteamExecutionRound round,
        SteamOrderJournalEntry entry,
        IReadOnlyList<SteamMarketReceipt> evidence,
        CancellationToken cancellationToken)
    {
        SteamAttribution[] added = [.. evidence.Select(receipt => new SteamAttribution(receipt.Id, entry.ClientOrderId, entry.BotId))];

        if (!await attributions.Attribute(credentialId, added, clock.GetUtcNow(), cancellationToken))
        {
            return false;
        }

        round.Add(added);

        return true;
    }

    private async Task Finish(SteamOrderJournalEntry entry, SteamOrderOutcome outcome, CancellationToken cancellationToken)
    {
        await journal.Transition(
            entry with { State = SteamOrderJournalState.Done, Outcome = outcome, WaitingCode = null, UpdatedAt = clock.GetUtcNow() },
            entry.State,
            cancellationToken);
    }

    /// <summary>
    ///     The order's cumulative fill: items, what the buyers paid, and for a sale Steam's fees, the difference to what the
    ///     seller received. A buyer pays the listed price and no fee of its own.
    /// </summary>
    internal static ExternalOrderUpdate Fill(SteamOrderJournalEntry entry, IReadOnlyList<SteamMarketReceipt> receipts, SteamCurrency currency, bool final)
    {
        decimal items = receipts.Sum(receipt => receipt.Quantity);
        decimal buyers = receipts.Sum(receipt => receipt.BuyerPaysMinorUnits) * currency.MinorUnit;
        decimal fee = entry.Side == OrderSide.Sell ? buyers - receipts.Sum(receipt => receipt.SellerReceivesMinorUnits) * currency.MinorUnit : 0m;

        return final
            ? ExternalOrderUpdate.Final(entry.EngineOrderId, items, buyers, fee)
            : ExternalOrderUpdate.Partial(entry.EngineOrderId, items, buyers, fee);
    }
}