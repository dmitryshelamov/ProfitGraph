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
///     Designer ADR 0057). The candidate takes the run's parameter values fitted to its parameters: a removed parameter's value
///     is dropped and a new parameter takes its default (ADR 0039). A candidate that does not take those values, does not
///     compile or that the engine refuses is rejected once, and the bot keeps its version.
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
    ///     The owner's values were checked against the strategy's latest version, so the run takes them on that version in one
    ///     root, fitted to its parameters (ADR 0039). A refusal leaves the run as it was and rejects that version once (ADR 0019).
    /// </summary>
    public async Task ApplySettings(Bot bot, LeaseHolderId holder, long epoch, BotEngineSession session, CancellationToken cancellationToken)
    {
        if (session.Checkpoint is not { } checkpoint || session.LastCandle is not { } last || !Differs(bot, session, checkpoint))
        {
            return;
        }

        StrategyVersion latest = await strategies.Latest(bot, cancellationToken);
        BotParameters values = bot.Parameters.Fit(latest.Definition.Parameters);
        string? refused;

        try
        {
            (CompiledTradingGraph plan, LoweredStrategyGraph lowered) = Prepare(latest, values);
            refused = await session.ApplySettings(
                latest.Id,
                values,
                latest.Definition.Parameters,
                bot.AllocationCap,
                lowered,
                await History(bot, last, plan, cancellationToken),
                cancellationToken);
        }
        catch (ApplicationValidationException exception)
        {
            refused = exception.Message;
        }

        await Reject(bot, holder, epoch, latest.Id, refused, cancellationToken);
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

        BotParameters values = session.AppliedParameters.Fit(latest.Definition.Parameters);
        string? refused;

        try
        {
            (CompiledTradingGraph plan, LoweredStrategyGraph lowered) = Prepare(latest, values);
            refused = await session.ChangeGraph(
                latest.Id,
                values,
                latest.Definition.Parameters,
                lowered,
                await History(bot, last, plan, cancellationToken),
                cancellationToken);
        }
        catch (ApplicationValidationException exception)
        {
            refused = exception.Message;
        }

        await Reject(bot, holder, epoch, latest.Id, refused, cancellationToken);
    }

    /// <summary>
    ///     Checks the values against the version, compiles and lowers it with them; a mismatch or a graph that does not compile throws.
    /// </summary>
    private (CompiledTradingGraph Plan, LoweredStrategyGraph Lowered) Prepare(StrategyVersion version, BotParameters values)
    {
        BotParameterRules.Ensure(version.Definition, values.Values);
        CompiledTradingGraph plan = compiler.Compile(version.Definition, values.Values);

        return (plan, compiler.Lower(version.Definition, values.Values));
    }

    /// <summary>
    ///     The closed history a switch warms on: the venue's candles before the run's last candle, then that candle itself.
    /// </summary>
    private async Task<IReadOnlyList<Candle>> History(Bot bot, Candle last, CompiledTradingGraph plan, CancellationToken cancellationToken)
    {
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

        return [.. closed.Where(candle => candle.OpenTime < last.OpenTime), last];
    }

    private async Task Reject(Bot bot, LeaseHolderId holder, long epoch, StrategyVersionId version, string? refused, CancellationToken cancellationToken)
    {
        if (refused is not null && !await sender.Send(new RejectBotStrategyVersionCommand(bot.Id.Value, holder.Value, epoch, version.Value), cancellationToken))
        {
            throw new BotLeaseLostException();
        }
    }
}