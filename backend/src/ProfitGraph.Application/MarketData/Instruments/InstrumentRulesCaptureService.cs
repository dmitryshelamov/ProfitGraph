using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.Instruments;

/// <summary>
///     Captures the venue's current rules of an instrument as an immutable snapshot (Profit Fox Designer ADR 0021). The
///     venue call happens outside any transaction; only the insert is transactional.
/// </summary>
public sealed class InstrumentRulesCaptureService(
    VenuePorts<IInstrumentRulesSource> sources,
    IInstrumentRulesRepository repository,
    ITransactionExecutor transactions,
    TimeProvider clock)
{
    public async Task<InstrumentRulesSnapshotInternal> Capture(VenueId venue, InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        transactions.EnsureNoTransaction();
        IInstrumentRulesSource source = sources.Find(venue)
            ?? throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, "This venue offers no instrument rules.");
        InstrumentRulesDefinitionInternal definition = await source.Get(symbol, cancellationToken);

        if (definition.Venue != venue.Value || definition.Symbol != symbol.Value)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidProviderData, "The venue returned rules of another instrument.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The database keeps microseconds; truncating here makes the returned snapshot equal to the stored one.
        long now = clock.GetUtcNow().UtcTicks;
        var capturedAt = new DateTimeOffset(now - now % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        var snapshot = new InstrumentRulesSnapshotInternal(Guid.NewGuid(), definition, capturedAt);

        return await transactions.ExecuteAsync(
            async () =>
            {
                await repository.Add(snapshot, cancellationToken);

                return snapshot;
            },
            cancellationToken);
    }
}