using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotOrderWaitingDbConverter
{
    public static BotOrderWaiting ToDomain(this BotOrderWaitingDb row)
    {
        return BotOrderWaiting.Restore(
            new BotId(row.BotId),
            row.EngineOrderId,
            Enum.Parse<OrderWaitingCode>(row.Code),
            row.Since.ToUniversalTime(),
            row.Until?.ToUniversalTime(),
            row.Low,
            row.High);
    }

    public static BotOrderWaitingDb ToDb(this BotOrderWaiting waiting)
    {
        return new BotOrderWaitingDb
        {
            BotId = waiting.BotId.Value,
            EngineOrderId = waiting.EngineOrderId,
            Code = waiting.Code.ToString(),
            Since = waiting.Since,
            Until = waiting.Until,
            Low = waiting.Low,
            High = waiting.High
        };
    }
}