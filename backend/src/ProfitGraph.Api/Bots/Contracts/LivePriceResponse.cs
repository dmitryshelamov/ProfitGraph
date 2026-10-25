namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A venue's display price of an instrument (S5-19); the price is an exact decimal string.
/// </summary>
public sealed record LivePriceResponse(string Venue, string Symbol, string Price, DateTimeOffset ObservedAt);