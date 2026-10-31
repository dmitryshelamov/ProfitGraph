namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     What the price did after a preset's signals and how its trade scenarios did (ADR 0059, sections 6 and 8), and how well the odds
///     of the owner's other backtests predicted its cards, null without such backtests (ADR 0073).
/// </summary>
public sealed record ScanStudySummaryResponse(
    int Signals,
    ScanOutcomeSummaryResponse Change15m,
    ScanOutcomeSummaryResponse Change1h,
    ScanOutcomeSummaryResponse Change4h,
    ScanOutcomeSummaryResponse Change24h,
    ScanOutcomeSummaryResponse MaxRise24h,
    ScanOutcomeSummaryResponse MaxDrop24h,
    IReadOnlyList<ScanScenarioSummaryResponse> Scenarios,
    ScanCardCalibrationResponse? Calibration);