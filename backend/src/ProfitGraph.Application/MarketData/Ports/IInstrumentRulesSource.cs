using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     A venue's current trading rules of an instrument. Failures are <see cref="InstrumentRulesException" />.
/// </summary>
public interface IInstrumentRulesSource : IVenuePort
{
    Task<InstrumentRulesDefinitionInternal> Get(InstrumentSymbol symbol, CancellationToken cancellationToken);
}