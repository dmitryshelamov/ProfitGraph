using MediatR;

using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.FollowScanPaperTrades;

/// <summary>
///     A rule whose paper trades closed today lost its daily limit pauses until the next UTC day.
/// </summary>
internal sealed class FollowScanPaperTradesCommandHandler(IScanPaperTradeRepository paperTrades, IScanAutoTradeRuleRepository rules, TimeProvider clock)
    : IRequestHandler<FollowScanPaperTradesCommand>
{
    public async Task Handle(FollowScanPaperTradesCommand command, CancellationToken cancellationToken)
    {
        HashSet<ScanAutoTradeRuleId> closed = [];

        foreach (ScanPaperTrade trade in await paperTrades.ListOpenForUpdate(new VenueId(command.Venue), cancellationToken))
        {
            if (!command.Minutes.TryGetValue(trade.Symbol, out IReadOnlyList<ScanCandle>? minutes))
            {
                continue;
            }

            if (trade.Advance(minutes) && trade.Pnl < 0m)
            {
                closed.Add(trade.RuleId);
            }

            await paperTrades.Update(trade, cancellationToken);
        }

        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset dayStart = ScanAutoTradeTally.DayStart(now);

        foreach (ScanAutoTradeRuleId ruleId in closed)
        {
            if (await rules.GetForUpdate(ruleId, cancellationToken) is not { } rule)
            {
                continue;
            }

            ScanAutoTradeTally tally = ScanAutoTradeTally.Of(await paperTrades.ListRecent(ruleId, dayStart, cancellationToken), dayStart);

            if (tally.Loss >= rule.Limits.DailyLossLimit)
            {
                rule.Pause(DateOnly.FromDateTime(dayStart.UtcDateTime), now);
                await rules.Update(rule, cancellationToken);
            }
        }
    }
}