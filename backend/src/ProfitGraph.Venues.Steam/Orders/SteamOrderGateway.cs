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

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Steam Community Market execution for live bots (ADR 0022). Registered only by a Worker with the live role and trading
///     enabled. Buys and sells go to the module's journal-first order flows; every update comes from the journal and its
///     attributed history, so a reconnecting runner hears the same state again. Reconciliation refuses an account whose
///     Steam Guard is not ready, whose wallet is in another currency than the item or whose protection is open, and a
///     foreign buy order on the bot's item unless the bot allows them. An order whose write may have reached Steam without a
///     clear answer is unresolved.
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
    IServiceScopeFactory scopes) : IOrderGateway
{
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

            if (entry.State is SteamOrderJournalState.Placing or SteamOrderJournalState.Approving or SteamOrderJournalState.Unknown ||
                (entry.Side == OrderSide.Sell && (await journal.Reservations(entry.ClientOrderId, cancellationToken)).Any(reservation =>
                    reservation.State is SteamReservationState.Listing or SteamReservationState.Approving or SteamReservationState.Delisting or
                        SteamReservationState.Unknown)))
            {
                return new BotReconciliationInternal(known, found, BotStatusReason.UnresolvedOrder);
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