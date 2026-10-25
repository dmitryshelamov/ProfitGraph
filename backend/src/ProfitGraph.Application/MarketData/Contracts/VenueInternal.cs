namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     A venue as clients see it: its timeframes, its backtest defaults, and the defaults of its own bot settings as a JSON
///     object (ADR 0022, C9), or null for a venue without bot settings.
/// </summary>
public sealed record VenueInternal(
    string Id,
    string Name,
    IReadOnlyList<string> Timeframes,
    VenueExecutionDefaultsInternal? ExecutionDefaults,
    string? BotSettings = null);