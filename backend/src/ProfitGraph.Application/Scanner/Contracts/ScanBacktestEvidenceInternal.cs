using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A completed preset backtest whose cards were stored (ADR 0071, section 5): its filter, period and how far the venue's reference
///     pair moved over it.
/// </summary>
public sealed record ScanBacktestEvidenceInternal(
    Guid BacktestId,
    string PresetName,
    ScanFilter Filter,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset CompletedAt,
    decimal? MarketChange);