using System.Threading.Channels;

using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Execution;

/// <summary>
///     Stores what a replay reports while it runs (ADR 0012): the engine writes into a bounded channel and waits when it
///     is full; one consumer commits the records in batches, each in its own short transaction, and reports progress
///     after each commit. Cancellation of the replay never drops accepted records: <see cref="Complete" /> stores them all.
/// </summary>
/// <remarks>
///     While the replay runs the consumer is the only user of the job's database connection.
/// </remarks>
public sealed class BacktestReplaySession : IBacktestObserver
{
    private readonly BacktestRunId _runId;
    private readonly IBacktestFactStore _store;
    private readonly ITransactionExecutor _transactions;
    private readonly IBackgroundJobProgress _progress;
    private readonly int _batchSize;
    private readonly int _total;
    private readonly Channel<BacktestReplayRecord> _channel;
    private readonly Task _consumer;
    private MetricsAccumulator _metrics;
    private long _ledgerThrough;
    private long _diagnosticThrough;
    private int _executions;
    private bool _completed;

    public BacktestReplaySession(
        BacktestRunId runId,
        IBacktestFactStore store,
        ITransactionExecutor transactions,
        IBackgroundJobProgress progress,
        BacktestExecutionOptions options,
        decimal startingCapital,
        SampleRanges ranges)
    {
        _runId = runId;
        _store = store;
        _transactions = transactions;
        _progress = progress;
        _batchSize = options.BatchSize;
        _total = ranges.TotalBars;
        _metrics = new MetricsAccumulator(startingCapital, ranges);
        _channel = Channel.CreateBounded<BacktestReplayRecord>(
            new BoundedChannelOptions(options.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        _consumer = Task.Run(Consume);
    }

    public void OnLedger(BacktestLedgerEntry entry)
    {
        Write(new BacktestReplayRecord(Ledger: entry));
    }

    public void OnCandleProcessed(CandleSnapshot snapshot)
    {
        Write(new BacktestReplayRecord(Candle: snapshot));
    }

    public void OnDiagnostic(BacktestDiagnostic diagnostic)
    {
        Write(new BacktestReplayRecord(Diagnostic: diagnostic));
    }

    public void OnTerminal(BacktestTerminalSnapshot terminal)
    {
        // The terminal summary is stored on the run after the replay returns.
    }

    /// <summary>
    ///     Waits until every accepted record is stored and returns the metrics of the stored candles, also storing the
    ///     metrics' own diagnostics. A storage failure is <see cref="BacktestErrorCodes.PersistenceFailed" />.
    /// </summary>
    public async Task<BacktestRunMetrics?> Complete()
    {
        if (_completed)
        {
            throw new InvalidOperationException("A replay session completes once.");
        }

        _completed = true;
        _channel.Writer.TryComplete();

        try
        {
            await _consumer;

            if (_metrics.ObservedCandles == 0)
            {
                return null;
            }

            BacktestMetricsSnapshot snapshot = _metrics.Snapshot;

            if (snapshot.Diagnostics.Count > 0)
            {
                await Store(new BacktestFactBatch([], [], [.. snapshot.Diagnostics.Select(Number)]));
            }

            return new BacktestRunMetrics(snapshot.Full, snapshot.InSample, snapshot.OutOfSample);
        }
        catch (Exception exception) when (exception is not BacktestException)
        {
            throw new BacktestException(BacktestErrorCodes.PersistenceFailed, $"Storing the run's results failed: {exception.Message}");
        }
    }

    private void Write(BacktestReplayRecord record)
    {
        // The replay runs on its own thread, so waiting for room in the channel blocks only the replay.
        _channel.Writer.WriteAsync(record, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    private async Task Consume()
    {
        try
        {
            var records = new List<BacktestReplayRecord>(_batchSize);

            while (await _channel.Reader.WaitToReadAsync(CancellationToken.None))
            {
                records.Clear();

                while (records.Count < _batchSize && _channel.Reader.TryRead(out BacktestReplayRecord? record))
                {
                    records.Add(record);
                }

                BacktestFactBatch batch = Build(records);
                await Store(batch);

                if (batch.Candles.Count > 0)
                {
                    await _progress.Report(batch.Candles[^1].Snapshot.Sequence, _total, CancellationToken.None);
                }
            }
        }
        catch (Exception exception)
        {
            // The replay sees the closed channel at its next record and stops.
            _channel.Writer.TryComplete(exception);

            throw;
        }
    }

    private BacktestFactBatch Build(IReadOnlyList<BacktestReplayRecord> records)
    {
        var ledger = new List<BacktestLedgerEntry>();
        var candles = new List<BacktestCandleObservation>();
        var diagnostics = new List<BacktestDiagnosticRecord>();

        foreach (BacktestReplayRecord record in records)
        {
            if (record.Ledger is { } entry)
            {
                ledger.Add(entry);
                _ledgerThrough = entry.Sequence;

                if (entry.Fill is not null)
                {
                    _executions++;
                }
            }
            else if (record.Diagnostic is { } diagnostic)
            {
                diagnostics.Add(Number(diagnostic));
            }
            else if (record.Candle is { } candle)
            {
                _metrics = _metrics.Advance(candle, _executions);
                _executions = 0;
                candles.Add(new BacktestCandleObservation(candle, _ledgerThrough, _diagnosticThrough));
            }
        }

        return new BacktestFactBatch(ledger, candles, diagnostics);
    }

    private BacktestDiagnosticRecord Number(BacktestDiagnostic diagnostic)
    {
        return new BacktestDiagnosticRecord(++_diagnosticThrough, diagnostic);
    }

    private Task Store(BacktestFactBatch batch)
    {
        return _transactions.ExecuteAsync(
            async () =>
            {
                await _store.Append(_runId, batch, CancellationToken.None);

                return true;
            },
            CancellationToken.None);
    }
}