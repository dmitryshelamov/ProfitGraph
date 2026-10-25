using MediatR;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.RejectBotStrategyVersion;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Before each root of a running bot, switches its engine to the strategy's latest saved version (ADR 0019, Profit Fox
///     Designer ADR 0057). The candidate takes the run's parameter values; a candidate whose parameters no longer match,
///     that does not compile or that the engine refuses is rejected once, and the bot keeps its version.
/// </summary>
public sealed class BotGraphFollower(
    IStrategyRepository strategies,
    IBotRepository bots,
    VenuePorts<IHistoricalCandleSource> histories,
    BacktestCompiler compiler,
    ISender sender)
{
    public async Task Follow(Bot bot, LeaseHolderId holder, long epoch, BotEngineSession session, CancellationToken cancellationToken)
    {
        if (session.LastCandle is not { } last)
        {
            return;
        }

        StrategyVersion latest = await strategies.Latest(bot, cancellationToken);

        if (latest.Id == session.Version || (await bots.Get(bot.Id, cancellationToken))?.RejectedVersionId == latest.Id)
        {
            return;
        }

        string? refused;

        try
        {
            IReadOnlyDictionary<string, decimal> values = session.AppliedParameters.Values;
            BotParameterRules.Ensure(latest.Definition, values);
            CompiledTradingGraph plan = compiler.Compile(latest.Definition, values);
            Timeframe timeframe = bot.Series.Timeframe;
            IHistoricalCandleSource history = histories.Find(bot.Series.Venue) ?? throw new InvalidOperationException("The venue has no candle history.");
            DateTimeOffset from = last.OpenTime - timeframe.Duration * plan.WarmupCandles;
            IReadOnlyList<Candle> closed = await history.Get(bot.Series.Symbol, timeframe, from, last.OpenTime + timeframe.Duration, cancellationToken);
            refused = await session.ChangeGraph(
                latest.Id,
                compiler.Lower(latest.Definition, values),
                [.. closed.Where(candle => candle.OpenTime < last.OpenTime), last],
                cancellationToken);
        }
        catch (ApplicationValidationException exception)
        {
            refused = exception.Message;
        }

        if (refused is not null && !await sender.Send(new RejectBotStrategyVersionCommand(bot.Id.Value, holder.Value, epoch, latest.Id.Value), cancellationToken))
        {
            throw new BotLeaseLostException();
        }
    }
}