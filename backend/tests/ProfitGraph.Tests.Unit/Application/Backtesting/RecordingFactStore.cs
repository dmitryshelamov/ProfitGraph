using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Tests.Unit.Application.Backtesting;

/// <summary>
///     Keeps every appended batch; a test can slow appends down or make one fail.
/// </summary>
internal sealed class RecordingFactStore : IBacktestFactStore
{
    public List<BacktestFactBatch> Batches { get; } = [];

    public Func<int, Task>? BeforeAppend { get; init; }

    public async Task Append(BacktestRunId runId, BacktestFactBatch batch, CancellationToken cancellationToken)
    {
        if (BeforeAppend is not null)
        {
            await BeforeAppend(Batches.Count + 1);
        }

        Batches.Add(batch);
    }
}