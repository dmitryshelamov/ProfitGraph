using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;

namespace ProfitGraph.Application.Scanner.GetScanTradeSettings;

internal sealed class GetScanTradeSettingsQueryHandler(IEnumerable<IMarketScanSource> sources, IScanTradeSettingsRepository settings)
    : IRequestHandler<GetScanTradeSettingsQuery, ScanTradeSettingsInternal?>
{
    public async Task<ScanTradeSettingsInternal?> Handle(GetScanTradeSettingsQuery query, CancellationToken cancellationToken)
    {
        sources.Find(query.Venue);

        return (await settings.Get(new UserId(query.OwnerId), new VenueId(query.Venue), cancellationToken))?.ToInternal();
    }
}