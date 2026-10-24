namespace ProfitGraph.Application.MarketData.ImportCandles;

/// <summary>
///     Limits of the streamed CSV import (section <c>CsvImport</c>), validated at startup.
/// </summary>
public sealed class CsvImportOptions
{
    public const string SectionName = "CsvImport";

    public int BatchSize { get; init; } = 1000;
    public int MaxRows { get; init; } = 100_000;
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;
    public long MaxRequestSizeBytes { get; init; } = 12 * 1024 * 1024;
    public int MaxRowLength { get; init; } = 1024;

    /// <summary>
    ///     Detailed row errors in a response, including the reason reading stopped; a summary line comes on top.
    /// </summary>
    public int MaxDetailedErrors { get; init; } = 20;

    public bool IsValid()
    {
        return MaxRows is > 0 and < int.MaxValue && BatchSize > 0 && BatchSize <= MaxRows &&
            MaxFileSizeBytes > 0 && MaxRequestSizeBytes > MaxFileSizeBytes && MaxRowLength > 0 &&
            MaxDetailedErrors > 0 && MaxDetailedErrors <= MaxRows;
    }
}