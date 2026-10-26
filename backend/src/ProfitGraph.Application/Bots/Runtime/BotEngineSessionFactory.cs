using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
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
///     history ending at its last candle, warms on the candles missed since and resumes without <c>Start</c>. History, rules
///     and gaps are read with the bot's credential as market data access, and history reaches back over market closures
///     (ADR 0023, TC3).
/// </summary>
public sealed class BotEngineSessionFactory(
    IStrategyRepository strategies,
    IBotCheckpointRepository checkpoints,
    IOrderIntentRepository intents,
    VenuePorts<IInstrumentRulesSource> rules,
    VenuePorts<IHistoricalCandleSource> histories,
    BacktestCompiler compiler,
    BotRootCommitter committer,
    BotMarketDataAccess access,
    TimeProvider clock)
{
    public async Task<BotEngineSession> Open(Bot bot, LeaseHolderId holder, long epoch, IOrderGateway gateway, CancellationToken cancellationToken)
    {
        using IDisposable reads = MarketDataAccess.Use(await access.Read(bot, cancellationToken));

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
        ExecutionSettings settings = bot.FeeRates.SellerFees is { } sellerFees
            ? new ExecutionSettings(bot.AllocationCap, slippage: 0m, sellerFees)
            : new ExecutionSettings(bot.AllocationCap, slippage: 0m, bot.FeeRates.LimitRate, bot.FeeRates.MarketRate);
        Timeframe timeframe = bot.Series.Timeframe;
        DateTimeOffset to = timeframe.FloorToGrid(clock.GetUtcNow());
        DateTimeOffset through = stored?.Engine.LastClosedCandle.OpenTime ?? to - timeframe.Duration;
        IHistoricalCandleSource source = Port(histories, bot);
        HistoricalCandlesInternal history = through < to
            ? await LiveHistory.Read(source, bot.Series.Symbol, timeframe, through, to, plan.WarmupCandles + 1, cancellationToken)
            : HistoricalCandlesInternal.Contiguous([]);
        IReadOnlyList<Candle> closed = history.Candles;
        IReadOnlyList<MarketClosure> closures = LiveHistory.Closures(history);
        Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<HistoricalCandlesInternal>> gaps = async (gapFrom, gapTo, token) =>
        {
            using IDisposable gapReads = MarketDataAccess.Use(await access.Read(bot, token));

            return await source.Get(bot.Series.Symbol, timeframe, gapFrom, gapTo, token);
        };

        if (stored is null)
        {
            // A run that starts over after a forgotten one continues its order identifiers: its venue keeps the old ones.
            var account = new ExternallyExecutedAccount(settings, instrument);
            IReadOnlyList<OrderIntent> earlier = await intents.ListByBot(bot.Id, cancellationToken);

            if (earlier.Count > 0)
            {
                account.ContinueOrderIdsAfter(earlier.Max(intent => intent.EngineOrderId));
            }

            var dispatcher = new CausalDispatcher(plan, account, graphVersionId: versionId, closures: closures);
            var session = new BotEngineSession(bot.Id, holder, epoch, timeframe, dispatcher, gateway, committer, clock, applied, version.Id, checkpoint: null, gaps);
            await session.StartFromHistory(closed, cancellationToken);

            return session;
        }

        Candle[] restore = [.. closed.Where(candle => candle.OpenTime <= stored.Engine.LastClosedCandle.OpenTime)];

        if (restore.Length == 0 || restore[^1] != stored.Engine.LastClosedCandle)
        {
            throw new InvalidOperationException("Closed history cannot restore the bot's checkpoint.");
        }

        CausalDispatcher restored = CausalDispatcher.RestoreWithoutResume(stored.Engine, plan, settings, instrument, restore, closures: closures);
        var resumed = new BotEngineSession(bot.Id, holder, epoch, timeframe, restored, gateway, committer, clock, applied, version.Id, stored.Engine, gaps);
        await resumed.ResumeAndWarm([.. closed.Where(candle => candle.OpenTime > stored.Engine.LastClosedCandle.OpenTime)], cancellationToken);
        await ApplyStoredRejections(bot.Id, resumed, cancellationToken);

        return resumed;
    }

    /// <summary>
    ///     A rejection stored after the last checkpoint, which the previous runner did not apply, reaches the engine now.
    /// </summary>
    private async Task ApplyStoredRejections(BotId botId, BotEngineSession session, CancellationToken cancellationToken)
    {
        foreach (OrderIntent intent in await intents.ListByBot(botId, cancellationToken))
        {
            if (intent is { Status: OrderIntentStatus.Rejected, Rejection: { } rejection } &&
                session.Checkpoint!.Account.Orders.Any(order => order.Id == intent.EngineOrderId && order.Status == OrderStatus.PendingSubmit))
            {
                await session.ProcessExternalUpdate(ExternalOrderUpdate.Reject(intent.EngineOrderId, rejection.Reason, rejection.Message), cancellationToken);
            }
        }
    }

    private static TPort Port<TPort>(VenuePorts<TPort> ports, Bot bot)
        where TPort : IVenuePort
    {
        return ports.Find(bot.Series.Venue) ?? throw new InvalidOperationException($"The venue has no {typeof(TPort).Name}.");
    }
}