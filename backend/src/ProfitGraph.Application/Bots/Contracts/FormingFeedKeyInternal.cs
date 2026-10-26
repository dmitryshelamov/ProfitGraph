namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     One shared upstream feed of the forming candle: a series, and for a venue that reads market data with the caller's
///     credential, that credential, so one owner's token never serves another owner.
/// </summary>
public sealed record FormingFeedKeyInternal(string Venue, string Symbol, string Timeframe, Guid? CredentialId);