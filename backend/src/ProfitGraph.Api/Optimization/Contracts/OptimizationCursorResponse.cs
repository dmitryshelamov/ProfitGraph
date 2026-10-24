namespace ProfitGraph.Api.Optimization.Contracts;

/// <param name="LastSequence">The last stored trial; messages follow it, and earlier trials are at /trials.</param>
public sealed record OptimizationCursorResponse(long LastSequence, string Status);