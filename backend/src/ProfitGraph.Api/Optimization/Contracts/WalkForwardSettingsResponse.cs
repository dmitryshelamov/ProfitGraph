namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record WalkForwardSettingsResponse(int Windows, int TrainToTestRatio, string Scheme);