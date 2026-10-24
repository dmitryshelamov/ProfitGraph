using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

internal sealed class WalkForwardSettingsDb
{
    public int Windows { get; init; }
    public int TrainToTestRatio { get; init; }
    public WalkForwardScheme Scheme { get; init; }
}