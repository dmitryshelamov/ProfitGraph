using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Infrastructure.MarketData.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.MarketData.Repositories.Converters;

internal static class CandleClosureDbConverter
{
    public static CandleRangeInternal ToInternal(this CandleClosureDb row)
    {
        return new CandleRangeInternal(row.FromTime.ToUniversalTime(), row.ToTime.ToUniversalTime());
    }
}