using System.Text.Json;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     Creates a stopped bot; decimals are exact invariant strings. Parameters give a value for every parameter of the
///     strategy's latest version. <see cref="Venue" /> is the bot's venue, one that accepts the credential, such as a futures
///     venue that takes the spot venue's key (ADR 0061); without it the bot trades on the credential's own venue.
///     <see cref="VenueSettings" /> is the venue module's own settings object, decimals as strings; without it the bot takes the
///     module's defaults.
/// </summary>
public sealed record CreateBotRequest(
    Guid StrategyId,
    Guid CredentialId,
    string? Symbol,
    string? Timeframe,
    IReadOnlyDictionary<string, string>? Parameters,
    string? AllocationCap,
    string? MaxOrderQuote,
    string? DailyLossLimitQuote,
    bool AllowForeignOrders,
    JsonElement? VenueSettings = null,
    string? Venue = null);