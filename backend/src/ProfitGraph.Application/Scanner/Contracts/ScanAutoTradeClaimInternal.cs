namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     An auto trade request the Worker took up (ADR 0071, section 6): claimed, so its pair's cards are read next, or already skipped.
/// </summary>
public sealed record ScanAutoTradeClaimInternal(Guid RequestId, Guid OwnerId, string Venue, string Symbol, bool Claimed);