using MediatR;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.RejectBotStrategyVersion;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
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
    BotMarketDataAccess access,
    ISender sender)
{
    /// <summary>
    ///     The strategy's latest version when the bot does not run it yet and has not rejected it, else null.
    /// </summary>
    public async Task<StrategyVersionId?> Candidate(Bot bot, StrategyVersionId current, CancellationToken cancellationToken)
    {
        StrategyVersion latest = await strategies.Latest(bot, cancellationToken);

        return latest.Id == current || (await bots.Get(bot.Id, cancellationToken))?.RejectedVersionId == latest.Id ? null : latest.Id;
    }

    /// <summary>
    ///     Gives a stored run the bot's current values and allocation once, when the runner opens it after a start (ADR 0025).
    ///     Settings change only while the bot is stopped, so a difference from the run's applied values is the owner's change.
    ///     A refusal leaves the run as it was and is recorded like a refused strategy version (ADR 0019).
    /// </summary>
    public async Task ApplySettings(Bot bot, LeaseHolderId holder, long epoch, BotEngineSession session, CancellationToken cancellationToken)
    {
        if (session.Checkpoint is not { } checkpoint || session.LastCandle is not { } last || !Differs(bot, session, checkpoint))
        {
            return;
        }

        StrategyVersion version = (await strategies.GetVersions(bot.StrategyId, cancellationToken)).Single(candidate => candidate.Id == session.Version);
        string? refused;

        try
        {
            IReadOnlyDictionary<string, decimal> values = bot.Parameters.Values;
            BotParameterRules.Ensure(version.Definition, values);
            CompiledTradingGraph plan = compiler.Compile(version.Definition, values);
            Timeframe timeframe = bot.Series.Timeframe;
            IHistoricalCandleSource history = histories.Find(bot.Series.Venue) ?? throw new InvalidOperationException("The venue has no candle history.");
            IReadOnlyList<Candle> closed;

            using (MarketDataAccess.Use(await access.Read(bot, cancellationToken)))
            {
                closed = (await LiveHistory.Read(
                    history,
                    bot.Series.Symbol,
                    timeframe,
                    last.OpenTime,
                    last.OpenTime + timeframe.Duration,
                    plan.WarmupCandles + 1,
                    cancellationToken)).Candles;
            }

            refused = await session.ApplySettings(
                bot.Parameters,
                bot.AllocationCap,
                compiler.Lower(version.Definition, values),
                [.. closed.Where(candle => candle.OpenTime < last.OpenTime), last],
                cancellationToken);
        }
        catch (ApplicationValidationException exception)
        {
            refused = exception.Message;
        }

        if (refused is not null && !await sender.Send(new RejectBotStrategyVersionCommand(bot.Id.Value, holder.Value, epoch, session.Version.Value), cancellationToken))
        {
            throw new BotLeaseLostException();
        }
    }

    private static bool Differs(Bot bot, BotEngineSession session, GraphEngineCheckpoint checkpoint)
    {
        IReadOnlyDictionary<string, decimal> applied = session.AppliedParameters.Values;

        return checkpoint.Account.StartingCapital != bot.AllocationCap || applied.Count != bot.Parameters.Values.Count ||
            bot.Parameters.Values.Any(pair => !applied.TryGetValue(pair.Key, out decimal value) || value != pair.Value);
    }

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
            IReadOnlyList<Candle> closed;

            using (MarketDataAccess.Use(await access.Read(bot, cancellationToken)))
            {
                closed = (await LiveHistory.Read(
                    history,
                    bot.Series.Symbol,
                    timeframe,
                    last.OpenTime,
                    last.OpenTime + timeframe.Duration,
                    plan.WarmupCandles + 1,
                    cancellationToken)).Candles;
            }

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