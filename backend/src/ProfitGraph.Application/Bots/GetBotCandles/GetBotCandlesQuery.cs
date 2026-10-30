using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotCandles;

/// <summary>
///     A page of the bot's chart for the half-open UTC range [From, To): the venue, symbol and market data access come from the
///     bot itself, and the timeframe is the bot's own unless the request names another one its venue offers. From is rounded down
///     and To up to that timeframe's grid, and To is cut at the latest closed candle.
/// </summary>
public sealed record GetBotCandlesQuery(Guid OwnerId, Guid BotId, DateTimeOffset From, DateTimeOffset To, string? Timeframe = null) : IQuery<BotCandlesInternal>;