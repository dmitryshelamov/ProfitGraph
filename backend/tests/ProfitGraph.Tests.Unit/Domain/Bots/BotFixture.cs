using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     Builds bots in a known state for transition tests.
/// </summary>
internal static class BotFixture
{
    internal static readonly DateTimeOffset Created = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    internal static readonly StrategyVersionId FirstVersion = StrategyVersionId.New();

    internal static Bot Stopped()
    {
        return Bot.Create(
            BotId.New(),
            UserId.New(),
            StrategyId.New(),
            FirstVersion,
            ExchangeCredentialId.New(),
            new CandleSeries(new VenueId("bybit"), new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h")),
            new BotParameters(new Dictionary<string, decimal> { ["drop"] = 0.02m }),
            allocationCap: 100m,
            new BotRiskLimits(maxOrderQuote: 20m, dailyLossLimitQuote: 10m),
            new BotFeeRates(limitRate: 0.001m, marketRate: 0.001m, Created),
            Created);
    }

    internal static Bot Started()
    {
        Bot bot = Stopped();
        bot.Start(FirstVersion, hasCheckpoint: false, tradingHalted: false, Created);
        bot.ClearDomainEvents();

        return bot;
    }

    internal static Bot Running()
    {
        Bot bot = Started();
        bot.RecordLeaseAcquired(Created);
        bot.MarkStarting(Created);
        bot.MarkRunning(Created);
        bot.ClearDomainEvents();

        return bot;
    }

    internal static Bot FailedOnce()
    {
        Bot bot = Running();
        bot.RecordRuntimeFailure(Created);
        bot.ClearDomainEvents();

        return bot;
    }

    internal static Bot DegradedBy(BotStatusReason reason)
    {
        Bot bot = Running();
        bot.Degrade(reason, Created);
        bot.ClearDomainEvents();

        return bot;
    }
}