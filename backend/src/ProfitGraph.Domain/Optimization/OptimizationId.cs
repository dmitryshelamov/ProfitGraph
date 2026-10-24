using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     Identifies an optimization; it is the identifier of the background job that executes it.
/// </summary>
public readonly record struct OptimizationId
{
    public Guid Value { get; }

    public OptimizationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An optimization identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public OptimizationId(BackgroundJobId job)
        : this(job.Value)
    {
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}