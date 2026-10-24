namespace ProfitGraph.Api.Optimization.Contracts;

/// <summary>
///     Omitted values are six windows, a train range four times its test range and the rolling scheme.
/// </summary>
/// <param name="Scheme"><c>rolling</c> or <c>anchored</c>.</param>
public sealed record WalkForwardRequest(int? Windows, int? TrainToTestRatio, string? Scheme);