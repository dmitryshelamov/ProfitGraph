using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.MarketData;
using ProfitGraph.Venues.Steam.Protection;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Steam Community Market execution for live bots (ADR 0022). Registered only by a Worker with the live role and trading
///     enabled. Buys and sells go to the module's journal-first order flows; every update comes from the journal and its
///     attributed history, so a reconnecting runner hears the same state again. Reconciliation refuses an account whose
///     Steam Guard is not ready, whose wallet is in another currency than the item or whose protection is open, and a
///     foreign buy order on the bot's item unless the bot allows them. An order whose write may have reached Steam without a
///     clear answer is unresolved, unless the owner confirmed a buy absent and Steam shows nothing of it: no buy order of the
///     account on the item and no purchase of it since the place request in freshly read history (Profit Fox Designer ADR
///     0055). Such a buy ends rejected and frees its slot. The same evidence closes it without the owner once its place request
///     is a day old (ADR 0030).
/// </summary>
internal sealed class SteamOrderGateway(
    ISteamBotAccounts accounts,
    ISteamOrderJournal journal,
    ISteamMarketHistoryStore receipts,
    ISteamMarketAttributions attributions,
    SteamBuyOrders buys,
    SteamSells sells,
    SteamOrderUpdates updates,
    SteamMarketDataAccount marketData,
    SteamAccountProtection protection,
    IServiceScopeFactory scopes,
    SteamMarketHistory history,
    SteamAccountSessions sessions,
    TimeProvider clock) : IOrderGateway
{
    /// <summary>
    ///     How much earlier than its place request a purchase may be stamped by Steam and still be the buy's (Profit Fox
    ///     Designer's fixed local tolerance, as in <see cref="SteamExecutions" />).
    /// </summary>
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     How old a buy's place request must be before Steam's silence about it counts as proof (ADR 0030): the market history
    ///     lags, and a day is far beyond any lag Profit Fox Designer saw.
    /// </summary>
    internal static readonly TimeSpan AbsenceSettleDelay = TimeSpan.FromHours(24);

    /// <summary>
    ///     The codes of the checks that close a buy by itself, in order (ADR 0030).
    /// </summary>
    private static readonly string[] AutomaticChecks = ["NoBuyOrderOnTheItem", "NoPurchaseInHistory", "SettleDelayPassed"];

    public VenueId Venue => SteamVenue.VenueId;

    public ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId)
    {
        return SteamClientOrderIds.For(botId, engineOrderId);
    }

    public async IAsyncEnumerable<ExternalOrderUpdate> Updates(BotId botId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (ExternalOrderUpdate update in updates.Read(botId.Value, cancellationToken))
        {
            yield return update;
        }
    }

    public async Task<ExternalOrderUpdate?> Submit(OrderSubmitInternal submit, Func<CancellationToken, Task> markPlaceStarted, CancellationToken cancellationToken)
    {
        SteamBotAccount bot = await accounts.Get(submit.BotId.Value, cancellationToken);

        return submit.Order.Side == OrderSide.Buy
            ? await buys.Submit(bot, submit.ClientOrderId.Value, submit.Order, markPlaceStarted, cancellationToken)
            : await sells.Submit(bot, submit.ClientOrderId.Value, submit.Order, markPlaceStarted, cancellationToken);
    }

    public async Task Cancel(BotId botId, ClientOrderId clientOrderId, CancellationToken cancellationToken)
    {
        SteamOrderJournalEntry? entry = await journal.Get(clientOrderId.Value, cancellationToken);

        if (entry is null)
        {
            // The journal comes before any request, so an order it does not know never reached Steam.
            if (SteamClientOrderIds.TryParse(clientOrderId.Value, out BotId? owner, out long engineOrderId) && owner == botId)
            {
                updates.Publish(botId.Value, ExternalOrderUpdate.ConfirmCancel(engineOrderId));
            }

            return;
        }

        SteamBotAccount bot = await accounts.Get(botId.Value, cancellationToken);

        if (entry.Side == OrderSide.Buy)
        {
            await buys.Cancel(bot, clientOrderId.Value, cancellationToken);
        }
        else
        {
            await sells.Cancel(bot, clientOrderId.Value, cancellationToken);
        }
    }

    public async Task<BotReconciliationInternal> Reconcile(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        SteamBotAccount bot = await accounts.Get(request.BotId.Value, cancellationToken);

        if (await Refusal(bot, request.AllowForeignOrders, cancellationToken) is { } refusal)
        {
            return new BotReconciliationInternal([], [], refusal);
        }

        Dictionary<string, SteamMarketReceipt> all = (await receipts.From(bot.CredentialId, position: 0, cancellationToken)).ToDictionary(
            stored => stored.Receipt.Id,
            stored => stored.Receipt,
            StringComparer.Ordinal);
        IReadOnlyList<SteamAttribution> attributed = await attributions.Of(bot.CredentialId, cancellationToken);
        var found = new List<ReconciledOrderInternal>();
        var known = new List<ExternalOrderUpdate>();

        foreach (ReconcileOrderInternal order in request.Orders)
        {
            if (await journal.Get(order.ClientOrderId.Value, cancellationToken) is not { } entry || entry.BotId != bot.BotId)
            {
                continue;
            }

            if (order.OwnerConfirmedAbsent && await ProvenAbsent(bot, entry, cancellationToken))
            {
                known.Add(
                    ExternalOrderUpdate.Reject(
                        entry.EngineOrderId,
                        RejectionReason.OwnerConfirmedAbsent,
                        "The owner confirmed the buy absent and Steam shows nothing of it.",
                        ownerConfirmedAbsent: true));
                found.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true));

                continue;
            }

            string? notClosed = null;

            if (!order.OwnerConfirmedAbsent && Unresolved(entry))
            {
                notClosed = await AutomaticFailure(bot, entry, order, close: true, cancellationToken);

                if (notClosed is null)
                {
                    known.Add(
                        ExternalOrderUpdate.Reject(
                            entry.EngineOrderId,
                            RejectionReason.AbsenceProven,
                            "Steam shows nothing of the buy after the settle delay.",
                            ownerConfirmedAbsent: true));
                    found.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true, new OrderAbsenceProofInternal(AutomaticChecks)));

                    continue;
                }
            }

            if (Unresolved(entry) || (entry.Side == OrderSide.Sell && (await journal.Reservations(entry.ClientOrderId, cancellationToken)).Any(reservation =>
                    reservation.State is SteamReservationState.Listing or SteamReservationState.Approving or SteamReservationState.Delisting or
                        SteamReservationState.Unknown)))
            {
                return new BotReconciliationInternal(known, found, BotStatusReason.UnresolvedOrder, notClosed is null ? null : $"AutoCheck:{notClosed}");
            }

            SteamMarketReceipt[] fills =
            [
                .. attributed.Where(attribution => attribution.ClientOrderId == entry.ClientOrderId)
                    .Select(attribution => all.GetValueOrDefault(attribution.ReceiptId))
                    .OfType<SteamMarketReceipt>()
                    .Where(receipt => receipt.Kind is SteamMarketReceiptKind.Bought or SteamMarketReceiptKind.Sold)
            ];
            known.AddRange(Updates(entry, fills, bot.Instrument.Currency));
            found.Add(new ReconciledOrderInternal(order.ClientOrderId, entry.State == SteamOrderJournalState.Done));
        }

        return new BotReconciliationInternal(known, found, Degradation: null);
    }

    private static bool Unresolved(SteamOrderJournalEntry entry)
    {
        return entry.State is SteamOrderJournalState.Placing or SteamOrderJournalState.Approving or SteamOrderJournalState.Unknown;
    }

    /// <summary>
    ///     Why Steam's records do not close the buy without the owner, or null when they do and the entry ended rejected
    ///     (<paramref name="close" />) or would. Only a buy in placing or unknown state without a Steam order ID qualifies, and only
    ///     a day after its place request; a buy awaiting its mobile confirmation, one Steam gave an ID, and a sell never do.
    ///     A page or history Steam cannot give now is a failed check.
    /// </summary>
    private async Task<string?> AutomaticFailure(
        SteamBotAccount bot,
        SteamOrderJournalEntry entry,
        ReconcileOrderInternal order,
        bool close,
        CancellationToken cancellationToken)
    {
        if (entry is not { Side: OrderSide.Buy, State: SteamOrderJournalState.Placing or SteamOrderJournalState.Unknown, SteamOrderId: null })
        {
            return "NotProvable";
        }

        if ((entry.PlaceStartedAt ?? order.PlaceStartedAt) is not { } placed)
        {
            return "PlaceStartUnknown";
        }

        if (clock.GetUtcNow() - placed < AbsenceSettleDelay)
        {
            return "SettleDelayNotPassed";
        }

        try
        {
            return await ProvenAbsent(bot, entry, close, cancellationToken) ? null : "EvidenceFound";
        }
        catch (SteamOrderUnresolvedException)
        {
            return "ReadFailed";
        }
    }

    private Task<bool> ProvenAbsent(SteamBotAccount bot, SteamOrderJournalEntry entry, CancellationToken cancellationToken)
    {
        return ProvenAbsent(bot, entry, close: true, cancellationToken);
    }

    /// <summary>
    ///     Whether a buy left nothing on Steam, read fresh: no buy order of the account on the item other than the journal's known
    ///     ones, and no unattributed purchase of the item since its place request. When so and <paramref name="close" /> is set,
    ///     its entry ends rejected. A buy awaiting its mobile confirmation, a sell, or anything Steam may hold stays unresolved;
    ///     a page or history Steam cannot give now is retried later.
    /// </summary>
    private async Task<bool> ProvenAbsent(SteamBotAccount bot, SteamOrderJournalEntry entry, bool close, CancellationToken cancellationToken)
    {
        if (entry is not { Side: OrderSide.Buy, State: SteamOrderJournalState.Placing or SteamOrderJournalState.Unknown, SteamOrderId: null } ||
            !SteamInstrument.TryParse(entry.Instrument, out SteamInstrument? instrument))
        {
            return false;
        }

        SteamWallet wallet;
        IReadOnlyList<SteamAccountBuyOrder>? orders;

        try
        {
            byte[] page = await marketData.MarketPageOf(bot.CredentialId, cancellationToken);
            wallet = SteamWalletParser.Read(page);
            orders = SteamAccountBuyOrdersParser.Read(page);
        }
        catch (SteamMarketDataException exception)
        {
            throw new SteamOrderUnresolvedException(exception.Message);
        }

        HashSet<string> known =
        [
            .. (await journal.Open(bot.CredentialId, cancellationToken))
            .Where(open => open.ClientOrderId != entry.ClientOrderId)
            .Select(open => open.SteamOrderId)
            .OfType<string>()
        ];

        if (orders is null)
        {
            throw new SteamOrderUnresolvedException("The account's buy orders are unreadable.");
        }

        if (orders.Any(order => order.AppId == instrument.AppId && order.MarketHashName == instrument.MarketHashName && !known.Contains(order.Id)))
        {
            return false;
        }

        if (await history.Read(sessions.For(bot.OwnerId, bot.CredentialId), bot.CredentialId, wallet.Currency, wallet.SellerFees, cancellationToken) is null)
        {
            throw new SteamOrderUnresolvedException("The account's market history is unknown now.");
        }

        HashSet<string> attributed = [.. (await attributions.Of(bot.CredentialId, cancellationToken)).Select(attribution => attribution.ReceiptId)];
        DateTimeOffset since = (entry.PlaceStartedAt ?? entry.CreatedAt) - Skew;

        if ((await receipts.From(bot.CredentialId, position: 0, cancellationToken)).Any(stored => stored.Receipt is
                {
                    Kind: SteamMarketReceiptKind.Bought
                } receipt && receipt.AppId == instrument.AppId && receipt.MarketHashName == instrument.MarketHashName && receipt.At >= since &&
                !attributed.Contains(receipt.Id)))
        {
            return false;
        }

        return !close || await journal.Transition(
            entry with { State = SteamOrderJournalState.Done, Outcome = SteamOrderOutcome.Rejected, UpdatedAt = clock.GetUtcNow() },
            entry.State,
            cancellationToken);
    }

    /// <summary>
    ///     Whether reconciliation would now close, on Steam's own records, every unresolved buy of the request and has no other
    ///     reason to stop the bot (ADR 0030). It only reads: no entry moves.
    /// </summary>
    public async Task<bool> CanProveAbsence(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        SteamBotAccount bot = await accounts.Get(request.BotId.Value, cancellationToken);

        if (await Refusal(bot, request.AllowForeignOrders, cancellationToken) is not null)
        {
            return false;
        }

        var proven = false;

        foreach (ReconcileOrderInternal order in request.Orders)
        {
            if (await journal.Get(order.ClientOrderId.Value, cancellationToken) is not { } entry || entry.BotId != bot.BotId)
            {
                continue;
            }

            if (Unresolved(entry) || (entry.Side == OrderSide.Sell && (await journal.Reservations(entry.ClientOrderId, cancellationToken)).Any(reservation =>
                    reservation.State is SteamReservationState.Listing or SteamReservationState.Approving or SteamReservationState.Delisting or
                        SteamReservationState.Unknown)))
            {
                if (!Unresolved(entry) || await AutomaticFailure(bot, entry, order, close: false, cancellationToken) is not null)
                {
                    return false;
                }

                proven = true;
            }
        }

        return proven;
    }

    /// <summary>
    ///     Why the bot's account may not trade now, or null.
    /// </summary>
    private async Task<BotStatusReason?> Refusal(SteamBotAccount bot, bool allowForeignOrders, CancellationToken cancellationToken)
    {
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            if (await scope.ServiceProvider.GetRequiredService<ISteamAccountStore>().Get(bot.CredentialId, cancellationToken) is not
                { GuardStatus: SteamGuardStatus.Ready })
            {
                return BotStatusReason.KeyPermissions;
            }
        }

        if (await protection.Holds(bot.CredentialId, cancellationToken))
        {
            return BotStatusReason.VenueAccountProtection;
        }

        byte[] page;

        try
        {
            page = await marketData.MarketPageOf(bot.CredentialId, cancellationToken);
        }
        catch (SteamMarketDataException exception)
        {
            // The runner tries again later; an unreadable page proves nothing about the account.
            throw new SteamOrderUnresolvedException(exception.Message);
        }

        try
        {
            if (SteamWalletParser.Read(page).Currency != bot.Instrument.Currency)
            {
                return BotStatusReason.KeyPermissions;
            }
        }
        catch (SteamMarketDataException exception)
        {
            throw new SteamOrderUnresolvedException(exception.Message);
        }

        if (allowForeignOrders)
        {
            return null;
        }

        IReadOnlyList<SteamAccountBuyOrder> orders = SteamAccountBuyOrdersParser.Read(page) ??
            throw new SteamOrderUnresolvedException("The account's buy orders are unreadable.");
        HashSet<string> ours =
        [
            .. (await journal.Open(bot.CredentialId, cancellationToken)).Select(entry => entry.SteamOrderId).OfType<string>()
        ];

        return orders.Any(order => order.AppId == bot.Instrument.AppId && order.MarketHashName == bot.Instrument.MarketHashName && !ours.Contains(order.Id))
            ? BotStatusReason.ForeignOrder
            : null;
    }

    /// <summary>
    ///     What the engine should know of a journaled order: its acceptance, its proven fills and how it ended.
    /// </summary>
    private static IEnumerable<ExternalOrderUpdate> Updates(SteamOrderJournalEntry entry, IReadOnlyList<SteamMarketReceipt> fills, SteamCurrency currency)
    {
        bool accepted = entry.State is SteamOrderJournalState.Active or SteamOrderJournalState.Cancelling ||
            (entry.State == SteamOrderJournalState.Done && entry.Outcome != SteamOrderOutcome.Rejected);

        if (entry is { State: SteamOrderJournalState.Done, Outcome: SteamOrderOutcome.Rejected })
        {
            yield return ExternalOrderUpdate.Reject(entry.EngineOrderId, RejectionReason.BrokerRejected, "Steam refused the order.");

            yield break;
        }

        yield return accepted ? ExternalOrderUpdate.Accept(entry.EngineOrderId) : ExternalOrderUpdate.AcceptLocally(entry.EngineOrderId);

        if (fills.Count > 0)
        {
            yield return SteamExecutions.Fill(entry, fills, currency, entry.Outcome == SteamOrderOutcome.Filled);
        }

        if (entry.Outcome == SteamOrderOutcome.Cancelled)
        {
            yield return ExternalOrderUpdate.ConfirmCancel(entry.EngineOrderId);
        }
    }
}