using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Tests.Unit.Application.Backtesting;

/// <summary>
///     Keeps every appended batch; a test can slow appends down, make one fail, or cancel the replay when a batch arrives.
/// </summary>
internal sealed class RecordingFactStore : IBacktestFactStore, IDisposable
{
    private readonly CancellationTokenSource _replay = new CancellationTokenSource();

    public List<BacktestFactBatch> Batches { get; } = [];

    public Func<int, Task>? BeforeAppend { get; init; }

    /// <summary>
    ///     The number of the batch whose arrival cancels <see cref="ReplayToken" />.
    /// </summary>
    public int? CancelReplayAtBatch { get; init; }

    public CancellationToken ReplayToken => _replay.Token;

    public void Dispose()
    {
        _replay.Dispose();
    }

    public async Task Append(BacktestRunId runId, BacktestFactBatch batch, CancellationToken cancellationToken)
    {
        int number = Batches.Count + 1;

        if (number == CancelReplayAtBatch)
        {
            await _replay.CancelAsync();
        }

        if (BeforeAppend is not null)
        {
            await BeforeAppend(number);
        }

        Batches.Add(batch);
    }
}