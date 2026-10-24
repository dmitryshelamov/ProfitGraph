using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Contracts;

public sealed record OptimizationWindowsInternal(IReadOnlyList<WalkForwardWindowResult> Windows);