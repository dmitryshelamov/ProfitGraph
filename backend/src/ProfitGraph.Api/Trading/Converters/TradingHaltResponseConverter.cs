using ProfitGraph.Api.Trading.Contracts;
using ProfitGraph.Application.Trading.Contracts;

namespace ProfitGraph.Api.Trading.Converters;

internal static class TradingHaltResponseConverter
{
    public static TradingHaltResponse ToResponse(this TradingHaltInternal halt)
    {
        return new TradingHaltResponse(halt.IsHalted, halt.UpdatedAt);
    }
}