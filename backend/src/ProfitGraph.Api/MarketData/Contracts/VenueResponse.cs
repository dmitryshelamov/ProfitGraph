using System.Text.Json;

namespace ProfitGraph.Api.MarketData.Contracts;

/// <summary>
///     A venue: its timeframes, its backtest defaults, and the defaults of its own bot settings as the JSON object
///     <c>POST /api/bots</c> takes as <c>venueSettings</c>, or null for a venue without them, the codes of what its backtests do
///     not model (ADR 0023), and the other venue whose credentials it also takes, or null (ADR 0061): a bot or scanner trade here
///     may use a key of that venue.
/// </summary>
public sealed record VenueResponse(
    string Id,
    string Name,
    IReadOnlyList<string> Timeframes,
    VenueExecutionDefaultsResponse? ExecutionDefaults,
    JsonElement? BotSettings,
    IReadOnlyList<string> BacktestLimits,
    string? CredentialVenue);