using System.Globalization;

using Microsoft.Extensions.Options;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     The buy side of Steam bot orders (ADR 0022). A buy is journaled before anything is sent, so a missing entry proves
///     that nothing was. The journal's slot index lets one buy order per item and account reach Steam; a further buy is
///     accepted locally with <see cref="SteamWaitingCode.SingleBuyOrderSlot" /> and placed by <see cref="Advance" /> when
///     the slot frees, the one nearest the market first. A market buy is a buy order at the lowest listing plus the
///     configured slippage whose remainder is cancelled after <see cref="SteamOptions.MarketBuyDeadline" />. A write that
///     may have reached Steam without a clear answer leaves its entry <see cref="SteamOrderJournalState.Unknown" />, holding
///     the slot, and is never sent again; fills come from reconciliation with market history (ADR 0022).
/// </summary>
internal sealed class SteamBuyOrders(
    ISteamOrderJournal journal,
    SteamAccountSessions sessions,
    SteamBuyOrderClient client,
    SteamOrderUpdates updates,
    IOptions<SteamOptions> options,
    TimeProvider clock)
{
    /// <summary>
    ///     Journals and places a buy. Returns a rejection, or null when the order is accepted, locally or by Steam, with its
    ///     update published. A submit that must look up first places only an entry that is provably unsent.
    /// </summary>
    public async Task<ExternalOrderUpdate?> Submit(
        SteamBotAccount bot,
        string clientOrderId,
        OrderSnapshot order,
        Func<CancellationToken, Task> markPlaceStarted,
        CancellationToken cancellationToken)
    {
        if (order.Side != OrderSide.Buy)
        {
            throw new ArgumentException("Only buys are placed as Steam buy orders.", nameof(order));
        }

        SteamOrderJournalEntry? entry = await journal.Get(clientOrderId, cancellationToken);

        if (entry is null)
        {
            (SteamOrderJournalEntry? planned, ExternalOrderUpdate? rejection) = await Plan(bot, clientOrderId, order, cancellationToken);

            if (rejection is not null)
            {
                return rejection;
            }

            entry = await journal.Add(planned!, cancellationToken) ? planned! : await journal.Get(clientOrderId, cancellationToken) ?? throw Unresolved();
        }

        return await Continue(bot.OwnerId, entry, markPlaceStarted, cancellationToken);
    }

    /// <summary>
    ///     Cancels a buy: locally while Steam has nothing, otherwise on Steam. A cancel is confirmed only once the order's
    ///     counters show it inactive with nothing bought; an order with purchases waits for reconciliation.
    /// </summary>
    public async Task Cancel(SteamBotAccount bot, string clientOrderId, CancellationToken cancellationToken)
    {
        SteamOrderJournalEntry? entry = await journal.Get(clientOrderId, cancellationToken);

        switch (entry?.State)
        {
            case null:
                return;
            case SteamOrderJournalState.Waiting or SteamOrderJournalState.AwaitingConfirmation:
                // A buy awaiting its confirmation has no buy order yet; the confirmation worker approves only entries that wait.
                if (!await journal.Transition(Finish(entry, SteamOrderOutcome.Cancelled), entry.State, cancellationToken))
                {
                    throw Unresolved();
                }

                updates.Publish(entry.BotId, ExternalOrderUpdate.ConfirmCancel(entry.EngineOrderId));

                return;
            case SteamOrderJournalState.Active or SteamOrderJournalState.Cancelling:
                if (!await CancelOnSteam(bot.OwnerId, entry, cancellationToken))
                {
                    throw Unresolved();
                }

                return;
            case SteamOrderJournalState.Done:
                Republish(entry);

                return;
            default:
                throw Unresolved();
        }
    }

    /// <summary>
    ///     Moves an account's buys along: cancels market buys past their deadline, confirms finished cancels and places the
    ///     waiting buy nearest the market of each item whose slot is free. A buy that cannot be resolved now stays as it is.
    /// </summary>
    public async Task Advance(Guid ownerId, Guid credentialId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SteamOrderJournalEntry> open = await journal.Open(credentialId, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();

        foreach (SteamOrderJournalEntry entry in open.Where(entry =>
                     entry.Side == OrderSide.Buy && entry.State is SteamOrderJournalState.Active or SteamOrderJournalState.Cancelling))
        {
            if (entry.State == SteamOrderJournalState.Cancelling || entry.Deadline <= now)
            {
                await Attempt(() => CancelOnSteam(ownerId, entry, cancellationToken));
            }
        }

        IReadOnlyList<SteamOrderJournalEntry> current = await journal.Open(credentialId, cancellationToken);
        IEnumerable<SteamOrderJournalEntry> nearest = current
            .Where(entry => entry.Side == OrderSide.Buy)
            .GroupBy(entry => entry.Instrument, StringComparer.Ordinal)
            .Where(item => !item.Any(entry => entry.HoldsSlot))
            .Select(item => item
                .Where(entry => entry.State == SteamOrderJournalState.Waiting)
                .OrderByDescending(entry => entry.PriceMinorUnits)
                .ThenBy(entry => entry.CreatedAt)
                .FirstOrDefault())
            .OfType<SteamOrderJournalEntry>();

        foreach (SteamOrderJournalEntry entry in nearest)
        {
            await Attempt(() => Place(ownerId, entry, beforeSend: null, cancellationToken));
        }
    }

    /// <summary>
    ///     Sends a buy whose mobile confirmation was approved again with the confirmation's creator, as Steam's own page does,
    ///     so Steam creates the buy order. Only the new buy order's ID proves it; any other answer leaves the entry unknown, as
    ///     in Profit Fox Designer, and a request that never left keeps it approved.
    /// </summary>
    public async Task Finalize(Guid ownerId, SteamOrderJournalEntry entry, CancellationToken cancellationToken)
    {
        if (entry is not { Side: OrderSide.Buy, State: SteamOrderJournalState.Approved, ConfirmationCreator: { } creator })
        {
            throw new ArgumentException("Only a buy whose confirmation was approved is finalized.", nameof(entry));
        }

        DateTimeOffset now = clock.GetUtcNow();
        SteamOrderJournalEntry placing = entry with { State = SteamOrderJournalState.Placing, UpdatedAt = now };
        await Store(placing, SteamOrderJournalState.Approved, cancellationToken);
        SteamWriteReceipt receipt = await client.Create(
            sessions.For(ownerId, entry.CredentialId),
            Instrument(entry),
            entry.Quantity,
            entry.PriceMinorUnits,
            creator,
            cancellationToken);
        DateTimeOffset answered = clock.GetUtcNow();

        switch (receipt.Kind)
        {
            case SteamWriteReceiptKind.Accepted when receipt.SteamOrderId is { } steamOrderId:
                await Store(
                    placing with
                    {
                        State = SteamOrderJournalState.Active,
                        WaitingCode = null,
                        SteamOrderId = steamOrderId,
                        Deadline = entry.Type == OrderType.Market ? answered + options.Value.MarketBuyDeadline : null,
                        UpdatedAt = answered
                    },
                    SteamOrderJournalState.Placing,
                    cancellationToken);
                updates.Publish(entry.BotId, ExternalOrderUpdate.Accept(entry.EngineOrderId));

                return;
            case SteamWriteReceiptKind.NotSent:
                await Store(placing with { State = SteamOrderJournalState.Approved, UpdatedAt = answered }, SteamOrderJournalState.Placing, cancellationToken);

                return;
            default:
                await Store(placing with { State = SteamOrderJournalState.Unknown, UpdatedAt = answered }, SteamOrderJournalState.Placing, cancellationToken);

                return;
        }
    }

    private async Task<(SteamOrderJournalEntry? Entry, ExternalOrderUpdate? Rejection)> Plan(
        SteamBotAccount bot,
        string clientOrderId,
        OrderSnapshot order,
        CancellationToken cancellationToken)
    {
        decimal minor = bot.Instrument.Currency.MinorUnit;
        long price;

        if (order.Type == OrderType.Limit)
        {
            decimal units = (order.LimitPrice ?? 0m) / minor;

            if (units <= 0m || units != decimal.Truncate(units) || units > long.MaxValue)
            {
                return (null, ExternalOrderUpdate.Reject(order.Id, RejectionReason.InvalidPrice, "The price is not a whole Steam minor unit."));
            }

            price = (long)units;
        }
        else if (await client.LowestListing(sessions.For(bot.OwnerId, bot.CredentialId), bot.Instrument, cancellationToken) is { } lowest)
        {
            price = (long)decimal.Ceiling(lowest * (1m + options.Value.MarketBuySlippage));
        }
        else
        {
            return (null, ExternalOrderUpdate.Reject(order.Id, RejectionReason.InstrumentUnavailable, "Nobody sells the item on Steam now."));
        }

        decimal each = price * minor;
        decimal wanted = order.Quantity ?? (order.QuoteBudget is { } budget ? decimal.Floor(budget / each) : 0m);

        if (order.Type == OrderType.Market)
        {
            wanted = Math.Min(wanted, decimal.Floor(order.Reservation / each));
        }

        if (wanted <= 0m || wanted != decimal.Truncate(wanted) || wanted > int.MaxValue)
        {
            return (null, ExternalOrderUpdate.Reject(
                order.Id,
                wanted <= 0m ? RejectionReason.ZeroQuantity : RejectionReason.InvalidAmount,
                string.Create(CultureInfo.InvariantCulture, $"A Steam buy needs a whole number of items; {wanted} at {each} is not.")));
        }

        DateTimeOffset now = clock.GetUtcNow();

        return (new SteamOrderJournalEntry(
            clientOrderId,
            bot.BotId,
            bot.CredentialId,
            order.Id,
            bot.Instrument.Symbol.Value,
            OrderSide.Buy,
            order.Type,
            (int)wanted,
            price,
            SteamOrderJournalState.Waiting,
            WaitingCode: null,
            SteamOrderId: null,
            ConfirmationCreator: null,
            Outcome: null,
            PlaceStartedAt: null,
            Deadline: null,
            now,
            now), null);
    }

    private async Task<ExternalOrderUpdate?> Continue(
        Guid ownerId,
        SteamOrderJournalEntry entry,
        Func<CancellationToken, Task>? beforeSend,
        CancellationToken cancellationToken)
    {
        switch (entry.State)
        {
            case SteamOrderJournalState.Waiting:
                return await Place(ownerId, entry, beforeSend, cancellationToken);
            case SteamOrderJournalState.Placing or SteamOrderJournalState.Unknown:
                throw Unresolved();
            case SteamOrderJournalState.Done when entry.Outcome == SteamOrderOutcome.Rejected:
                return Rejection(entry);
            default:
                Republish(entry);

                return null;
        }
    }

    /// <summary>
    ///     Takes the item's slot and sends the buy order. Without the slot the buy waits; a request that never left goes back
    ///     to waiting and is tried again.
    /// </summary>
    private async Task<ExternalOrderUpdate?> Place(
        Guid ownerId,
        SteamOrderJournalEntry entry,
        Func<CancellationToken, Task>? beforeSend,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        SteamOrderJournalEntry placing = entry with { State = SteamOrderJournalState.Placing, WaitingCode = null, PlaceStartedAt = now, UpdatedAt = now };

        if (!await journal.Transition(placing, SteamOrderJournalState.Waiting, cancellationToken))
        {
            SteamOrderJournalEntry current = await journal.Get(entry.ClientOrderId, cancellationToken) ?? throw Unresolved();

            return current.State == SteamOrderJournalState.Waiting
                ? await Wait(current, SteamWaitingCode.SingleBuyOrderSlot, cancellationToken)
                : await Continue(ownerId, current, beforeSend, cancellationToken);
        }

        if (beforeSend is not null)
        {
            await beforeSend(cancellationToken);
        }

        SteamInstrument instrument = Instrument(entry);
        SteamWriteReceipt receipt = await client.Create(
            sessions.For(ownerId, entry.CredentialId),
            instrument,
            entry.Quantity,
            entry.PriceMinorUnits,
            confirmationId: null,
            cancellationToken);
        DateTimeOffset answered = clock.GetUtcNow();

        switch (receipt.Kind)
        {
            case SteamWriteReceiptKind.NotSent:
                SteamOrderJournalEntry back = placing with { State = SteamOrderJournalState.Waiting, PlaceStartedAt = null, UpdatedAt = answered };
                await Store(back, SteamOrderJournalState.Placing, cancellationToken);

                return await Wait(back, SteamWaitingCode.RateLimited, cancellationToken);
            case SteamWriteReceiptKind.Accepted when receipt.SteamOrderId is { } steamOrderId:
                await Store(
                    placing with
                    {
                        State = SteamOrderJournalState.Active,
                        SteamOrderId = steamOrderId,
                        Deadline = entry.Type == OrderType.Market ? answered + options.Value.MarketBuyDeadline : null,
                        UpdatedAt = answered
                    },
                    SteamOrderJournalState.Placing,
                    cancellationToken);
                updates.Publish(entry.BotId, ExternalOrderUpdate.Accept(entry.EngineOrderId));

                return null;
            case SteamWriteReceiptKind.NeedsConfirmation:
                await Store(
                    placing with
                    {
                        State = SteamOrderJournalState.AwaitingConfirmation,
                        WaitingCode = SteamWaitingCode.AwaitingConfirmation,
                        SteamOrderId = receipt.SteamOrderId,
                        ConfirmationCreator = receipt.ConfirmationCreator,
                        UpdatedAt = answered
                    },
                    SteamOrderJournalState.Placing,
                    cancellationToken);
                updates.Publish(entry.BotId, ExternalOrderUpdate.AcceptLocally(entry.EngineOrderId));

                return null;
            case SteamWriteReceiptKind.Rejected:
                SteamOrderJournalEntry rejected = Finish(placing, SteamOrderOutcome.Rejected) with { UpdatedAt = answered };
                await Store(rejected, SteamOrderJournalState.Placing, cancellationToken);

                return Publish(beforeSend, rejected, receipt.SteamCode);
            default:
                await Store(placing with { State = SteamOrderJournalState.Unknown, UpdatedAt = answered }, SteamOrderJournalState.Placing, cancellationToken);

                throw Unresolved();
        }
    }

    private async Task<ExternalOrderUpdate?> Wait(SteamOrderJournalEntry entry, SteamWaitingCode code, CancellationToken cancellationToken)
    {
        if (entry.WaitingCode != code)
        {
            await journal.Transition(entry with { WaitingCode = code, UpdatedAt = clock.GetUtcNow() }, SteamOrderJournalState.Waiting, cancellationToken);
        }

        updates.Publish(entry.BotId, ExternalOrderUpdate.AcceptLocally(entry.EngineOrderId));

        return null;
    }

    /// <summary>
    ///     Cancels the buy order on Steam unless its counters show it ended already, and confirms the cancel when nothing was
    ///     bought. False while that cannot be proved.
    /// </summary>
    private async Task<bool> CancelOnSteam(Guid ownerId, SteamOrderJournalEntry entry, CancellationToken cancellationToken)
    {
        SteamAccountSession session = sessions.For(ownerId, entry.CredentialId);
        string steamOrderId = entry.SteamOrderId ?? throw Unresolved();
        SteamBuyOrderStatus status = await client.Status(session, steamOrderId, cancellationToken);

        if (status.Active)
        {
            SteamWriteReceipt receipt = await client.Cancel(session, steamOrderId, cancellationToken);

            if (receipt.Kind != SteamWriteReceiptKind.Accepted)
            {
                return false;
            }

            if (entry.State == SteamOrderJournalState.Active)
            {
                SteamOrderJournalEntry cancelling = entry with { State = SteamOrderJournalState.Cancelling, UpdatedAt = clock.GetUtcNow() };
                await Store(cancelling, SteamOrderJournalState.Active, cancellationToken);
                entry = cancelling;
            }

            status = await client.Status(session, steamOrderId, cancellationToken);
        }

        if (status.Active || status.Quantity != entry.Quantity || status.Purchased > 0)
        {
            // A purchase is a fill only with its market history receipt; reconciliation settles it.
            return false;
        }

        await Store(Finish(entry, SteamOrderOutcome.Cancelled), entry.State, cancellationToken);
        updates.Publish(entry.BotId, ExternalOrderUpdate.ConfirmCancel(entry.EngineOrderId));

        return true;
    }

    private async Task Store(SteamOrderJournalEntry next, SteamOrderJournalState expected, CancellationToken cancellationToken)
    {
        if (!await journal.Transition(next, expected, cancellationToken))
        {
            throw Unresolved();
        }
    }

    private ExternalOrderUpdate? Publish(Func<CancellationToken, Task>? beforeSend, SteamOrderJournalEntry rejected, int? steamCode)
    {
        // A buy placed for its intent returns the rejection; one that waited for its slot reports it as an update.
        if (beforeSend is not null)
        {
            return Rejection(rejected, steamCode);
        }

        updates.Publish(rejected.BotId, Rejection(rejected, steamCode));

        return null;
    }

    private void Republish(SteamOrderJournalEntry entry)
    {
        ExternalOrderUpdate? update = entry.State switch
        {
            SteamOrderJournalState.Waiting or SteamOrderJournalState.AwaitingConfirmation or SteamOrderJournalState.Approving or SteamOrderJournalState.Approved =>
                ExternalOrderUpdate.AcceptLocally(entry.EngineOrderId),
            SteamOrderJournalState.Active or SteamOrderJournalState.Cancelling => ExternalOrderUpdate.Accept(entry.EngineOrderId),
            SteamOrderJournalState.Done when entry.Outcome == SteamOrderOutcome.Cancelled => ExternalOrderUpdate.ConfirmCancel(entry.EngineOrderId),
            SteamOrderJournalState.Done when entry.Outcome == SteamOrderOutcome.Rejected => Rejection(entry),
            _ => null
        };

        if (update is not null)
        {
            updates.Publish(entry.BotId, update);
        }
    }

    private SteamOrderJournalEntry Finish(SteamOrderJournalEntry entry, SteamOrderOutcome outcome)
    {
        return entry with { State = SteamOrderJournalState.Done, Outcome = outcome, WaitingCode = null, UpdatedAt = clock.GetUtcNow() };
    }

    private static ExternalOrderUpdate Rejection(SteamOrderJournalEntry entry, int? steamCode = null)
    {
        string code = steamCode is { } number ? string.Create(CultureInfo.InvariantCulture, $" ({number})") : string.Empty;

        return ExternalOrderUpdate.Reject(entry.EngineOrderId, RejectionReason.BrokerRejected, $"Steam refused the buy order{code}.");
    }

    private static SteamInstrument Instrument(SteamOrderJournalEntry entry)
    {
        return SteamInstrument.TryParse(entry.Instrument, out SteamInstrument? instrument) ? instrument : throw Unresolved();
    }

    private static async Task Attempt(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (SteamOrderUnresolvedException)
        {
            // The entry keeps its state; the next round, or reconciliation, tries again.
        }
    }

    private static SteamOrderUnresolvedException Unresolved()
    {
        return new SteamOrderUnresolvedException("The Steam buy order's state is not proved.");
    }
}