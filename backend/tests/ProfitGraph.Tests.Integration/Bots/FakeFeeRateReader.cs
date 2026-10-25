using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Fee rates of the test venue; it records which credential asked, without contacting anything.
/// </summary>
public sealed class FakeFeeRateReader : IVenueFeeRateReader
{
    public static readonly VenueFeeRatesInternal DefaultRates = new VenueFeeRatesInternal(MakerRate: 0.0008m, TakerRate: 0.001m);

    public VenueFeeRatesInternal Rates { get; set; } = DefaultRates;

    public string? LastApiKey { get; private set; }

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public Task<VenueFeeRatesInternal> Read(ExchangeCredentialSecretsInternal credential, InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        LastApiKey = credential.ApiKey;

        return Task.FromResult(Rates);
    }
}