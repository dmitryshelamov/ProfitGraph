using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.MarketData.DownloadCandles;

/// <summary>
///     Downloads the missing closed candles of a series in chunks; the Worker runs it as a background job and reports
///     progress after each chunk. Omitted bounds mean the venue's first candle and the latest closed candle. A named
///     credential of the owner gives a venue without anonymous market data its access (ADR 0023, TC6).
/// </summary>
public sealed record DownloadCandlesCommand(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? OwnerId = null,
    Guid? CredentialId = null)
    : IBackgroundJobRequest<HistoricalDownloadInternal>
{
    public static BackgroundJobKind Kind { get; } = new BackgroundJobKind("historical_download");
}