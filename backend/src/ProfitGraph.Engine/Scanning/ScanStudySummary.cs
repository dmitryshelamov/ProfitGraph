namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     What the price did after a preset's signals: each horizon's change and the largest rise and drop within 24 hours.
/// </summary>
public sealed record ScanStudySummary(
    int Signals,
    ScanOutcomeSummary Change15m,
    ScanOutcomeSummary Change1h,
    ScanOutcomeSummary Change4h,
    ScanOutcomeSummary Change24h,
    ScanOutcomeSummary MaxRise24h,
    ScanOutcomeSummary MaxDrop24h);