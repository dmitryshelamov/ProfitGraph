namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed record ScanFilterDb(int FormatVersion, IReadOnlyList<ScanPresetConditionDb> Conditions, bool HideStale);