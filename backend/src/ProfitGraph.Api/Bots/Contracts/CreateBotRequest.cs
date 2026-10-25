using System.Text.Json;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     Creates a stopped bot; decimals are exact invariant strings. Parameters give a value for every parameter of the
///     strategy's latest version. The venue comes from the credential. <see cref="VenueSettings" /> is the venue module's own
///     settings object, decimals as strings; without it the bot takes the module's defaults.
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
    JsonElement? VenueSettings = null);