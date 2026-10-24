using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Opens the engine of a leased bot (Profit Fox Designer ADR 0054): a first run compiles its strategy version with
///     the bot's values, warms on public closed history and starts once; a stored run restores its checkpoint on the
///     history ending at its last candle, warms on the candles missed since and resumes without <c>Start</c>.
/// </summary>
public sealed class BotEngineSessionFactory(
    IStrategyRepository strategies,
    IBotCheckpointRepository checkpoints,
    VenuePorts<IInstrumentRulesSource> rules,
    VenuePorts<IHistoricalCandleSource> histories,
    BacktestCompiler compiler,
    BotRootCommitter committer,
    TimeProvider clock)
{
    public async Task<BotEngineSession> Open(Bot bot, LeaseHolderId holder, long epoch, IOrderGateway gateway, CancellationToken cancellationToken)
    {
        BotCheckpoint? stored = await checkpoints.Get(bot.Id, cancellationToken);

        if (stored is null && bot.HasRun)
        {
            throw new InvalidOperationException("A bot that has run cannot recreate its missing checkpoint.");
        }

        BotParameters applied = stored?.AppliedParameters ?? bot.Parameters;
        IReadOnlyList<StrategyVersion> versions = await strategies.GetVersions(bot.StrategyId, cancellationToken);
        string versionId = stored?.Engine.GraphVersionId ?? bot.StrategyVersionId.Value.ToString("D");
        StrategyVersion version = versions.SingleOrDefault(candidate => candidate.Id.Value.ToString("D") == versionId)
            ?? throw new InvalidOperationException("The bot's strategy version does not exist.");
        CompiledTradingGraph plan = compiler.Compile(version.Definition, applied.Values);
        InstrumentRules instrument = (await Port(rules, bot).Get(bot.Series.Symbol, cancellationToken)).Rules;
        var settings = new ExecutionSettings(bot.AllocationCap, slippage: 0m, bot.FeeRates.LimitRate, bot.FeeRates.MarketRate);
        Timeframe timeframe = bot.Series.Timeframe;
        DateTimeOffset to = timeframe.FloorToGrid(clock.GetUtcNow());
        DateTimeOffset from = stored is null
            ? to - timeframe.Duration * Math.Max(plan.WarmupCandles + 1, val2: 1)
            : stored.Engine.LastClosedCandle.OpenTime - timeframe.Duration * plan.WarmupCandles;
        IReadOnlyList<Candle> closed = from < to ? await Port(histories, bot).Get(bot.Series.Symbol, timeframe, from, to, cancellationToken) : [];

        if (stored is null)
        {
            var dispatcher = new CausalDispatcher(plan, new ExternallyExecutedAccount(settings, instrument), graphVersionId: versionId);
            var session = new BotEngineSession(bot.Id, holder, epoch, timeframe, dispatcher, gateway, committer, clock, applied, version.Id, checkpoint: null);
            await session.StartFromHistory(closed, cancellationToken);

            return session;
        }

        Candle[] restore = [.. closed.Where(candle => candle.OpenTime <= stored.Engine.LastClosedCandle.OpenTime)];

        if (restore.Length == 0 || restore[^1] != stored.Engine.LastClosedCandle)
        {
            throw new InvalidOperationException("Closed history cannot restore the bot's checkpoint.");
        }

        CausalDispatcher restored = CausalDispatcher.RestoreWithoutResume(stored.Engine, plan, settings, instrument, restore);
        var resumed = new BotEngineSession(bot.Id, holder, epoch, timeframe, restored, gateway, committer, clock, applied, version.Id, stored.Engine);
        await resumed.ResumeAndWarm([.. closed.Where(candle => candle.OpenTime > stored.Engine.LastClosedCandle.OpenTime)], cancellationToken);

        return resumed;
    }

    private static TPort Port<TPort>(VenuePorts<TPort> ports, Bot bot)
        where TPort : IVenuePort
    {
        return ports.Find(bot.Series.Venue) ?? throw new InvalidOperationException($"The venue has no {typeof(TPort).Name}.");
    }
}