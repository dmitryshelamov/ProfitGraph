using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Api.Strategies.Contracts;

/// <summary>
///     A running bot that switches to the saved version; <see cref="Parameters" /> are its values as exact decimal strings.
/// </summary>
public sealed record LiveImpactBotResponse(
    Guid BotId,
    string Venue,
    string Symbol,
    string Timeframe,
    Guid StrategyVersionId,
    BotRuntimeStatus RuntimeStatus,
    IReadOnlyDictionary<string, string> Parameters);