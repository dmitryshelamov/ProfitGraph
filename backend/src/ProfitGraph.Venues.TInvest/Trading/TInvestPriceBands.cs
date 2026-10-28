using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The share's price band from <c>MarketDataService/GetOrderBook</c> at depth 1, kept for five seconds per share (ADR
///     0023). A missing or inverted band is null: it never means that every price is allowed.
/// </summary>
internal sealed class TInvestPriceBands(TInvestTransport transport, IOptions<TInvestOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, (TInvestPriceBand? Band, DateTimeOffset ReadAt)> _bands =
        new ConcurrentDictionary<string, (TInvestPriceBand? Band, DateTimeOffset ReadAt)>(StringComparer.Ordinal);

    public async Task<TInvestPriceBand?> Get(TInvestBotContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        if (_bands.TryGetValue(context.Share.Uid, out (TInvestPriceBand? Band, DateTimeOffset ReadAt) cached) && now - cached.ReadAt < options.Value.PriceBandLifetime)
        {
            return cached.Band;
        }

        GetOrderBookResponse book = await transport.Unary(
            context.Token,
            TInvestMethodGroup.MarketData,
            (invoker, options) => new MarketDataService.MarketDataServiceClient(invoker).GetOrderBookAsync(
                new GetOrderBookRequest { InstrumentId = context.Share.Uid, Depth = 1 },
                options),
            cancellationToken);
        decimal low = book.LimitDown?.ToDecimal() ?? 0m;
        decimal high = book.LimitUp?.ToDecimal() ?? 0m;
        TInvestPriceBand? band = low > 0m && high >= low ? new TInvestPriceBand(low, high) : null;
        _bands[context.Share.Uid] = (band, now);

        return band;
    }
}