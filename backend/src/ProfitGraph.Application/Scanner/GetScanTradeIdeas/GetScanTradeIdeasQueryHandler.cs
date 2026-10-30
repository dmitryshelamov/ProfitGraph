using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Scanning;

using ScanTrade = ProfitGraph.Domain.Scanner.Trades.ScanTrade;

namespace ProfitGraph.Application.Scanner.GetScanTradeIdeas;

/// <summary>
///     Sizes every proposal with the costs of the pair's trades (ADR 0065) and leaves out those whose stop is nearer the entry than what
///     a stopped trade pays, counting them. Each proposal takes its odds from the owner's latest preset backtest on the venue that
///     followed cards (ADR 0071, section 1), banded by the pair's NATR in the snapshot.
/// </summary>
internal sealed class GetScanTradeIdeasQueryHandler(
    IEnumerable<IMarketScanSource> sources,
    ScanTradeIdeasReader reader,
    ScanTradeCostsReader costs,
    IScanSnapshotRepository snapshots,
    IScanTradeSettingsRepository settings,
    IScanTradeRepository trades,
    IScanBacktestReader backtests,
    ScanTradeOptions options) : IRequestHandler<GetScanTradeIdeasQuery, ScanTradeIdeasInternal>
{
    public async Task<ScanTradeIdeasInternal> Handle(GetScanTradeIdeasQuery query, CancellationToken cancellationToken)
    {
        sources.Find(query.Venue);
        var owner = new UserId(query.OwnerId);
        var venue = new VenueId(query.Venue);
        (decimal? price, IReadOnlyList<(string Timeframe, ScanTradeIdea Idea)> ideas) = await reader.Read(query.Venue, query.Symbol, cancellationToken);
        ScanTradeSettings? sizing = await settings.Get(owner, venue, cancellationToken);
        ScanSnapshotInternal? snapshot = await snapshots.Get(query.Venue, cancellationToken);
        ScanSnapshotRowInternal? row = snapshot?.Rows.FirstOrDefault(item => item.Symbol == query.Symbol);
        decimal? spread = row?.Metrics.Spread;
        ScanTradeCostsInternal paid = await costs.Read(sizing, query.Venue, query.Symbol, spread, cancellationToken);
        ScanTrade[] live = [.. (await trades.List(owner, venue, query.Symbol, options.ListLimit, cancellationToken)).Where(trade => trade.IsLive)];
        (string Timeframe, ScanTradeIdea Idea)[] kept = [.. ideas.Where(item => !paid.Costs.TooTight(item.Idea.Entry, item.Idea.Stop))];
        ScanCardOddsSourceInternal? source = await backtests.LatestCardOdds(query.OwnerId, query.Venue, cancellationToken);

        return new ScanTradeIdeasInternal(
            query.Venue,
            query.Symbol,
            price,
            sizing is not null,
            [
                .. kept.Select(item =>
                {
                    ScanTradeIdea idea = item.Idea;
                    (decimal Amount, decimal Risk)? size = sizing?.Size(idea.Entry, idea.Stop, paid.Costs);

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
                        idea.Direction,
                        size is { } sized ? paid.Costs.Profit(sized.Amount, idea.Entry, idea.Target) : null,
                        source is null
                            ? null
                            : ScanCardChance.Of(source.Odds, item.Timeframe, idea, row?.Metrics.Natr24h, paid.Costs, options.MinOddsTrades));
                })
            ],
            paid,
            ideas.Count - kept.Length,
            source);
    }
}