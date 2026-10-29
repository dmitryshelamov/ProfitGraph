namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     An instrument of a venue's scan universe (ADR 0059) and, when the venue publishes it, the time it was listed.
/// </summary>
public sealed record ScanInstrumentInternal(string Symbol, DateTimeOffset? ListedAt);