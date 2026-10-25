using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The Bybit account value read against the mock exchange (Profit Fox Designer ADR 0069). Keys are invented and nothing
///     leaves the process.
/// </summary>
public sealed class BybitAccountBalanceReaderTests
{
    private BybitGatewayHarness _harness = null!;
    private BybitAccountBalanceReader _reader = null!;

    [SetUp]
    public void Create()
    {
        _harness = new BybitGatewayHarness();
        _reader = new BybitAccountBalanceReader(_harness.ReadClient, TimeProvider.System);
    }

    [Test]
    public async Task AccountTotalsAndCoinsAreReadAndBadCoinsSkipped()
    {
        AccountBalanceReadInternal read = await Read();

        AccountBalanceObservationInternal observation = read.Observation.ShouldNotBeNull();
        observation.TotalValueUsd.ShouldBe(1234.5m);
        observation.AvailableValueUsd.ShouldBe(1000m);
        observation.Coins.ShouldBe(
            [new AccountCoinBalanceInternal("USDT", Balance: 1000m, UsdValue: 1000m), new AccountCoinBalanceInternal("BTC", Balance: 0.0035m, UsdValue: 234.5m)]);
        _harness.Exchange.Requests.ShouldContain((HttpMethod.Get, "/v5/account/wallet-balance"));
    }

    [Test]
    public async Task MissingEquityIsAnUnsupportedResponse()
    {
        _harness.Exchange.WalletBalance = """{"list":[{"accountType":"UNIFIED","totalAvailableBalance":""}]}""";

        AccountBalanceReadInternal read = await Read();

        read.ShouldBe(AccountBalanceReadInternal.Failed("unsupported_response", "totalEquity"));
    }

    [Test]
    public async Task UnavailableVenueIsAFailureWithItsStatus()
    {
        _harness.Exchange.WalletBalance = null;

        AccountBalanceReadInternal read = await Read();

        read.ShouldBe(AccountBalanceReadInternal.Failed("venue_unavailable", "503"));
    }

    private Task<AccountBalanceReadInternal> Read()
    {
        return _reader.Read(
            new ExchangeCredentialSecretsInternal(Guid.NewGuid(), "bybit", MockBybitExchange.ApiKey, MockBybitExchange.ApiSecret),
            CancellationToken.None);
    }
}