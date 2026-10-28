using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.Trading;

using Tinkoff.InvestApi.V1;

using EngineOrderType = ProfitGraph.Engine.Accounting.OrderType;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     How a T-Invest order state is recorded on its attempt (ADR 0023) when another writer moved the attempt after the
///     reader read it, as the gateway does once its place request returns while the order state stream already reads the
///     order. Values are invented.
/// </summary>
public sealed class TInvestOrderProgressTests
{
    private const string ExchangeOrderId = "7000001";

    private TInvestGatewayHarness _harness = null!;

    [SetUp]
    public void CreateHarness()
    {
        _harness = new TInvestGatewayHarness();
    }

    [Test]
    public async Task AFillReadForAnAttemptStillSeenAsSentIsRecordedOnTheAttemptAsStored()
    {
        TInvestOrderAttempt active = await Stored(TInvestOrderAttemptState.Active);

        IReadOnlyList<ExternalOrderUpdate> updates = await _harness.Progress.Apply(
            _harness.Context,
            active with { State = TInvestOrderAttemptState.Sent, ExchangeOrderId = null },
            Filled(active.RequestId),
            CancellationToken.None);

        updates.ShouldBe(
        [
            ExternalOrderUpdate.Accept(orderId: 1),
            ExternalOrderUpdate.Final(orderId: 1, grossBase: 10m, grossQuote: 1000m, fee: 0m, commissionPending: true, commissionPartitioned: true)
        ]);
        _harness.Attempts.All.ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                attempt => attempt.State.ShouldBe(TInvestOrderAttemptState.Final),
                attempt => attempt.ExchangeOrderId.ShouldBe(ExchangeOrderId),
                attempt => attempt.ExecutedLots.ShouldBe(1),
                attempt => attempt.ExecutedQuote.ShouldBe(1000m),
                attempt => attempt.ExecutedFee.ShouldBe(0m));
    }

    [Test]
    public async Task AFillReadForAnAttemptThatHasEndedSinceKeepsTheStoredOutcome()
    {
        TInvestOrderAttempt final = await Stored(TInvestOrderAttemptState.Final, executedFee: 0.5m);

        await _harness.Progress.Apply(_harness.Context, final with { State = TInvestOrderAttemptState.Active }, Filled(final.RequestId), CancellationToken.None);

        _harness.Attempts.All.ShouldHaveSingleItem().ShouldBe(final);
    }

    /// <summary>
    ///     A limit buy of one lot at 100 as the attempts table holds it.
    /// </summary>
    private async Task<TInvestOrderAttempt> Stored(TInvestOrderAttemptState state, decimal executedFee = 0m)
    {
        bool final = state == TInvestOrderAttemptState.Final;
        var attempt = new TInvestOrderAttempt(
            _harness.Bot.Value,
            EngineOrderId: 1,
            Attempt: 1,
            Guid.NewGuid(),
            TInvestGatewayHarness.ShareUid,
            OrderSide.Buy,
            EngineOrderType.Limit,
            Lots: 1,
            Price: 100m,
            state,
            ExchangeOrderId,
            BrokerCode: null,
            TInvestGatewayHarness.Now,
            TInvestGatewayHarness.Now,
            ExecutedLots: final ? 1 : 0,
            ExecutedQuote: final ? 1000m : 0m,
            ExecutedFee: executedFee);
        (await _harness.Attempts.Add(attempt, CancellationToken.None)).ShouldBeTrue();

        return attempt;
    }

    /// <summary>
    ///     What T-Invest reports for the attempt once its lot filled at 100, with the commission not charged yet.
    /// </summary>
    private static OrderState Filled(Guid requestId)
    {
        var state = new OrderState
        {
            OrderId = ExchangeOrderId,
            OrderRequestId = requestId.ToString(),
            InstrumentUid = TInvestGatewayHarness.ShareUid,
            LotsRequested = 1,
            LotsExecuted = 1,
            ExecutionReportStatus = OrderExecutionReportStatus.ExecutionReportStatusFill,
            ExecutedCommission = new MoneyValue { Currency = "rub" }
        };
        state.Stages.Add(new OrderStage { Quantity = 1, Price = new MoneyValue { Currency = "rub", Units = 100 }, TradeId = "t1" });

        return state;
    }
}