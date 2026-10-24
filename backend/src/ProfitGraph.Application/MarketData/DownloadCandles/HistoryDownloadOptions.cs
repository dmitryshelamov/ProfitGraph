namespace ProfitGraph.Application.MarketData.DownloadCandles;

/// <summary>
///     Section <c>HistoricalDownload</c>: each chunk is fetched, checked and stored in its own short transaction.
/// </summary>
public sealed class HistoryDownloadOptions
{
    public const string SectionName = "HistoricalDownload";

    public int MaxCandlesPerChunk { get; init; } = 10_000;

    public bool IsValid()
    {
        return MaxCandlesPerChunk is > 0 and <= 100_000;
    }
}