using System.Globalization;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     The sell side of Steam bot orders (ADR 0022). A sell reserves whole items the bot owns, each asset by one open sell
///     of the account only, together with its journal entry. Every unlocked asset is listed on its own at what the seller
///     receives for the (raised) buyer price; an asset under its trade hold waits with <see cref="SteamWaitingCode.SettlementHold" />
///     and is listed by <see cref="Advance" /> once it unlocks, while the core releases its bot. A listing's ID, and so its confirmation's creator, comes from
///     <c>mylistings</c>, which also shows when a confirmed listing is active. A market sell lists at the highest buy order.
///     A write that may have reached Steam without a clear answer leaves its asset <see cref="SteamReservationState.Unknown" />
///     and is never sent again; sales come from reconciliation with market history.
/// </summary>
internal sealed class SteamSells(
    ISteamOrderJournal journal,
    ISteamOwnedAssets owned,
    SteamAccountSessions sessions,
    SteamListingClient client,
    SteamSellSettlement settlement,
    SteamOrderUpdates updates,
    ISteamOrderWaiting waiting,
    TimeProvider clock)
{
    /// <summary>
    ///     Journals the sell with its reserved assets and lists those that are unlocked. Returns a rejection, or null when
    ///     the order is accepted, locally or by Steam, with its update published.
    /// </summary>
    public async Task<ExternalOrderUpdate?> Submit(
        SteamBotAccount bot,
        string clientOrderId,
        OrderSnapshot order,
        Func<CancellationToken, Task> markPlaceStarted,
        CancellationToken cancellationToken)
    {
        if (order.Side != OrderSide.Sell || bot.SellerFees is null)
        {
            throw new ArgumentException("Only sells with the wallet's seller fees are listed.", nameof(order));
        }

        SteamOrderJournalEntry? entry = await journal.Get(clientOrderId, cancellationToken);

        if (entry is null)
        {
            (SteamOrderJournalEntry? planned, IReadOnlyList<SteamAssetReservation> reservations, ExternalOrderUpdate? rejection) =
                await Plan(bot, clientOrderId, order, cancellationToken);

            if (rejection is not null)
            {
                return rejection;
            }

            entry = await journal.AddSell(planned!, reservations, cancellationToken)
                ? planned!
                : await journal.Get(clientOrderId, cancellationToken) ?? throw Unresolved();
        }

        if (entry.State == SteamOrderJournalState.Done)
        {
            Republish(entry);

            return null;
        }

        await List(bot, entry, markPlaceStarted, cancellationToken);

        return null;
    }

    /// <summary>
    ///     Cancels a sell: assets not listed are released, listings are removed. The cancel is confirmed once every asset is
    ///     released; an asset whose listing was removed is released by its cancelled event in market history, and one whose
    ///     listing may have sold waits for reconciliation.
    /// </summary>
    public async Task Cancel(SteamBotAccount bot, string clientOrderId, CancellationToken cancellationToken)
    {
        SteamOrderJournalEntry? entry = await journal.Get(clientOrderId, cancellationToken);

        if (entry is null)
        {
            return;
        }

        if (entry.State == SteamOrderJournalState.Done)
        {
            Republish(entry);

            return;
        }

        if (entry.State != SteamOrderJournalState.Cancelling)
        {
            SteamOrderJournalEntry cancelling = entry with { State = SteamOrderJournalState.Cancelling, UpdatedAt = clock.GetUtcNow() };

            if (!await journal.Transition(cancelling, entry.State, cancellationToken))
            {
                throw Unresolved();
            }

            entry = cancelling;
        }

        SteamAccountSession session = sessions.For(bot.OwnerId, entry.CredentialId);
        var released = new List<SteamReservationState?>();

        foreach (SteamAssetReservation reservation in await journal.Reservations(clientOrderId, cancellationToken))
        {
            released.Add(await Release(session, reservation, cancellationToken));
        }

        if (released.Contains(value: null))
        {
            throw Unresolved();
        }

        // History confirms the cancel once it shows every removed listing's item back (SteamExecutions).
        if (released.Contains(SteamReservationState.Removed))
        {
            return;
        }

        // Items sold before the cancel were reported as fills already; the remainder is cancelled.
        await Finish(entry, SteamOrderOutcome.Cancelled, cancellationToken);
        updates.Publish(entry.BotId, ExternalOrderUpdate.ConfirmCancel(entry.EngineOrderId));
    }

    /// <summary>
    ///     Moves an account's sells along: lists assets that unlocked and reads <c>mylistings</c> while a listing's ID or state
    ///     is not known yet.
    /// </summary>
    public async Task Advance(Guid ownerId, Guid credentialId, CancellationToken cancellationToken)
    {
        SteamOrderJournalEntry[] sells =
        [
            .. (await journal.Open(credentialId, cancellationToken)).Where(entry => entry.Side == OrderSide.Sell && entry.State != SteamOrderJournalState.Cancelling)
        ];

        foreach (SteamOrderJournalEntry entry in sells)
        {
            if (SteamInstrument.TryParse(entry.Instrument, out SteamInstrument? instrument))
            {
                await Attempt(() => List(new SteamBotAccount(entry.BotId, ownerId, credentialId, instrument), entry, beforeSend: null, cancellationToken));
            }
        }

        IReadOnlyList<SteamAssetReservation> reserved = await journal.Reserved(credentialId, cancellationToken);

        if (reserved.Any(reservation => reservation.State == SteamReservationState.AwaitingConfirmation))
        {
            await Attempt(() => Bind(ownerId, credentialId, reserved, cancellationToken));
        }
    }

    private async Task<(SteamOrderJournalEntry? Entry, IReadOnlyList<SteamAssetReservation> Reservations, ExternalOrderUpdate? Rejection)> Plan(
        SteamBotAccount bot,
        string clientOrderId,
        OrderSnapshot order,
        CancellationToken cancellationToken)
    {
        SteamCurrency currency = bot.Instrument.Currency;
        long buyerPrice;

        if (order.Type == OrderType.Limit)
        {
            decimal raised = bot.SellerFees!.RaiseToRepresentable(order.LimitPrice ?? 0m, currency.Precision) / currency.MinorUnit;

            if (raised <= 0m || raised != decimal.Truncate(raised) || raised > long.MaxValue)
            {
                return (null, [], ExternalOrderUpdate.Reject(order.Id, RejectionReason.InvalidPrice, "The price is not a whole Steam minor unit."));
            }

            buyerPrice = (long)raised;
        }
        else if (await client.HighestBuyOrder(sessions.For(bot.OwnerId, bot.CredentialId), bot.Instrument, cancellationToken) is { } highest)
        {
            buyerPrice = highest;
        }
        else
        {
            return (null, [], ExternalOrderUpdate.Reject(order.Id, RejectionReason.InstrumentUnavailable, "Nobody buys the item on Steam now."));
        }

        long sellerUnits = SellerUnits(bot.SellerFees!, buyerPrice, currency);

        if (sellerUnits < 1)
        {
            return (null, [], ExternalOrderUpdate.Reject(order.Id, RejectionReason.InvalidPrice, "The seller would receive nothing at this price."));
        }

        decimal wanted = order.Quantity ?? 0m;

        if (wanted <= 0m || wanted != decimal.Truncate(wanted) || wanted > int.MaxValue)
        {
            return (null, [], ExternalOrderUpdate.Reject(order.Id, RejectionReason.InvalidAmount, "A Steam sell needs a whole number of items."));
        }

        IReadOnlyList<SteamAssetReservation> taken = await journal.Reserved(bot.CredentialId, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        SteamOwnedAsset[] free =
        [
            .. (await owned.Owned(bot, cancellationToken))
            .Where(asset => !taken.Any(other => other.AppId == asset.AppId && other.ContextId == asset.ContextId && other.AssetId == asset.AssetId))
            .OrderBy(asset => asset.UnlockAt)
            .ThenBy(asset => asset.AssetId, StringComparer.Ordinal)
            .Take((int)wanted)
        ];

        if (free.Length < wanted)
        {
            return (null, [], ExternalOrderUpdate.Reject(
                order.Id,
                RejectionReason.InsufficientBase,
                string.Create(CultureInfo.InvariantCulture, $"The bot owns {free.Length} free items of the {wanted} to sell.")));
        }

        var entry = new SteamOrderJournalEntry(
            clientOrderId,
            bot.BotId,
            bot.CredentialId,
            order.Id,
            bot.Instrument.Symbol.Value,
            OrderSide.Sell,
            order.Type,
            (int)wanted,
            buyerPrice,
            SteamOrderJournalState.Waiting,
            WaitingCode: null,
            SteamOrderId: null,
            ConfirmationCreator: null,
            Outcome: null,
            PlaceStartedAt: null,
            Deadline: null,
            now,
            now);
        SteamAssetReservation[] reservations =
        [
            .. free.Select(asset => new SteamAssetReservation(
                clientOrderId,
                bot.CredentialId,
                asset.AppId,
                asset.ContextId,
                asset.AssetId,
                asset.UnlockAt,
                sellerUnits,
                SteamReservationState.Reserved,
                ListingId: null,
                now,
                now))
        ];

        return (entry, reservations, null);
    }

    /// <summary>
    ///     Lists every reserved asset that has unlocked, one request each. A sell with nothing listed yet is accepted locally,
    ///     with <see cref="SteamWaitingCode.SettlementHold" /> while its items are locked. Outside a runner turn, without
    ///     <paramref name="beforeSend" />, nothing is listed unless the core releases the bot.
    /// </summary>
    private async Task List(SteamBotAccount bot, SteamOrderJournalEntry entry, Func<CancellationToken, Task>? beforeSend, CancellationToken cancellationToken)
    {
        SteamAccountSession session = sessions.For(bot.OwnerId, entry.CredentialId);
        IReadOnlyList<SteamAssetReservation> reservations = await journal.Reservations(entry.ClientOrderId, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        var started = false;

        foreach (SteamAssetReservation reservation in
                 reservations.Where(reservation => reservation.State == SteamReservationState.Reserved && reservation.UnlockAt <= now))
        {
            if (!started && beforeSend is null && !await waiting.MayRelease(entry.BotId, cancellationToken))
            {
                break;
            }

            SteamAssetReservation listing = reservation with { State = SteamReservationState.Listing, UpdatedAt = clock.GetUtcNow() };

            if (!await journal.Transition(listing, SteamReservationState.Reserved, cancellationToken))
            {
                continue;
            }

            if (!started && beforeSend is not null)
            {
                await beforeSend(cancellationToken);
            }

            started = true;
            SteamWriteReceipt receipt = await client.Sell(session, listing, listing.SellerMinorUnits, cancellationToken);
            SteamReservationState next = receipt.Kind switch
            {
                // Steam names no listing ID here; mylistings binds it and tells a pending confirmation from an active listing.
                SteamWriteReceiptKind.Accepted or SteamWriteReceiptKind.NeedsConfirmation => SteamReservationState.AwaitingConfirmation,
                SteamWriteReceiptKind.NotSent => SteamReservationState.Reserved,
                SteamWriteReceiptKind.Rejected => SteamReservationState.Released,
                _ => SteamReservationState.Unknown
            };
            await Store(
                listing with { State = next, ListingId = receipt.Kind == SteamWriteReceiptKind.Accepted ? receipt.SteamOrderId : null, UpdatedAt = clock.GetUtcNow() },
                SteamReservationState.Listing,
                cancellationToken);
        }

        await settlement.Settle(entry, cancellationToken, beforeSend is not null);
    }

    /// <summary>
    ///     Reads the account's listings and binds each asset awaiting its confirmation to its listing ID, or marks it listed
    ///     once Steam shows the listing active.
    /// </summary>
    private async Task Bind(Guid ownerId, Guid credentialId, IReadOnlyList<SteamAssetReservation> reserved, CancellationToken cancellationToken)
    {
        if (await client.Listings(sessions.For(ownerId, credentialId), cancellationToken) is not { } listings)
        {
            return;
        }

        foreach (SteamAssetReservation reservation in reserved.Where(reservation => reservation.State == SteamReservationState.AwaitingConfirmation))
        {
            SteamListing[] matching =
            [
                .. listings.Where(listing =>
                    listing.AppId == reservation.AppId && listing.ContextId == reservation.ContextId && listing.AssetId == reservation.AssetId)
            ];

            if (matching.Length != 1)
            {
                continue;
            }

            SteamListing found = matching[0];
            SteamAssetReservation next = reservation with
            {
                State = found.AwaitingConfirmation ? SteamReservationState.AwaitingConfirmation : SteamReservationState.Listed,
                ListingId = found.Id,
                UpdatedAt = clock.GetUtcNow()
            };

            if (next != reservation)
            {
                await journal.Transition(next, SteamReservationState.AwaitingConfirmation, cancellationToken);
            }
        }

        foreach (string clientOrderId in reserved.Select(reservation => reservation.ClientOrderId).Distinct(StringComparer.Ordinal))
        {
            if (await journal.Get(clientOrderId, cancellationToken) is { } entry)
            {
                await settlement.Settle(entry, cancellationToken);
            }
        }
    }

    /// <summary>
    ///     Frees one asset of a cancelled sell. Returns the state it ends in, or null when it is unresolved now.
    /// </summary>
    private async Task<SteamReservationState?> Release(SteamAccountSession session, SteamAssetReservation reservation, CancellationToken cancellationToken)
    {
        switch (reservation.State)
        {
            case SteamReservationState.Released or SteamReservationState.Sold or SteamReservationState.Removed:
                return reservation.State;
            case SteamReservationState.Reserved:
                return await journal.Transition(
                    reservation with { State = SteamReservationState.Released, UpdatedAt = clock.GetUtcNow() },
                    SteamReservationState.Reserved,
                    cancellationToken)
                    ? SteamReservationState.Released
                    : null;
            case SteamReservationState.Listed or SteamReservationState.AwaitingConfirmation when reservation.ListingId is { } listingId:
                SteamAssetReservation delisting = reservation with { State = SteamReservationState.Delisting, UpdatedAt = clock.GetUtcNow() };

                if (!await journal.Transition(delisting, reservation.State, cancellationToken))
                {
                    return null;
                }

                SteamWriteReceipt receipt = await client.Remove(session, listingId, cancellationToken);
                SteamReservationState next = receipt.Kind switch
                {
                    SteamWriteReceiptKind.Accepted => SteamReservationState.Removed,
                    SteamWriteReceiptKind.NotSent => reservation.State,
                    _ => SteamReservationState.Unknown
                };
                await Store(delisting with { State = next, UpdatedAt = clock.GetUtcNow() }, SteamReservationState.Delisting, cancellationToken);

                return next == SteamReservationState.Removed ? next : null;
            default:
                return null;
        }
    }

    private async Task Finish(SteamOrderJournalEntry entry, SteamOrderOutcome outcome, CancellationToken cancellationToken)
    {
        await journal.Transition(
            entry with { State = SteamOrderJournalState.Done, Outcome = outcome, WaitingCode = null, UpdatedAt = clock.GetUtcNow() },
            entry.State,
            cancellationToken);
    }

    private void Republish(SteamOrderJournalEntry entry)
    {
        updates.Publish(
            entry.BotId,
            entry.Outcome == SteamOrderOutcome.Rejected
                ? ExternalOrderUpdate.Reject(entry.EngineOrderId, RejectionReason.BrokerRejected, "Steam refused every listing.")
                : ExternalOrderUpdate.ConfirmCancel(entry.EngineOrderId));
    }

    private async Task Store(SteamAssetReservation next, SteamReservationState expected, CancellationToken cancellationToken)
    {
        if (!await journal.Transition(next, expected, cancellationToken))
        {
            throw Unresolved();
        }
    }

    private static long SellerUnits(SellerFeeSchedule fees, long buyerMinorUnits, SteamCurrency currency)
    {
        return (long)(fees.SellerAmount(buyerMinorUnits * currency.MinorUnit, currency.Precision) / currency.MinorUnit);
    }

    private static async Task Attempt(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (SteamOrderUnresolvedException)
        {
            // The sell keeps its state; the next round, or reconciliation, tries again.
        }
    }

    private static SteamOrderUnresolvedException Unresolved()
    {
        return new SteamOrderUnresolvedException("The Steam sell's state is not proved.");
    }
}