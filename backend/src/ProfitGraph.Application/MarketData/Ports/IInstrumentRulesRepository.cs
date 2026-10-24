using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     Append-only storage of captured instrument rules.
/// </summary>
public interface IInstrumentRulesRepository
{
    Task Add(InstrumentRulesSnapshotInternal snapshot, CancellationToken cancellationToken);

    Task<InstrumentRulesSnapshotInternal?> Get(Guid id, CancellationToken cancellationToken);
}