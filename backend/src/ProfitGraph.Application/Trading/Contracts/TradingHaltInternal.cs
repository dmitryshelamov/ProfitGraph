namespace ProfitGraph.Application.Trading.Contracts;

/// <summary>
///     Whether the global halt blocks every submit and start, and when it last changed.
/// </summary>
public sealed record TradingHaltInternal(bool IsHalted, DateTimeOffset UpdatedAt);