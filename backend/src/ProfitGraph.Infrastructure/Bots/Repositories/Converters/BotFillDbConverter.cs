using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotFillDbConverter
{
    public static SequencedBotFill ToDomain(this BotFillDb row)
    {
        return new SequencedBotFill(
            row.Sequence,
            BotFill.Restore(
                new BotFillId(row.Id),
                new BotId(row.BotId),
                row.EngineOrderId,
                Enum.Parse<OrderSide>(row.Side),
                row.Tag,
                row.GrossBase,
                row.GrossQuote,
                row.Fee,
                row.CumulativeGrossBase,
                row.FilledAt,
                row.FeeAsset is null ? null : Enum.Parse<FeeAsset>(row.FeeAsset)));
    }

    public static BotFillDb ToDb(this BotFill fill)
    {
        return new BotFillDb
        {
            Id = fill.Id.Value,
            BotId = fill.BotId.Value,
            EngineOrderId = fill.EngineOrderId,
            Side = fill.Side.ToString(),
            Tag = fill.Tag,
            GrossBase = fill.GrossBase,
            GrossQuote = fill.GrossQuote,
            Fee = fill.Fee,
            FeeAsset = fill.FeeAsset.ToString(),
            CumulativeGrossBase = fill.CumulativeGrossBase,
            FilledAt = fill.FilledAt
        };
    }
}