using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;

namespace ProfitGraph.Application.Scanner.GetScanTrades;

internal sealed class GetScanTradesQueryHandler(IEnumerable<IMarketScanSource> sources, IScanTradeRepository trades, ScanTradeOptions options)
    : IRequestHandler<GetScanTradesQuery, IReadOnlyList<ScanTradeInternal>>
{
    public async Task<IReadOnlyList<ScanTradeInternal>> Handle(GetScanTradesQuery query, CancellationToken cancellationToken)
    {
        sources.Find(query.Venue);
        IReadOnlyList<ScanTrade> list = await trades.List(
            new UserId(query.OwnerId),
            new VenueId(query.Venue),
            string.IsNullOrWhiteSpace(query.Symbol) ? null : query.Symbol,
            options.ListLimit,
            cancellationToken);

        return [.. list.Select(trade => trade.ToInternal())];
    }
}