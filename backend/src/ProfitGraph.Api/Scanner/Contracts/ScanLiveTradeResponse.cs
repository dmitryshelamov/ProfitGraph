namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A trade created from a card (ADR 0060) with real orders through its own bot: its prices, amount and risk, its bot, when its
///     entry expires, where it is and, once closed, the bot's realized profit; decimals are exact strings.
/// </summary>
public sealed record ScanLiveTradeResponse(
    Guid Id,
    string Venue,
    string Symbol,
    string Scenario,
    string LevelsTimeframe,
    string Entry,
    string Stop,
    string Target,
    string Amount,
    string Risk,
    Guid BotId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt,
    string? RealizedPnl,
    string? FailureCode);