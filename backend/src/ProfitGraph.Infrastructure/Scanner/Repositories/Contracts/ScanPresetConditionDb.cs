namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed record ScanPresetConditionDb(string Metric, decimal? Minimum, decimal? Maximum);