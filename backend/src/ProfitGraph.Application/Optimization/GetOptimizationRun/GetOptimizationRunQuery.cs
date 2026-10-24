using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.GetOptimizationRun;

public sealed record GetOptimizationRunQuery(Guid UserId, Guid Id) : IQuery<OptimizationRunInternal>;