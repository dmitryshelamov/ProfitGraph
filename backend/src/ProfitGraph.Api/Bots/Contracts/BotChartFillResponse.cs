using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A fill the bot's chart marks; the price and base quantity are exact decimal strings.
/// </summary>
public sealed record BotChartFillResponse(Guid Id, DateTimeOffset FilledAt, OrderSide Side, string Price, string GrossBase, string? Tag);