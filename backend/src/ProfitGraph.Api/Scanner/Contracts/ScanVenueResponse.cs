namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A venue the scanner scans, the currency its pairs share, and whether its pairs have short trade cards (ADR 0061).
/// </summary>
public sealed record ScanVenueResponse(string Venue, string Name, string? QuoteAsset, bool SupportsShort);
