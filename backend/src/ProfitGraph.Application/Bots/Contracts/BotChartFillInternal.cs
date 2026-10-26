using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     A fill of the bot as its chart marks it: when, which side, at what average price and how much base.
/// </summary>
public sealed record BotChartFillInternal(Guid Id, DateTimeOffset FilledAt, OrderSide Side, decimal Price, decimal GrossBase, string? Tag);