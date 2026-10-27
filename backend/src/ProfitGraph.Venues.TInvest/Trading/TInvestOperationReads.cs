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
    ///     movements, without commissions and trade details. Only an order cancelled with nothing executed counts as no
    ///     execution; anything else, including a state the module does not know, counts as one.
    /// </summary>
    public async Task<TInvestShareOperations> OfShare(TInvestBotContext context, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var total = 0;
        var executed = 0;
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
            executed += response.Items.Count(item => item.State != OperationState.Canceled || item.QuantityDone > 0);

            if (!response.HasNext)
            {
                return new TInvestShareOperations(total, executed, Complete: true);
            }

            if (string.IsNullOrEmpty(response.NextCursor) || response.NextCursor == cursor)
            {
                break;
            }

            cursor = response.NextCursor;
        }

        return new TInvestShareOperations(total, executed, Complete: false);
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
            WithoutTrades = true
        };

        if (cursor is not null)
        {
            request.Cursor = cursor;
        }

        return request;
    }
}
