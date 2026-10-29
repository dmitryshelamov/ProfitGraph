using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.Common.Queries;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.MarketData.Converters;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Fills;

namespace ProfitGraph.Application.Bots.GetBotCandles;

/// <summary>
///     Reads a page of the bot's chart (ADR 0026) through <see cref="CandleChartReader" /> with the bot's market data access.
///     Fills come from the bot's record for the same range.
/// </summary>
internal sealed class GetBotCandlesQueryHandler(
    IBotRepository bots,
    IBotFillRepository fills,
    BotMarketDataAccess access,
    CandleChartReader chart,
    QueryLimitOptions limits) : IRequestHandler<GetBotCandlesQuery, BotCandlesInternal>
{
    public async Task<BotCandlesInternal> Handle(GetBotCandlesQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        CandleChartPage page;

        // A venue without anonymous market data is read with the bot's own credential (ADR 0023, TC6).
        using (MarketDataAccess.Use(await access.Read(bot, cancellationToken)))
        {
            page = await chart.Read(bot.Series, query.From, query.To, limits.BotChartCandles, cancellationToken);
        }

        IReadOnlyList<BotFill> made = page.To > page.From
            ? await fills.ListFilled(bot.Id, page.From, page.To, limits.BotChartFills + 1, cancellationToken)
            : [];

        BotChartFillInternal[] marked =
        [
            .. made.Take(limits.BotChartFills).Select(fill => new BotChartFillInternal(fill.Id.Value, fill.FilledAt, fill.Side, fill.Price, fill.GrossBase, fill.Tag))
        ];

        return new BotCandlesInternal(
            bot.Series.Venue.Value,
            bot.Series.Symbol.Value,
            bot.Series.Timeframe.Key,
            page.From,
            page.To,
            page.LatestClosed,
            page.Status,
            page.ErrorCode,
            [.. page.Candles.Select(candle => candle.ToInternal())],
            marked,
            made.Count > limits.BotChartFills);
    }
}