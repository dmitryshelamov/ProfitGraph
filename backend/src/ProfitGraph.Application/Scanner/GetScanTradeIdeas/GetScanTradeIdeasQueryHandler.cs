using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.GetScanTradeIdeas;

internal sealed class GetScanTradeIdeasQueryHandler(
    IEnumerable<IMarketScanSource> sources,
    ScanTradeIdeasReader reader,
    IScanTradeSettingsRepository settings,
    IScanTradeRepository trades,
    ScanTradeOptions options) : IRequestHandler<GetScanTradeIdeasQuery, ScanTradeIdeasInternal>
{
    public async Task<ScanTradeIdeasInternal> Handle(GetScanTradeIdeasQuery query, CancellationToken cancellationToken)
    {
        sources.Find(query.Venue);
        var owner = new UserId(query.OwnerId);
        var venue = new VenueId(query.Venue);
        (ScanSnapshotRowInternal? row, DateTimeOffset? computedAt, IReadOnlyList<ScanTradeIdea> ideas) = await reader.Read(query.Venue, query.Symbol, cancellationToken);
        ScanTradeSettings? sizing = await settings.Get(owner, venue, cancellationToken);
        bool live = await trades.HasLive(owner, venue, query.Symbol, cancellationToken);

        return new ScanTradeIdeasInternal(
            query.Venue,
            query.Symbol,
            row?.Metrics.Price,
            computedAt,
            sizing is not null,
            live,
            [
                .. ideas.Select(idea =>
                {
                    (decimal Amount, decimal Risk)? size = sizing?.Size(idea.Entry, idea.Stop, options.FeeRate);

                    return new ScanTradeIdeaInternal(
                        idea.Scenario,
                        idea.Entry,
                        idea.Stop,
                        idea.Target,
                        idea.RewardRisk,
                        idea.EntryValidity,
                        size?.Amount,
                        size?.Risk);
                })
            ]);
    }
}