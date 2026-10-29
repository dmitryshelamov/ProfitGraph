namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A pair entering the preset's result in history: when, the entry price, its metrics then and the price changes after it, in
///     percent, and its scenarios' closed trades; an empty change means the history does not reach that far yet.
/// </summary>
public sealed record ScanSignalResponse(
    string Symbol,
    DateTimeOffset At,
    string? EntryPrice,
    ScanSnapshotRowResponse Metrics,
    string? Change15m,
    string? Change1h,
    string? Change4h,
    string? Change24h,
    string? MaxRise24h,
    string? MaxDrop24h,
    IReadOnlyList<ScanTradeResponse> Trades);