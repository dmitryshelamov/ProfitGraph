namespace ProfitGraph.Api.Scanner.Contracts;

public sealed record ScanStudySummaryResponse(
    int Signals,
    ScanOutcomeSummaryResponse Change15m,
    ScanOutcomeSummaryResponse Change1h,
    ScanOutcomeSummaryResponse Change4h,
    ScanOutcomeSummaryResponse Change24h,
    ScanOutcomeSummaryResponse MaxRise24h,
    ScanOutcomeSummaryResponse MaxDrop24h);