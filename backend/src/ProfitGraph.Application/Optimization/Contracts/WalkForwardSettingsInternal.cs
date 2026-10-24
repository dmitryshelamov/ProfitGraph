namespace ProfitGraph.Application.Optimization.Contracts;

/// <param name="Scheme"><c>rolling</c> or <c>anchored</c>.</param>
public sealed record WalkForwardSettingsInternal(int Windows, int TrainToTestRatio, string Scheme);