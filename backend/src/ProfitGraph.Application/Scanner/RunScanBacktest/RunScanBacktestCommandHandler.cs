using System.Collections.Concurrent;

using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.RunScanBacktest;

/// <summary>
///     Runs a scanner backtest (ADR 0059, section 6): for every pair of the venue's universe it reads the minutes of the period and
///     of the eight days before it (the longest metric window) through the scanner's request budget, replays them and collects the
///     pairs' signals, eight pairs at a time, since each request waits for the venue far longer than the budget asks. A pair whose
///     history cannot be read is listed as failed; a request pause after the venue's ban fails the job. Conditions on the spread are
///     not checked, since history has no quotes.
/// </summary>
internal sealed class RunScanBacktestCommandHandler(
    IEnumerable<IMarketScanSource> sources,
    IScanBacktestRepository backtests,
    ITransactionExecutor transactions,
    IBackgroundJobProgress progress,
    TimeProvider clock) : IRequestHandler<RunScanBacktestCommand, ScanBacktestCompletedInternal>
{
    private const int ParallelPairs = 8;
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
        IReadOnlyList<ScanInstrumentInternal> universe = await source.Universe(cancellationToken);
        var signals = new ConcurrentBag<ScanSignal>();
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
                    foreach (ScanSignal signal in await Replay(source, backtest, instrument, token))
                    {
                        signals.Add(signal);
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
            ScanStudy.Summarize(all),
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

    private static async Task<IReadOnlyList<ScanSignal>> Replay(
        IMarketScanSource source,
        ScanBacktest backtest,
        ScanInstrumentInternal instrument,
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
            return [];
        }

        var replay = new ScanReplay(minutes, instrument.ListedAt);

        return ScanStudy.Signals(
            instrument.Symbol,
            replay,
            metrics => backtest.Filter.Keeps(metrics, Unknown),
            backtest.From,
            backtest.To,
            ScanAlertWatch.Cooldown);
    }
}