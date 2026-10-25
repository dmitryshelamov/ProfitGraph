using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Numbers;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotOrderWaitingResponseConverter
{
    public static BotOrderWaitingResponse ToResponse(this BotOrderWaitingInternal waiting)
    {
        return new BotOrderWaitingResponse(
            waiting.EngineOrderId,
            waiting.Code,
            waiting.Since,
            waiting.Until,
            waiting.Low is { } low ? ExactDecimalText.Format(low) : null,
            waiting.High is { } high ? ExactDecimalText.Format(high) : null);
    }
}