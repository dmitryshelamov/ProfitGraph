using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Contracts;

public sealed record WalkForwardTrialResultInternal(int WindowIndex, OptimizationTrialResult Result);