using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Reads of the account's operations and positions (<c>OperationsService</c>) that the proof of an order's absence needs
///     (ADR 0030). Operations come from <c>GetOperationsByCursor</c>: <c>GetOperations</c> is deprecated by the broker and
///     cannot be paged. Every call goes through the token's <c>Operations</c> request limit. An answer that cannot be read or
///     cannot be complete is never taken as an empty one.
/// </summary>
internal sealed class TInvestOperationReads(TInvestTransport transport)
{
    /// <summary>
    ///     The broker's largest page.
    /// </summary>
    internal const int PageSize = 1000;

    /// <summary>
    ///     The most pages read for one proof; a window with more operations is left to the owner.
    /// </summary>
    internal const int MaxPages = 5;

    /// <summary>
    ///     The operations of the share between two instants: orders (executed, still executing or cancelled) and other
    ///     movements, with their trade numbers but without commissions. An operation cancelled with nothing executed counts as
    ///     no execution, and so does one that <paramref name="ownTrades" /> (trade number to the engine order that made the trade)
    ///     explains whole (ADR 0033); it is counted by the order. Anything else, including a state or a type the module does not
    ///     know, counts as an execution.
    /// </summary>
    public async Task<TInvestShareOperations> OfShare(
        TInvestBotContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, long> ownTrades,
        CancellationToken cancellationToken)
    {
        var total = 0;
        var executed = 0;
        List<long> own = [];
        string? cursor = null;

        for (var page = 1; page <= MaxPages; page++)
        {
            string? at = cursor;
            GetOperationsByCursorResponse response = await transport.Unary(
                context.Token,
                TInvestMethodGroup.Operations,
                (invoker, options) => new OperationsService.OperationsServiceClient(invoker).GetOperationsByCursorAsync(Request(context, from, to, at), options),
                cancellationToken);

            total += response.Items.Count;

            foreach (OperationItem item in response.Items.Where(item => item.State != OperationState.Canceled || item.QuantityDone > 0))
            {
                if (OwnOrderOf(item, ownTrades) is { } engineOrderId)
                {
                    own.Add(engineOrderId);
                }
                else
                {
                    executed++;
                }
            }

            if (!response.HasNext)
            {
                return new TInvestShareOperations(total, executed, Complete: true, own);
            }

            if (string.IsNullOrEmpty(response.NextCursor) || response.NextCursor == cursor)
            {
                break;
            }

            cursor = response.NextCursor;
        }

        return new TInvestShareOperations(total, executed, Complete: false, own);
    }

    /// <summary>
    ///     The account's position in the share, or null while the broker is still loading the account's limits, when the
    ///     answer cannot be trusted.
    /// </summary>
    public async Task<TInvestSharePosition?> PositionOf(TInvestBotContext context, CancellationToken cancellationToken)
    {
        PositionsResponse response = await transport.Unary(
            context.Token,
            TInvestMethodGroup.Operations,
            (invoker, options) => new OperationsService.OperationsServiceClient(invoker).GetPositionsAsync(
                new PositionsRequest { AccountId = context.AccountId },
                options),
            cancellationToken);

        if (response.LimitsLoadingInProgress)
        {
            return null;
        }

        PositionsSecurities[] ofShare = [.. response.Securities.Where(security => security.InstrumentUid == context.Share.Uid)];

        return new TInvestSharePosition(ofShare.Sum(security => security.Balance), ofShare.Sum(security => security.Blocked));
    }

    /// <summary>
    ///     The engine order whose trades make up the whole operation, or null when the operation cannot be tied to one order
    ///     without a guess: it is not a finished purchase or sale, it has no trade numbers, a number is unknown or belongs to
    ///     another order, or its trades do not add up to its executed quantity.
    /// </summary>
    private static long? OwnOrderOf(OperationItem item, IReadOnlyDictionary<string, long> ownTrades)
    {
        if (item.Type is not (OperationType.Buy or OperationType.Sell) || item.State is not (OperationState.Executed or OperationState.Canceled))
        {
            return null;
        }

        IReadOnlyList<OperationItemTrade> trades = item.TradesInfo?.Trades ?? [];

        if (trades.Count == 0 || trades.Sum(trade => trade.Quantity) != item.QuantityDone)
        {
            return null;
        }

        long? order = null;

        foreach (OperationItemTrade trade in trades)
        {
            if (string.IsNullOrEmpty(trade.Num) || !ownTrades.TryGetValue(trade.Num, out long engineOrderId) || (order is { } seen && seen != engineOrderId))
            {
                return null;
            }

            order = engineOrderId;
        }

        return order;
    }

    private static GetOperationsByCursorRequest Request(TInvestBotContext context, DateTimeOffset from, DateTimeOffset to, string? cursor)
    {
        var request = new GetOperationsByCursorRequest
        {
            AccountId = context.AccountId,
            InstrumentId = context.Share.Uid,
            From = from.ToTimestamp(),
            To = to.ToTimestamp(),
            Limit = PageSize,
            WithoutCommissions = true,
            WithoutTrades = false
        };

        if (cursor is not null)
        {
            request.Cursor = cursor;
        }

        return request;
    }
}
