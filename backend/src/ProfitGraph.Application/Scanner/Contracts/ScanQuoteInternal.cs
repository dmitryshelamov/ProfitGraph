namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The best bid and ask of one instrument at the moment of a quote snapshot; a side the venue did not report is null.
/// </summary>
public sealed record ScanQuoteInternal(string Symbol, decimal? BestBid, decimal? BestAsk);