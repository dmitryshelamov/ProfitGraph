using ProfitGraph.Domain.Optimization.WalkForward;

namespace ProfitGraph.Application.Optimization.Contracts;

public sealed record OptimizationWindowsInternal(IReadOnlyList<WalkForwardWindowResult> Windows);