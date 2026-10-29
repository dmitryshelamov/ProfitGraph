using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Tests.Unit.Domain.Scanner.Trades;

/// <summary>
///     Trade card settings (ADR 0060): a positive risk and largest amount; a trade spends what loses the risk at its stop after fees,
///     at most the largest amount, and then risks less.
/// </summary>
public sealed class ScanTradeSettingsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 30, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void SizesTheAmountByTheRiskAtTheStopWithFees()
    {
        ScanTradeSettings settings = Settings(risk: 10m, maxAmount: 1000m);

        // A 5 % stop and 0.1 % on each side lose 5.2 % of the amount: 10 / 0.052.
        (decimal amount, decimal risk) = settings.Size(entry: 100m, stop: 95m, feeRate: 0.001m);

        amount.ShouldBe(10m / 0.052m);
        risk.ShouldBe(10m);
    }

    [Test]
    public void CapsTheAmountAndRisksLess()
    {
        ScanTradeSettings settings = Settings(risk: 10m, maxAmount: 100m);

        settings.Size(entry: 100m, stop: 95m, feeRate: 0.001m).ShouldBe((100m, 5.2m));
    }

    [Test]
    public void ChangesAndRemembersTheStrategy()
    {
        ScanTradeSettings settings = Settings(risk: 10m, maxAmount: 100m);
        var credential = ExchangeCredentialId.New();
        var strategy = StrategyId.New();

        settings.Change(credential, risk: 5m, maxAmount: 50m, Now.AddMinutes(1));
        settings.UseStrategy(strategy, Now.AddMinutes(2));

        (settings.CredentialId, settings.Risk, settings.MaxAmount, settings.StrategyId, settings.UpdatedAt)
            .ShouldBe((credential, 5m, 50m, (StrategyId?)strategy, Now.AddMinutes(2)));
    }

    [Test]
    public void KeepsTheShortStrategyAndTheLeverageApart()
    {
        var settings = ScanTradeSettings.Create(UserId.New(), new VenueId("futures"), ExchangeCredentialId.New(), risk: 10m, maxAmount: 1000m, Now, leverage: 3m);
        var strategy = StrategyId.New();

        settings.UseShortStrategy(strategy, Now.AddMinutes(1));

        (settings.ShortStrategyId, settings.StrategyId, settings.Leverage).ShouldBe(((StrategyId?)strategy, (StrategyId?)null, (decimal?)3m));

        // A short's stop above its entry sizes like a long's below it.
        settings.Size(entry: 100m, stop: 105m, feeRate: 0.001m).Amount.ShouldBe(10m / 0.052m);
        Should.Throw<ScanTradeRuleException>(() => settings.Change(settings.CredentialId, risk: 10m, maxAmount: 1000m, Now, leverage: 0.5m));
    }

    [TestCase(arg1: 0, arg2: 100)]
    [TestCase(arg1: 10, arg2: 0)]
    public void RefusesANonPositiveRiskOrAmount(decimal risk, decimal maxAmount)
    {
        Should.Throw<ScanTradeRuleException>(() => Settings(risk, maxAmount)).Code.ShouldBe(ScanTradeRuleException.InvalidSettings);
        Should.Throw<ScanTradeRuleException>(() => Settings(risk: 1m, maxAmount: 1m).Change(ExchangeCredentialId.New(), risk, maxAmount, Now));
    }

    private static ScanTradeSettings Settings(decimal risk, decimal maxAmount)
    {
        return ScanTradeSettings.Create(UserId.New(), new VenueId("bybit"), ExchangeCredentialId.New(), risk, maxAmount, Now);
    }
}