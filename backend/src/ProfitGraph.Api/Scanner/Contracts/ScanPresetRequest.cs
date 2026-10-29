namespace ProfitGraph.Api.Scanner.Contracts;

public sealed record ScanPresetRequest(string Name, IReadOnlyList<ScanConditionRequest> Conditions, string SortMetric, bool SortDescending, bool HideStale);