using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.GetOptimizationWindows;

/// <summary>
///     The finished walk-forward windows of an optimization, in order; empty for a search without walk-forward.
/// </summary>
public sealed record GetOptimizationWindowsQuery(Guid UserId, Guid Id) : IQuery<OptimizationWindowsInternal>;