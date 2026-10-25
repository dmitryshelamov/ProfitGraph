using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class LiveMarkPriceDbConverter
{
    public static LiveMarkPrice ToDomain(this LiveMarkPriceDb row)
    {
        return new LiveMarkPrice(new VenueId(row.Venue), new InstrumentSymbol(row.Symbol), row.Price, row.ObservedAt);
    }
}