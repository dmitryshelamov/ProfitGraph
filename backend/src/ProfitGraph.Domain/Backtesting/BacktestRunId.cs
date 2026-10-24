using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     Identifies a backtest run; it is the identifier of the background job that executes the run.
/// </summary>
public readonly record struct BacktestRunId
{
    public Guid Value { get; }

    public BacktestRunId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A backtest run identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public BacktestRunId(BackgroundJobId job)
        : this(job.Value)
    {
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}