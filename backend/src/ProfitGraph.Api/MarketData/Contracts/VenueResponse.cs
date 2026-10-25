using System.Text.Json;

namespace ProfitGraph.Api.MarketData.Contracts;

/// <summary>
///     A venue: its timeframes, its backtest defaults, and the defaults of its own bot settings as the JSON object
///     <c>POST /api/bots</c> takes as <c>venueSettings</c>, or null for a venue without them, and the codes of what its backtests do
///     not model (ADR 0023).
/// </summary>
public sealed record VenueResponse(
    string Id,
    string Name,
    IReadOnlyList<string> Timeframes,
    VenueExecutionDefaultsResponse? ExecutionDefaults,
    JsonElement? BotSettings,
    IReadOnlyList<string> BacktestLimits);