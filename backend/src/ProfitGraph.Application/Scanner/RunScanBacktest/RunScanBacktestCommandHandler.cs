using System.Collections.Concurrent;

using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.RunScanBacktest;

/// <summary>
///     Runs a scanner backtest (ADR 0059, section 6): for every pair of the venue's universe it reads the minutes of the period and
///     of the eight days before it (the longest metric window) through the scanner's request budget, replays them and collects the
///     pairs' signals, eight pairs at a time, since each request waits for the venue far longer than the budget asks. A pair whose
///     history cannot be read is listed as failed; a request pause after the venue's ban fails the job. The trend of the venue's
///     reference pair is replayed first and given to every pair's minutes, as a live snapshot gives it to every row (ADR 0071). Conditions on the spread are
///     not checked, since history has no quotes. On a venue that allows shorts the short scenarios run too (ADR 0061). At every
///     signal the cards of the pair's page are followed by the bot's rules and summed up by kind and band (ADR 0071, section 2): their
///     fees are the owner's for the venue as the cards read them, once per backtest, and their spread the pair's in the current
///     snapshot, or the snapshot's median spread without one.
/// </summary>
internal sealed class RunScanBacktestCommandHandler(
    IEnumerable<IMarketScanSource> sources,
    VenueRegistry venues,
    IScanBacktestRepository backtests,
    IScanTradeSettingsRepository settings,
    IScanSnapshotRepository snapshots,
    ScanTradeCostsReader costs,
    ScanTradeOptions options,
    ITransactionExecutor transactions,
    IBackgroundJobProgress progress,
    TimeProvider clock) : IRequestHandler<RunScanBacktestCommand, ScanBacktestCompletedInternal>
{
    private const int ParallelPairs = 8;
    private const decimal Percent = 100m;
    private static readonly TimeSpan Warmup = TimeSpan.FromDays(8);
    private static readonly Timeframe MinuteFrame = Timeframe.Parse("1m");
    private static readonly IReadOnlySet<ScanMetric> Unknown = new HashSet<ScanMetric> { ScanMetric.Spread };

    public async Task<ScanBacktestCompletedInternal> Handle(RunScanBacktestCommand command, CancellationToken cancellationToken)
    {
        var id = new ScanBacktestId(command.BacktestId);
        ScanBacktest backtest = await transactions.ExecuteAsync(() => backtests.GetForUpdate(id, cancellationToken), cancellationToken) ??
            throw new ResourceNotFoundException("The scanner backtest does not exist.", ScanBacktestErrorCodes.NotFound);

        if (backtest.Result is { } done)
        {
            return new ScanBacktestCompletedInternal(command.BacktestId, done.Summary.Signals);
        }

        IMarketScanSource source = sources.Find(backtest.Venue.Value);
        bool shorts = venues.Find(backtest.Venue.Value) is { SupportsShort: true };
        IReadOnlyList<ScanInstrumentInternal> universe = await source.Universe(cancellationToken);
        ScanCardSetup setup = Setup(backtest.Venue.Value, shorts);
        Func<string, ScanTradeCosts> costsOf = await Costs(backtest, universe, cancellationToken);
        Func<DateTimeOffset, decimal?>? market = await MarketTrend(source, backtest, universe, cancellationToken);
        var signals = new ConcurrentBag<ScanSignal>();
        var cards = new ConcurrentBag<ScanCardTrade>();
        var failed = new ConcurrentBag<string>();
        var completed = 0;
        await progress.Report(completed: 0, universe.Count, cancellationToken);

        await Parallel.ForEachAsync(
            universe,
            new ParallelOptions { MaxDegreeOfParallelism = ParallelPairs, CancellationToken = cancellationToken },
            async (instrument, token) =>
            {
                try
                {
                    (IReadOnlyList<ScanSignal> found, IReadOnlyList<ScanCardTrade> followed) =
                        await Replay(source, backtest, instrument, setup, costsOf(instrument.Symbol), market, token);

                    foreach (ScanSignal signal in found)
                    {
                        signals.Add(signal);
                    }

                    foreach (ScanCardTrade card in followed)
                    {
                        cards.Add(card);
                    }
                }
                catch (Exception exception) when (exception is MarketScanException { Code: not MarketScanErrorCodes.RequestsPaused } or ScanInputException)
                {
                    failed.Add(instrument.Symbol);
                }

                await progress.Report(Interlocked.Increment(ref completed), universe.Count, token);
            });

        ScanSignal[] all = [.. signals.OrderByDescending(signal => signal.At).ThenBy(signal => signal.Symbol, StringComparer.Ordinal)];
        var result = new ScanBacktestResult(
            universe.Count,
            [.. failed.Order(StringComparer.Ordinal)],
            ScanStudy.Summarize(all, shorts, ScanStudy.Odds(cards)),
            [.. all.Take(ScanBacktest.MaxStoredSignals)],
            all.Length > ScanBacktest.MaxStoredSignals);

        await transactions.ExecuteAsync(
            async () =>
            {
                ScanBacktest current = await backtests.GetForUpdate(id, cancellationToken) ??
                    throw new ResourceNotFoundException("The scanner backtest does not exist.", ScanBacktestErrorCodes.NotFound);

                if (current.Complete(result, clock.GetUtcNow()))
                {
                    await backtests.Update(current, cancellationToken);
                }

                return true;
            },
            cancellationToken);

        return new ScanBacktestCompletedInternal(command.BacktestId, all.Length);
    }

    private ScanCardSetup Setup(string venue, bool shorts)
    {
        HashSet<string> offered = [.. venues.Find(venue)?.Timeframes.Select(timeframe => timeframe.Key) ?? []];

        return new ScanCardSetup(
            [.. options.LevelTimeframes.Where(offered.Contains).Select(Timeframe.Parse)],
            offered.Contains(ScanTradeIdeas.PullbackTimeframe),
            offered.Contains(ScanTradeIdeas.ExtremeTimeframe) && offered.Contains(ScanTradeIdeas.PullbackTimeframe),
            shorts);
    }

    /// <summary>
    ///     The costs of each pair's cards: the owner's fee rates read once with the universe's first pair, as a card reads them, and the
    ///     pair's spread in the current snapshot or the snapshot's median spread.
    /// </summary>
    private async Task<Func<string, ScanTradeCosts>> Costs(
        ScanBacktest backtest,
        IReadOnlyList<ScanInstrumentInternal> universe,
        CancellationToken cancellationToken)
    {
        ScanTradeSettings? sizing = await settings.Get(backtest.OwnerId, backtest.Venue, cancellationToken);
        ScanTradeCosts fees = universe.Count == 0
            ? ScanTradeCosts.Flat(options.FeeRate)
            : (await costs.Read(sizing, backtest.Venue.Value, universe[0].Symbol, spreadPercent: null, cancellationToken)).Costs;
        ScanSnapshotInternal? snapshot = await snapshots.Get(backtest.Venue.Value, cancellationToken);
        Dictionary<string, decimal> spreads = snapshot?.Rows
            .Where(row => row.Metrics.Spread is >= 0m)
            .ToDictionary(row => row.Symbol, row => row.Metrics.Spread!.Value / Percent, StringComparer.Ordinal) ?? [];
        decimal[] ordered = [.. spreads.Values.Order()];
        decimal median = ordered.Length == 0 ? 0m :
            ordered.Length % 2 == 1 ? ordered[ordered.Length / 2] : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2m;

        return symbol => new ScanTradeCosts(fees.LimitFeeRate, fees.MarketFeeRate, Math.Min(spreads.GetValueOrDefault(symbol, median), val2: 0.5m));
    }

    /// <summary>
    ///     The trend of the venue's reference pair at the end of each minute of its history (ADR 0071, section 3), as every row of a live
    ///     snapshot carries it; null when the venue has no reference pair in its universe or its history cannot be read.
    /// </summary>
    private static async Task<Func<DateTimeOffset, decimal?>?> MarketTrend(
        IMarketScanSource source,
        ScanBacktest backtest,
        IReadOnlyList<ScanInstrumentInternal> universe,
        CancellationToken cancellationToken)
    {
        if (source.ReferenceSymbol is not { } reference || universe.FirstOrDefault(instrument => instrument.Symbol == reference) is not { } instrument)
        {
            return null;
        }

        List<ScanCandle> minutes;

        try
        {
            minutes = ScanHistory.Contiguous(await source.Candles(new InstrumentSymbol(reference), MinuteFrame, backtest.From - Warmup, backtest.To, cancellationToken));
        }
        catch (MarketScanException exception) when (exception.Code != MarketScanErrorCodes.RequestsPaused)
        {
            return null;
        }

        if (minutes.Count == 0)
        {
            return null;
        }

        var replay = new ScanReplay(minutes, instrument.ListedAt);
        Dictionary<DateTimeOffset, decimal?> trends = Enumerable.Range(start: 0, replay.Count).ToDictionary(replay.EndOf, index => replay.At(index).TrendEma50);

        return at => trends.GetValueOrDefault(at);
    }

    private static async Task<(IReadOnlyList<ScanSignal> Signals, IReadOnlyList<ScanCardTrade> Cards)> Replay(
        IMarketScanSource source,
        ScanBacktest backtest,
        ScanInstrumentInternal instrument,
        ScanCardSetup setup,
        ScanTradeCosts costs,
        Func<DateTimeOffset, decimal?>? market,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScanCandle> candles = await source.Candles(
            new InstrumentSymbol(instrument.Symbol),
            MinuteFrame,
            backtest.From - Warmup,
            backtest.To,
            cancellationToken);
        List<ScanCandle> minutes = ScanHistory.Contiguous(candles);

        if (minutes.Count == 0)
        {
            return ([], []);
        }

        var replay = new ScanReplay(minutes, instrument.ListedAt);
        IReadOnlyList<ScanSignal> signals = ScanStudy.Signals(
            instrument.Symbol,
            replay,
            metrics => backtest.Filter.Keeps(metrics, Unknown),
            backtest.From,
            backtest.To,
            ScanAlertWatch.Cooldown,
            setup.Shorts,
            market);

        return (signals, ScanStudy.Cards(instrument.Symbol, replay, signals, setup, costs));
    }
}