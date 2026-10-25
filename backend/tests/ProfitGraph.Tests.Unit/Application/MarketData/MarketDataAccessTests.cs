using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData;

namespace ProfitGraph.Tests.Unit.Application.MarketData;

/// <summary>
///     The access flows into awaited calls of the use case that opened it and ends with its scope (ADR 0023, TC6).
/// </summary>
public sealed class MarketDataAccessTests
{
    private static readonly ExchangeCredentialSecretsInternal Outer = new ExchangeCredentialSecretsInternal(Guid.NewGuid(), "tinvest", "outer-key", "outer-secret");
    private static readonly ExchangeCredentialSecretsInternal Inner = new ExchangeCredentialSecretsInternal(Guid.NewGuid(), "tinvest", "inner-key", "inner-secret");

    [Test]
    public async Task AnAccessFlowsIntoAwaitedCalls_NestsAndEndsWithItsScope()
    {
        MarketDataAccess.Current.ShouldBeNull();

        using (MarketDataAccess.Use(Outer))
        {
            (await Read()).ShouldBe(Outer);

            using (MarketDataAccess.Use(Inner))
            {
                (await Read()).ShouldBe(Inner);
            }

            MarketDataAccess.Current.ShouldBe(Outer);

            using (MarketDataAccess.Use(credential: null))
            {
                (await Read()).ShouldBeNull();
            }
        }

        MarketDataAccess.Current.ShouldBeNull();
    }

    [Test]
    public async Task WorkStartedAfterTheScopeDoesNotSeeTheAccess()
    {
        using (MarketDataAccess.Use(Outer))
        {
            (await Task.Run(() => MarketDataAccess.Current)).ShouldBe(Outer);
        }

        (await Task.Run(() => MarketDataAccess.Current)).ShouldBeNull();
    }

    private static async Task<ExchangeCredentialSecretsInternal?> Read()
    {
        await Task.Yield();

        return MarketDataAccess.Current;
    }
}