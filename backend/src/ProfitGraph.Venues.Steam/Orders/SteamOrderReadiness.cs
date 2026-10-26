using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Whether a Steam submit may be sent now (ADR 0024). A sell none of whose items has left its trade hold waits with
///     <see cref="OrderWaitingCode.SettlementHold" /> until the earliest unlock; as soon as one item is tradable the sell goes
///     and the module lists the unlocked items, as before. Everything else is ready here: buys keep their slot and
///     rate-limit waits in the module, and a sell the module already journaled or cannot cover is left to it. Reads the
///     module's own state only; it reserves and writes nothing.
/// </summary>
internal sealed class SteamOrderReadiness(ISteamBotAccounts accounts, ISteamOrderJournal journal, SteamSells sells, TimeProvider clock) : IOrderReadiness
{
    public VenueId Venue => SteamVenue.VenueId;

    public async Task<OrderReadinessInternal> Evaluate(OrderSubmitInternal submit, CancellationToken cancellationToken)
    {
        OrderSnapshot order = submit.Order;

        if (order.Side != OrderSide.Sell || order.Quantity is not { } wanted || wanted <= 0m || wanted != decimal.Truncate(wanted) || wanted > int.MaxValue ||
            await journal.Get(submit.ClientOrderId.Value, cancellationToken) is not null)
        {
            return OrderReadinessInternal.Ready;
        }

        SteamBotAccount bot = await accounts.Get(submit.BotId.Value, cancellationToken);
        IReadOnlyList<SteamOwnedAsset> free = await sells.FreeAssets(bot, (int)wanted, cancellationToken);

        if (free.Count < wanted)
        {
            return OrderReadinessInternal.Ready;
        }

        DateTimeOffset now = clock.GetUtcNow();

        if (free.Any(asset => asset.UnlockAt <= now))
        {
            return OrderReadinessInternal.Ready;
        }

        DateTimeOffset unlock = free.Min(asset => asset.UnlockAt);

        return new OrderReadinessInternal(OrderWaitingCode.SettlementHold, unlock, RecheckAt: unlock);
    }
}