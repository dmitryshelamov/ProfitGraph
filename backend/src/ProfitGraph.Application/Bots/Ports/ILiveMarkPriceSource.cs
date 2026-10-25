using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     A venue's public last-trade price of a symbol for display (stage 5 plan S5-19): observations as the venue reports
///     them, never used by the engine or by replay. The stream ends or throws when the venue connection is lost.
/// </summary>
public interface ILiveMarkPriceSource : IVenuePort
{
    IAsyncEnumerable<LiveMarkPriceInternal> Subscribe(InstrumentSymbol symbol, CancellationToken cancellationToken);
}