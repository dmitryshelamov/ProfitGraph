namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A venue that has a market scanner, with the currency its turnover, trade amounts and results are in when all its pairs
///     share one.
/// </summary>
public sealed record ScanVenueInternal(string Venue, string Name, string? QuoteAsset, bool SupportsShort = false);
