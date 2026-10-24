using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.GetOptimizationCursor;

public sealed record GetOptimizationCursorQuery(Guid UserId, Guid Id) : IQuery<OptimizationCursorInternal>;