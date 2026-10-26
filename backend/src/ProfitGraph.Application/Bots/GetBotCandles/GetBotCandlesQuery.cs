using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotCandles;

/// <summary>
///     A page of the bot's chart for the half-open UTC range [From, To): the venue, symbol, timeframe and market data
///     access come from the bot itself. From is rounded down and To up to the bot's timeframe grid, and To is cut at the
///     latest closed candle.
/// </summary>
public sealed record GetBotCandlesQuery(Guid OwnerId, Guid BotId, DateTimeOffset From, DateTimeOffset To) : IQuery<BotCandlesInternal>;