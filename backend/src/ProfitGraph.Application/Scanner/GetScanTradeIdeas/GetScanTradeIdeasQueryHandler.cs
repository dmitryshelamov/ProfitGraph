using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Scanning;

using ScanTrade = ProfitGraph.Domain.Scanner.Trades.ScanTrade;

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
        (decimal? price, IReadOnlyList<(string Timeframe, ScanTradeIdea Idea)> ideas) = await reader.Read(query.Venue, query.Symbol, cancellationToken);
        ScanTradeSettings? sizing = await settings.Get(owner, venue, cancellationToken);
        ScanTrade[] live = [.. (await trades.List(owner, venue, query.Symbol, options.ListLimit, cancellationToken)).Where(trade => trade.IsLive)];

        return new ScanTradeIdeasInternal(
            query.Venue,
            query.Symbol,
            price,
            sizing is not null,
            [
                .. ideas.Select(item =>
                {
                    ScanTradeIdea idea = item.Idea;
                    (decimal Amount, decimal Risk)? size = sizing?.Size(idea.Entry, idea.Stop, options.FeeRate);

                    return new ScanTradeIdeaInternal(
                        item.Timeframe,
                        idea.Scenario,
                        idea.Entry,
                        idea.Stop,
                        idea.Target,
                        idea.RewardRisk,
                        idea.EntryValidity,
                        size?.Amount,
                        size?.Risk,
                        live.Any(trade => trade.Takes(idea.Scenario, idea.Entry, idea.Stop, idea.Target)),
                        idea.Direction);
                })
            ]);
    }
}