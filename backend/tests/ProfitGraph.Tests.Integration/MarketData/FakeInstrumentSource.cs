using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     The test venue's instruments: every symbol ending in USDT trades with fixed rules unless a test replaces them.
/// </summary>
public sealed class FakeInstrumentSource : IInstrumentRulesSource, IInstrumentCatalog
{
    public static readonly InstrumentRules DefaultRules = new InstrumentRules(
        quantityStep: 0.000001m,
        priceTick: 0.01m,
        minQuantity: 0m,
        minNotional: 5m,
        basePrecision: 6,
        quotePrecision: 8);

    public Func<InstrumentSymbol, InstrumentRulesDefinitionInternal>? Rules { get; set; }

    public IReadOnlyList<InstrumentInternal> Instruments { get; set; } = [];

    public Exception? SearchFailure { get; set; }

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public Task<IReadOnlyList<InstrumentInternal>> Search(string query, int limit, CancellationToken cancellationToken)
    {
        if (SearchFailure is not null)
        {
            throw SearchFailure;
        }

        return Task.FromResult<IReadOnlyList<InstrumentInternal>>(
            [.. Instruments.Where(instrument => instrument.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit)]);
    }

    public Task<InstrumentRulesDefinitionInternal> Get(InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        if (Rules is not null)
        {
            return Task.FromResult(Rules(symbol));
        }

        return symbol.Value.EndsWith("USDT", StringComparison.Ordinal)
            ? Task.FromResult(Definition(symbol.Value))
            : throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, $"No instrument '{symbol.Value}'.");
    }

    public void Reset()
    {
        Rules = null;
        Instruments = [];
        SearchFailure = null;
    }

    public static InstrumentRulesDefinitionInternal Definition(string symbol)
    {
        return new InstrumentRulesDefinitionInternal(
            TestHistoryVenue.WithHistory,
            "test",
            "spot",
            symbol,
            symbol[..^4],
            "USDT",
            BasePrecisionIncrement: 0.000001m,
            QuotePrecisionIncrement: 0.00000001m,
            DefaultRules);
    }
}