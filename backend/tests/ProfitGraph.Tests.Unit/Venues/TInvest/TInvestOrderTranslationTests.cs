using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.Trading;

using Tinkoff.InvestApi.V1;

using EngineOrderType = ProfitGraph.Engine.Accounting.OrderType;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     How an invented T-Invest order state becomes engine updates (ADR 0023), and which condition makes a state unresolved.
/// </summary>
public sealed class TInvestOrderTranslationTests
{
    private const decimal Lot = 10m;

    [Test]
    public void AnExecutedMarketOrderWhoseCommissionIsAZeroWithoutCurrencyIsAPendingFill()
    {
        OrderState state = Filled(lots: 2, price: 114.45m);
        state.ExecutedCommission = new MoneyValue();

        IReadOnlyList<ExternalOrderUpdate> updates = TInvestOrderTranslation.Updates(Attempt(), state, Lot, TInvestExecution.None);

        updates.ShouldBe(
        [
            ExternalOrderUpdate.Accept(orderId: 1),
            ExternalOrderUpdate.Final(orderId: 1, grossBase: 20m, grossQuote: 2289m, fee: 0m, commissionPending: true, commissionPartitioned: true)
        ]);
    }

    [Test]
    public void ACommissionInAnotherCurrencyIsUnresolved()
    {
        OrderState state = Filled(lots: 2, price: 114.45m);
        state.ExecutedCommission = new MoneyValue { Currency = "usd", Units = 1 };

        var error = Should.Throw<TInvestOrderUnresolvedException>(() => TInvestOrderTranslation.Updates(Attempt(), state, Lot, TInvestExecution.None));

        error.Reason.ShouldBe("CommissionNotInRoubles");
        error.IsTransient.ShouldBeFalse();
    }

    [Test]
    public void StagesThatDoNotYetCoverTheExecutedLotsAreUnresolvedButWorthReadingAgain()
    {
        OrderState state = Filled(lots: 2, price: 114.45m);
        state.Stages.Clear();

        var error = Should.Throw<TInvestOrderUnresolvedException>(() => TInvestOrderTranslation.Updates(Attempt(), state, Lot, TInvestExecution.None));

        error.Reason.ShouldBe("StagesDisagreeWithExecutedLots");
        error.IsTransient.ShouldBeTrue();
    }

    [Test]
    public void AStagePriceInAnotherCurrencyOrMissingIsUnresolvedForeverWithItsReason()
    {
        OrderState foreign = Filled(lots: 2, price: 114.45m);
        foreign.Stages[0].Price.Currency = "usd";
        OrderState missing = Filled(lots: 2, price: 114.45m);
        missing.Stages[0].Price = null;

        Should.Throw<TInvestOrderUnresolvedException>(() => TInvestOrderTranslation.Updates(Attempt(), foreign, Lot, TInvestExecution.None))
            .Reason.ShouldBe("StagePriceNotInRoubles");
        Should.Throw<TInvestOrderUnresolvedException>(() => TInvestOrderTranslation.Updates(Attempt(), missing, Lot, TInvestExecution.None))
            .Reason.ShouldBe("StagePriceMissing");
    }

    private static OrderState Filled(long lots, decimal price)
    {
        var state = new OrderState
        {
            OrderId = "7000001",
            LotsRequested = lots,
            LotsExecuted = lots,
            ExecutionReportStatus = OrderExecutionReportStatus.ExecutionReportStatusFill,
            ExecutedCommission = new MoneyValue { Currency = "rub" }
        };
        state.Stages.Add(
            new OrderStage
            {
                Quantity = lots,
                Price = new MoneyValue { Currency = "rub", Units = (long)price, Nano = (int)((price - (long)price) * 1_000_000_000m) },
                TradeId = "t1"
            });

        return state;
    }

    private static TInvestOrderAttempt Attempt()
    {
        return new TInvestOrderAttempt(
            Guid.Parse("4f1d2c3b-5a69-4788-9a0b-1c2d3e4f5a6b"),
            EngineOrderId: 1,
            Attempt: 1,
            Guid.NewGuid(),
            "e6123145-9665-43e0-8413-cd61b8aa9b13",
            OrderSide.Buy,
            EngineOrderType.Market,
            Lots: 2,
            Price: null,
            TInvestOrderAttemptState.Active,
            "7000001",
            BrokerCode: null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
    }
}